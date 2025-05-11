using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns execution jobs to eligible wardens for guilty colonists.
    /// Optimized to use the PawnControl framework for better performance.
    /// </summary>
    public class JobGiver_Warden_ExecuteGuilty_PawnControl : JobGiver_Warden_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "ExecuteGuilty";

        /// <summary>
        /// Cache update interval in ticks (300 ticks = 5 seconds)
        /// </summary>
        protected override int CacheUpdateInterval => 300;

        /// <summary>
        /// Distance thresholds for bucketing (10, 20, 30 tiles)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 100f, 400f, 900f };

        #endregion

        #region Core flow

        protected override float GetBasePriority(string workTag)
        {
            // Execution is important but not an emergency
            return 6.5f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Skip if pawn is incapable of violence
            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                return null;
            }

            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Warden_ExecuteGuilty_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) => {
                    // Process cached targets to create job
                    if (p?.Map == null) return null;

                    int mapId = p.Map.uniqueID;
                    List<Thing> targets;
                    if (_prisonerCache.TryGetValue(mapId, out var guiltyList) && guiltyList != null)
                    {
                        targets = new List<Thing>(guiltyList.Cast<Thing>());
                    }
                    else
                    {
                        targets = new List<Thing>();
                    }

                    return ProcessCachedTargets(p, targets, forced);
                },
                debugJobDesc: "execute guilty colonist");
        }

        public override bool ShouldSkip(Pawn pawn)
        {
            // Use base checks first
            if (base.ShouldSkip(pawn))
                return true;

            // Cannot execute if incapable of violence
            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
                return true;

            return false;
        }

        #endregion

        #region Target processing

        /// <summary>
        /// Get all guilty colonists eligible for execution
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null) yield break;

            // Get all colonists on the map
            foreach (Pawn colonist in map.mapPawns.FreeColonistsSpawned)
            {
                if (FilterGuiltyColonists(colonist))
                {
                    yield return colonist;
                }
            }
        }

        /// <summary>
        /// Filter function to identify guilty colonists awaiting execution
        /// </summary>
        private bool FilterGuiltyColonists(Pawn colonist)
        {
            return colonist?.guilt != null &&
                   colonist.guilt.IsGuilty &&
                   colonist.guilt.awaitingExecution &&
                   !colonist.InAggroMentalState &&
                   !colonist.IsFormingCaravan();
        }

        /// <summary>
        /// Process the cached targets to create jobs
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn warden, List<Thing> targets, bool forced)
        {
            if (warden?.Map == null || targets.Count == 0)
                return null;

            int mapId = warden.Map.uniqueID;

            // Create distance buckets for optimized searching
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets<Pawn>(
                warden,
                targets.ConvertAll(t => t as Pawn),
                (colonist) => (colonist.Position - warden.Position).LengthHorizontalSquared,
                DistanceThresholds
            );

            // Find the first valid colonist to execute
            Pawn targetColonist = Utility_JobGiverManager.FindFirstValidTargetInBuckets<Pawn>(
                buckets,
                warden,
                (colonist, p) => IsValidGuiltyTarget(colonist, p),
                _prisonerReachabilityCache.TryGetValue(mapId, out var cache) ?
                    new Dictionary<int, Dictionary<Pawn, bool>> { { mapId, cache } } :
                    new Dictionary<int, Dictionary<Pawn, bool>>()
            );

            if (targetColonist == null)
                return null;

            // Create execution job
            return CreateGuiltyExecutionJob(warden, targetColonist);
        }

        /// <summary>
        /// Validates if a warden can execute a guilty colonist
        /// </summary>
        protected override bool IsValidPrisonerTarget(Pawn colonist, Pawn warden)
        {
            return IsValidGuiltyTarget(colonist, warden);
        }

        /// <summary>
        /// Specialized check for guilty colonists to be executed
        /// </summary>
        private bool IsValidGuiltyTarget(Pawn colonist, Pawn warden)
        {
            // Skip if no longer valid target
            if (colonist?.guilt == null ||
                !colonist.guilt.IsGuilty ||
                !colonist.guilt.awaitingExecution ||
                colonist.InAggroMentalState ||
                colonist.IsFormingCaravan())
                return false;

            // Check if warden is capable of violence
            if (warden.WorkTagIsDisabled(WorkTags.Violent))
                return false;

            // Check basic reachability
            if (colonist.IsForbidden(warden) ||
                !warden.CanReserveAndReach(colonist, PathEndMode.Touch, warden.NormalMaxDanger()))
                return false;

            // Check if this action is allowed by ideology system
            return new HistoryEvent(HistoryEventDefOf.ExecutedColonist, warden.Named(HistoryEventArgsNames.Doer)).Notify_PawnAboutToDo_Job();
        }

        /// <summary>
        /// Determines if the job giver should execute based on cache status
        /// </summary>
        protected override bool ShouldExecuteNow(int mapId)
        {
            // Check if there's any free colonists on the map
            Map map = Find.Maps.Find(m => m.uniqueID == mapId);
            if (map == null || map.mapPawns.FreeColonistsSpawnedCount == 0)
                return false;

            // Use guilty check to avoid unnecessary cache updates
            bool hasGuilty = map.mapPawns.FreeColonistsSpawned.Any(c =>
                c?.guilt != null && c.guilt.IsGuilty && c.guilt.awaitingExecution);

            if (!hasGuilty)
                return false;

            return !_lastWardenCacheUpdate.TryGetValue(mapId, out int lastUpdate) ||
                   Find.TickManager.TicksGame > lastUpdate + CacheUpdateInterval;
        }

        #endregion

        #region Job creation

        /// <summary>
        /// Creates the execution job for the warden
        /// </summary>
        private Job CreateGuiltyExecutionJob(Pawn warden, Pawn colonist)
        {
            Job job = JobMaker.MakeJob(JobDefOf.GuiltyColonistExecution, colonist);

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"{warden.LabelShort} created job to execute guilty colonist {colonist.LabelShort}");
            }

            return job;
        }

        #endregion

        #region Utility

        /// <summary>
        /// For debugging purposes
        /// </summary>
        public override string ToString()
        {
            return "JobGiver_Warden_ExecuteGuilty_PawnControl";
        }

        #endregion
    }
}