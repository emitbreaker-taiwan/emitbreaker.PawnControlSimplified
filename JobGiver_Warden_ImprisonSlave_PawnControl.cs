using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks for wardens to imprison slaves
    /// </summary>
    public class JobGiver_Warden_ImprisonSlave_PawnControl : JobGiver_Warden_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "ImprisonSlave";

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
            // Imprisoning slaves has high priority but lower than emergency tasks
            return 6.0f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Warden_ImprisonSlave_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) => {
                    // Process cached targets to create job
                    if (p?.Map == null) return null;

                    int mapId = p.Map.uniqueID;
                    List<Pawn> targets;
                    if (_prisonerCache.TryGetValue(mapId, out var prisonerList) && prisonerList != null)
                    {
                        targets = new List<Pawn>(prisonerList);
                    }
                    else
                    {
                        targets = new List<Pawn>();
                    }

                    return ProcessCachedTargets(p, targets.Cast<Thing>().ToList(), forced);
                },
                debugJobDesc: "imprison slave");
        }

        public override bool ShouldSkip(Pawn pawn)
        {
            // Check if ideology is active
            if (!ModLister.CheckIdeology("Slave imprisonment"))
                return true;

            // Use base checks next
            return base.ShouldSkip(pawn);
        }

        #endregion

        #region Target processing

        /// <summary>
        /// Get all slaves eligible for imprisonment
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null) yield break;

            // Find all slaves on the map
            foreach (Pawn slave in map.mapPawns.SlavesOfColonySpawned)
            {
                if (slave != null && !slave.Destroyed && slave.Spawned)
                {
                    if (slave.guest.slaveInteractionMode == SlaveInteractionModeDefOf.Imprison)
                    {
                        yield return slave;
                    }
                }
            }
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

            // Find the first valid slave to imprison
            Thing targetThing = Utility_JobGiverManager.FindFirstValidTargetInBuckets<Thing>(
                buckets,
                warden,
                (thing, p) => IsValidPrisonerTarget(thing as Pawn, p),
                _prisonerReachabilityCache.TryGetValue(mapId, out var cache) ?
                    new Dictionary<int, Dictionary<Thing, bool>> { { mapId, cache.ToDictionary(kvp => (Thing)kvp.Key, kvp => kvp.Value) } } :
                    new Dictionary<int, Dictionary<Thing, bool>>()
            );

            if (targetThing == null || !(targetThing is Pawn targetSlave))
                return null;

            // Create imprisonment job
            return CreateImprisonmentJob(warden, targetSlave);
        }

        /// <summary>
        /// Check if this slave is a valid target for imprisonment
        /// </summary>
        protected override bool IsValidPrisonerTarget(Pawn slave, Pawn warden)
        {
            // Check if the slave exists and is valid
            if (slave == null || !slave.Spawned || slave.Downed || slave.IsPrisoner)
                return false;

            // Check if the slave is actually a slave and set to be imprisoned
            if (!slave.IsSlave || slave.guest.slaveInteractionMode != SlaveInteractionModeDefOf.Imprison)
                return false;

            // Check if warden can access the slave
            if (!warden.CanReach(slave, PathEndMode.Touch, Danger.Deadly))
                return false;

            // Check if the slave can be reserved
            if (!warden.CanReserve(slave, 1, -1, null, false))
                return false;

            // Check if a bed is available for imprisonment
            Building_Bed bed = RestUtility.FindBedFor(slave, warden, checkSocialProperness: false, ignoreOtherReservations: false, GuestStatus.Prisoner);
            if (bed == null)
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
                s.guest.slaveInteractionMode == SlaveInteractionModeDefOf.Imprison))
                return false;

            return !_lastWardenCacheUpdate.TryGetValue(mapId, out int lastUpdate) ||
                   Find.TickManager.TicksGame > lastUpdate + CacheUpdateInterval;
        }

        #endregion

        #region Job creation

        /// <summary>
        /// Creates the imprisonment job for the warden
        /// </summary>
        private Job CreateImprisonmentJob(Pawn warden, Pawn slave)
        {
            Building_Bed bed = RestUtility.FindBedFor(slave, warden, checkSocialProperness: false, 
                ignoreOtherReservations: false, GuestStatus.Prisoner);
            
            if (bed == null)
                return null;

            Job job = JobMaker.MakeJob(JobDefOf.Arrest, slave, bed);
            job.count = 1;

            Utility_DebugManager.LogNormal($"{warden.LabelShort} created job to imprison slave {slave.LabelShort}");

            return job;
        }

        #endregion

        #region Utility

        /// <summary>
        /// For debugging purposes
        /// </summary>
        public override string ToString()
        {
            return "JobGiver_Warden_ImprisonSlave_PawnControl";
        }

        #endregion
    }
}