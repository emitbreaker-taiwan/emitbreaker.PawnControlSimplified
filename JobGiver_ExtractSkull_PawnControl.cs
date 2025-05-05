using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Enhanced JobGiver that assigns skull extraction jobs to eligible pawns.
    /// Requires the BasicWorker work tag to be enabled.
    /// </summary>
    public class JobGiver_ExtractSkull_PawnControl : ThinkNode_JobGiver
    {
        // Cache system to improve performance with large numbers of pawns
        private static readonly Dictionary<int, List<Corpse>> _skullExtractableCache = new Dictionary<int, List<Corpse>>();
        private static readonly Dictionary<int, Dictionary<Corpse, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Corpse, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 300; // Update every 5 seconds

        // Local optimization parameters
        private const int MAX_CACHE_ENTRIES = 100;  // Cap cache size to avoid memory issues

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 400f, 1600f, 2500f }; // 20, 40, 50 tiles

        public override float GetPriority(Pawn pawn)
        {
            // Medium priority - similar to other basic work
            return 6.0f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Make sure we can extract skulls on this game
            if (ModsConfig.IdeologyActive && !CanPawnExtractSkull(pawn))
            {
                return null;
            }

            // Quick skip check - if no skull extraction designations on map
            if (!pawn.Map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.ExtractSkull))
            {
                return null;
            }

            return Utility_JobGiverManager.StandardTryGiveJob<Plant>(
                pawn,
                "BasicWorker",
                (p, forced) => {
                    // Update plant cache
                    Utility_CacheManager.UpdateDesignationBasedCache(
                    p.Map,
                    ref _lastCacheUpdateTick,
                    CACHE_UPDATE_INTERVAL,
                    _skullExtractableCache,
                    _reachabilityCache,
                    DesignationDefOf.ExtractSkull,
                    (des) => {
                        if (des?.target.Thing is Corpse corpse &&
                            corpse.Spawned &&
                            corpse.InnerPawn.health.hediffSet.HasHead)
                            return corpse;
                        return null;
                    },
                    MAX_CACHE_ENTRIES);

                    // Find and create a job for cutting plants with VALID DESIGNATORS ONLY
                    return Utility_JobGiverManager.TryCreateDesignatedJob(
                            pawn,
                            _skullExtractableCache,
                            _reachabilityCache,
                            "BasicWorker",
                            DesignationDefOf.ExtractSkull,
                            JobDefOf.ExtractSkull,
                            extraValidation: (corpse) => corpse is Corpse c && c.InnerPawn.health.hediffSet.HasHead,
                            reachabilityFunc: (corpse, q) => !corpse.IsForbidden(q) &&
                                                             q.CanReserveAndReach(corpse, PathEndMode.OnCell, Danger.Deadly),
                            distanceThresholds: DISTANCE_THRESHOLDS);
                },
                debugJobDesc: "firefighting assignment");
        }

        /// <summary>
        /// Checks if the pawn can extract skulls based on ideology requirements
        /// </summary>
        private bool CanPawnExtractSkull(Pawn pawn)
        {
            // Non-ideology games can always extract skulls
            if (!ModsConfig.IdeologyActive)
                return true;

            // Default to player's ideology
            return CanPlayerExtractSkull();
        }

        /// <summary>
        /// Checks if player factions can extract skulls based on ideological requirements
        /// Direct port of WorkGiver_ExtractSkull.CanPlayerExtractSkull
        /// </summary>
        public bool CanPlayerExtractSkull()
        {
            if (Find.IdeoManager.classicMode || CanExtractSkull(Faction.OfPlayer.ideos.PrimaryIdeo))
                return true;

            foreach (Ideo ideo in Faction.OfPlayer.ideos.IdeosMinorListForReading)
            {
                if (CanExtractSkull(ideo))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Checks if a specific ideology allows skull extraction
        /// Direct port of WorkGiver_ExtractSkull.CanExtractSkull
        /// </summary>
        public static bool CanExtractSkull(Ideo ideo)
        {
            if (ideo.classicMode || ideo.HasPrecept(PreceptDefOf.Skullspike_Desired))
                return true;

            return ModsConfig.AnomalyActive && ResearchProjectDefOf.AdvancedPsychicRituals.IsFinished;
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_skullExtractableCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_ExtractSkull_PawnControl";
        }
    }
}