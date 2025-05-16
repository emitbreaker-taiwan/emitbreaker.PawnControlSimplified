using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns execution jobs to eligible wardens for guilty colonists.
    /// Optimized to use the PawnControl framework for better performance.
    /// </summary>
    public class JobGiver_Warden_ExecuteGuilty_PawnControl : JobGiver_Warden_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "ExecuteGuilty";

        /// <summary>
        /// Cache update interval in ticks (300 ticks = 5 seconds)
        /// </summary>
        protected override int CacheUpdateInterval => base.CacheUpdateInterval;

        /// <summary>
        /// Distance thresholds for bucketing (10, 20, 30 tiles)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 100f, 400f, 900f };

        /// <summary>
        /// Cache key identifier for this specific job giver
        /// </summary>
        private const string CACHE_KEY_SUFFIX = "_ExecuteGuilty";

        #endregion

        #region Initialization

        /// <summary>
        /// Reset the cache for this job giver
        /// </summary>
        public static void ResetExecuteGuiltyCache()
        {
            // Clear all execute guilty related caches from all maps
            foreach (Map map in Find.Maps)
            {
                int mapId = map.uniqueID;
                string cacheKey = typeof(JobGiver_Warden_ExecuteGuilty_PawnControl).Name + PRISONERS_CACHE_SUFFIX + CACHE_KEY_SUFFIX;
                string reachCacheKey = typeof(JobGiver_Warden_ExecuteGuilty_PawnControl).Name + "_ReachCache" + CACHE_KEY_SUFFIX;

                var colonistCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Pawn>>(mapId);
                if (colonistCache.ContainsKey(cacheKey))
                {
                    colonistCache.Remove(cacheKey);
                    Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, -1);
                }

                // Clear reachability cache
                var reachabilityCache = Utility_MapCacheManager.GetOrCreateMapCache<Pawn, bool>(mapId);
                reachabilityCache.Clear();
                Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, reachCacheKey, -1);
            }

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal("Reset all execute guilty caches");
            }
        }

        #endregion

        #region Core flow

        /// <summary>
        /// Execution has high priority
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            // Execution is important but not an emergency
            return 6.5f;
        }

        /// <summary>
        /// Try to give a job, using the centralized cache system
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            // Skip if pawn is incapable of violence
            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                return null;
            }

            if (ShouldSkip(pawn))
            {
                return null;
            }

            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Warden_ExecuteGuilty_PawnControl>(
                pawn,
                WorkTag,
                CreateJobFromCachedTargets,
                debugJobDesc: "execute guilty colonist");
        }

        /// <summary>
        /// Create a job using the cached targets
        /// </summary>
        private Job CreateJobFromCachedTargets(Pawn pawn, bool forced)
        {
            // Process cached targets to create job
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;

            // Get guilty colonists from cache using the proper cache key
            string cacheKey = this.GetType().Name + PRISONERS_CACHE_SUFFIX + CACHE_KEY_SUFFIX;
            var colonistCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Pawn>>(mapId);
            List<Pawn> guiltyList = colonistCache.TryGetValue(cacheKey, out var cachedList) ? cachedList : null;

            List<Thing> targets;
            if (guiltyList != null)
            {
                targets = new List<Thing>(guiltyList.Cast<Thing>());
            }
            else
            {
                // If cache miss, update the cache with guilty colonists
                var freshGuiltyColonists = GetPrisonersMatchingCriteria(pawn.Map).ToList();
                colonistCache[cacheKey] = freshGuiltyColonists;
                Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, Find.TickManager.TicksGame);
                targets = new List<Thing>(freshGuiltyColonists.Cast<Thing>());
            }

            return ProcessCachedTargets(pawn, targets, forced);
        }

        /// <summary>
        /// Determines if this job giver should be skipped for the given pawn
        /// </summary>
        public override bool ShouldSkip(Pawn pawn)
        {
            // Use base checks first
            if (base.ShouldSkip(pawn))
                return true;

            // Cannot execute if incapable of violence
            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
                return true;

            return false;
        }

        #endregion

        #region Target processing

        /// <summary>
        /// Get all guilty colonists eligible for execution
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null) yield break;

            // Get all colonists on the map
            foreach (Pawn colonist in map.mapPawns.FreeColonistsSpawned)
            {
                if (FilterGuiltyColonists(colonist))
                {
                    yield return colonist;
                }
            }
        }

        /// <summary>
        /// Get guilty colonists matching specific criteria for this job giver
        /// </summary>
        protected override IEnumerable<Pawn> GetPrisonersMatchingCriteria(Map map)
        {
            if (map == null) yield break;

            // In this case, we're dealing with guilty colonists, not prisoners
            foreach (Pawn colonist in map.mapPawns.FreeColonistsSpawned)
            {
                if (FilterGuiltyColonists(colonist))
                {
                    yield return colonist;
                }
            }
        }

        /// <summary>
        /// Filter function to identify guilty colonists awaiting execution
        /// </summary>
        private bool FilterGuiltyColonists(Pawn colonist)
        {
            return colonist?.guilt != null &&
                   colonist.guilt.IsGuilty &&
                   colonist.guilt.awaitingExecution &&
                   !colonist.InAggroMentalState &&
                   !colonist.IsFormingCaravan();
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
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets<Pawn>(
                warden,
                targets.ConvertAll(t => t as Pawn),
                (colonist) => (colonist.Position - warden.Position).LengthHorizontalSquared,
                DistanceThresholds
            );

            // Get reachability cache for this job giver
            string reachCacheKey = this.GetType().Name + "_ReachCache" + CACHE_KEY_SUFFIX;
            var reachabilityCache = Utility_MapCacheManager.GetOrCreateMapCache<Pawn, bool>(mapId);

            // Find the first valid colonist to execute
            Pawn targetColonist = Utility_JobGiverManager.FindFirstValidTargetInBuckets<Pawn>(
                buckets,
                warden,
                (colonist, p) => IsValidGuiltyTarget(colonist, p),
                WorkTag
            );

            if (targetColonist == null)
                return null;

            // Create job for the guilty colonist
            return CreateJobForPrisoner(warden, targetColonist, forced);
        }

        /// <summary>
        /// Validates if a warden can execute a specific guilty colonist
        /// </summary>
        protected override bool IsValidPrisonerTarget(Pawn colonist, Pawn warden)
        {
            return IsValidGuiltyTarget(colonist, warden);
        }

        /// <summary>
        /// Specialized check for guilty colonists to be executed
        /// </summary>
        private bool IsValidGuiltyTarget(Pawn colonist, Pawn warden)
        {
            // Skip if no longer valid target
            if (colonist?.guilt == null ||
                !colonist.guilt.IsGuilty ||
                !colonist.guilt.awaitingExecution ||
                colonist.InAggroMentalState ||
                colonist.IsFormingCaravan())
                return false;

            // Check if warden is capable of violence
            if (warden.WorkTagIsDisabled(WorkTags.Violent))
                return false;

            // Check basic reachability
            if (colonist.IsForbidden(warden) ||
                !warden.CanReserveAndReach(colonist, PathEndMode.Touch, warden.NormalMaxDanger()))
                return false;

            // Check if this action is allowed by ideology system
            return new HistoryEvent(HistoryEventDefOf.ExecutedColonist, warden.Named(HistoryEventArgsNames.Doer)).Notify_PawnAboutToDo_Job();
        }

        /// <summary>
        /// Determines if the job giver should execute based on cache status
        /// </summary>
        protected override bool ShouldExecuteNow(int mapId)
        {
            // Check if there's any free colonists on the map
            Map map = Find.Maps.Find(m => m.uniqueID == mapId);
            if (map == null || map.mapPawns.FreeColonistsSpawnedCount == 0)
                return false;

            // Use guilty check to avoid unnecessary cache updates
            bool hasGuilty = map.mapPawns.FreeColonistsSpawned.Any(c =>
                c?.guilt != null && c.guilt.IsGuilty && c.guilt.awaitingExecution);

            if (!hasGuilty)
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
        /// Creates a job for the given guilty colonist
        /// </summary>
        protected override Job CreateJobForPrisoner(Pawn warden, Pawn colonist, bool forced)
        {
            return CreateGuiltyExecutionJob(warden, colonist);
        }

        /// <summary>
        /// Creates the execution job for the warden
        /// </summary>
        private Job CreateGuiltyExecutionJob(Pawn warden, Pawn colonist)
        {
            Job job = JobMaker.MakeJob(JobDefOf.GuiltyColonistExecution, colonist);

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"{warden.LabelShort} created job to execute guilty colonist {colonist.LabelShort}");
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
                string colonistCacheKey = this.GetType().Name + PRISONERS_CACHE_SUFFIX + CACHE_KEY_SUFFIX;
                string reachCacheKey = this.GetType().Name + "_ReachCache" + CACHE_KEY_SUFFIX;

                // Clear guilty colonist cache
                var colonistCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Pawn>>(mapId);
                if (colonistCache.ContainsKey(colonistCacheKey))
                {
                    colonistCache.Remove(colonistCacheKey);
                    Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, colonistCacheKey, -1);
                }

                // Clear reachability cache
                var reachabilityCache = Utility_MapCacheManager.GetOrCreateMapCache<Pawn, bool>(mapId);
                reachabilityCache.Clear();
                Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, reachCacheKey, -1);
            }

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Reset execute guilty cache for {this.GetType().Name}");
            }
        }

        #endregion

        #region Debug support

        /// <summary>
        /// For debugging purposes
        /// </summary>
        public override string ToString()
        {
            return "JobGiver_Warden_ExecuteGuilty_PawnControl";
        }

        #endregion
    }
}