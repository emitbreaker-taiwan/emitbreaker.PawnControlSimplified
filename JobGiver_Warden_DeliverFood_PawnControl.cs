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
        protected override int CacheUpdateInterval => base.CacheUpdateInterval;

        /// <summary>
        /// Distance thresholds for bucketing (10, 20, 30 tiles)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 100f, 400f, 900f };

        #endregion

        #region Core flow

        /// <summary>
        /// Gets base priority for the job giver
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            // Delivering food to prisoners is important but lower priority than direct feeding
            return 5.8f;
        }

        /// <summary>
        /// Creates a job for the warden to deliver food to prisoners
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (ShouldSkip(pawn))
            {
                return null;
            }

            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Warden_DeliverFood_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) => {
                    // Process cached targets to create job
                    if (p?.Map == null) return null;

                    // Get prisoners from centralized cache system
                    var prisoners = GetOrCreatePrisonerCache(p.Map);

                    // Convert to Thing list for processing
                    List<Thing> targets = new List<Thing>();
                    foreach (Pawn prisoner in prisoners)
                    {
                        if (prisoner != null && !prisoner.Dead && prisoner.Spawned)
                            targets.Add(prisoner);
                    }

                    return ProcessCachedTargets(p, targets, forced);
                },
                debugJobDesc: "deliver food to prisoner");
        }

        /// <summary>
        /// Checks whether this job giver should be skipped for a pawn
        /// </summary>
        public override bool ShouldSkip(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.InMentalState)
                return true;

            var modExtension = Utility_UnifiedCache.GetModExtension(pawn.def);
            if (modExtension == null)
                return true;

            // Skip if pawn is not a warden
            if (!Utility_TagManager.IsWorkEnabled(pawn, WorkTag))
                return true;

            return false;
        }

        #endregion

        #region Prisoner Selection

        /// <summary>
        /// Get prisoners that match the criteria for food delivery
        /// </summary>
        protected override IEnumerable<Pawn> GetPrisonersMatchingCriteria(Map map)
        {
            if (map == null)
                yield break;

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
        /// Validates if food can be delivered to this prisoner
        /// </summary>
        protected override bool IsValidPrisonerTarget(Pawn prisoner, Pawn warden)
        {
            // First check base class validation
            if (!base.IsValidPrisonerTarget(prisoner, warden))
                return false;

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

            return true;
        }

        /// <summary>
        /// Create a job for the given prisoner
        /// </summary>
        protected override Job CreateJobForPrisoner(Pawn warden, Pawn prisoner, bool forced)
        {
            return CreateDeliverFoodJob(warden, prisoner);
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

            // Check if cache needs updating
            string cacheKey = this.GetType().Name + PRISONERS_CACHE_SUFFIX;
            int lastUpdateTick = Utility_MapCacheManager.GetLastCacheUpdateTick(mapId, cacheKey);

            return Find.TickManager.TicksGame > lastUpdateTick + CacheUpdateInterval;
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

                if (Prefs.DevMode)
                {
                    Utility_DebugManager.LogNormal($"{warden.LabelShort} created job to deliver food to prisoner {prisoner.LabelShort}");
                }

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

        #endregion

        #region Debug

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