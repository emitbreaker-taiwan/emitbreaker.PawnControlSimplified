using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Abstract base class for animal interaction job givers (training, taming).
    /// Uses the Handling work tag for eligibility checking.
    /// </summary>
    public abstract class JobGiver_Handling_InteractAnimal_PawnControl : JobGiver_Handling_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected abstract override string DebugName { get; }

        /// <summary>
        /// Update cache every ~4 seconds
        /// </summary>
        protected override int CacheUpdateInterval => 250;

        /// <summary>
        /// Distance thresholds for bucketing (15, 25, 40 tiles)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 225f, 625f, 1600f };

        /// <summary>
        /// Whether the interaction can be done while the animal is sleeping
        /// </summary>
        protected virtual bool CanInteractWhileSleeping => false;

        /// <summary>
        /// Whether the interaction can be done while the animal is roaming
        /// </summary>
        protected virtual bool CanInteractWhileRoaming => false;

        /// <summary>
        /// Whether to ignore skill requirements for the interaction
        /// </summary>
        protected virtual bool IgnoreSkillRequirements => false;

        #endregion

        #region Static data

        protected static string NoUsableFoodTrans;
        protected static string AnimalInteractedTooRecentlyTrans;
        private static string CantInteractAnimalDownedTrans;
        private static string CantInteractAnimalAsleepTrans;
        private static string CantInteractAnimalBusyTrans;

        /// <summary>
        /// Initialize static translations
        /// </summary>
        public static void InitializeStaticData()
        {
            if (NoUsableFoodTrans == null)
            {
                NoUsableFoodTrans = "NoUsableFood".Translate();
                AnimalInteractedTooRecentlyTrans = "AnimalInteractedTooRecently".Translate();
                CantInteractAnimalDownedTrans = "CantInteractAnimalDowned".Translate();
                CantInteractAnimalAsleepTrans = "CantInteractAnimalAsleep".Translate();
                CantInteractAnimalBusyTrans = "CantInteractAnimalBusy".Translate();
            }
        }

        #endregion

        #region Cache Management

        // Interaction-specific cache
        private static readonly Dictionary<int, List<Pawn>> _interactableAnimalsCache = new Dictionary<int, List<Pawn>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _interactableReachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static int _lastInteractCacheUpdateTick = -999;

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetInteractCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_interactableAnimalsCache, _interactableReachabilityCache);
            _lastInteractCacheUpdateTick = -999;
        }

        #endregion

        #region Core flow

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Initialize static data if needed
            InitializeStaticData();

            // Basic validation
            if (pawn?.Map == null)
                return null;

            // Check if Animals skill is disabled
            if (pawn.WorkTagIsDisabled(WorkTags.Animals))
                return null;

            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Handling_InteractAnimal_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) => {
                    if (p?.Map == null)
                        return null;

                    int mapId = p.Map.uniqueID;
                    int now = Find.TickManager.TicksGame;

                    if (!ShouldExecuteNow(mapId))
                        return null;

                    // Update the cache if needed
                    if (now > _lastInteractCacheUpdateTick + CacheUpdateInterval ||
                        !_interactableAnimalsCache.ContainsKey(mapId))
                    {
                        UpdateInteractableAnimalsCache(p.Map);
                    }

                    // Process cached targets
                    Job job = ProcessInteractTargets(p, forced);

                    // If we need food for the interaction but don't have it,
                    // try to get some food first
                    if (job == null && NeedsFoodForInteraction())
                    {
                        Pawn targetAnimal = FindBestInteractTarget(p, forced);
                        if (targetAnimal != null && !HasFoodToInteractAnimal(p, targetAnimal))
                        {
                            return TakeFoodForAnimalInteractJob(p, targetAnimal);
                        }
                    }

                    return job;
                },
                debugJobDesc: DebugName);
        }

        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // Implement in derived classes based on specific interaction type
            if (map == null) yield break;

            foreach (Pawn animal in map.mapPawns.AllPawnsSpawned)
            {
                if (CanInteractWithAnimalInPrinciple(animal))
                {
                    yield return animal;
                }
            }
        }

        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // Just use the specialized version that works with pawns directly
            return ProcessInteractTargets(pawn, forced);
        }

        /// <summary>
        /// Process the cached targets for animal interaction
        /// </summary>
        protected virtual Job ProcessInteractTargets(Pawn pawn, bool forced = false)
        {
            if (pawn?.Map == null)
                return null;

            int mapId = pawn.Map.uniqueID;
            if (!_interactableAnimalsCache.ContainsKey(mapId) || _interactableAnimalsCache[mapId].Count == 0)
                return null;

            // Find the best animal to interact with
            Pawn targetAnimal = FindBestInteractTarget(pawn, forced);
            if (targetAnimal == null)
                return null;

            // Create specific job for this animal
            Job job = MakeInteractionJob(pawn, targetAnimal, forced);
            if (job != null)
            {
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to {DebugName.ToLower()} {targetAnimal.LabelShort}");
            }

            return job;
        }

        /// <summary>
        /// Determines if the job giver should execute based on cache status
        /// </summary>
        protected override bool ShouldExecuteNow(int mapId)
        {
            return Find.TickManager.TicksGame > _lastInteractCacheUpdateTick + CacheUpdateInterval ||
                  !_interactableAnimalsCache.ContainsKey(mapId);
        }

        /// <summary>
        /// Override the base class method for animal handling
        /// </summary>
        protected override bool CanHandleAnimal(Pawn animal, Pawn handler)
        {
            // Use the WorkGiver_InteractAnimal logic
            return CanInteractWithAnimal(handler, animal, false);
        }

        /// <summary>
        /// Implements the animal-specific validity check
        /// </summary>
        protected override bool IsValidAnimalTarget(Pawn animal, Pawn handler)
        {
            // Use the WorkGiver_InteractAnimal logic
            string failReason = null;
            return CanInteractWithAnimal(handler, animal, out failReason, false,
                CanInteractWhileSleeping, IgnoreSkillRequirements, CanInteractWhileRoaming);
        }

        #endregion

        #region Target processing

        /// <summary>
        /// Updates the cache of animals that can be interacted with
        /// </summary>
        private void UpdateInteractableAnimalsCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            // Clear outdated cache
            if (_interactableAnimalsCache.ContainsKey(mapId))
                _interactableAnimalsCache[mapId].Clear();
            else
                _interactableAnimalsCache[mapId] = new List<Pawn>();

            // Clear reachability cache too
            if (_interactableReachabilityCache.ContainsKey(mapId))
                _interactableReachabilityCache[mapId].Clear();
            else
                _interactableReachabilityCache[mapId] = new Dictionary<Pawn, bool>();

            // Find all animals that can potentially be interacted with
            foreach (Pawn animal in map.mapPawns.AllPawnsSpawned)
            {
                if (CanInteractWithAnimalInPrinciple(animal))
                {
                    _interactableAnimalsCache[mapId].Add(animal);
                }
            }

            _lastInteractCacheUpdateTick = currentTick;
        }

        /// <summary>
        /// Finds the best animal to interact with
        /// </summary>
        protected virtual Pawn FindBestInteractTarget(Pawn pawn, bool forced)
        {
            if (pawn?.Map == null)
                return null;

            int mapId = pawn.Map.uniqueID;
            if (!_interactableAnimalsCache.ContainsKey(mapId) || _interactableAnimalsCache[mapId].Count == 0)
                return null;

            // Use distance bucketing to find animals efficiently
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                _interactableAnimalsCache[mapId],
                (animal) => (animal.Position - pawn.Position).LengthHorizontalSquared,
                DistanceThresholds);

            // Find first valid animal
            return Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (animal, handler) => {
                    // Check if this specific animal is valid for interaction
                    string failReason;
                    return CanInteractWithAnimal(handler, animal, out failReason, forced,
                        CanInteractWhileSleeping, IgnoreSkillRequirements, CanInteractWhileRoaming)
                        && IsValidForSpecificInteraction(handler, animal, forced);
                },
                _interactableReachabilityCache);
        }

        #endregion

        #region Animal interaction helpers

        /// <summary>
        /// Determine if an animal is valid for interaction in principle (without checking handler)
        /// </summary>
        protected virtual bool CanInteractWithAnimalInPrinciple(Pawn animal)
        {
            return animal != null && animal.RaceProps.Animal && animal.Spawned && !animal.Dead;
        }

        /// <summary>
        /// Checks if the handler can interact with the animal
        /// </summary>
        protected virtual bool CanInteractWithAnimal(Pawn handler, Pawn animal, bool forced)
        {
            string failReason;
            bool result = CanInteractWithAnimal(handler, animal, out failReason, forced,
                CanInteractWhileSleeping, IgnoreSkillRequirements, CanInteractWhileRoaming);

            if (!result && failReason != null)
            {
                JobFailReason.Is(failReason);
            }

            return result;
        }

        /// <summary>
        /// Checks if a pawn can interact with an animal
        /// </summary>
        protected virtual bool CanInteractWithAnimal(Pawn pawn, Pawn animal, out string jobFailReason, bool forced,
            bool canInteractWhileSleeping = false, bool ignoreSkillRequirements = false, bool canInteractWhileRoaming = false)
        {
            jobFailReason = null;

            // Basic checks
            if (animal == null || pawn == null || animal == pawn)
                return false;

            if (!pawn.CanReserve(animal, 1, -1, null, forced))
                return false;

            if (animal.Downed)
            {
                jobFailReason = CantInteractAnimalDownedTrans;
                return false;
            }

            if (!animal.Awake() && !canInteractWhileSleeping)
            {
                jobFailReason = CantInteractAnimalAsleepTrans;
                return false;
            }

            if (!animal.CanCasuallyInteractNow(twoWayInteraction: false,
                canInteractWhileSleeping, canInteractWhileRoaming))
            {
                jobFailReason = CantInteractAnimalBusyTrans;
                return false;
            }

            // Check handling skill
            int minHandlingSkill = TrainableUtility.MinimumHandlingSkill(animal);
            if (!ignoreSkillRequirements && minHandlingSkill > pawn.skills.GetSkill(SkillDefOf.Animals).Level)
            {
                jobFailReason = "AnimalsSkillTooLow".Translate(minHandlingSkill);
                return false;
            }

            // Additional checks specific to the interaction
            return true;
        }

        /// <summary>
        /// Override in derived classes to add specific checks for different interaction types
        /// </summary>
        protected abstract bool IsValidForSpecificInteraction(Pawn handler, Pawn animal, bool forced);

        /// <summary>
        /// Create the specific job for interacting with the animal
        /// </summary>
        protected abstract Job MakeInteractionJob(Pawn handler, Pawn animal, bool forced);

        /// <summary>
        /// Whether this interaction needs food
        /// </summary>
        protected virtual bool NeedsFoodForInteraction() => false;

        /// <summary>
        /// Checks if the handler has food suitable for animal interaction
        /// </summary>
        protected virtual bool HasFoodToInteractAnimal(Pawn pawn, Pawn animal)
        {
            // Skip if food isn't needed
            if (!NeedsFoodForInteraction())
                return true;

            ThingOwner<Thing> inventory = pawn.inventory.innerContainer;
            int totalFeeds = 0;
            float requiredNutritionPerFeed = JobDriver_InteractAnimal.RequiredNutritionPerFeed(animal);
            float accumulatedNutrition = 0f;

            // Check each item in inventory
            for (int i = 0; i < inventory.Count; i++)
            {
                Thing thing = inventory[i];
                if (!animal.WillEat(thing, pawn) ||
                    (int)thing.def.ingestible.preferability > 5 ||
                    thing.def.IsDrug)
                {
                    continue;
                }

                // Calculate how many feeds are possible with this stack
                for (int j = 0; j < thing.stackCount; j++)
                {
                    accumulatedNutrition += thing.GetStatValue(StatDefOf.Nutrition);
                    if (accumulatedNutrition >= requiredNutritionPerFeed)
                    {
                        totalFeeds++;
                        accumulatedNutrition = 0f;
                    }

                    // Need at least 2 feeds for most interactions
                    if (totalFeeds >= 2)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Creates a job to take food from the map for animal interaction
        /// </summary>
        protected virtual Job TakeFoodForAnimalInteractJob(Pawn pawn, Pawn animal)
        {
            if (!NeedsFoodForInteraction())
                return null;

            // Find best food source
            ThingDef foodDef;
            float nutrition = JobDriver_InteractAnimal.RequiredNutritionPerFeed(animal) * 2f * 4f;

            Thing foodSource = FoodUtility.BestFoodSourceOnMap(
                pawn,
                animal,
                desperate: false,
                out foodDef,
                FoodPreferability.RawTasty,
                allowPlant: false,
                allowDrug: false,
                allowCorpse: false,
                allowDispenserFull: false,
                allowDispenserEmpty: false,
                allowForbidden: false,
                allowSociallyImproper: false,
                allowHarvest: false,
                forceScanWholeMap: false,
                ignoreReservations: false,
                calculateWantedStackCount: false,
                FoodPreferability.Undefined,
                nutrition);

            if (foodSource == null)
                return null;

            // Calculate how much to take
            float nutritionPerItem = FoodUtility.GetNutrition(animal, foodSource, foodDef);
            int count = FoodUtility.StackCountForNutrition(nutrition, nutritionPerItem);

            // Create the job
            Job job = JobMaker.MakeJob(JobDefOf.TakeInventory, foodSource);
            job.count = count;
            return job;
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return $"JobGiver_{DebugName}_PawnControl";
        }

        #endregion
    }
}