using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to take prisoners to beds.
    /// Optimized to use the PawnControl framework for better performance.
    /// </summary>
    public class JobGiver_Warden_TakeToBed_PawnControl : ThinkNode_JobGiver
    {
        // Cached prisoners to improve performance with large colonies
        private static readonly Dictionary<int, List<Pawn>> _prisonerCache = new Dictionary<int, List<Pawn>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 120; // Update every 2 seconds

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 100f, 400f, 900f }; // 10, 20, 30 tiles

        public override float GetPriority(Pawn pawn)
        {
            // Taking prisoners to beds is important for their care
            return 6.0f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<Pawn>(
                pawn,
                "Warden",
                (p, forced) => {
                    // Update prisoner cache with standardized method
                    Utility_JobGiverManager.UpdatePrisonerCache(
                        p.Map,
                        ref _lastCacheUpdateTick,
                        CACHE_UPDATE_INTERVAL,
                        _prisonerCache,
                        _reachabilityCache,
                        FilterPrisonersNeedingBeds);

                    // Create job using standardized method
                    return Utility_JobGiverManager.TryCreatePrisonerInteractionJob(
                        p,
                        _prisonerCache,
                        _reachabilityCache,
                        ValidateCanTakeToBed,
                        CreateTakeToBedJob,
                        DISTANCE_THRESHOLDS);
                },
                debugJobDesc: "prisoner bed assignment");
        }

        /// <summary>
        /// Filter function to identify prisoners that need to be taken to beds
        /// </summary>
        private bool FilterPrisonersNeedingBeds(Pawn prisoner)
        {
            if (prisoner.guest == null || prisoner.InAggroMentalState || prisoner.IsFormingCaravan())
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
        /// Validates if a warden can take a specific prisoner to bed
        /// </summary>
        private bool ValidateCanTakeToBed(Pawn prisoner, Pawn warden)
        {
            // Skip if no longer a valid prisoner
            if (prisoner?.guest == null || !prisoner.IsPrisoner ||
                prisoner.InAggroMentalState || prisoner.IsFormingCaravan())
                return false;

            // Check if warden can reach prisoner
            if (!warden.CanReserveAndReach(prisoner, PathEndMode.OnCell, warden.NormalMaxDanger()))
                return false;

            // Try to create a job for this prisoner
            return TryMakeJob(warden, prisoner, false) != null;
        }

        /// <summary>
        /// Creates a job to take a prisoner to bed
        /// </summary>
        private Job CreateTakeToBedJob(Pawn warden, Pawn prisoner)
        {
            Job job = TryMakeJob(warden, prisoner, false);

            if (job != null && Prefs.DevMode)
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

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_prisonerCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_Warden_TakeToBed_PawnControl";
        }
    }
}