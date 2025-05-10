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
    public abstract class JobGiver_Handling_GatherAnimalBodyResources_PawnControl : ThinkNode_JobGiver
    {
        // Constants
        protected const int CACHE_REFRESH_INTERVAL = 250;
        protected const int MAX_CACHE_SIZE = 1000;

        // Cache for animals that have gatherable resources
        protected static Dictionary<int, List<Pawn>> _gatherableAnimalsCache = new Dictionary<int, List<Pawn>>();
        protected static Dictionary<int, Dictionary<Pawn, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        protected static int _lastCacheUpdateTick = -999;

        // Distance thresholds for bucketing (25, 50, 100 tiles squared)
        protected static readonly float[] DISTANCE_THRESHOLDS = new float[] { 625f, 2500f, 10000f };

        /// <summary>
        /// The JobDef to use when creating jobs
        /// </summary>
        protected abstract JobDef JobDef { get; }

        /// <summary>
        /// Gets the appropriate CompHasGatherableBodyResource component from the animal
        /// </summary>
        protected abstract CompHasGatherableBodyResource GetComp(Pawn animal);

        public override float GetPriority(Pawn pawn)
        {
            // Gathering has moderate priority among work tasks
            return 5.2f;
        }

        /// <summary>
        /// Updates the cache of animals that have gatherable resources
        /// </summary>
        protected void UpdateGatherableAnimalsCacheSafely(Map map, Faction faction)
        {
            if (map == null || faction == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_REFRESH_INTERVAL ||
                !_gatherableAnimalsCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_gatherableAnimalsCache.ContainsKey(mapId))
                    _gatherableAnimalsCache[mapId].Clear();
                else
                    _gatherableAnimalsCache[mapId] = new List<Pawn>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Pawn, bool>();

                // Get animals from the faction that have gatherable resources
                foreach (Pawn animal in map.mapPawns.SpawnedPawnsInFaction(faction))
                {
                    if (animal != null && animal.IsNonMutantAnimal)
                    {
                        CompHasGatherableBodyResource comp = GetComp(animal);
                        if (comp != null && comp.ActiveAndFull && !animal.Downed &&
                            animal.CanCasuallyInteractNow() &&
                            (animal.roping == null || !animal.roping.IsRopedByPawn))
                        {
                            _gatherableAnimalsCache[mapId].Add(animal);
                        }
                    }
                }

                // Limit cache size for performance
                if (_gatherableAnimalsCache[mapId].Count > MAX_CACHE_SIZE)
                {
                    _gatherableAnimalsCache[mapId] = _gatherableAnimalsCache[mapId].Take(MAX_CACHE_SIZE).ToList();
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Basic validation - only player faction pawns can gather resources
            if (pawn?.Map == null || pawn.Faction != Faction.OfPlayer)
                return null;

            return Utility_JobGiverManager.StandardTryGiveJob<Pawn>(
                pawn,
                "Handling",
                (p, forced) => {
                    // Update gatherable animals cache
                    UpdateGatherableAnimalsCacheSafely(p.Map, p.Faction);

                    // Create gathering job
                    return TryCreateGatherResourceJob(p, forced);
                },
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

        /// <summary>
        /// Creates a job for gathering resources from an animal
        /// </summary>
        protected Job TryCreateGatherResourceJob(Pawn pawn, bool forced = false)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_gatherableAnimalsCache.ContainsKey(mapId) || _gatherableAnimalsCache[mapId].Count == 0)
                return null;

            // Create distance buckets for optimized searching
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                _gatherableAnimalsCache[mapId],
                (animal) => (animal.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Find a valid animal to gather resources from
            Pawn targetAnimal = Utility_JobGiverManager.FindFirstValidTargetInBuckets<Pawn>(
                buckets,
                pawn,
                (animal, p) => {
                    // Skip if trying to gather from self
                    if (animal == p)
                        return false;

                    // Skip if no longer valid
                    if (animal.Destroyed || !animal.Spawned || animal.Map != p.Map)
                        return false;

                    // Skip if not of same faction
                    if (animal.Faction != p.Faction)
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
                    if (animal.IsForbidden(p) ||
                        !p.CanReserve((LocalTargetInfo)animal, ignoreOtherReservations: forced) ||
                        !p.CanReach((LocalTargetInfo)animal, PathEndMode.Touch, Danger.Some))
                        return false;

                    return true;
                },
                _reachabilityCache
            );

            if (targetAnimal == null)
                return null;

            // Create job if target found
            Job job = JobMaker.MakeJob(JobDef, targetAnimal);
            Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to gather resources from {targetAnimal.LabelShort}");
            return job;
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_gatherableAnimalsCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }
    }
}