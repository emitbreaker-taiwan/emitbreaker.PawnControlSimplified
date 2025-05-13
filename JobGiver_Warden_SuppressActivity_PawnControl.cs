using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks for wardens to suppress activity in anomalous entities
    /// </summary>
    public class JobGiver_Warden_SuppressActivity_PawnControl : JobGiver_Warden_PawnControl
    {
        #region Configuration

        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.ActivitySuppression;

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "SuppressActivity";

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
        private const string CACHE_KEY_SUFFIX = "_SuppressActivity";

        #endregion

        #region Initialization

        /// <summary>
        /// Reset the cache for activity suppression job giver
        /// </summary>
        public static void ResetSuppressionCache()
        {
            // Clear all activity suppression related caches from all maps
            foreach (Map map in Find.Maps)
            {
                int mapId = map.uniqueID;
                string cacheKey = typeof(JobGiver_Warden_SuppressActivity_PawnControl).Name + PRISONERS_CACHE_SUFFIX + CACHE_KEY_SUFFIX;
                string reachCacheKey = typeof(JobGiver_Warden_SuppressActivity_PawnControl).Name + "_ReachCache" + CACHE_KEY_SUFFIX;

                var thingCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Thing>>(mapId);
                if (thingCache.ContainsKey(cacheKey))
                {
                    thingCache.Remove(cacheKey);
                    Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, -1);
                }

                // Clear reachability cache
                var reachabilityCache = Utility_MapCacheManager.GetOrCreateMapCache<Thing, bool>(mapId);
                reachabilityCache.Clear();
                Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, reachCacheKey, -1);
            }

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal("Reset all activity suppression caches");
            }
        }

        #endregion

        #region Core flow

        /// <summary>
        /// Activity suppression has high priority
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            // Activity suppression has high priority
            return 6.5f;
        }

        /// <summary>
        /// Try to give a job, using the centralized cache system
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            // Check if anomaly mod is active
            if (!ModsConfig.AnomalyActive)
                return null;

            // Check if pawn can actually suppress activity
            if (StatDefOf.ActivitySuppressionRate.Worker.IsDisabledFor(pawn) ||
                StatDefOf.ActivitySuppressionRate.Worker.GetValue(pawn) <= 0f)
            {
                return null;
            }

            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Warden_SuppressActivity_PawnControl>(
                pawn,
                WorkTag,
                CreateJobFromCachedTargets,
                debugJobDesc: "suppress activity");
        }

        /// <summary>
        /// Create a job using the cached targets
        /// </summary>
        private Job CreateJobFromCachedTargets(Pawn pawn, bool forced)
        {
            // Process cached targets to create job
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;

            // Get targets from cache using the proper cache key
            string cacheKey = this.GetType().Name + PRISONERS_CACHE_SUFFIX + CACHE_KEY_SUFFIX;
            var thingCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Thing>>(mapId);
            List<Thing> thingList = thingCache.TryGetValue(cacheKey, out var cachedList) ? cachedList : null;

            List<Thing> targets;
            if (thingList != null)
            {
                targets = new List<Thing>(thingList);
            }
            else
            {
                // If cache miss, update the cache
                var freshTargets = GetTargets(pawn.Map).ToList();
                thingCache[cacheKey] = freshTargets;
                Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, Find.TickManager.TicksGame);
                targets = freshTargets;
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

            // Check if pawn can actually suppress activity
            if (StatDefOf.ActivitySuppressionRate.Worker.IsDisabledFor(pawn) ||
                StatDefOf.ActivitySuppressionRate.Worker.GetValue(pawn) <= 0f)
            {
                return true;
            }

            // Use base checks next
            return base.ShouldSkip(pawn);
        }

        #endregion

        #region Target processing

        /// <summary>
        /// Get all suppressable things on the map
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null) yield break;

            // Find all suppressable things on the map
            foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Suppressable))
            {
                if (thing != null && !thing.Destroyed && thing.Spawned)
                {
                    Thing thingToSuppress = GetThingToSuppress(thing, false);
                    if (thingToSuppress != null)
                    {
                        yield return thing;
                    }
                }
            }
        }

        /// <summary>
        /// Get prisoners matching specific criteria for this job giver
        /// Override but return empty since we're dealing with things, not prisoners
        /// </summary>
        protected override IEnumerable<Pawn> GetPrisonersMatchingCriteria(Map map)
        {
            // This method is not applicable for activity suppression
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

            // Sort targets by activity level for prioritization
            targets = targets.OrderByDescending(t => {
                Thing thingToSuppress = GetThingToSuppress(t, forced);
                if (thingToSuppress != null)
                {
                    return thingToSuppress.TryGetComp<CompActivity>()?.ActivityLevel ?? 0f;
                }
                return 0f;
            }).ToList();

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

            // Find the first valid thing to suppress
            Thing targetThing = Utility_JobGiverManager.FindFirstValidTargetInBuckets<Thing>(
                buckets,
                warden,
                (thing, p) => IsValidSuppressionTarget(thing, p, forced),
                new Dictionary<int, Dictionary<Thing, bool>> { { mapId, reachabilityCache } }
            );

            if (targetThing == null)
                return null;

            // Create job for the thing
            return CreateJobForThing(warden, targetThing, forced);
        }

        /// <summary>
        /// Check if this thing is a valid target for activity suppression
        /// </summary>
        private bool IsValidSuppressionTarget(Thing thing, Pawn warden, bool forced)
        {
            // Get the actual thing to suppress
            Thing thingToSuppress = GetThingToSuppress(thing, forced);
            if (thingToSuppress == null)
                return false;

            // Get the activity component
            CompActivity compActivity = thingToSuppress.TryGetComp<CompActivity>();
            if (compActivity == null)
                return false;

            // Check if suppression is possible
            if (!ActivitySuppressionUtility.CanBeSuppressed(thingToSuppress, true, forced))
                return false;

            // Check activity level threshold unless forced
            if (!forced && compActivity.ActivityLevel < compActivity.suppressIfAbove)
                return false;

            // Check if warden can reserve the target
            if (!warden.CanReserve(thingToSuppress, 1, -1, null, forced))
                return false;

            // Special case for holding platforms
            if (thingToSuppress.ParentHolder is Building_HoldingPlatform platform &&
                !warden.CanReserve(platform, 1, -1, null, forced))
                return false;

            // Check if warden can stand near the thing
            if (!InteractionUtility.TryGetAdjacentInteractionCell(warden, thingToSuppress, forced, out var _))
                return false;

            return true;
        }

        /// <summary>
        /// We don't need to use this method since we're dealing with general things, not prisoners
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
            // Check if there are any suppressable things on the map
            Map map = Find.Maps.Find(m => m.uniqueID == mapId);
            if (map == null || !map.listerThings.ThingsInGroup(ThingRequestGroup.Suppressable).Any(
                t => GetThingToSuppress(t, false) != null))
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
        /// Required by parent class but not used directly
        /// </summary>
        protected override Job CreateJobForPrisoner(Pawn warden, Pawn prisoner, bool forced)
        {
            // Not applicable for this job giver
            return null;
        }

        /// <summary>
        /// Creates a job for the given thing
        /// </summary>
        private Job CreateJobForThing(Pawn warden, Thing target, bool forced)
        {
            return CreateSuppressionJob(warden, target, forced);
        }

        /// <summary>
        /// Creates the activity suppression job
        /// </summary>
        private Job CreateSuppressionJob(Pawn warden, Thing target, bool forced)
        {
            Job job = JobMaker.MakeJob(WorkJobDef, target);
            job.playerForced = forced;

            Thing thingToSuppress = GetThingToSuppress(target, forced);
            if (thingToSuppress != null)
            {
                Utility_DebugManager.LogNormal($"{warden.LabelShort} created job to suppress activity in {thingToSuppress.LabelShort}");
            }

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
                string thingCacheKey = this.GetType().Name + PRISONERS_CACHE_SUFFIX + CACHE_KEY_SUFFIX;
                string reachCacheKey = this.GetType().Name + "_ReachCache" + CACHE_KEY_SUFFIX;

                // Clear thing cache
                var thingCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Thing>>(mapId);
                if (thingCache.ContainsKey(thingCacheKey))
                {
                    thingCache.Remove(thingCacheKey);
                    Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, thingCacheKey, -1);
                }

                // Clear reachability cache
                var reachabilityCache = Utility_MapCacheManager.GetOrCreateMapCache<Thing, bool>(mapId);
                reachabilityCache.Clear();
                Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, reachCacheKey, -1);
            }

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Reset suppress activity cache for {this.GetType().Name}");
            }
        }

        #endregion

        #region Utility

        /// <summary>
        /// Get the actual thing to suppress from a potential target
        /// </summary>
        private Thing GetThingToSuppress(Thing thing, bool playerForced)
        {
            Thing thingToSuppress = thing;

            // Handle holding platforms
            if (thing is Building_HoldingPlatform platform)
            {
                thingToSuppress = platform.HeldPawn;
            }

            // Check if the thing can be suppressed
            if (thingToSuppress == null || !ActivitySuppressionUtility.CanBeSuppressed(thingToSuppress, true, playerForced))
            {
                return null;
            }

            return thingToSuppress;
        }

        #endregion

        #region Debug support

        /// <summary>
        /// For debugging purposes
        /// </summary>
        public override string ToString()
        {
            return "JobGiver_Warden_SuppressActivity_PawnControl";
        }

        #endregion
    }
}