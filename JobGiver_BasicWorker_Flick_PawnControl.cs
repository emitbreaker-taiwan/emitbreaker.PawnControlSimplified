using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Enhanced JobGiver that assigns switch flicking jobs to eligible pawns.
    /// Requires the BasicWorker work tag to be enabled.
    /// </summary>
    public class JobGiver_BasicWorker_Flick_PawnControl : ThinkNode_JobGiver
    {
        // Cache system to improve performance with large numbers of pawns
        private static readonly Dictionary<int, List<Thing>> _flickableCache = new Dictionary<int, List<Thing>>();
        private static readonly Dictionary<int, Dictionary<Thing, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Thing, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 240; // Update every ~4 seconds for performance

        // Local optimization parameters
        private const int MAX_CACHE_ENTRIES = 100;  // Cap cache size to avoid memory issues

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 400f, 1600f, 3600f }; // 20, 40, 60 tiles

        public override float GetPriority(Pawn pawn)
        {
            // Medium priority - not as urgent as firefighting but more important than plant cutting
            return 6.2f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // IMPORTANT: Only player pawns and slaves owned by player should flick switches
            if (pawn.Faction != Faction.OfPlayer &&
                !(pawn.IsSlave && pawn.HostFaction == Faction.OfPlayer))
                return null;

            // Quick skip check - if no flick designations on map
            if (!pawn.Map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.Flick))
            {
                return null;
            }

            return Utility_JobGiverManagerOld.StandardTryGiveJob<Plant>(
                pawn,
                "BasicWorker",
                (p, forced) => {
                    // Update plant cache
                    Utility_CacheManager.UpdateDesignationBasedCache(
                        p.Map,
                        ref _lastCacheUpdateTick,
                        CACHE_UPDATE_INTERVAL,
                        _flickableCache,
                        _reachabilityCache,
                        DesignationDefOf.Flick,
                        (des) => des?.target.Thing,
                        MAX_CACHE_ENTRIES);

                    // Find and create a job for cutting plants with VALID DESIGNATORS ONLY
                    return Utility_JobGiverManagerOld.TryCreateDesignatedJob(
                            pawn,
                            _flickableCache,
                            _reachabilityCache,
                            "BasicWorker",
                            DesignationDefOf.Flick,
                            JobDefOf.Flick,
                            reachabilityFunc: (thing, q) => !thing.IsForbidden(q) &&
                                                           q.CanReserveAndReach(thing, PathEndMode.Touch, Danger.Deadly),
                            distanceThresholds: DISTANCE_THRESHOLDS);
                            },
                debugJobDesc: "flick assignment");
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_flickableCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_Flick_PawnControl";
        }
    }
}