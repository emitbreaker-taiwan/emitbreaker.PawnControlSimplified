using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks for wardens to suppress slaves
    /// </summary>
    public class JobGiver_Warden_SuppressSlave_PawnControl : JobGiver_Warden_PawnControl
    {
        #region Configuration

        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.SlaveSuppress;

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "SuppressSlave";

        /// <summary>
        /// Cache update interval in ticks (180 ticks = 3 seconds)
        /// </summary>
        protected override int CacheUpdateInterval => 180;

        /// <summary>
        /// Distance thresholds for bucketing (10, 15, 25 tiles)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 100f, 225f, 625f };

        #endregion

        #region Core flow

        protected override float GetBasePriority(string workTag)
        {
            // Slave suppression has high priority
            return 6.0f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Warden_SuppressSlave_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) => {
                    // Process cached targets to create job
                    if (p?.Map == null) return null;

                    int mapId = p.Map.uniqueID;
                    List<Pawn> targets;
                    if (_prisonerCache.TryGetValue(mapId, out var slaveList) && slaveList != null)
                    {
                        targets = new List<Pawn>(slaveList);
                    }
                    else
                    {
                        targets = new List<Pawn>();
                    }

                    return ProcessCachedTargets(p, targets.Cast<Thing>().ToList(), forced);
                },
                debugJobDesc: "suppress slave");
        }

        public override bool ShouldSkip(Pawn pawn)
        {
            // Check if Ideology DLC is active
            if (!ModLister.CheckIdeology("Slave suppression"))
                return true;

            // Use base checks next
            return base.ShouldSkip(pawn);
        }

        #endregion

        #region Target processing

        /// <summary>
        /// Get all slaves eligible for suppression
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null) yield break;

            // Find all slaves on the map
            foreach (Pawn slave in map.mapPawns.SlavesOfColonySpawned)
            {
                if (slave != null && !slave.Destroyed && slave.Spawned)
                {
                    if (CanBeSuppressed(slave))
                    {
                        yield return slave;
                    }
                }
            }
        }

        /// <summary>
        /// Check if slave can be suppressed
        /// </summary>
        private bool CanBeSuppressed(Pawn slave)
        {
            // Check if the slave exists and has guest component
            if (slave?.guest == null)
                return false;

            // Check if slave is set to suppression mode
            if (slave.guest.slaveInteractionMode != SlaveInteractionModeDefOf.Suppress)
                return false;

            // Skip downed slaves
            if (slave.Downed)
                return false;

            // Slave must be awake
            if (!slave.Awake())
                return false;

            // Check if the slave has a suppression need and can be suppressed now
            Need_Suppression suppressionNeed = slave.needs?.TryGetNeed<Need_Suppression>();
            if (suppressionNeed == null || !suppressionNeed.CanBeSuppressedNow)
                return false;

            // Check if the slave is scheduled for suppression
            if (!slave.guest.ScheduledForSlaveSuppression)
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
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets<Thing>(
                warden,
                targets,
                (thing) => (thing.Position - warden.Position).LengthHorizontalSquared,
                DistanceThresholds
            );

            // Find the first valid slave to suppress
            Thing targetThing = Utility_JobGiverManager.FindFirstValidTargetInBuckets<Thing>(
                buckets,
                warden,
                (thing, p) => IsValidPrisonerTarget(thing as Pawn, p),
                _prisonerReachabilityCache.TryGetValue(mapId, out var cache) ?
                    new Dictionary<int, Dictionary<Thing, bool>> { { mapId, cache.ToDictionary(kvp => (Thing)kvp.Key, kvp => kvp.Value) } } :
                    new Dictionary<int, Dictionary<Thing, bool>>()
            );

            if (targetThing == null || !(targetThing is Pawn slave))
                return null;

            // Create suppression job
            return CreateSuppressionJob(warden, slave);
        }

        /// <summary>
        /// Check if this slave is a valid target for suppression
        /// </summary>
        protected override bool IsValidPrisonerTarget(Pawn slave, Pawn warden)
        {
            // Basic validation
            if (slave == null || !slave.Spawned)
                return false;

            // Ensure it's a slave
            if (!slave.IsSlave)
                return false;

            // Check if slave can be suppressed
            if (!CanBeSuppressed(slave))
                return false;

            // Check if warden can reserve the slave
            if (!warden.CanReserve(slave))
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
            if (map == null || !map.mapPawns.SlavesOfColonySpawned.Any(s => 
                s.guest?.slaveInteractionMode == SlaveInteractionModeDefOf.Suppress && 
                s.guest.ScheduledForSlaveSuppression))
                return false;

            return !_lastWardenCacheUpdate.TryGetValue(mapId, out int lastUpdate) ||
                   Find.TickManager.TicksGame > lastUpdate + CacheUpdateInterval;
        }

        #endregion

        #region Job creation

        /// <summary>
        /// Creates the suppression job for the warden
        /// </summary>
        private Job CreateSuppressionJob(Pawn warden, Pawn slave)
        {
            Job job = JobMaker.MakeJob(WorkJobDef, slave);

            Utility_DebugManager.LogNormal($"{warden.LabelShort} created job to suppress slave {slave.LabelShort}");

            return job;
        }

        #endregion

        #region Utility

        /// <summary>
        /// For debugging purposes
        /// </summary>
        public override string ToString()
        {
            return "JobGiver_Warden_SuppressSlave_PawnControl";
        }

        #endregion
    }
}