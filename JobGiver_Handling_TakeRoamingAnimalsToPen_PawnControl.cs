using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver specialized for handling roaming animals specifically.
    /// Uses the Handling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Handling_TakeRoamingAnimalsToPen_PawnControl : JobGiver_Handling_TakeToPen_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "TakeRoamingToPen";

        /// <summary>
        /// Constructor to set specialized configuration for roaming animals
        /// </summary>
        public JobGiver_Handling_TakeRoamingAnimalsToPen_PawnControl()
        {
            this.targetRoamingAnimals = true;  // Specifically target roaming animals
            this.allowUnenclosedPens = true;   // Allow taking them to unenclosed pens
            this.ropingPriority = RopingPriority.Closest;  // Use additional priority
        }

        /// <summary>
        /// Cache key suffix specifically for roaming animals
        /// </summary>
        private const string ROAMING_ANIMALS_CACHE_SUFFIX = "_RoamingAnimals";

        /// <summary>
        /// Define the base priority for this job - higher than regular animal handling
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            // Higher priority than regular animal handling
            return 6.0f;
        }

        #endregion

        #region Caching

        // Cache for roaming animals - additional to parent caches
        private readonly Dictionary<Map, List<Pawn>> _roamingAnimalsCached = new Dictionary<Map, List<Pawn>>();
        private int _roamingAnimalsCachedTick = -999;

        #endregion

        #region Core flow

        /// <summary>
        /// Override TryGiveJob to implement specialized roaming animal handling
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (ShouldSkip(pawn))
            {
                return null;
            }

            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Handling_TakeRoamingAnimalsToPen_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) => {
                    // Refresh roaming animals cache if needed
                    RefreshRoamingAnimalsCache(p);

                    // Use parent class implementation
                    return base.TryGiveJob(p);
                },
                debugJobDesc: "take roaming animal to pen");
        }

        /// <summary>
        /// Refresh the cache of roaming animals
        /// </summary>
        private void RefreshRoamingAnimalsCache(Pawn pawn)
        {
            if (pawn?.Map == null)
                return;

            int currentTick = Find.TickManager.TicksGame;
            if (currentTick - _roamingAnimalsCachedTick < CacheUpdateInterval)
                return;

            // Mark cache as updated
            _roamingAnimalsCachedTick = currentTick;

            // Get or create the cache for this map
            if (!_roamingAnimalsCached.TryGetValue(pawn.Map, out List<Pawn> roamingAnimals))
            {
                roamingAnimals = new List<Pawn>();
                _roamingAnimalsCached[pawn.Map] = roamingAnimals;
            }

            // Clear the previous cache
            roamingAnimals.Clear();

            // Add all roaming animals
            foreach (Pawn animal in pawn.Map.mapPawns.AllPawnsSpawned)
            {
                if (animal.RaceProps?.Animal == true &&
                    animal.Faction == Faction.OfPlayer &&
                    animal.MentalStateDef == MentalStateDefOf.Roaming)
                {
                    roamingAnimals.Add(animal);
                }
            }

            // Store in centralized cache
            StoreRoamingAnimalsCache(pawn.Map, roamingAnimals);
        }

        #endregion

        #region Target Selection

        /// <summary>
        /// Get roaming animals that need to be taken to pens on the given map
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null)
                yield break;

            // Get cached roaming animals from centralized cache
            var animals = GetOrCreateRoamingAnimalsCache(map);

            // Return animals as targets
            foreach (Pawn animal in animals)
            {
                if (animal != null && !animal.Dead && animal.Spawned)
                    yield return animal;
            }
        }

        /// <summary>
        /// Update the job-specific cache with roaming animals that need to be taken to pens
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            if (map == null)
                yield break;

            // Find all roaming animals
            List<Pawn> roamingAnimals = new List<Pawn>();

            // Add player faction animals that are roaming
            foreach (Pawn animal in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
            {
                // Skip if not an animal
                if (!animal.IsNonMutantAnimal)
                    continue;

                // We only want roaming animals
                if (animal.MentalStateDef != MentalStateDefOf.Roaming)
                    continue;

                // Skip animals marked for release to wild
                if (map.designationManager.DesignationOn(animal, DesignationDefOf.ReleaseAnimalToWild) != null)
                    continue;

                // Add this roaming animal to the list
                roamingAnimals.Add(animal);
            }

            // Store in the centralized cache
            StoreRoamingAnimalsCache(map, roamingAnimals);

            // Convert to Things for the base class caching system
            foreach (Pawn animal in roamingAnimals)
            {
                yield return animal;
            }
        }

        /// <summary>
        /// Gets or creates a cache of roaming animals for a specific map
        /// </summary>
        protected List<Pawn> GetOrCreateRoamingAnimalsCache(Map map)
        {
            if (map == null)
                return new List<Pawn>();

            int mapId = map.uniqueID;
            string cacheKey = this.GetType().Name + ROAMING_ANIMALS_CACHE_SUFFIX;

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

                // Add player faction animals that are roaming
                foreach (Pawn animal in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
                {
                    // Skip if not an animal
                    if (!animal.IsNonMutantAnimal)
                        continue;

                    // We only want roaming animals
                    if (animal.MentalStateDef != MentalStateDefOf.Roaming)
                        continue;

                    // Skip animals marked for release to wild
                    if (map.designationManager.DesignationOn(animal, DesignationDefOf.ReleaseAnimalToWild) != null)
                        continue;

                    // Add this roaming animal to the list
                    animals.Add(animal);
                }

                // Store in the central cache
                StoreRoamingAnimalsCache(map, animals);
            }

            return animals;
        }

        /// <summary>
        /// Store a list of roaming animals in the centralized cache
        /// </summary>
        private void StoreRoamingAnimalsCache(Map map, List<Pawn> animals)
        {
            if (map == null)
                return;

            int mapId = map.uniqueID;
            string cacheKey = this.GetType().Name + ROAMING_ANIMALS_CACHE_SUFFIX;
            int currentTick = Find.TickManager.TicksGame;

            var animalCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Pawn>>(mapId);
            animalCache[cacheKey] = animals;
            Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, currentTick);

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Updated roaming animals cache for {this.GetType().Name}, found {animals.Count} animals");
            }
        }

        #endregion

        #region Job Processing

        /// <summary>
        /// Implements the animal-specific validity check with focus on roaming animals
        /// </summary>
        protected override bool IsValidAnimalTarget(Pawn animal, Pawn handler)
        {
            // First check the basic validity using parent method
            if (!base.IsValidAnimalTarget(animal, handler))
                return false;

            // Additional requirement: animal must be roaming
            if (animal.MentalStateDef != MentalStateDefOf.Roaming)
                return false;

            return true;
        }

        /// <summary>
        /// Process targets specifically for roaming animals
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // Filter to keep only roaming animals
            if (targets != null && targets.Count > 0)
            {
                targets = targets
                    .OfType<Pawn>()
                    .Where(p => p.MentalStateDef == MentalStateDefOf.Roaming)
                    .Cast<Thing>()
                    .ToList();
            }

            // Use parent implementation with the filtered targets
            return base.ProcessCachedTargets(pawn, targets, forced);
        }

        #endregion

        #region Reset

        /// <summary>
        /// Reset the cache - implements IResettableCache
        /// </summary>
        public override void Reset()
        {
            // Call parent reset first
            base.Reset();

            // Clear the roaming animals cache
            _roamingAnimalsCached.Clear();
            _roamingAnimalsCachedTick = -999;

            // Now clear roaming-specific caches
            foreach (int mapId in Find.Maps.Select(map => map.uniqueID))
            {
                string cacheKey = this.GetType().Name + ROAMING_ANIMALS_CACHE_SUFFIX;
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
                Utility_DebugManager.LogNormal($"Reset roaming animals cache for {this.GetType().Name}");
            }
        }

        #endregion

        #region Debug

        public override string ToString()
        {
            return "JobGiver_Handling_TakeRoamingAnimalsToPen_PawnControl";
        }

        #endregion
    }
}