using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks for wardens to execute slaves.
    /// Requires the Ideology DLC.
    /// </summary>
    public class JobGiver_Warden_ExecuteSlave_PawnControl : JobGiver_Warden_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "ExecuteSlave";

        /// <summary>
        /// Cache update interval in ticks (180 ticks = 3 seconds)
        /// </summary>
        protected override int CacheUpdateInterval => 180;

        /// <summary>
        /// Distance thresholds for bucketing (10, 15, 25 tiles)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 100f, 225f, 625f };

        /// <summary>
        /// Static translation cache for performance
        /// </summary>
        private static string IncapableOfViolenceLowerTrans;

        #endregion

        #region Initialization

        /// <summary>
        /// Reset static data when language changes
        /// </summary>
        public static void ResetStaticData()
        {
            IncapableOfViolenceLowerTrans = "IncapableOfViolenceLower".Translate();
        }

        #endregion

        #region Core flow

        protected override float GetBasePriority(string workTag)
        {
            // Executing slaves has high priority
            return 7.0f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Check for Ideology DLC first
            if (!ModLister.CheckIdeology("SlaveExecution"))
            {
                return null;
            }

            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Warden_ExecuteSlave_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) => {
                    // Process cached targets to create job
                    if (p?.Map == null) return null;

                    int mapId = p.Map.uniqueID;
                    List<Thing> targets;
                    if (_prisonerCache.TryGetValue(mapId, out var slaveList) && slaveList != null)
                    {
                        targets = new List<Thing>(slaveList.Cast<Thing>());
                    }
                    else
                    {
                        targets = new List<Thing>();
                    }

                    return ProcessCachedTargets(p, targets, forced);
                },
                debugJobDesc: "execute slave");
        }

        public override bool ShouldSkip(Pawn pawn)
        {
            // Use base checks first
            if (base.ShouldSkip(pawn))
                return true;

            // Ideology required
            if (!ModLister.CheckIdeology("SlaveExecution"))
                return true;

            // Cannot execute if incapable of violence
            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                JobFailReason.Is(IncapableOfViolenceLowerTrans);
                return true;
            }

            return false;
        }

        #endregion

        #region Target processing

        /// <summary>
        /// Get all slaves eligible for execution
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null) yield break;

            // Get all slave pawns on the map
            foreach (Pawn slave in map.mapPawns.SlavesOfColonySpawned)
            {
                if (FilterExecutableSlaves(slave))
                {
                    yield return slave;
                }
            }
        }

        /// <summary>
        /// Filter function to identify slaves ready for execution
        /// </summary>
        private bool FilterExecutableSlaves(Pawn slave)
        {
            // Check if the slave exists and has guest component
            if (slave?.guest == null)
                return false;

            // Skip slaves in mental states
            if (slave.InMentalState)
                return false;

            // Only include slaves set for execution
            if (slave.guest.slaveInteractionMode != SlaveInteractionModeDefOf.Execute)
                return false;

            return true;
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
                (slave) => (slave.Position - warden.Position).LengthHorizontalSquared,
                DistanceThresholds
            );

            // Find the first valid slave to execute
            Pawn targetSlave = Utility_JobGiverManager.FindFirstValidTargetInBuckets<Pawn>(
                buckets,
                warden,
                (slave, p) => IsValidSlaveTarget(slave, p),
                _prisonerReachabilityCache.TryGetValue(mapId, out var cache) ?
                    new Dictionary<int, Dictionary<Pawn, bool>> { { mapId, cache } } :
                    new Dictionary<int, Dictionary<Pawn, bool>>()
            );

            if (targetSlave == null)
                return null;

            // Create execution job
            return CreateExecutionJob(warden, targetSlave);
        }

        /// <summary>
        /// Validates if a warden can execute a specific slave
        /// </summary>
        protected override bool IsValidPrisonerTarget(Pawn slave, Pawn warden)
        {
            return IsValidSlaveTarget(slave, warden);
        }

        /// <summary>
        /// Specialized check for slaves to be executed
        /// </summary>
        private bool IsValidSlaveTarget(Pawn slave, Pawn warden)
        {
            if (slave?.guest == null)
                return false;

            // Check if slave is set for execution
            if (slave.guest.slaveInteractionMode != SlaveInteractionModeDefOf.Execute)
                return false;

            // Check if warden is capable of violence
            if (warden.WorkTagIsDisabled(WorkTags.Violent))
                return false;

            // Check basic reachability
            if (!slave.Spawned || slave.IsForbidden(warden) ||
                !warden.CanReserve(slave, 1, -1, null, false))
                return false;

            // Check ideology compatibility
            if (!IsExecutionIdeoAllowed(warden, slave))
                return false;

            return true;
        }

        /// <summary>
        /// Determines if the job giver should execute based on cache status
        /// </summary>
        protected override bool ShouldExecuteNow(int mapId)
        {
            // Check if there are any slaves on the map
            Map map = Find.Maps.Find(m => m.uniqueID == mapId);
            if (map == null || map.mapPawns.SlavesOfColonySpawned == null || map.mapPawns.SlavesOfColonySpawned.Count == 0)
                return false;

            return !_lastWardenCacheUpdate.TryGetValue(mapId, out int lastUpdate) ||
                   Find.TickManager.TicksGame > lastUpdate + CacheUpdateInterval;
        }

        #endregion

        #region Job creation

        /// <summary>
        /// Creates the execution job for the warden
        /// </summary>
        private Job CreateExecutionJob(Pawn warden, Pawn slave)
        {
            Job job = JobMaker.MakeJob(JobDefOf.SlaveExecution, slave);
            job.count = 1;

            Utility_DebugManager.LogNormal($"{warden.LabelShort} created job to execute slave {slave.LabelShort}");

            return job;
        }

        #endregion

        #region Utility

        /// <summary>
        /// Check if the execution is allowed by the warden's ideology
        /// </summary>
        private bool IsExecutionIdeoAllowed(Pawn warden, Pawn slave)
        {
            // This method is imported from the original WorkGiver
            // Additional implementation is needed based on the game's requirements
            return true;
        }

        /// <summary>
        /// For debugging purposes
        /// </summary>
        public override string ToString()
        {
            return "JobGiver_Warden_ExecuteSlave_PawnControl";
        }

        #endregion
    }
}