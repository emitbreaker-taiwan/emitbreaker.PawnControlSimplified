using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns execution jobs to eligible wardens.
    /// Optimized to use the PawnControl framework for better performance.
    /// </summary>
    public class JobGiver_Warden_ExecuteGuilty_PawnControl : ThinkNode_JobGiver
    {
        // Cache for guilty pawns to improve performance
        private static readonly Dictionary<int, List<Pawn>> _guiltyPawnCache = new Dictionary<int, List<Pawn>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 300; // Update every 5 seconds

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 100f, 400f, 900f }; // 10, 20, 30 tiles

        public override float GetPriority(Pawn pawn)
        {
            // Execution is important but not an emergency
            return 6.5f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<Pawn>(
                pawn,
                "Warden",
                (p, forced) => {
                    // Update guilty pawn cache
                    UpdateGuiltyPawnCache(p.Map);

                    // Find and create execution job
                    return TryCreateExecutionJob(p);
                },
                // Additional check for violent capability
                (p, setFailReason) => {
                    if (p.WorkTagIsDisabled(WorkTags.Violent))
                    {
                        // We don't set any explicit JobFailReason here
                        return false;
                    }
                    return true;
                },
                "guilty execution");
        }

        /// <summary>
        /// Updates the cache of guilty pawns awaiting execution
        /// </summary>
        private void UpdateGuiltyPawnCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            // Only update periodically to save performance
            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_guiltyPawnCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_guiltyPawnCache.ContainsKey(mapId))
                    _guiltyPawnCache[mapId].Clear();
                else
                    _guiltyPawnCache[mapId] = new List<Pawn>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Pawn, bool>();

                // Find all guilty colonists
                foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
                {
                    if (pawn?.guilt != null && pawn.guilt.IsGuilty && pawn.guilt.awaitingExecution &&
                        !pawn.InAggroMentalState && !pawn.IsFormingCaravan())
                    {
                        _guiltyPawnCache[mapId].Add(pawn);
                    }
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Create an execution job using distance-based bucketing
        /// </summary>
        private Job TryCreateExecutionJob(Pawn pawn)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_guiltyPawnCache.ContainsKey(mapId) || _guiltyPawnCache[mapId].Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing and target selection
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                _guiltyPawnCache[mapId],
                (target) => (target.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Validation function for guilty pawns
            Pawn targetPawn = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (target, p) =>
                {
                    return !target.IsForbidden(p) &&
                           target?.guilt != null &&
                           target.guilt.IsGuilty &&
                           target.guilt.awaitingExecution &&
                           !target.InAggroMentalState &&
                           !target.IsFormingCaravan() &&
                           p.CanReserveAndReach(target, PathEndMode.Touch, p.NormalMaxDanger()) &&
                           new HistoryEvent(HistoryEventDefOf.ExecutedColonist, p.Named(HistoryEventArgsNames.Doer)).Notify_PawnAboutToDo_Job();
                },
                _reachabilityCache
            );

            // Create job if target found
            if (targetPawn != null)
            {
                Job job = JobMaker.MakeJob(JobDefOf.GuiltyColonistExecution, targetPawn);

                if (Prefs.DevMode)
                {
                    Log.Message($"[PawnControl] {pawn.LabelShort} created job to execute guilty colonist {targetPawn.LabelShort}");
                }

                return job;
            }

            return null;
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_guiltyPawnCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_Warden_ExecuteGuilty_PawnControl";
        }
    }
}