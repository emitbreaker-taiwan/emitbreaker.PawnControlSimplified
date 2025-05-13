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
        /// Cache key identifier for this specific job giver
        /// </summary>
        private const string CACHE_KEY_SUFFIX = "_ExecuteEntity";

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

        /// <summary>
        /// Reset the cache for this job giver
        /// </summary>
        public static void ResetExecuteEntityCache()
        {
            // Clear all execute entity related caches from all maps
            foreach (Map map in Find.Maps)
            {
                int mapId = map.uniqueID;
                string cacheKey = typeof(JobGiver_Warden_ExecuteEntity_PawnControl).Name + PRISONERS_CACHE_SUFFIX + CACHE_KEY_SUFFIX;
                string reachCacheKey = typeof(JobGiver_Warden_ExecuteEntity_PawnControl).Name + "_ReachCache" + CACHE_KEY_SUFFIX;

                var platformCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Thing>>(mapId);
                if (platformCache.ContainsKey(cacheKey))
                {
                    platformCache.Remove(cacheKey);
                    Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, -1);
                }

                // Clear reachability cache
                var reachabilityCache = Utility_MapCacheManager.GetOrCreateMapCache<Thing, bool>(mapId);
                reachabilityCache.Clear();
                Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, reachCacheKey, -1);
            }

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal("Reset all execute entity caches");
            }
        }

        #endregion

        #region Core flow

        /// <summary>
        /// Enslave has high priority as it changes prisoner status
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            // Executing entities has high priority
            return 7.0f;
        }

        /// <summary>
        /// Try to give a job, using the centralized cache system
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!ModsConfig.AnomalyActive)
                return null;

            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Warden_ExecuteEntity_PawnControl>(
                pawn,
                WorkTag,
                CreateJobFromCachedTargets,
                debugJobDesc: "execute entity");
        }

        /// <summary>
        /// Create a job using the cached targets
        /// </summary>
        private Job CreateJobFromCachedTargets(Pawn pawn, bool forced)
        {
            // Process cached targets to create job
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;

            // Get platforms from cache using the proper cache key
            string cacheKey = this.GetType().Name + PRISONERS_CACHE_SUFFIX + CACHE_KEY_SUFFIX;
            var platformCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Thing>>(mapId);
            List<Thing> platformList = platformCache.TryGetValue(cacheKey, out var cachedList) ? cachedList : null;

            List<Thing> targets;
            if (platformList != null)
            {
                targets = new List<Thing>(platformList);
            }
            else
            {
                // If cache miss, update the cache
                var freshPlatforms = GetTargets(pawn.Map).ToList();
                platformCache[cacheKey] = freshPlatforms;
                Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, Find.TickManager.TicksGame);
                targets = freshPlatforms;
            }

            return ProcessCachedTargets(pawn, targets, forced);
        }

        /// <summary>
        /// Determines if this job giver should be skipped for the given pawn
        /// </summary>
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
        /// Get prisoners matching specific criteria for this job giver
        /// </summary>
        protected override IEnumerable<Pawn> GetPrisonersMatchingCriteria(Map map)
        {
            // This job giver doesn't work with prisoners directly, so return empty
            yield break;
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

            // Get reachability cache for this job giver
            string reachCacheKey = this.GetType().Name + "_ReachCache" + CACHE_KEY_SUFFIX;
            var reachabilityCache = Utility_MapCacheManager.GetOrCreateMapCache<Thing, bool>(mapId);

            // Find the first valid platform with an entity to execute
            Thing targetPlatform = Utility_JobGiverManager.FindFirstValidTargetInBuckets<Thing>(
                buckets,
                warden,
                (platform, p) => IsValidPlatformTarget(platform, p),
                new Dictionary<int, Dictionary<Thing, bool>> { { mapId, reachabilityCache } }
            );

            if (targetPlatform == null)
                return null;

            // Create job for the platform
            return CreateExecutionJob(warden, targetPlatform);
        }

        /// <summary>
        /// Validates if a warden can enslave a specific prisoner
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

            // Check cache update interval
            string cacheKey = this.GetType().Name + PRISONERS_CACHE_SUFFIX + CACHE_KEY_SUFFIX;
            int currentTick = Find.TickManager.TicksGame;
            int lastUpdateTick = Utility_MapCacheManager.GetLastCacheUpdateTick(mapId, cacheKey);

            return currentTick - lastUpdateTick >= CacheUpdateInterval;
        }

        #endregion

        #region Job creation

        /// <summary>
        /// Creates a job for the given prisoner
        /// </summary>
        protected override Job CreateJobForPrisoner(Pawn warden, Pawn prisoner, bool forced)
        {
            // Not used directly for this job giver, but required by base class
            return null;
        }

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

        #region Cache management

        /// <summary>
        /// Reset the cache for this job giver
        /// </summary>
        public override void Reset()
        {
            base.Reset();

            // Clear specific caches for this job giver
            foreach (Map map in Find.Maps)
            {
                int mapId = map.uniqueID;
                string platformCacheKey = this.GetType().Name + PRISONERS_CACHE_SUFFIX + CACHE_KEY_SUFFIX;
                string reachCacheKey = this.GetType().Name + "_ReachCache" + CACHE_KEY_SUFFIX;

                // Clear platform cache
                var platformCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Thing>>(mapId);
                if (platformCache.ContainsKey(platformCacheKey))
                {
                    platformCache.Remove(platformCacheKey);
                    Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, platformCacheKey, -1);
                }

                // Clear reachability cache
                var reachabilityCache = Utility_MapCacheManager.GetOrCreateMapCache<Thing, bool>(mapId);
                reachabilityCache.Clear();
                Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, reachCacheKey, -1);
            }

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Reset execute entity cache for {this.GetType().Name}");
            }
        }

        #endregion

        #region Debug support

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