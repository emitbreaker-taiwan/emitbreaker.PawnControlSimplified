using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to deliver food to prisoners who can feed themselves.
    /// Optimized to use the PawnControl framework for better performance.
    /// </summary>
    public class JobGiver_Warden_DeliverFood_PawnControl : JobGiver_Warden_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "DeliverFood";

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
            // Delivering food to prisoners is important but lower priority than direct feeding
            return 5.8f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Warden_DeliverFood_PawnControl>(
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
                debugJobDesc: "deliver food to prisoner");
        }

        public override bool ShouldSkip(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.InMentalState)
                return true;
            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null)
                return true;
            // Skip if pawn is not a warden
            if (!Utility_TagManager.WorkEnabled(pawn.def, WorkTag))
                return true;
            return false;
        }

        #endregion

        #region Target processing

        /// <summary>
        /// Get all prisoners eligible for food delivery
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null) yield break;

            // Get all prisoner pawns on the map who need food delivered
            foreach (Pawn prisoner in map.mapPawns.PrisonersOfColonySpawned)
            {
                if (FilterPrisonersNeedingFood(prisoner))
                {
                    yield return prisoner;
                }
            }
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

            // Find the first valid prisoner to deliver food to
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

            // Create food delivery job
            return CreateDeliverFoodJob(warden, targetPrisoner);
        }

        /// <summary>
        /// Validates if food can be delivered to this prisoner
        /// </summary>
        protected override bool IsValidPrisonerTarget(Pawn prisoner, Pawn warden)
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
            if (foodSource.GetRoom() == prisoner.GetRoom())
                return false;

            // Check basic reachability
            if (!prisoner.Spawned || prisoner.IsForbidden(warden) ||
                !warden.CanReserve(prisoner, 1, -1, null, false))
                return false;

            return true;
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

            return !_lastWardenCacheUpdate.TryGetValue(mapId, out int lastUpdate) ||
                   Find.TickManager.TicksGame > lastUpdate + CacheUpdateInterval;
        }

        #endregion

        #region Job creation

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

                Utility_DebugManager.LogNormal($"{warden.LabelShort} created job to deliver food to prisoner {prisoner.LabelShort}");

                return job;
            }

            return null;
        }

        #endregion

        #region Utility methods

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
        /// For debugging purposes
        /// </summary>
        public override string ToString()
        {
            return "JobGiver_Warden_DeliverFood_PawnControl";
        }

        #endregion
    }
}