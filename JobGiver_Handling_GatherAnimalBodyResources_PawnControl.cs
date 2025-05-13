using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Abstract JobGiver for gathering resources from animals (milk, wool, etc.).
    /// Uses the Handler work tag for eligibility checking.
    /// </summary>
    public abstract class JobGiver_Handling_GatherAnimalBodyResources_PawnControl : JobGiver_Handling_PawnControl
    {
        #region Configuration

        public override string WorkTag => "Handling";

        protected override int CacheUpdateInterval => 250;

        // Distance thresholds for bucketing (25, 50, 100 tiles squared)
        protected override float[] DistanceThresholds => new float[] { 625f, 2500f, 10000f };

        // Cache key suffix for gatherable animals
        private const string GATHERABLE_CACHE_SUFFIX = "_GatherableAnimals";

        /// <summary>
        /// The JobDef to use when creating jobs
        /// </summary>
        protected override JobDef WorkJobDef { get; }

        #endregion

        #region Target Selection

        /// <summary>
        /// Gets the appropriate CompHasGatherableBodyResource component from the animal
        /// </summary>
        protected abstract CompHasGatherableBodyResource GetComp(Pawn animal);

        /// <summary>
        /// Get animals that have gatherable resources
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null) yield break;

            Faction playerFaction = Faction.OfPlayer;
            if (playerFaction == null) yield break;

            // Get cached gatherable animals
            var gatherableAnimals = GetOrCreateGatherableAnimalsCache(map);

            // Return animals as targets
            foreach (Pawn animal in gatherableAnimals)
            {
                if (animal != null && !animal.Dead && animal.Spawned)
                    yield return animal;
            }
        }

        /// <summary>
        /// Job-specific cache update method - finds all animals with gatherable resources
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            if (map == null)
                yield break;

            Faction playerFaction = Faction.OfPlayer;
            if (playerFaction == null)
                yield break;

            // Find all animals with gatherable resources
            List<Pawn> gatherableAnimals = new List<Pawn>();

            foreach (Pawn animal in map.mapPawns.SpawnedPawnsInFaction(playerFaction))
            {
                if (animal != null && animal.IsNonMutantAnimal)
                {
                    CompHasGatherableBodyResource comp = GetComp(animal);
                    if (comp != null && comp.ActiveAndFull && !animal.Downed &&
                        animal.CanCasuallyInteractNow() &&
                        (animal.roping == null || !animal.roping.IsRopedByPawn))
                    {
                        gatherableAnimals.Add(animal);
                    }
                }
            }

            // Store in the centralized cache
            StoreGatherableAnimalsCache(map, gatherableAnimals);

            // Convert to Things for the base class
            foreach (Pawn animal in gatherableAnimals)
            {
                yield return animal;
            }
        }

        /// <summary>
        /// Gets or creates a cache of animals with gatherable resources for a specific map
        /// </summary>
        protected List<Pawn> GetOrCreateGatherableAnimalsCache(Map map)
        {
            if (map == null)
                return new List<Pawn>();

            int mapId = map.uniqueID;
            string cacheKey = this.GetType().Name + GATHERABLE_CACHE_SUFFIX;

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
                Faction playerFaction = Faction.OfPlayer;

                if (playerFaction != null)
                {
                    foreach (Pawn animal in map.mapPawns.SpawnedPawnsInFaction(playerFaction))
                    {
                        if (animal != null && animal.IsNonMutantAnimal)
                        {
                            CompHasGatherableBodyResource comp = GetComp(animal);
                            if (comp != null && comp.ActiveAndFull && !animal.Downed &&
                                animal.CanCasuallyInteractNow() &&
                                (animal.roping == null || !animal.roping.IsRopedByPawn))
                            {
                                animals.Add(animal);
                            }
                        }
                    }
                }

                // Store in the central cache
                StoreGatherableAnimalsCache(map, animals);
            }

            return animals;
        }

        /// <summary>
        /// Store a list of gatherable animals in the centralized cache
        /// </summary>
        private void StoreGatherableAnimalsCache(Map map, List<Pawn> animals)
        {
            if (map == null)
                return;

            int mapId = map.uniqueID;
            string cacheKey = this.GetType().Name + GATHERABLE_CACHE_SUFFIX;
            int currentTick = Find.TickManager.TicksGame;

            var animalCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Pawn>>(mapId);
            animalCache[cacheKey] = animals;
            Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, currentTick);

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Updated gatherable animals cache for {this.GetType().Name}, found {animals.Count} animals");
            }
        }

        #endregion

        #region Job Processing

        /// <summary>
        /// Processes cached targets to find a valid job for gathering resources
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            if (pawn?.Map == null || targets.Count == 0)
                return null;

            // Create distance buckets for optimized searching
            var animalTargets = targets.Cast<Pawn>().Where(a => a != null && a.Spawned && !a.Dead).ToList();

            // Use the centralized map cache for reachability
            int mapId = pawn.Map.uniqueID;
            string cacheKey = this.GetType().Name + "_Reachability";

            var reachabilityCache = Utility_MapCacheManager.GetOrCreateMapCache<Pawn, bool>(mapId);

            // Find a valid animal to gather resources from using the parent class method
            Pawn targetAnimal = FindBestGatherableAnimal(pawn, animalTargets, forced);

            if (targetAnimal == null)
                return null;

            // Create job if target found
            Job job = JobMaker.MakeJob(WorkJobDef, targetAnimal);

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to gather resources from {targetAnimal.LabelShort}");
            }

            return job;
        }

        /// <summary>
        /// Find the best animal to gather resources from
        /// </summary>
        protected virtual Pawn FindBestGatherableAnimal(Pawn pawn, List<Pawn> animals, bool forced)
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

            // Get the map-specific reachability cache
            int mapId = pawn.Map.uniqueID;
            string cacheKey = this.GetType().Name + "_Reachability";

            var reachabilityCache = Utility_MapCacheManager.GetOrCreateMapCache<Pawn, bool>(mapId);

            // Find the closest valid animal
            for (int b = 0; b < buckets.Length; b++)
            {
                if (buckets[b].Count == 0)
                    continue;

                // Randomize within each bucket for even distribution
                buckets[b].Shuffle();

                foreach (Pawn animal in buckets[b])
                {
                    // Check if this animal is valid
                    if (IsValidGatherableAnimal(animal, pawn, forced))
                    {
                        return animal;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Check if an animal is valid for gathering resources
        /// </summary>
        protected virtual bool IsValidGatherableAnimal(Pawn animal, Pawn handler, bool forced)
        {
            // Skip if trying to gather from self
            if (animal == handler)
                return false;

            // Skip if no longer valid
            if (animal.Destroyed || !animal.Spawned || animal.Map != handler.Map)
                return false;

            // Skip if not of same faction
            if (animal.Faction != handler.Faction)
                return false;

            // Skip if not an animal or doesn't have gatherable resources
            CompHasGatherableBodyResource comp = GetComp(animal);
            if (!animal.IsNonMutantAnimal || comp == null || !comp.ActiveAndFull)
                return false;

            // Skip if downed, roped, or can't interact
            if (animal.Downed || !animal.CanCasuallyInteractNow() ||
                (animal.roping != null && animal.roping.IsRopedByPawn))
                return false;

            // Skip if forbidden or unreachable
            if (animal.IsForbidden(handler) ||
                !handler.CanReserve((LocalTargetInfo)animal, ignoreOtherReservations: forced) ||
                !handler.CanReach((LocalTargetInfo)animal, PathEndMode.Touch, Danger.Some))
                return false;

            return true;
        }

        /// <summary>
        /// Override TryGiveJob to handle special case for animal gathering
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            // Basic validation - only player faction pawns can gather resources
            if (pawn?.Map == null || pawn.Faction != Faction.OfPlayer)
                return null;

            return Utility_JobGiverManager.StandardTryGiveJob<Pawn>(
                pawn,
                WorkTag,
                (p, forced) => base.TryGiveJob(p),
                (p, setFailReason) => {
                    // Additional check for animals work tag
                    if (p.WorkTagIsDisabled(WorkTags.Animals))
                    {
                        if (setFailReason)
                            JobFailReason.Is("CannotPrioritizeWorkTypeDisabled".Translate(WorkTypeDefOf.Handling.gerundLabel).CapitalizeFirst());
                        return false;
                    }
                    return true;
                },
                debugJobDesc: "animal resource gathering");
        }

        #endregion

        #region Reset

        /// <summary>
        /// Reset the cache - extends base Reset implementation
        /// </summary>
        public override void Reset()
        {
            // Call base reset first to handle animal caches
            base.Reset();

            // Now clear gathering-specific caches
            foreach (int mapId in Find.Maps.Select(map => map.uniqueID))
            {
                string cacheKey = this.GetType().Name + GATHERABLE_CACHE_SUFFIX;
                var animalCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Pawn>>(mapId);

                if (animalCache.ContainsKey(cacheKey))
                {
                    animalCache.Remove(cacheKey);
                }

                // Clear the reachability cache too
                string reachabilityKey = this.GetType().Name + "_Reachability";
                var reachCache = Utility_MapCacheManager.GetOrCreateMapCache<Pawn, bool>(mapId);
                // Just clear the entire cache since we can't easily filter it
                reachCache.Clear();

                // Clear the update tick records
                Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, -1);
                Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, reachabilityKey, -1);
            }

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Reset gatherable animals caches for {this.GetType().Name}");
            }
        }

        /// <summary>
        /// Reset reachability cache (replace the static one)
        /// </summary>
        public static void ResetReachabilityCache()
        {
            // Clear all reachability caches from all maps
            foreach (Map map in Find.Maps)
            {
                int mapId = map.uniqueID;

                // Find and clear any reachability caches
                foreach (Type type in typeof(JobGiver_Handling_GatherAnimalBodyResources_PawnControl).AllSubclasses())
                {
                    string reachabilityKey = type.Name + "_Reachability";
                    var reachCache = Utility_MapCacheManager.GetOrCreateMapCache<Pawn, bool>(mapId);
                    // Just clear the entire cache since we can't easily filter it
                    reachCache.Clear();

                    // Reset the update tick
                    Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, reachabilityKey, -1);
                }
            }

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal("Reset all resource gathering reachability caches");
            }
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return this.GetType().Name;
        }

        #endregion
    }
}