using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns prisoner release jobs to eligible wardens.
    /// Optimized to use the PawnControl framework for better performance.
    /// </summary>
    public class JobGiver_Warden_ReleasePrisoner_PawnControl : ThinkNode_JobGiver
    {
        // Cached prisoners to improve performance with large colonies
        private static readonly Dictionary<int, List<Pawn>> _prisonerCache = new Dictionary<int, List<Pawn>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 180; // Update every 3 seconds

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 100f, 400f, 900f }; // 10, 20, 30 tiles

        public override float GetPriority(Pawn pawn)
        {
            // Standard priority for prisoner handling
            return 5.5f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManagerOld.StandardTryGiveJob<Pawn>(
                pawn,
                "Warden",
                (p, forced) => {
                    // Update prisoner cache with standardized method
                    Utility_JobGiverManagerOld.UpdatePrisonerCache(
                        p.Map,
                        ref _lastCacheUpdateTick,
                        CACHE_UPDATE_INTERVAL,
                        _prisonerCache,
                        _reachabilityCache,
                        FilterReleasablePrisoners);

                    // Create job using standardized method
                    return Utility_JobGiverManagerOld.TryCreatePrisonerInteractionJob(
                        p,
                        _prisonerCache,
                        _reachabilityCache,
                        ValidateCanReleasePrisoner,
                        CreateReleasePrisonerJob,
                        DISTANCE_THRESHOLDS);
                },
                debugJobDesc: "release prisoner assignment");
        }

        /// <summary>
        /// Filter function to identify prisoners that can be released
        /// </summary>
        private bool FilterReleasablePrisoners(Pawn prisoner)
        {
            return prisoner.guest != null &&
                   prisoner.guest.IsInteractionEnabled(PrisonerInteractionModeDefOf.Release) &&
                   !prisoner.Downed &&
                   !prisoner.guest.Released &&
                   !prisoner.InMentalState;
        }

        /// <summary>
        /// Validates if a warden can release a specific prisoner
        /// </summary>
        private bool ValidateCanReleasePrisoner(Pawn prisoner, Pawn warden)
        {
            // Skip if not actually a valid prisoner for release anymore
            if (prisoner?.guest == null ||
                !prisoner.IsPrisoner ||
                prisoner.Downed ||
                prisoner.InMentalState ||
                prisoner.guest.Released ||
                !prisoner.guest.IsInteractionEnabled(PrisonerInteractionModeDefOf.Release))
                return false;

            // Check if warden can reach prisoner
            if (!warden.CanReserveAndReach(prisoner, PathEndMode.Touch, warden.NormalMaxDanger()))
                return false;

            // Check for valid release cell
            IntVec3 releaseCell;
            return RCellFinder.TryFindPrisonerReleaseCell(prisoner, warden, out releaseCell);
        }

        /// <summary>
        /// Creates a job to release a prisoner
        /// </summary>
        private Job CreateReleasePrisonerJob(Pawn warden, Pawn prisoner)
        {
            // Find release cell
            IntVec3 releaseCell;
            if (RCellFinder.TryFindPrisonerReleaseCell(prisoner, warden, out releaseCell))
            {
                Job job = JobMaker.MakeJob(JobDefOf.ReleasePrisoner, prisoner, releaseCell);
                job.count = 1;

                if (Prefs.DevMode)
                {
                    Utility_DebugManager.LogNormal($"{warden.LabelShort} created job to release prisoner {prisoner.LabelShort}");
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
            Utility_CacheManager.ResetJobGiverCache(_prisonerCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_Warden_ReleasePrisoner_PawnControl";
        }
    }
}