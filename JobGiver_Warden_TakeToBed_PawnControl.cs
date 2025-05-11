using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to take prisoners to beds.
    /// Optimized to use the PawnControl framework for better performance.
    /// </summary>
    public class JobGiver_Warden_TakeToBed_PawnControl : JobGiver_Warden_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "TakeToBed";

        /// <summary>
        /// Cache update interval in ticks (120 ticks = 2 seconds)
        /// </summary>
        protected override int CacheUpdateInterval => 120;

        /// <summary>
        /// Distance thresholds for bucketing (10, 20, 30 tiles)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 100f, 400f, 900f };

        #endregion

        #region Core flow

        protected override float GetBasePriority(string workTag)
        {
            // Taking prisoners to beds is important for their care
            return 6.0f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Warden_TakeToBed_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) => {
                    // Process cached targets to create job
                    if (p?.Map == null) return null;

                    int mapId = p.Map.uniqueID;
                    List<Thing> targets;
                    if (_prisonerCache.TryGetValue(mapId, out var prisonerList) && prisonerList != null)
                    {
                        targets = new List<Thing>(prisonerList.Cast<Thing>());
                    }
                    else
                    {
                        targets = new List<Thing>();
                    }

                    return ProcessCachedTargets(p, targets, forced);
                },
                debugJobDesc: "take prisoner to bed");
        }

        #endregion

        #region Target processing

        /// <summary>
        /// Get all prisoners that need to be taken to beds
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null) yield break;

            // Get all prisoner pawns on the map
            foreach (Pawn prisoner in map.mapPawns.PrisonersOfColonySpawned)
            {
                if (FilterPrisonersNeedingBeds(prisoner))
                {
                    yield return prisoner;
                }
            }
        }

        /// <summary>
        /// Filter function to identify prisoners that need to be taken to beds
        /// </summary>
        private bool FilterPrisonersNeedingBeds(Pawn prisoner)
        {
            if (prisoner?.guest == null || prisoner.InAggroMentalState || prisoner.IsFormingCaravan())
                return false;

            // Add downed prisoners needing medical rest
            if (prisoner.Downed && HealthAIUtility.ShouldSeekMedicalRest(prisoner) && !prisoner.InBed())
                return true;

            // Add non-downed prisoners without proper beds
            if (!prisoner.Downed && RestUtility.FindBedFor(prisoner, prisoner, true, guestStatus: new GuestStatus?(GuestStatus.Prisoner)) == null)
                return true;

            return false;
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
                (prisoner) => (prisoner.Position - warden.Position).LengthHorizontalSquared,
                DistanceThresholds
            );

            // Find the first valid prisoner to take to bed
            Pawn targetPrisoner = Utility_JobGiverManager.FindFirstValidTargetInBuckets<Pawn>(
                buckets,
                warden,
                (prisoner, p) => IsValidPrisonerTarget(prisoner, p),
                _prisonerReachabilityCache.TryGetValue(mapId, out var cache) ?
                    new Dictionary<int, Dictionary<Pawn, bool>> { { mapId, cache } } :
                    new Dictionary<int, Dictionary<Pawn, bool>>()
            );

            if (targetPrisoner == null)
                return null;

            // Create take to bed job
            return CreateTakeToBedJob(warden, targetPrisoner);
        }

        /// <summary>
        /// Validates if a warden can take a specific prisoner to bed
        /// </summary>
        protected override bool IsValidPrisonerTarget(Pawn prisoner, Pawn warden)
        {
            // Skip if not actually a valid prisoner
            if (prisoner?.guest == null || !prisoner.IsPrisoner ||
                prisoner.InAggroMentalState || prisoner.IsFormingCaravan())
                return false;

            // Check basic reachability
            if (!prisoner.Spawned || prisoner.IsForbidden(warden) ||
                !warden.CanReserveAndReach(prisoner, PathEndMode.OnCell, warden.NormalMaxDanger()))
                return false;

            // Try to create a job for this prisoner
            return TryMakeJob(warden, prisoner, false) != null;
        }

        /// <summary>
        /// Determines if the job giver should execute based on cache status
        /// </summary>
        protected override bool ShouldExecuteNow(int mapId)
        {
            // Check if there's any prisoners of the colony on the map
            Map map = Find.Maps.Find(m => m.uniqueID == mapId);
            if (map == null || map.mapPawns.PrisonersOfColonySpawnedCount == 0)
                return false;

            // Check for prisoners needing beds to avoid unnecessary cache updates
            bool hasPrisonersNeedingBeds = false;
            foreach (Pawn prisoner in map.mapPawns.PrisonersOfColonySpawned)
            {
                if (FilterPrisonersNeedingBeds(prisoner))
                {
                    hasPrisonersNeedingBeds = true;
                    break;
                }
            }

            if (!hasPrisonersNeedingBeds)
                return false;

            return !_lastWardenCacheUpdate.TryGetValue(mapId, out int lastUpdate) ||
                   Find.TickManager.TicksGame > lastUpdate + CacheUpdateInterval;
        }

        #endregion

        #region Job creation

        /// <summary>
        /// Creates a job to take a prisoner to bed
        /// </summary>
        private Job CreateTakeToBedJob(Pawn warden, Pawn prisoner)
        {
            Job job = TryMakeJob(warden, prisoner, false);

            if (job != null)
            {
                string jobType = job.def == JobDefOf.TakeWoundedPrisonerToBed ? "medical bed" : "assigned bed";
                Utility_DebugManager.LogNormal($"{warden.LabelShort} created job to take prisoner {prisoner.LabelShort} to {jobType}");
            }

            return job;
        }

        /// <summary>
        /// Try to create a job to take a prisoner to bed, mirroring the original WorkGiver logic
        /// </summary>
        private Job TryMakeJob(Pawn pawn, Pawn prisoner, bool forced = false)
        {
            // Try taking a downed prisoner to bed first
            Job downedToBedJob = TakeDownedToBedJob(prisoner, pawn);
            if (downedToBedJob != null)
                return downedToBedJob;

            // Try taking prisoner to preferred bed
            Job toPreferredBedJob = TakeToPreferredBedJob(prisoner, pawn);
            if (toPreferredBedJob != null)
                return toPreferredBedJob;

            return null;
        }

        /// <summary>
        /// Try to create a job to take a prisoner to their preferred bed
        /// </summary>
        private Job TakeToPreferredBedJob(Pawn prisoner, Pawn warden)
        {
            if (prisoner.Downed || !warden.CanReserve((LocalTargetInfo)(Thing)prisoner))
                return null;

            if (RestUtility.FindBedFor(prisoner, prisoner, true, guestStatus: new GuestStatus?(GuestStatus.Prisoner)) != null)
                return null;

            Room room = prisoner.GetRoom();
            Building_Bed bedFor = RestUtility.FindBedFor(prisoner, warden, false, guestStatus: new GuestStatus?(GuestStatus.Prisoner));

            if (bedFor == null || bedFor.GetRoom() == room)
                return null;

            Job toPreferredBedJob = JobMaker.MakeJob(JobDefOf.EscortPrisonerToBed, (LocalTargetInfo)(Thing)prisoner, (LocalTargetInfo)(Thing)bedFor);
            toPreferredBedJob.count = 1;

            return toPreferredBedJob;
        }

        /// <summary>
        /// Try to create a job to take a downed prisoner to a hospital bed
        /// </summary>
        private Job TakeDownedToBedJob(Pawn prisoner, Pawn warden)
        {
            if (!prisoner.Downed || !HealthAIUtility.ShouldSeekMedicalRest(prisoner) ||
                prisoner.InBed() || !warden.CanReserve((LocalTargetInfo)(Thing)prisoner))
                return null;

            Building_Bed bedFor = RestUtility.FindBedFor(prisoner, warden, true, guestStatus: new GuestStatus?(GuestStatus.Prisoner));
            if (bedFor == null)
                return null;

            Job downedToBedJob = JobMaker.MakeJob(JobDefOf.TakeWoundedPrisonerToBed, (LocalTargetInfo)(Thing)prisoner, (LocalTargetInfo)(Thing)bedFor);
            downedToBedJob.count = 1;

            return downedToBedJob;
        }

        #endregion

        #region Utility

        /// <summary>
        /// For debugging purposes
        /// </summary>
        public override string ToString()
        {
            return "JobGiver_Warden_TakeToBed_PawnControl";
        }

        #endregion
    }
}