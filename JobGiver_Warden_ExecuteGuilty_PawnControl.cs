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
                    // Update guilty pawn cache with standardized method
                    Utility_JobGiverManager.UpdatePrisonerCache(
                        p.Map,
                        ref _lastCacheUpdateTick,
                        CACHE_UPDATE_INTERVAL,
                        _guiltyPawnCache,
                        _reachabilityCache,
                        FilterGuiltyColonists);

                    // Create job using standardized method
                    return Utility_JobGiverManager.TryCreatePrisonerInteractionJob(
                        p,
                        _guiltyPawnCache,
                        _reachabilityCache,
                        ValidateCanExecuteGuilty,
                        CreateGuiltyExecutionJob,
                        DISTANCE_THRESHOLDS);
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
        /// Filter function to identify guilty colonists awaiting execution
        /// </summary>
        private bool FilterGuiltyColonists(Pawn pawn)
        {
            // Note: This is called with colonists, not prisoners
            return pawn?.guilt != null &&
                   pawn.guilt.IsGuilty &&
                   pawn.guilt.awaitingExecution &&
                   !pawn.InAggroMentalState &&
                   !pawn.IsFormingCaravan();
        }

        /// <summary>
        /// Validates if a warden can execute a guilty colonist
        /// </summary>
        private bool ValidateCanExecuteGuilty(Pawn target, Pawn executor)
        {
            // Skip if no longer valid target
            if (target?.guilt == null ||
                !target.guilt.IsGuilty ||
                !target.guilt.awaitingExecution ||
                target.InAggroMentalState ||
                target.IsFormingCaravan())
                return false;

            // Check if executor can handle this execution
            if (target.IsForbidden(executor) ||
                !executor.CanReserveAndReach(target, PathEndMode.Touch, executor.NormalMaxDanger()))
                return false;

            // Check if this action is allowed by ideology system
            return new HistoryEvent(HistoryEventDefOf.ExecutedColonist, executor.Named(HistoryEventArgsNames.Doer)).Notify_PawnAboutToDo_Job();
        }

        /// <summary>
        /// Creates the execution job for the warden
        /// </summary>
        private Job CreateGuiltyExecutionJob(Pawn executor, Pawn target)
        {
            Job job = JobMaker.MakeJob(JobDefOf.GuiltyColonistExecution, target);

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"{executor.LabelShort} created job to execute guilty colonist {target.LabelShort}");
            }

            return job;
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