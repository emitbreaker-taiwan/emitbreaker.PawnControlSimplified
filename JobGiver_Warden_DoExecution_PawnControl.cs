using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Enhanced JobGiver that assigns prisoner execution jobs to eligible pawns.
    /// Requires the Warden work tag to be enabled.
    /// </summary>
    public class JobGiver_Warden_DoExecution_PawnControl : ThinkNode_JobGiver
    {
        // Cache system to improve performance with large numbers of pawns
        private static readonly Dictionary<int, List<Pawn>> _executionTargetsCache = new Dictionary<int, List<Pawn>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 180; // Update every 3 seconds

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 400f, 1600f, 2500f }; // 20, 40, 50 tiles

        // Static string caching for better performance
        private static string IncapableOfViolenceLowerTrans;

        // Reset static strings when language changes
        public static void ResetStaticData()
        {
            IncapableOfViolenceLowerTrans = "IncapableOfViolenceLower".Translate();
        }

        public override float GetPriority(Pawn pawn)
        {
            // Higher priority than basic tasks but lower than emergency tasks
            return 7.0f;
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
                        _executionTargetsCache,
                        _reachabilityCache,
                        FilterExecutionTargets);

                    // Create job using standardized method
                    return Utility_JobGiverManager.TryCreatePrisonerInteractionJob(
                        p,
                        _executionTargetsCache,
                        _reachabilityCache,
                        ValidateCanExecute,
                        CreateExecutionJob,
                        DISTANCE_THRESHOLDS);
                },
                // Additional check with JobFailReason
                (p, setFailReason) => {
                    if (p.WorkTagIsDisabled(WorkTags.Violent))
                    {
                        if (setFailReason)
                            JobFailReason.Is(IncapableOfViolenceLowerTrans);
                        return false;
                    }
                    return true;
                },
                "prisoner execution");
        }

        /// <summary>
        /// Filter function to identify prisoners marked for execution
        /// </summary>
        private bool FilterExecutionTargets(Pawn prisoner)
        {
            // Skip invalid prisoners
            if (prisoner?.guest == null || prisoner.Dead || !prisoner.Spawned)
                return false;

            // Only include prisoners explicitly marked for execution
            return !prisoner.guest.IsInteractionDisabled(PrisonerInteractionModeDefOf.Execution) &&
                   prisoner.guest.ExclusiveInteractionMode == PrisonerInteractionModeDefOf.Execution;
        }

        /// <summary>
        /// Validates if a warden can execute a specific prisoner
        /// </summary>
        private bool ValidateCanExecute(Pawn prisoner, Pawn warden)
        {
            // Skip if interaction mode changed
            if (prisoner?.guest == null ||
                prisoner.guest.ExclusiveInteractionMode != PrisonerInteractionModeDefOf.Execution ||
                prisoner.guest.IsInteractionDisabled(PrisonerInteractionModeDefOf.Execution))
                return false;

            // Check base requirements
            if (!ShouldTakeCareOfPrisoner(warden, prisoner) ||
                !IsExecutionIdeoAllowed(warden, prisoner) ||
                !warden.CanReserveAndReach(prisoner, PathEndMode.OnCell, Danger.Deadly))
                return false;

            return true;
        }

        /// <summary>
        /// Creates the execution job for the warden
        /// </summary>
        private Job CreateExecutionJob(Pawn warden, Pawn prisoner)
        {
            Job job = JobMaker.MakeJob(JobDefOf.PrisonerExecution, prisoner);

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"{warden.LabelShort} created job for executing prisoner {prisoner.LabelShort}");
            }

            return job;
        }

        /// <summary>
        /// Check if the pawn should take care of a prisoner (ported from WorkGiver_Warden)
        /// </summary>
        private bool ShouldTakeCareOfPrisoner(Pawn warden, Thing prisoner)
        {
            return prisoner is Pawn pawnPrisoner &&
                   pawnPrisoner.IsPrisoner &&
                   pawnPrisoner.Spawned &&
                   !pawnPrisoner.Dead &&
                   !pawnPrisoner.InAggroMentalState &&
                   pawnPrisoner.guest != null &&
                   pawnPrisoner.guest.PrisonerIsSecure &&
                   warden.CanReach(pawnPrisoner, PathEndMode.OnCell, Danger.Some);
        }

        /// <summary>
        /// Check if execution is allowed by the pawn's ideology
        /// </summary>
        private bool IsExecutionIdeoAllowed(Pawn executioner, Pawn victim)
        {
            // Skip ideology check for non-humanlike pawns
            if (!executioner.RaceProps.Humanlike)
                return true;

            if (!ModsConfig.IdeologyActive)
                return true;

            if (executioner.Ideo != null)
            {
                // Use Utility_Common.PreceptDefNamed instead of direct PreceptDefOf references
                if (victim.IsColonist &&
                    executioner.Ideo.HasPrecept(Utility_Common.PreceptDefNamed("Execution_Colonists_Forbidden")))
                    return false;

                if (victim.IsPrisonerOfColony &&
                    executioner.Ideo.HasPrecept(Utility_Common.PreceptDefNamed("Execution_Prisoners_Forbidden")))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_executionTargetsCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
            ResetStaticData();
        }

        public override string ToString()
        {
            return "JobGiver_Warden_DoExecution_PawnControl";
        }
    }
}