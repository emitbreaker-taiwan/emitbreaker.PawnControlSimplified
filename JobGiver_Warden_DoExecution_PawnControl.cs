using RimWorld;
using System;
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
                    // Update cache with execution targets
                    UpdateExecutionTargetsCache(p.Map);

                    // Use custom logic to find and create a job for the best execution target
                    return TryCreateExecutionJob(p);
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
        /// Update the execution targets cache periodically for improved performance
        /// </summary>
        private void UpdateExecutionTargetsCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_executionTargetsCache.ContainsKey(mapId))
            {
                // Initialize or clear caches
                Utility_CacheManager.ResetJobGiverCache(_executionTargetsCache, _reachabilityCache);
                if (!_executionTargetsCache.ContainsKey(mapId))
                    _executionTargetsCache[mapId] = new List<Pawn>();

                // Find all prisoners marked for execution
                foreach (Pawn prisoner in map.mapPawns.PrisonersOfColony)
                {
                    if (prisoner?.guest != null && !prisoner.Dead && prisoner.Spawned &&
                        !prisoner.guest.IsInteractionDisabled(PrisonerInteractionModeDefOf.Execution) &&
                        prisoner.guest.ExclusiveInteractionMode == PrisonerInteractionModeDefOf.Execution)
                    {
                        _executionTargetsCache[mapId].Add(prisoner);
                    }
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Create an execution job using the manager's bucket system
        /// </summary>
        private Job TryCreateExecutionJob(Pawn pawn)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_executionTargetsCache.ContainsKey(mapId) || _executionTargetsCache[mapId].Count == 0)
                return null;

            // Step 1: Pre-filter valid execution targets (with prisoner-specific validation)
            List<Pawn> validPrisoners = new List<Pawn>();
            foreach (Pawn prisoner in _executionTargetsCache[mapId])
            {
                // Skip invalid prisoners
                if (prisoner == null || prisoner.Dead || !prisoner.Spawned)
                    continue;

                // Skip if interaction mode changed
                if (prisoner.guest == null ||
                    prisoner.guest.ExclusiveInteractionMode != PrisonerInteractionModeDefOf.Execution ||
                    prisoner.guest.IsInteractionDisabled(PrisonerInteractionModeDefOf.Execution))
                    continue;

                validPrisoners.Add(prisoner);
            }

            // Step 2: Use JobGiverManager for distance bucketing
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                validPrisoners,
                (prisoner) => (prisoner.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Step 3: Find best target with specialized validation
            Pawn targetPrisoner = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (prisoner, p) => !prisoner.IsForbidden(p) &&
                                ShouldTakeCareOfPrisoner(p, prisoner) &&
                                IsExecutionIdeoAllowed(p, prisoner) &&
                                p.CanReserveAndReach(prisoner, PathEndMode.OnCell, Danger.Deadly),
                _reachabilityCache
            );

            // Step 4: Create job if we found a target
            if (targetPrisoner != null)
            {
                Job job = JobMaker.MakeJob(JobDefOf.PrisonerExecution, targetPrisoner);

                if (Prefs.DevMode)
                    Log.Message($"[PawnControl] {pawn.LabelShort} created job for executing prisoner {targetPrisoner.LabelShort}");

                return job;
            }

            return null;
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
        }

        public override string ToString()
        {
            return "JobGiver_Warden_DoExecution_PawnControl";
        }
    }
}