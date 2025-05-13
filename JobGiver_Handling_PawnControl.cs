using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Base class for animal handling job givers with specialized cache management
    /// </summary>
    public abstract class JobGiver_Handling_PawnControl : JobGiver_Scan_PawnControl
    {
        #region Configuration

        public override string WorkTag => "Handling";

        protected virtual float[] DistanceThresholds => new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        public override PawnEnumTags RequiredTag => PawnEnumTags.AllowWork_Handling; // Only animals

        /// <summary>
        /// Cache update interval for handling-related caches
        /// </summary>
        protected override int CacheUpdateInterval => 200; // Update every ~3.3 seconds

        /// <summary>
        /// Suffix for the animal cache key
        /// </summary>
        private const string ANIMAL_CACHE_SUFFIX = "_Animals";

        #endregion

        #region Target Selection

        /// <summary>
        /// Get potential animal targets on the given map
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null)
                yield break;

            // Get cached animals from centralized cache
            var animals = GetOrCreateAnimalCache(map);

            // Return animals as targets
            foreach (Pawn animal in animals)
            {
                if (animal != null && !animal.Dead && animal.Spawned)
                    yield return animal;
            }
        }

        /// <summary>
        /// Job-specific cache update method - gets all potential animals on the map
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            if (map == null)
                yield break;

            // Find all animals that could potentially be handled
            List<Pawn> validAnimals = new List<Pawn>();

            foreach (Pawn animal in map.mapPawns.AllPawns)
            {
                // Skip non-animals, dead animals, etc.
                if (animal == null || !animal.RaceProps.Animal || animal.Dead || !animal.Spawned)
                    continue;

                // Skip hostile or wild animals (unless this handler can tame)
                if (animal.Faction != Faction.OfPlayer && !CanHandleWildAnimals())
                    continue;

                // Add this animal to the potential targets
                validAnimals.Add(animal);
            }

            // Update the centralized cache
            StoreAnimalCache(map, validAnimals);

            // Convert to Things for the base class
            foreach (Pawn animal in validAnimals)
            {
                yield return animal;
            }
        }

        /// <summary>
        /// Gets or creates a cache of animals for a specific map
        /// </summary>
        protected List<Pawn> GetOrCreateAnimalCache(Map map)
        {
            if (map == null)
                return new List<Pawn>();

            int mapId = map.uniqueID;
            string cacheKey = this.GetType().Name + ANIMAL_CACHE_SUFFIX;

            // Try to get cached animals from the map cache manager
            var animalCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Pawn>>(mapId);

            // Check if we need to update the cache
            int currentTick = Find.TickManager.TicksGame;
            int lastUpdateTick = Utility_MapCacheManager.GetLastCacheUpdateTick(mapId, cacheKey);

            if (currentTick - lastUpdateTick > CacheUpdateInterval ||
                !animalCache.TryGetValue(cacheKey, out List<Pawn> animals) ||
                animals == null ||
                animals.Any(a => a == null || a.Dead || !a.Spawned))
            {
                // Cache is invalid or expired, rebuild it
                animals = new List<Pawn>();

                foreach (Pawn animal in map.mapPawns.AllPawns)
                {
                    // Skip non-animals, dead animals, etc.
                    if (animal == null || !animal.RaceProps.Animal || animal.Dead || !animal.Spawned)
                        continue;

                    // Skip hostile or wild animals (unless this handler can tame)
                    if (animal.Faction != Faction.OfPlayer && !CanHandleWildAnimals())
                        continue;

                    // Add this animal to the potential targets
                    animals.Add(animal);
                }

                // Store in the central cache
                StoreAnimalCache(map, animals);
            }

            return animals;
        }

        /// <summary>
        /// Store a list of animals in the centralized cache
        /// </summary>
        private void StoreAnimalCache(Map map, List<Pawn> animals)
        {
            if (map == null)
                return;

            int mapId = map.uniqueID;
            string cacheKey = this.GetType().Name + ANIMAL_CACHE_SUFFIX;
            int currentTick = Find.TickManager.TicksGame;

            var animalCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Pawn>>(mapId);
            animalCache[cacheKey] = animals;
            Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, currentTick);

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Updated animal cache for {this.GetType().Name}, found {animals.Count} animals");
            }
        }

        /// <summary>
        /// Whether this handler can work with wild animals (e.g., taming)
        /// </summary>
        protected virtual bool CanHandleWildAnimals()
        {
            return false;
        }

        #endregion

        #region Job Processing

        /// <summary>
        /// Processes cached targets to find a valid job
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            if (pawn == null || targets == null || targets.Count == 0)
                return null;

            // Filter valid animals for this specific handler
            List<Pawn> validAnimals = new List<Pawn>();

            foreach (Thing t in targets)
            {
                if (t is Pawn animal && IsValidAnimalTarget(animal, pawn) && CanHandleAnimal(animal, pawn))
                {
                    validAnimals.Add(animal);
                }
            }

            if (validAnimals.Count == 0)
                return null;

            // Sort animals by distance using distance buckets
            return FindBestAnimalJob(pawn, validAnimals, forced);
        }

        /// <summary>
        /// Find the best animal target and create a job for it
        /// </summary>
        protected virtual Job FindBestAnimalJob(Pawn pawn, List<Pawn> animals, bool forced)
        {
            if (pawn?.Map == null || animals == null || animals.Count == 0)
                return null;

            // Create distance buckets for more efficient selection
            List<Pawn>[] buckets = new List<Pawn>[DistanceThresholds.Length + 1];
            for (int i = 0; i < buckets.Length; i++)
            {
                buckets[i] = new List<Pawn>();
            }

            // Sort animals into buckets by distance
            foreach (Pawn animal in animals)
            {
                float distanceSq = (animal.Position - pawn.Position).LengthHorizontalSquared;

                int bucketIndex = 0;
                while (bucketIndex < DistanceThresholds.Length && distanceSq > DistanceThresholds[bucketIndex])
                {
                    bucketIndex++;
                }

                buckets[bucketIndex].Add(animal);
            }

            // Find the closest valid animal
            for (int b = 0; b < buckets.Length; b++)
            {
                if (buckets[b].Count == 0)
                    continue;

                // Randomize within each bucket for even distribution
                buckets[b].Shuffle();

                foreach (Pawn animal in buckets[b])
                {
                    // Try to create a job for this animal
                    Job job = CreateJobForAnimal(pawn, animal, forced);
                    if (job != null)
                        return job;
                }
            }

            return null;
        }

        /// <summary>
        /// Creates a job for a specific animal - should be overridden by derived classes
        /// </summary>
        protected virtual Job CreateJobForAnimal(Pawn pawn, Pawn animal, bool forced)
        {
            // Base implementation - should be overridden by derived classes
            return null;
        }

        #endregion

        #region Utility

        /// <summary>
        /// Specialized animal handling methods - should be overridden by derived classes
        /// </summary>
        protected virtual bool IsValidAnimalTarget(Pawn animal, Pawn handler)
        {
            return animal != null &&
                   animal.RaceProps.Animal &&
                   !animal.Dead &&
                   animal.Spawned &&
                   animal.Map == handler.Map;
        }

        /// <summary>
        /// Check if a pawn can handle a specific animal - should be overridden by derived classes
        /// </summary>
        protected virtual bool CanHandleAnimal(Pawn animal, Pawn handler)
        {
            if (animal == null || handler == null)
                return false;

            // Check if the animal can be reached by the handler
            if (animal.IsForbidden(handler))
                return false;

            // Check handler faction
            if (RequiresPlayerFaction && handler.Faction != Faction.OfPlayer)
                return false;

            // Check animal's position
            if (!handler.CanReach(animal, PathEndMode.Touch, Danger.Deadly))
                return false;

            return true;
        }

        #endregion

        #region Reset

        /// <summary>
        /// Reset the cache - implements IResettableCache
        /// </summary>
        public override void Reset()
        {
            // Call base reset first
            base.Reset();

            // Now clear handling-specific caches for all maps
            foreach (int mapId in Find.Maps.Select(map => map.uniqueID))
            {
                string cacheKey = this.GetType().Name + ANIMAL_CACHE_SUFFIX;
                var animalCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Pawn>>(mapId);

                if (animalCache.ContainsKey(cacheKey))
                {
                    animalCache.Remove(cacheKey);
                }

                // Clear the update tick record too
                Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, -1);
            }

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Reset animal cache for {this.GetType().Name}");
            }
        }

        /// <summary>
        /// Static method to reset all handling caches
        /// </summary>
        public static void ResetHandlingCache()
        {
            // Clear all animal caches from all maps
            foreach (Map map in Find.Maps)
            {
                int mapId = map.uniqueID;

                // Clear all handling-related caches
                var animalCache = Utility_MapCacheManager.GetOrCreateMapCache<string, object>(mapId);

                // Find all keys starting with JobGiver_Handling
                var keysToRemove = animalCache.Keys
                    .Where(k => k.Contains("JobGiver_Handling") && k.EndsWith(ANIMAL_CACHE_SUFFIX))
                    .ToList();

                foreach (string key in keysToRemove)
                {
                    animalCache.Remove(key);
                    Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, key, -1);
                }
            }

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal("Reset all handling caches");
            }
        }

        #endregion

        #region Debug

        public override string ToString()
        {
            return $"JobGiver_Handling_{this.GetType().Name}";
        }

        #endregion
    }
}