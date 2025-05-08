using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to deliver food to prisoners who can feed themselves.
    /// Optimized to use the PawnControl framework for better performance.
    /// </summary>
    public class JobGiver_Warden_DeliverFood_PawnControl : ThinkNode_JobGiver
    {
        // Cached prisoners that need food delivered
        private static readonly Dictionary<int, List<Pawn>> _prisonerCache = new Dictionary<int, List<Pawn>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 120; // Update every 2 seconds

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 100f, 400f, 900f }; // 10, 20, 30 tiles

        public override float GetPriority(Pawn pawn)
        {
            // Delivering food to prisoners is important but lower priority than direct feeding
            return 5.8f;
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
                        _prisonerCache,
                        _reachabilityCache,
                        FilterPrisonersNeedingFood);

                    // Create job using standardized method
                    return Utility_JobGiverManager.TryCreatePrisonerInteractionJob(
                        p,
                        _prisonerCache,
                        _reachabilityCache,
                        ValidateCanDeliverFood,
                        CreateDeliverFoodJob,
                        DISTANCE_THRESHOLDS);
                },
                debugJobDesc: "deliver food assignment");
        }

        /// <summary>
        /// Filter function to identify prisoners that need food delivered
        /// </summary>
        private bool FilterPrisonersNeedingFood(Pawn prisoner)
        {
            return prisoner.guest.CanBeBroughtFood &&
                prisoner.Position.IsInPrisonCell(prisoner.Map) &&
                prisoner.needs?.food != null &&
                prisoner.needs.food.CurLevelPercentage < prisoner.needs.food.PercentageThreshHungry + 0.02f &&
                !WardenFeedUtility.ShouldBeFed(prisoner) &&
                !FoodAvailableInRoomTo(prisoner);
        }

        /// <summary>
        /// Validates if food can be delivered to this prisoner
        /// </summary>
        private bool ValidateCanDeliverFood(Pawn prisoner, Pawn warden)
        {
            // Skip if no longer a valid prisoner for food delivery
            if (prisoner?.guest == null || !prisoner.IsPrisoner ||
                !prisoner.guest.CanBeBroughtFood ||
                !prisoner.Position.IsInPrisonCell(prisoner.Map) ||
                prisoner.needs?.food == null ||
                prisoner.needs.food.CurLevelPercentage >= prisoner.needs.food.PercentageThreshHungry + 0.02f ||
                WardenFeedUtility.ShouldBeFed(prisoner) ||
                FoodAvailableInRoomTo(prisoner))
                return false;

            // Check if there's food available for this prisoner
            Thing foodSource;
            ThingDef foodDef;
            if (!FoodUtility.TryFindBestFoodSourceFor(warden, prisoner,
                prisoner.needs.food.CurCategory == HungerCategory.Starving,
                out foodSource, out foodDef, false, allowCorpse: false, calculateWantedStackCount: true))
                return false;

            // Don't deliver if food is already in same room
            return foodSource.GetRoom() != prisoner.GetRoom();
        }

        /// <summary>
        /// Creates a job to deliver food to a prisoner
        /// </summary>
        private Job CreateDeliverFoodJob(Pawn warden, Pawn prisoner)
        {
            // Find best food source
            Thing foodSource;
            ThingDef foodDef;
            if (FoodUtility.TryFindBestFoodSourceFor(warden, prisoner,
                prisoner.needs.food.CurCategory == HungerCategory.Starving,
                out foodSource, out foodDef, false, allowCorpse: false, calculateWantedStackCount: true))
            {
                // Don't deliver if food is already in same room
                if (foodSource.GetRoom() == prisoner.GetRoom())
                    return null;

                float nutrition = FoodUtility.GetNutrition(prisoner, foodSource, foodDef);
                Job job = JobMaker.MakeJob(JobDefOf.DeliverFood, foodSource, prisoner);
                job.count = FoodUtility.WillIngestStackCountOf(prisoner, foodDef, nutrition);
                job.targetC = RCellFinder.SpotToChewStandingNear(prisoner, foodSource);

                if (Prefs.DevMode)
                {
                    Utility_DebugManager.LogNormal($"{warden.LabelShort} created job to deliver food to prisoner {prisoner.LabelShort}");
                }

                return job;
            }

            return null;
        }

        /// <summary>
        /// Check if food is already available in the prisoner's room
        /// Direct port from the original WorkGiver_Warden_DeliverFood
        /// </summary>
        private static bool FoodAvailableInRoomTo(Pawn prisoner)
        {
            if (prisoner.carryTracker.CarriedThing != null && NutritionAvailableForFrom(prisoner, prisoner.carryTracker.CarriedThing) > 0.0f)
                return true;

            float nutritionWanted = 0.0f;
            float nutritionAvailable = 0.0f;

            Room room = prisoner.GetRoom();
            if (room == null)
                return false;

            List<Region> regions = room.Regions;
            for (int i = 0; i < regions.Count; i++)
            {
                Region region = regions[i];

                // Check for available food in the room
                List<Thing> foodSources = region.ListerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree);
                for (int j = 0; j < foodSources.Count; j++)
                {
                    Thing foodSource = foodSources[j];
                    if (!foodSource.def.IsIngestible || foodSource.def.ingestible.preferability > FoodPreferability.DesperateOnlyForHumanlikes)
                        nutritionAvailable += NutritionAvailableForFrom(prisoner, foodSource);
                }

                // Check for hungry prisoners in the room
                List<Thing> pawnsInRoom = region.ListerThings.ThingsInGroup(ThingRequestGroup.Pawn);
                for (int k = 0; k < pawnsInRoom.Count; k++)
                {
                    Pawn p = (Pawn)pawnsInRoom[k];
                    if (p.IsPrisonerOfColony && p.needs.food != null &&
                        p.needs.food.CurLevelPercentage < p.needs.food.PercentageThreshHungry + 0.02f &&
                        (p.carryTracker.CarriedThing == null || !p.WillEat(p.carryTracker.CarriedThing)))
                    {
                        nutritionWanted += p.needs.food.NutritionWanted;
                    }
                }
            }

            return nutritionAvailable + 0.5f >= nutritionWanted;
        }

        /// <summary>
        /// Calculate how much nutrition is available from a food source for a specific pawn
        /// Direct port from the original WorkGiver_Warden_DeliverFood
        /// </summary>
        private static float NutritionAvailableForFrom(Pawn p, Thing foodSource)
        {
            if (foodSource.def.IsNutritionGivingIngestible && p.WillEat(foodSource))
                return foodSource.GetStatValue(StatDefOf.Nutrition) * foodSource.stackCount;

            if (p.RaceProps.ToolUser && p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) &&
                foodSource is Building_NutrientPasteDispenser dispenser &&
                dispenser.CanDispenseNow &&
                p.CanReach(dispenser.InteractionCell, PathEndMode.OnCell, Danger.Some))
                return 99999f;

            return 0.0f;
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_prisonerCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_Warden_DeliverFood_PawnControl";
        }
    }
}