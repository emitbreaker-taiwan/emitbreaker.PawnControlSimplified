using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to feed hungry prisoners.
    /// Optimized to use the PawnControl framework for better performance.
    /// </summary>
    public class JobGiver_Warden_Feed_PawnControl : ThinkNode_JobGiver
    {
        // Cached prisoners that need to be fed
        private static readonly Dictionary<int, List<Pawn>> _hungryPrisonerCache = new Dictionary<int, List<Pawn>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 120; // Update every 2 seconds

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 100f, 400f, 900f }; // 10, 20, 30 tiles

        public override float GetPriority(Pawn pawn)
        {
            // Feeding prisoners is important to prevent starvation
            return 6.2f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<Plant>(
                pawn,
                "Warden",
                (p, forced) => {
                    // Update plant cache
                    UpdateHungryPrisonerCache(p.Map);

                    // Find and create a job for cutting plants with VALID DESIGNATORS ONLY
                    return TryCreateFeedPrisonerJob(p);
                },
                debugJobDesc: "feed prisoner assignment");
        }

        /// <summary>
        /// Updates the cache of prisoners that need to be fed
        /// </summary>
        private void UpdateHungryPrisonerCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_hungryPrisonerCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_hungryPrisonerCache.ContainsKey(mapId))
                    _hungryPrisonerCache[mapId].Clear();
                else
                    _hungryPrisonerCache[mapId] = new List<Pawn>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Pawn, bool>();

                // Find all prisoners who need to be fed
                foreach (Pawn prisoner in map.mapPawns.PrisonersOfColonySpawned)
                {
                    if (prisoner == null || prisoner.Destroyed || !prisoner.Spawned)
                        continue;

                    // Check if prisoner should be fed
                    if (WardenFeedUtility.ShouldBeFed(prisoner) &&
                        prisoner.needs?.food != null &&
                        prisoner.needs.food.CurLevelPercentage < prisoner.needs.food.PercentageThreshHungry + 0.02f)
                    {
                        _hungryPrisonerCache[mapId].Add(prisoner);
                    }
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Create a job to feed a prisoner using manager-driven bucket processing
        /// </summary>
        private Job TryCreateFeedPrisonerJob(Pawn pawn)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_hungryPrisonerCache.ContainsKey(mapId) || _hungryPrisonerCache[mapId].Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing and target selection
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                _hungryPrisonerCache[mapId],
                (prisoner) => (prisoner.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Find the best prisoner to feed
            Pawn targetPrisoner = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (prisoner, p) => {
                    // IMPORTANT: Check faction interaction validity first
                    if (!Utility_JobGiverManager.IsValidFactionInteraction(prisoner, p, requiresDesignator: false))
                        return false;

                    // Skip if no longer a valid hungry prisoner
                    if (prisoner?.guest == null || !prisoner.IsPrisoner ||
                        !WardenFeedUtility.ShouldBeFed(prisoner) ||
                        prisoner.needs?.food == null ||
                        prisoner.needs.food.CurLevelPercentage >= prisoner.needs.food.PercentageThreshHungry + 0.02f)
                        return false;

                    // Check basic requirements
                    if (!prisoner.Spawned || prisoner.IsForbidden(p) ||
                        !p.CanReserveAndReach(prisoner, PathEndMode.OnCell, p.NormalMaxDanger()))
                        return false;

                    // Check food restriction policy
                    if (prisoner.foodRestriction != null)
                    {
                        FoodPolicy respectedRestriction = prisoner.foodRestriction.GetCurrentRespectedRestriction(p);
                        if (respectedRestriction != null && respectedRestriction.filter.AllowedDefCount == 0)
                            return false;
                    }

                    // Check if there's food available for this prisoner
                    Thing foodSource;
                    ThingDef foodDef;
                    return FoodUtility.TryFindBestFoodSourceFor(p, prisoner,
                        prisoner.needs.food.CurCategory == HungerCategory.Starving,
                        out foodSource, out foodDef, false, allowCorpse: false);
                },
                _reachabilityCache
            );

            // Create job if target found
            if (targetPrisoner != null)
            {
                // Find best food source
                Thing foodSource;
                ThingDef foodDef;
                if (FoodUtility.TryFindBestFoodSourceFor(pawn, targetPrisoner,
                    targetPrisoner.needs.food.CurCategory == HungerCategory.Starving,
                    out foodSource, out foodDef, false, allowCorpse: false))
                {
                    float nutrition = FoodUtility.GetNutrition(targetPrisoner, foodSource, foodDef);
                    Job job = JobMaker.MakeJob(JobDefOf.FeedPatient, foodSource, targetPrisoner);
                    job.count = FoodUtility.WillIngestStackCountOf(targetPrisoner, foodDef, nutrition);

                    if (Prefs.DevMode)
                    {
                        string foodInfo = "";
                        if (FoodUtility.MoodFromIngesting(targetPrisoner, foodSource, FoodUtility.GetFinalIngestibleDef(foodSource)) < 0)
                            foodInfo = " (disliked food)";

                        Log.Message($"[PawnControl] {pawn.LabelShort} created job to feed prisoner {targetPrisoner.LabelShort}{foodInfo}");
                    }

                    return job;
                }
            }

            return null;
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_hungryPrisonerCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_Warden_Feed_PawnControl";
        }
    }
}