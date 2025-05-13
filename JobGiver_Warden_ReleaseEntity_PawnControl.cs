using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks for wardens to release entities from holding platforms
    /// </summary>
    public class JobGiver_Warden_ReleaseEntity_PawnControl : JobGiver_Warden_PawnControl
    {
        #region Configuration

        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.ReleaseEntity;

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "ReleaseEntity";

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
        private const string CACHE_KEY_SUFFIX = "_ReleaseEntity";

        #endregion

        #region Initialization

        /// <summary>
        /// Reset the cache for release entity job giver
        /// </summary>
        public static void ResetReleaseEntityCache()
        {
            // Clear all release-entity related caches from all maps
            foreach (Map map in Find.Maps)
            {
                int mapId = map.uniqueID;
                string cacheKey = typeof(JobGiver_Warden_ReleaseEntity_PawnControl).Name + PRISONERS_CACHE_SUFFIX + CACHE_KEY_SUFFIX;
                string reachCacheKey = typeof(JobGiver_Warden_ReleaseEntity_PawnControl).Name + "_ReachCache" + CACHE_KEY_SUFFIX;

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
                Utility_DebugManager.LogNormal("Reset all release entity caches");
            }
        }

        #endregion

        #region Core flow

        /// <summary>
        /// Releasing entities has medium-high priority
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            // Releasing entities has medium-high priority
            return 5.5f;
        }

        /// <summary>
        /// Try to give a job, using the centralized cache system
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            // Check if anomaly mod is active
            if (!ModsConfig.AnomalyActive)
                return null;

            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Warden_ReleaseEntity_PawnControl>(
                pawn,
                WorkTag,
                CreateJobFromCachedTargets,
                debugJobDesc: "release entity");
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
            // Check if anomaly mod is active
            if (!ModsConfig.AnomalyActive)
                return true;

            // Use base checks next
            return base.ShouldSkip(pawn);
        }

        #endregion

        #region Target processing

        /// <summary>
        /// Get all holding platforms with entities ready to be released
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null) yield break;

            // Find all holding platforms on the map
            foreach (Building_HoldingPlatform platform in map.listerBuildings.AllBuildingsColonistOfClass<Building_HoldingPlatform>())
            {
                if (platform != null && !platform.Destroyed && platform.Spawned)
                {
                    Pawn entity = GetEntity(platform);
                    if (entity != null)
                    {
                        yield return platform;
                    }
                }
            }
        }

        /// <summary>
        /// Get platforms matching criteria for this job giver - overriding parent method
        /// </summary>
        protected override IEnumerable<Pawn> GetPrisonersMatchingCriteria(Map map)
        {
            // This method is not used for platforms
            yield break;
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

            // Get reachability cache for this job giver
            string reachCacheKey = this.GetType().Name + "_ReachCache" + CACHE_KEY_SUFFIX;
            var reachabilityCache = Utility_MapCacheManager.GetOrCreateMapCache<Thing, bool>(mapId);

            // Find the first valid platform to work with
            Thing targetPlatform = Utility_JobGiverManager.FindFirstValidTargetInBuckets<Thing>(
                buckets,
                warden,
                (thing, p) => IsValidPlatformTarget(thing, p, forced),
                new Dictionary<int, Dictionary<Thing, bool>> { { mapId, reachabilityCache } }
            );

            if (targetPlatform == null || !(targetPlatform is Building_HoldingPlatform platform))
                return null;

            // Get the entity to release
            Pawn entity = GetEntity(platform);
            if (entity == null)
                return null;

            // Create release job
            return CreateJobForPlatform(warden, platform, entity, forced);
        }

        /// <summary>
        /// Check if this platform is a valid target for releasing an entity
        /// </summary>
        private bool IsValidPlatformTarget(Thing platform, Pawn warden, bool forced)
        {
            // Check if the platform exists and is valid
            if (platform == null || !platform.Spawned || platform.Destroyed)
                return false;

            // Check if warden can access the platform
            if (!warden.CanReach(platform, PathEndMode.Touch, Danger.Deadly))
                return false;

            // Check if warden can reserve the platform
            if (!warden.CanReserve(platform, 1, -1, null, forced))
                return false;

            // Check if the platform has a releasable entity
            Pawn entity = GetEntity(platform);
            if (entity == null)
                return false;

            return true;
        }

        /// <summary>
        /// We don't need to use this method since we're dealing with platforms, not prisoners
        /// But we need to implement it since it's abstract in the base class
        /// </summary>
        protected override bool IsValidPrisonerTarget(Pawn prisoner, Pawn warden)
        {
            return false;
        }

        /// <summary>
        /// Determines if the job giver should execute based on cache status
        /// </summary>
        protected override bool ShouldExecuteNow(int mapId)
        {
            // Check if there are any holding platforms on the map
            Map map = Find.Maps.Find(m => m.uniqueID == mapId);
            if (map == null || !map.listerBuildings.AllBuildingsColonistOfClass<Building_HoldingPlatform>().Any())
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
        /// Creates a job for the given prisoner - required by parent class
        /// </summary>
        protected override Job CreateJobForPrisoner(Pawn warden, Pawn prisoner, bool forced)
        {
            // Not used for this job giver since we're working with platforms
            return null;
        }

        /// <summary>
        /// Creates a job for the given platform and entity
        /// </summary>
        private Job CreateJobForPlatform(Pawn warden, Building_HoldingPlatform platform, Pawn entity, bool forced)
        {
            return CreateReleaseJob(warden, platform, entity);
        }

        /// <summary>
        /// Creates the release job for the warden
        /// </summary>
        private Job CreateReleaseJob(Pawn warden, Building_HoldingPlatform platform, Pawn entity)
        {
            Job job = JobMaker.MakeJob(WorkJobDef, platform, entity);
            job.count = 1;

            Utility_DebugManager.LogNormal($"{warden.LabelShort} created job to release entity {entity.LabelShort}");

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
                Utility_DebugManager.LogNormal($"Reset release entity cache for {this.GetType().Name}");
            }
        }

        #endregion

        #region Utility

        /// <summary>
        /// Get the entity from a holding platform if it's ready to be released
        /// </summary>
        private Pawn GetEntity(Thing thing)
        {
            if (thing is Building_HoldingPlatform platform && platform.HeldPawn != null)
            {
                Pawn heldPawn = platform.HeldPawn;

                // Check if the entity can be released
                CompHoldingPlatformTarget compHoldingPlatformTarget = heldPawn.TryGetComp<CompHoldingPlatformTarget>();
                if (compHoldingPlatformTarget != null && compHoldingPlatformTarget.containmentMode == EntityContainmentMode.Release)
                {
                    return heldPawn;
                }
            }

            return null;
        }

        #endregion

        #region Debug support

        /// <summary>
        /// For debugging purposes
        /// </summary>
        public override string ToString()
        {
            return "JobGiver_Warden_ReleaseEntity_PawnControl";
        }

        #endregion
    }
}