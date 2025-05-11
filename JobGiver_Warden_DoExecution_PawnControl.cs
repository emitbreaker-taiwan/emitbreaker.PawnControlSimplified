using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Enhanced JobGiver that assigns prisoner execution jobs to eligible pawns.
    /// Requires the Warden work tag to be enabled.
    /// </summary>
    public class JobGiver_Warden_DoExecution_PawnControl : JobGiver_Warden_PawnControl
    {
        #region Configuration

        protected override float[] DistanceThresholds => new float[] { 400f, 1600f, 2500f }; // 20, 40, 50 tiles
        private const int CACHE_UPDATE_INTERVAL = 180; // Update every 3 seconds

        #endregion

        #region Static Resources

        // Static string caching for better performance
        private static string IncapableOfViolenceLowerTrans;

        #endregion

        #region Initialization

        // Reset static strings when language changes
        public static void ResetStaticData()
        {
            IncapableOfViolenceLowerTrans = "IncapableOfViolenceLower".Translate();
        }

        #endregion

        #region Job Priority

        protected override float GetBasePriority(string workTag)
        {
            // Higher priority than basic tasks but lower than emergency tasks
            return 7.0f;
        }

        #endregion

        #region Core Flow

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Check if pawn can do violent work first
            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                JobFailReason.Is(IncapableOfViolenceLowerTrans);
                return null;
            }

            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Warden_DoExecution_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) => {
                    if (p?.Map == null) return null;

                    // Update prisoner cache
                    int lastUpdateTick = 0;
                    if (_lastWardenCacheUpdate.ContainsKey(p.Map.uniqueID))
                    {
                        lastUpdateTick = _lastWardenCacheUpdate[p.Map.uniqueID];
                    }

                    UpdatePrisonerCache(
                        p.Map,
                        ref lastUpdateTick,
                        CACHE_UPDATE_INTERVAL,
                        _prisonerCache,
                        _prisonerReachabilityCache,
                        FilterExecutionTargets
                    );
                    _lastWardenCacheUpdate[p.Map.uniqueID] = lastUpdateTick;

                    // Process cached targets
                    List<Thing> targets;
                    if (_prisonerCache.TryGetValue(p.Map.uniqueID, out var prisonerList) && prisonerList != null)
                    {
                        targets = new List<Thing>(prisonerList.Cast<Thing>());
                    }
                    else
                    {
                        targets = new List<Thing>();
                    }

                    return ProcessCachedTargets(p, targets, forced);
                },
                debugJobDesc: "execute prisoner");
        }

        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // Assuming prisoners are the targets for this job
            foreach (var prisoner in map.mapPawns.PrisonersOfColony)
            {
                if (FilterExecutionTargets(prisoner))
                {
                    yield return prisoner;
                }
            }
        }

        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            foreach (var target in targets)
            {
                if (target is Pawn prisoner && ValidateCanExecute(prisoner, pawn))
                {
                    return CreateExecutionJob(pawn, prisoner);
                }
            }
            return null;
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
        #endregion

        #region Job Creation

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

        #endregion

        #region Validation

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

        #endregion

        #region Cache Management

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetDoExecutionCache()
        {
            ResetStaticData();
        }
        #endregion

        #region Object Information
        public override string ToString()
        {
            return "JobGiver_Warden_DoExecution_PawnControl";
        }
        #endregion
    }
}