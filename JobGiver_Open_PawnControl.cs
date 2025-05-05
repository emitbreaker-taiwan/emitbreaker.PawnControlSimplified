using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Enhanced JobGiver that assigns container opening jobs to eligible pawns.
    /// Requires the BasicWorker work tag to be enabled.
    /// </summary>
    public class JobGiver_Open_PawnControl : ThinkNode_JobGiver
    {
        // Cache system to improve performance with large numbers of pawns
        private static readonly Dictionary<int, List<Thing>> _openableCache = new Dictionary<int, List<Thing>>();
        private static readonly Dictionary<int, Dictionary<Thing, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Thing, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 300; // Update every 5 seconds

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 400f, 1600f, 2500f }; // 20, 40, 50 tiles

        public override float GetPriority(Pawn pawn)
        {
            // Medium priority - similar to flicking
            return 6.1f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Quick early exit if there are no strip designations
            if (!pawn.Map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.Open))
            {
                return null;
            }

            return Utility_JobGiverManager.StandardTryGiveJob<Plant>(
                pawn,
                "BasicWorker",
                (p, forced) => {
                    // Update plant cache
                    Utility_CacheManager.UpdateDesignationBasedCache(
                        pawn.Map,
                        ref _lastCacheUpdateTick,
                        CACHE_UPDATE_INTERVAL,
                        _openableCache,
                        _reachabilityCache,
                        DesignationDefOf.Open,
                        (des) => des?.target.Thing,
                        100);

                    // Find and create a job for cutting plants with VALID DESIGNATORS ONLY
                    return Utility_JobGiverManager.TryCreateDesignatedJob(
                            pawn,
                            _openableCache,
                            _reachabilityCache,
                            "BasicWorker",
                            DesignationDefOf.Open,
                            JobDefOf.Open,
                            reachabilityFunc: (thing, q) => !thing.IsForbidden(q) &&
                                                           q.CanReserveAndReach(thing, PathEndMode.ClosestTouch, Danger.Deadly),
                            distanceThresholds: DISTANCE_THRESHOLDS);
                },
                debugJobDesc: "open assignment");
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_openableCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_Open_PawnControl";
        }
    }
}