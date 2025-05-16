using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to feed hungry prisoners.
    /// Optimized to use the PawnControl framework for better performance.
    /// </summary>
    public class JobGiver_Warden_Feed_PawnControl : JobGiver_Warden_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "Feed";

        /// <summary>
        /// Cache update interval in ticks (120 ticks = 2 seconds)
        /// </summary>
        protected override int CacheUpdateInterval => base.CacheUpdateInterval;

        /// <summary>
        /// Distance thresholds for bucketing (10, 20, 30 tiles)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 100f, 400f, 900f };

        /// <summary>
        /// Cache key identifier for this specific job giver
        /// </summary>
        private const string CACHE_KEY_SUFFIX = "_Feed";

        #endregion

        #region Initialization

        /// <summary>
        /// Reset the cache for feed job giver
        /// </summary>
        public static void ResetFeedPrisonerCache()
        {
            // Clear all feed-prisoner related caches from all maps
            foreach (Map map in Find.Maps)
            {
                int mapId = map.uniqueID;
                string cacheKey = typeof(JobGiver_Warden_Feed_PawnControl).Name + PRISONERS_CACHE_SUFFIX + CACHE_KEY_SUFFIX;
                string reachCacheKey = typeof(JobGiver_Warden_Feed_PawnControl).Name + "_ReachCache" + CACHE_KEY_SUFFIX;

                var prisonerCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Pawn>>(mapId);
                if (prisonerCache.ContainsKey(cacheKey))
                {
                    prisonerCache.Remove(cacheKey);
                    Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, -1);
                }

                // Clear reachability cache
                var reachabilityCache = Utility_MapCacheManager.GetOrCreateMapCache<Pawn, bool>(mapId);
                reachabilityCache.Clear();
                Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, reachCacheKey, -1);
            }

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal("Reset all feed prisoner caches");
            }
        }

        #endregion

        #region Core flow

        /// <summary>
        /// Feeding prisoners has moderate priority
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            // Feeding prisoners is important to prevent starvation
            return 6.2f;
        }

        /// <summary>
        /// Try to give a job, using the centralized cache system
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (ShouldSkip(pawn))
            {
                return null;
            }

            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Warden_Feed_PawnControl>(
                pawn,
                WorkTag,
                CreateJobFromCachedTargets,
                debugJobDesc: "feed prisoner");
        }

        /// <summary>
        /// Create a job using the cached targets
        /// </summary>
        private Job CreateJobFromCachedTargets(Pawn pawn, bool forced)
        {
            // Process cached targets to create job
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;

            // Get hungry prisoners from cache using the proper cache key
            string cacheKey = this.GetType().Name + PRISONERS_CACHE_SUFFIX + CACHE_KEY_SUFFIX;
            var prisonerCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Pawn>>(mapId);
            List<Pawn> prisonerList = prisonerCache.TryGetValue(cacheKey, out var cachedList) ? cachedList : null;

            List<Thing> targets;
            if (prisonerList != null)
            {
                targets = new List<Thing>(prisonerList.Cast<Thing>());
            }
            else
            {
                // If cache miss, update the cache
                var freshPrisoners = GetPrisonersMatchingCriteria(pawn.Map).ToList();
                prisonerCache[cacheKey] = freshPrisoners;
                Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, Find.TickManager.TicksGame);
                targets = new List<Thing>(freshPrisoners.Cast<Thing>());
            }

            return ProcessCachedTargets(pawn, targets, forced);
        }

        #endregion

        #region Target processing

        /// <summary>
        /// Get all prisoners eligible for feeding
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null) yield break;

            // Get all prisoner pawns on the map
            foreach (Pawn prisoner in map.mapPawns.PrisonersOfColonySpawned)
            {
                if (FilterHungryPrisoners(prisoner))
                {
                    yield return prisoner;
                }
            }
        }

        /// <summary>
        /// Get prisoners matching specific criteria for this job giver
        /// </summary>
        protected override IEnumerable<Pawn> GetPrisonersMatchingCriteria(Map map)
        {
            if (map == null) yield break;

            // Get all hungry prisoners on the map
            foreach (Pawn prisoner in map.mapPawns.PrisonersOfColonySpawned)
            {
                if (FilterHungryPrisoners(prisoner))
                {
                    yield return prisoner;
                }
            }
        }

        /// <summary>
        /// Filter function to identify prisoners that need to be fed
        /// </summary>
        private bool FilterHungryPrisoners(Pawn prisoner)
        {
            return WardenFeedUtility.ShouldBeFed(prisoner) &&
                   prisoner.needs?.food != null &&
                   prisoner.needs.food.CurLevelPercentage < prisoner.needs.food.PercentageThreshHungry + 0.02f;
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
                (prisoner) => (prisoner.Position - warden.Position).LengthHorizontalSquared,
                DistanceThresholds
            );

            // Get reachability cache for this job giver
            string reachCacheKey = this.GetType().Name + "_ReachCache" + CACHE_KEY_SUFFIX;
            var reachabilityCache = Utility_MapCacheManager.GetOrCreateMapCache<Pawn, bool>(mapId);

            // Find the first valid prisoner to feed
            Pawn targetPrisoner = Utility_JobGiverManager.FindFirstValidTargetInBuckets<Pawn>(
                buckets,
                warden,
                (prisoner, p) => IsValidPrisonerTarget(prisoner, p),
                WorkTag
            );

            if (targetPrisoner == null)
                return null;

            // Create job for the prisoner
            return CreateJobForPrisoner(warden, targetPrisoner, forced);
        }

        /// <summary>
        /// Validates if a warden can feed a specific prisoner
        /// </summary>
        protected override bool IsValidPrisonerTarget(Pawn prisoner, Pawn warden)
        {
            if (prisoner?.guest == null || !prisoner.IsPrisoner)
                return false;

            // Skip if no longer a valid hungry prisoner
            if (!WardenFeedUtility.ShouldBeFed(prisoner) ||
                prisoner.needs?.food == null ||
                prisoner.needs.food.CurLevelPercentage >= prisoner.needs.food.PercentageThreshHungry + 0.02f)
                return false;

            // Check food restriction policy
            if (prisoner.foodRestriction != null)
            {
                FoodPolicy respectedRestriction = prisoner.foodRestriction.GetCurrentRespectedRestriction(warden);
                if (respectedRestriction != null && respectedRestriction.filter.AllowedDefCount == 0)
                    return false;
            }

            // Check basic reachability
            if (!prisoner.Spawned || prisoner.IsForbidden(warden) ||
                !warden.CanReserve(prisoner, 1, -1, null, false))
                return false;

            // Check if there's food available for this prisoner
            Thing foodSource;
            ThingDef foodDef;
            return FoodUtility.TryFindBestFoodSourceFor(warden, prisoner,
                prisoner.needs.food.CurCategory == HungerCategory.Starving,
                out foodSource, out foodDef, false, allowCorpse: false);
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

            // Check for hungry prisoners to avoid unnecessary cache updates
            bool hasHungryPrisoners = map.mapPawns.PrisonersOfColonySpawned.Any(p =>
                WardenFeedUtility.ShouldBeFed(p) && p.needs?.food != null &&
                p.needs.food.CurLevelPercentage < p.needs.food.PercentageThreshHungry + 0.02f);

            if (!hasHungryPrisoners)
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
            return CreateFeedPrisonerJob(warden, prisoner);
        }

        /// <summary>
        /// Creates a job to feed a prisoner
        /// </summary>
        private Job CreateFeedPrisonerJob(Pawn warden, Pawn prisoner)
        {
            // Find best food source
            Thing foodSource;
            ThingDef foodDef;
            if (FoodUtility.TryFindBestFoodSourceFor(warden, prisoner,
                prisoner.needs.food.CurCategory == HungerCategory.Starving,
                out foodSource, out foodDef, false, allowCorpse: false))
            {
                float nutrition = FoodUtility.GetNutrition(prisoner, foodSource, foodDef);
                Job job = JobMaker.MakeJob(JobDefOf.FeedPatient, foodSource, prisoner);
                job.count = FoodUtility.WillIngestStackCountOf(prisoner, foodDef, nutrition);

                string foodInfo = "";
                if (FoodUtility.MoodFromIngesting(prisoner, foodSource, FoodUtility.GetFinalIngestibleDef(foodSource)) < 0)
                    foodInfo = " (disliked food)";

                Utility_DebugManager.LogNormal($"{warden.LabelShort} created job to feed prisoner {prisoner.LabelShort}{foodInfo}");

                return job;
            }

            return null;
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
                string prisonerCacheKey = this.GetType().Name + PRISONERS_CACHE_SUFFIX + CACHE_KEY_SUFFIX;
                string reachCacheKey = this.GetType().Name + "_ReachCache" + CACHE_KEY_SUFFIX;

                // Clear prisoner cache
                var prisonerCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Pawn>>(mapId);
                if (prisonerCache.ContainsKey(prisonerCacheKey))
                {
                    prisonerCache.Remove(prisonerCacheKey);
                    Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, prisonerCacheKey, -1);
                }

                // Clear reachability cache
                var reachabilityCache = Utility_MapCacheManager.GetOrCreateMapCache<Pawn, bool>(mapId);
                reachabilityCache.Clear();
                Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, reachCacheKey, -1);
            }

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Reset feed prisoner cache for {this.GetType().Name}");
            }
        }

        #endregion

        #region Debug support

        /// <summary>
        /// For debugging purposes
        /// </summary>
        public override string ToString()
        {
            return "JobGiver_Warden_Feed_PawnControl";
        }

        #endregion
    }
}