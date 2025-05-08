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
            return Utility_JobGiverManager.StandardTryGiveJob<Pawn>(
                pawn,
                "Warden",
                (p, forced) => {
                    // Update prisoner cache with standardized method
                    Utility_JobGiverManager.UpdatePrisonerCache(
                        p.Map,
                        ref _lastCacheUpdateTick,
                        CACHE_UPDATE_INTERVAL,
                        _hungryPrisonerCache,
                        _reachabilityCache,
                        FilterHungryPrisoners);

                    // Create job using standardized method
                    return Utility_JobGiverManager.TryCreatePrisonerInteractionJob(
                        p,
                        _hungryPrisonerCache,
                        _reachabilityCache,
                        ValidateCanFeedPrisoner,
                        CreateFeedPrisonerJob,
                        DISTANCE_THRESHOLDS);
                },
                debugJobDesc: "feed prisoner assignment");
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
        /// Validates if a warden can feed a specific prisoner
        /// </summary>
        private bool ValidateCanFeedPrisoner(Pawn prisoner, Pawn warden)
        {
            // Skip if no longer a valid hungry prisoner
            if (prisoner?.guest == null || !prisoner.IsPrisoner ||
                !WardenFeedUtility.ShouldBeFed(prisoner) ||
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

            // Check if there's food available for this prisoner
            Thing foodSource;
            ThingDef foodDef;
            return FoodUtility.TryFindBestFoodSourceFor(warden, prisoner,
                prisoner.needs.food.CurCategory == HungerCategory.Starving,
                out foodSource, out foodDef, false, allowCorpse: false);
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

                if (Prefs.DevMode)
                {
                    string foodInfo = "";
                    if (FoodUtility.MoodFromIngesting(prisoner, foodSource, FoodUtility.GetFinalIngestibleDef(foodSource)) < 0)
                        foodInfo = " (disliked food)";

                    Utility_DebugManager.LogNormal($"{warden.LabelShort} created job to feed prisoner {prisoner.LabelShort}{foodInfo}");
                }

                return job;
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