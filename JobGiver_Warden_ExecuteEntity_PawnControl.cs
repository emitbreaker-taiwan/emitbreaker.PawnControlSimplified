using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks for wardens to execute entities on holding platforms.
    /// </summary>
    public class JobGiver_Warden_ExecuteEntity_PawnControl : JobGiver_Warden_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "ExecuteEntity";

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
            // Executing entities has high priority
            return 7.0f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Warden_ExecuteEntity_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) => {
                    // Process cached targets to create job
                    if (p?.Map == null) return null;

                    int mapId = p.Map.uniqueID;
                    List<Thing> targets;
                    if (_prisonerCache.TryGetValue(mapId, out var platformList) && platformList != null)
                    {
                        targets = new List<Thing>(platformList.Cast<Thing>());
                    }
                    else
                    {
                        targets = new List<Thing>();
                    }

                    return ProcessCachedTargets(p, targets, forced);
                },
                debugJobDesc: "execute entity");
        }

        public override bool ShouldSkip(Pawn pawn)
        {
            if (!ModsConfig.AnomalyActive)
                return true;

            // Use base checks first
            if (base.ShouldSkip(pawn))
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
        /// Get all holding platforms with pawns eligible for execution
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null) yield break;

            // Find all holding platforms on the map
            foreach (var building in map.listerBuildings.AllBuildingsColonistOfClass<Building_HoldingPlatform>())
            {
                if (FilterExecutablePlatform(building))
                {
                    yield return building;
                }
            }
        }

        /// <summary>
        /// Filter function to identify holding platforms with pawns ready for execution
        /// </summary>
        private bool FilterExecutablePlatform(Building_HoldingPlatform platform)
        {
            // Check if the platform exists and has a pawn
            if (platform == null || platform.HeldPawn == null)
                return false;

            Pawn heldPawn = platform.HeldPawn;

            // Check if the pawn has the execution component
            CompHoldingPlatformTarget compTarget = heldPawn.TryGetComp<CompHoldingPlatformTarget>();
            if (compTarget == null)
                return false;

            // Only include platforms where the pawn is set for execution
            if (compTarget.containmentMode != EntityContainmentMode.Execute)
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
                (platform) => (platform.Position - warden.Position).LengthHorizontalSquared,
                DistanceThresholds
            );

            // Find the first valid platform with an entity to execute
            Thing targetPlatform = Utility_JobGiverManager.FindFirstValidTargetInBuckets<Thing>(
                buckets,
                warden,
                (platform, p) => IsValidPlatformTarget(platform, p),
                _prisonerReachabilityCache.TryGetValue(mapId, out var cache) ?
                    new Dictionary<int, Dictionary<Thing, bool>> { { mapId, cache.ToDictionary(kvp => (Thing)kvp.Key, kvp => kvp.Value) } } :
                    new Dictionary<int, Dictionary<Thing, bool>>()
            );

            if (targetPlatform == null)
                return null;

            // Create execution job
            return CreateExecutionJob(warden, targetPlatform);
        }

        /// <summary>
        /// Validates if a warden can execute a specific entity on a platform
        /// </summary>
        protected override bool IsValidPrisonerTarget(Pawn prisoner, Pawn warden)
        {
            // This method is not used directly for platforms, but must be implemented
            return false;
        }

        /// <summary>
        /// Specialized check for platforms with entities to be executed
        /// </summary>
        private bool IsValidPlatformTarget(Thing platform, Pawn warden)
        {
            if (!(platform is Building_HoldingPlatform holdingPlatform) || holdingPlatform.HeldPawn == null)
                return false;

            Pawn targetPawn = holdingPlatform.HeldPawn;

            // Check if entity is set for execution
            CompHoldingPlatformTarget compTarget = targetPawn.TryGetComp<CompHoldingPlatformTarget>();
            if (compTarget == null || compTarget.containmentMode != EntityContainmentMode.Execute)
                return false;

            // Check if warden is capable of violence
            if (warden.WorkTagIsDisabled(WorkTags.Violent))
                return false;

            // Check basic reachability
            if (!platform.Spawned || platform.IsForbidden(warden) ||
                !warden.CanReserve(platform, 1, -1, null, false))
                return false;

            return true;
        }

        /// <summary>
        /// Determines if the job giver should execute based on cache status
        /// </summary>
        protected override bool ShouldExecuteNow(int mapId)
        {
            // Check if there are any holding platforms on the map
            Map map = Find.Maps.Find(m => m.uniqueID == mapId);
            if (map == null)
                return false;

            return !_lastWardenCacheUpdate.TryGetValue(mapId, out int lastUpdate) ||
                   Find.TickManager.TicksGame > lastUpdate + CacheUpdateInterval;
        }

        #endregion

        #region Job creation

        /// <summary>
        /// Creates the execution job for the warden
        /// </summary>
        private Job CreateExecutionJob(Pawn warden, Thing platform)
        {
            Job job = JobMaker.MakeJob(JobDefOf.ExecuteEntity, platform);
            job.count = 1;

            Utility_DebugManager.LogNormal($"{warden.LabelShort} created job to execute entity on platform {platform.Label}");

            return job;
        }

        #endregion

        #region Utility

        /// <summary>
        /// For debugging purposes
        /// </summary>
        public override string ToString()
        {
            return "JobGiver_Warden_ExecuteEntity_PawnControl";
        }

        #endregion
    }
}