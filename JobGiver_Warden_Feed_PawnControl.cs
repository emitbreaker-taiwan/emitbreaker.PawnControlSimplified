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
        protected override int CacheUpdateInterval => 120;

        /// <summary>
        /// Distance thresholds for bucketing (10, 20, 30 tiles)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 100f, 400f, 900f };

        #endregion

        #region Core flow

        protected override float GetBasePriority(string workTag)
        {
            // Feeding prisoners is important to prevent starvation
            return 6.2f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Warden_Feed_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) => {
                    // Process cached targets to create job
                    if (p?.Map == null) return null;

                    int mapId = p.Map.uniqueID;
                    List<Thing> targets;
                    if (_prisonerCache.TryGetValue(mapId, out var prisonerList) && prisonerList != null)
                    {
                        targets = new List<Thing>(prisonerList.Cast<Thing>());
                    }
                    else
                    {
                        targets = new List<Thing>();
                    }

                    return ProcessCachedTargets(p, targets, forced);
                },
                debugJobDesc: "feed prisoner");
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

            // Find the first valid prisoner to feed
            Pawn targetPrisoner = Utility_JobGiverManager.FindFirstValidTargetInBuckets<Pawn>(
                buckets,
                warden,
                (prisoner, p) => IsValidPrisonerTarget(prisoner, p),
                _prisonerReachabilityCache.TryGetValue(mapId, out var cache) ?
                    new Dictionary<int, Dictionary<Pawn, bool>> { { mapId, cache } } :
                    new Dictionary<int, Dictionary<Pawn, bool>>()
            );

            if (targetPrisoner == null)
                return null;

            // Create feeding job
            return CreateFeedPrisonerJob(warden, targetPrisoner);
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

            return !_lastWardenCacheUpdate.TryGetValue(mapId, out int lastUpdate) ||
                   Find.TickManager.TicksGame > lastUpdate + CacheUpdateInterval;
        }

        #endregion

        #region Job creation

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

        #region Utility

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