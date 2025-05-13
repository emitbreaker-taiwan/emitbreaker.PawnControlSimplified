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

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "DoExecution";

        /// <summary>
        /// Cache update interval in ticks (180 ticks = 3 seconds)
        /// </summary>
        protected override int CacheUpdateInterval => 180;

        /// <summary>
        /// Distance thresholds for bucketing (20, 40, 50 tiles squared)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 400f, 1600f, 2500f };

        #endregion

        #region Static Resources

        // Static string caching for better performance
        private static string IncapableOfViolenceLowerTrans;

        /// <summary>
        /// Reset static strings when language changes
        /// </summary>
        public static void ResetStaticData()
        {
            IncapableOfViolenceLowerTrans = "IncapableOfViolenceLower".Translate();
        }

        #endregion

        #region Core flow

        /// <summary>
        /// Gets base priority for the job giver
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            // Higher priority than basic tasks but lower than emergency tasks
            return 7.0f;
        }

        /// <summary>
        /// Creates a job for the warden to execute a prisoner
        /// </summary>
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
                    // Process cached targets to create job
                    if (p?.Map == null) return null;

                    // Get prisoners from centralized cache system
                    var prisoners = GetOrCreatePrisonerCache(p.Map);

                    // Convert to Thing list for processing
                    List<Thing> targets = new List<Thing>();
                    foreach (Pawn prisoner in prisoners)
                    {
                        if (prisoner != null && !prisoner.Dead && prisoner.Spawned)
                            targets.Add(prisoner);
                    }

                    return ProcessCachedTargets(p, targets, forced);
                },
                debugJobDesc: "execute prisoner");
        }

        /// <summary>
        /// Checks whether this job giver should be skipped for a pawn
        /// </summary>
        public override bool ShouldSkip(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.InMentalState)
                return true;

            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null)
                return true;

            // Skip if pawn is not a warden
            if (!Utility_TagManager.WorkEnabled(pawn.def, WorkTag))
                return true;

            // Skip if pawn can't do violent work
            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
                return true;

            return false;
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

            // Check if cache needs updating
            string cacheKey = this.GetType().Name + PRISONERS_CACHE_SUFFIX;
            int lastUpdateTick = Utility_MapCacheManager.GetLastCacheUpdateTick(mapId, cacheKey);

            return Find.TickManager.TicksGame > lastUpdateTick + CacheUpdateInterval;
        }

        #endregion

        #region Prisoner Selection

        /// <summary>
        /// Get prisoners that match the criteria for execution
        /// </summary>
        protected override IEnumerable<Pawn> GetPrisonersMatchingCriteria(Map map)
        {
            if (map == null)
                yield break;

            // Get all prisoner pawns on the map marked for execution
            foreach (Pawn prisoner in map.mapPawns.PrisonersOfColonySpawned)
            {
                if (FilterExecutionTargets(prisoner))
                {
                    yield return prisoner;
                }
            }
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
        protected override bool IsValidPrisonerTarget(Pawn prisoner, Pawn warden)
        {
            // First check base class validation
            if (!base.IsValidPrisonerTarget(prisoner, warden))
                return false;

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
        /// Create a job for the given prisoner
        /// </summary>
        protected override Job CreateJobForPrisoner(Pawn warden, Pawn prisoner, bool forced)
        {
            return CreateExecutionJob(warden, prisoner);
        }

        #endregion

        #region Job creation

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

        #region Debug

        /// <summary>
        /// For debugging purposes
        /// </summary>
        public override string ToString()
        {
            return "JobGiver_Warden_DoExecution_PawnControl";
        }

        #endregion
    }
}