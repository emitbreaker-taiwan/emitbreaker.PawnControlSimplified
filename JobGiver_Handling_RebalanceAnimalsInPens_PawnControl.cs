using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver specialized for handling rebalance animals in pens specifically.
    /// Uses the Handling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Handling_RebalanceAnimalsInPens_PawnControl : JobGiver_Handling_TakeToPen_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "RebalanceAnimalsInPens";

        /// <summary>
        /// Cache key suffix specifically for rebalancing-eligible animals
        /// </summary>
        private const string REBALANCE_ANIMALS_CACHE_SUFFIX = "_RebalanceAnimals";

        public JobGiver_Handling_RebalanceAnimalsInPens_PawnControl()
        {
            // Use balanced priority for pen distribution
            this.ropingPriority = RopingPriority.Balanced;
        }

        /// <summary>
        /// Define the base priority for this job
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            // Slightly lower priority than regular animal handling
            return 5.5f;
        }

        #endregion

        #region Caching

        // Cache for animals that need rebalancing
        private readonly Dictionary<Map, HashSet<Pawn>> _rebalanceCandidatesCached = new Dictionary<Map, HashSet<Pawn>>();
        private int _rebalanceCandidatesCachedTick = -999;

        #endregion

        #region Core flow

        /// <summary>
        /// Override TryGiveJob to implement specialized balanced animal distribution
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<Pawn>(
                pawn,
                WorkTag,
                (p, forced) => {
                    // Update rebalance candidates cache if needed
                    RefreshRebalanceCandidatesCache(p);

                    // Use base class implementation but directly return the Job object
                    return base.TryGiveJob(p);
                },
                debugJobDesc: "rebalance animals in pens");
        }

        /// <summary>
        /// Refreshes the cache of animals that need rebalancing
        /// </summary>
        private void RefreshRebalanceCandidatesCache(Pawn pawn)
        {
            if (pawn?.Map == null)
                return;

            int currentTick = Find.TickManager.TicksGame;
            if (currentTick - _rebalanceCandidatesCachedTick < CacheUpdateInterval)
                return;

            // Mark cache as updated
            _rebalanceCandidatesCachedTick = currentTick;

            // Get or create the set for this map
            if (!_rebalanceCandidatesCached.TryGetValue(pawn.Map, out HashSet<Pawn> candidates))
            {
                candidates = new HashSet<Pawn>();
                _rebalanceCandidatesCached[pawn.Map] = candidates;
            }

            // Clear the previous cache
            candidates.Clear();

            // Store in the centralized cache system
            StoreRebalanceCandidatesCache(pawn.Map, candidates);
        }

        #endregion

        #region Target Selection

        /// <summary>
        /// Get animals that need to be rebalanced between pens on the given map
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null)
                yield break;

            // Get cached rebalance animals from centralized cache
            var animals = GetOrCreateRebalanceCandidatesCache(map);

            // Return animals as targets
            foreach (Pawn animal in animals)
            {
                if (animal != null && !animal.Dead && animal.Spawned)
                    yield return animal;
            }
        }

        /// <summary>
        /// Update the job-specific cache with animals that need to be rebalanced
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            if (map == null)
                yield break;

            // Find all animals that can be rebalanced between pens
            List<Pawn> rebalanceAnimals = new List<Pawn>();

            // Add player faction animals that can be rebalanced
            foreach (Pawn animal in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
            {
                // Skip if not an animal
                if (!animal.IsNonMutantAnimal)
                    continue;

                // Skip animals with mental states
                if (animal.MentalStateDef != null)
                    continue;

                // Skip animals marked for release to wild
                if (map.designationManager.DesignationOn(animal, DesignationDefOf.ReleaseAnimalToWild) != null)
                    continue;

                // Add this animal to the list
                rebalanceAnimals.Add(animal);
            }

            // Store in the centralized cache
            StoreRebalanceCandidatesCache(map, rebalanceAnimals);

            // Convert to Things for the base class caching system
            foreach (Pawn animal in rebalanceAnimals)
            {
                yield return animal;
            }
        }

        /// <summary>
        /// Gets or creates a cache of animals that need to be rebalanced for a specific map
        /// </summary>
        protected List<Pawn> GetOrCreateRebalanceCandidatesCache(Map map)
        {
            if (map == null)
                return new List<Pawn>();

            int mapId = map.uniqueID;
            string cacheKey = this.GetType().Name + REBALANCE_ANIMALS_CACHE_SUFFIX;

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

                // Add player faction animals that can be rebalanced
                foreach (Pawn animal in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
                {
                    // Skip if not an animal
                    if (!animal.IsNonMutantAnimal)
                        continue;

                    // Skip animals with mental states
                    if (animal.MentalStateDef != null)
                        continue;

                    // Skip animals marked for release to wild
                    if (map.designationManager.DesignationOn(animal, DesignationDefOf.ReleaseAnimalToWild) != null)
                        continue;

                    // Add this animal to the list
                    animals.Add(animal);
                }

                // Store in the central cache
                StoreRebalanceCandidatesCache(map, animals);
            }

            return animals;
        }

        /// <summary>
        /// Store a list of rebalance-eligible animals in the centralized cache
        /// </summary>
        private void StoreRebalanceCandidatesCache(Map map, IEnumerable<Pawn> animals)
        {
            if (map == null)
                return;

            int mapId = map.uniqueID;
            string cacheKey = this.GetType().Name + REBALANCE_ANIMALS_CACHE_SUFFIX;
            int currentTick = Find.TickManager.TicksGame;

            // Convert IEnumerable to List if needed
            List<Pawn> animalsList = animals as List<Pawn> ?? animals.ToList();

            var animalCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Pawn>>(mapId);
            animalCache[cacheKey] = animalsList;
            Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, currentTick);

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Updated rebalance animals cache for {this.GetType().Name}, found {animalsList.Count} animals");
            }
        }

        #endregion

        #region Job Processing

        /// <summary>
        /// Processes cached targets to find a valid job
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // First, check if we have any valid targets
            if (targets == null || targets.Count == 0 || pawn?.Map == null)
                return null;

            // Use the base class implementation, which already handles animal pen balancing
            // The only difference is we use RopingPriority.Balanced set in the constructor
            return base.ProcessCachedTargets(pawn, targets, forced);
        }

        /// <summary>
        /// Implements the animal-specific validity check
        /// </summary>
        protected override bool IsValidAnimalTarget(Pawn animal, Pawn handler)
        {
            // Use base class validity checking, but add additional rebalancing-specific checks
            if (!base.IsValidAnimalTarget(animal, handler))
                return false;

            // Additional checks for rebalancing could be added here
            // For now, we'll use the base implementation since rebalancing
            // works with the same animals as regular pen assignments

            return true;
        }

        /// <summary>
        /// Determines if an animal needs to be rebalanced between pens
        /// </summary>
        private bool NeedsRebalancing(Pawn animal, Pawn handler, bool forced, AnimalPenBalanceCalculator balanceCalculator)
        {
            // We can leverage the base class's NeedsPenHandling method,
            // since we're using RopingPriority.Balanced in the constructor
            // which causes the balancing check to happen in AnimalPenUtility.GetPenAnimalShouldBeTakenTo()

            // This would be called by the base class, so no need to implement here directly
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

            // Clear the rebalance candidates cache
            _rebalanceCandidatesCached.Clear();
            _rebalanceCandidatesCachedTick = -999;

            // Now clear rebalance-specific caches
            foreach (int mapId in Find.Maps.Select(map => map.uniqueID))
            {
                string cacheKey = this.GetType().Name + REBALANCE_ANIMALS_CACHE_SUFFIX;
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
                Utility_DebugManager.LogNormal($"Reset rebalance animals cache for {this.GetType().Name}");
            }
        }

        #endregion

        #region Debug

        public override string ToString()
        {
            return "JobGiver_Handling_RebalanceAnimalsInPens_PawnControl";
        }

        #endregion
    }
}