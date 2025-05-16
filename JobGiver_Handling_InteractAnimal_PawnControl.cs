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
        /// The job to create when a valid target is found
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.TakeInventory;

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected abstract override string DebugName { get; }

        /// <summary>
        /// Update cache every ~4 seconds
        /// </summary>
        protected override int CacheUpdateInterval => base.CacheUpdateInterval;

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

        /// <summary>
        /// Cache key suffix specifically for interactable animals
        /// </summary>
        private const string INTERACTABLE_CACHE_SUFFIX = "_InteractableAnimals";

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

        #region Target Selection

        /// <summary>
        /// Get interactable animals on the given map
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null) yield break;

            // Get cached interactable animals from centralized cache
            var animals = GetOrCreateInteractableAnimalsCache(map);

            // Return animals as targets
            foreach (Pawn animal in animals)
            {
                if (animal != null && !animal.Dead && animal.Spawned)
                    yield return animal;
            }
        }

        /// <summary>
        /// Override to use interaction-specific cache keys
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            if (map == null)
                yield break;

            // Find all animals that can potentially be interacted with
            List<Pawn> interactableAnimals = new List<Pawn>();

            foreach (Pawn animal in map.mapPawns.AllPawnsSpawned)
            {
                if (CanInteractWithAnimalInPrinciple(animal))
                {
                    interactableAnimals.Add(animal);
                }
            }

            // Store in the centralized cache
            StoreInteractableAnimalsCache(map, interactableAnimals);

            // Convert to Things for the base class caching system
            foreach (Pawn animal in interactableAnimals)
            {
                yield return animal;
            }
        }

        /// <summary>
        /// Gets or creates a cache of interactable animals for a specific map
        /// </summary>
        protected List<Pawn> GetOrCreateInteractableAnimalsCache(Map map)
        {
            if (map == null)
                return new List<Pawn>();

            int mapId = map.uniqueID;
            string cacheKey = this.GetType().Name + INTERACTABLE_CACHE_SUFFIX;

            // Try to get cached animals from the map cache manager
            var animalCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Pawn>>(mapId);

            // Check if we need to update the cache
            int currentTick = Find.TickManager.TicksGame;
            int lastUpdateTick = Utility_MapCacheManager.GetLastCacheUpdateTick(mapId, cacheKey);

            if (currentTick - lastUpdateTick > CacheUpdateInterval ||
                !animalCache.TryGetValue(cacheKey, out List<Pawn> animals) ||
                animals == null ||
                animals.Any(a => a == null || a.Dead || !a.Spawned))
            {
                // Cache is invalid or expired, rebuild it
                animals = new List<Pawn>();

                foreach (Pawn animal in map.mapPawns.AllPawnsSpawned)
                {
                    if (CanInteractWithAnimalInPrinciple(animal))
                    {
                        animals.Add(animal);
                    }
                }

                // Store in the central cache
                StoreInteractableAnimalsCache(map, animals);
            }

            return animals;
        }

        /// <summary>
        /// Store a list of interactable animals in the centralized cache
        /// </summary>
        private void StoreInteractableAnimalsCache(Map map, List<Pawn> animals)
        {
            if (map == null)
                return;

            int mapId = map.uniqueID;
            string cacheKey = this.GetType().Name + INTERACTABLE_CACHE_SUFFIX;
            int currentTick = Find.TickManager.TicksGame;

            var animalCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Pawn>>(mapId);
            animalCache[cacheKey] = animals;
            Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, currentTick);

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Updated interactable animals cache for {this.GetType().Name}, found {animals.Count} animals");
            }
        }

        /// <summary>
        /// Creates a reachability cache for this specific JobGiver
        /// </summary>
        protected Dictionary<Pawn, bool> GetOrCreateReachabilityCache(Map map)
        {
            if (map == null)
                return new Dictionary<Pawn, bool>();

            int mapId = map.uniqueID;
            string cacheKey = this.GetType().Name + "_Reachability";

            return Utility_MapCacheManager.GetOrCreateMapCache<Pawn, bool>(mapId);
        }

        #endregion

        #region Job Processing

        /// <summary>
        /// Processes cached targets to find a valid job for animal interaction
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            if (pawn?.Map == null || targets.Count == 0)
                return null;

            // Initialize static data if needed
            InitializeStaticData();

            // Filter to valid animals
            var animalTargets = targets.OfType<Pawn>().Where(a => a != null && a.Spawned && !a.Dead).ToList();

            // Find the best animal to interact with
            Pawn targetAnimal = FindBestInteractTarget(pawn, animalTargets, forced);
            if (targetAnimal == null)
                return null;

            // Create specific job for this animal
            Job job = MakeInteractionJob(pawn, targetAnimal, forced);
            if (job != null && Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to {DebugName.ToLower()} {targetAnimal.LabelShort}");
            }

            return job;
        }

        /// <summary>
        /// Core job creation method that handles food acquisition if needed
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            // Initialize static data if needed
            InitializeStaticData();

            // Basic validation
            if (pawn?.Map == null)
                return null;

            if (ShouldSkip(pawn))
            {
                return null;
            }

            // Check if Animals skill is disabled
            if (pawn.WorkTagIsDisabled(WorkTags.Animals))
                return null;

            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Handling_InteractAnimal_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) => {
                    if (p?.Map == null)
                        return null;

                    // Use the base job creation method which handles caching
                    Job job = base.TryGiveJob(p);

                    // If we need food for the interaction but don't have it,
                    // try to get some food first
                    if (job == null && NeedsFoodForInteraction())
                    {
                        List<Pawn> animals = GetOrCreateInteractableAnimalsCache(p.Map);
                        if (animals.Count > 0)
                        {
                            // Find a valid animal to interact with
                            Pawn targetAnimal = FindBestInteractTarget(p, animals, forced);
                            if (targetAnimal != null && !HasFoodToInteractAnimal(p, targetAnimal))
                            {
                                return TakeFoodForAnimalInteractJob(p, targetAnimal);
                            }
                        }
                    }

                    return job;
                },
                (p, setFailReason) => {
                    // Additional check for animals work tag
                    if (p.WorkTagIsDisabled(WorkTags.Animals))
                    {
                        if (setFailReason)
                            JobFailReason.Is("CannotPrioritizeWorkTypeDisabled".Translate(WorkTypeDefOf.Handling.gerundLabel).CapitalizeFirst());
                        return false;
                    }
                    return true;
                },
                debugJobDesc: DebugName);
        }

        /// <summary>
        /// Finds the best animal to interact with
        /// </summary>
        protected virtual Pawn FindBestInteractTarget(Pawn pawn, List<Pawn> animals, bool forced)
        {
            if (pawn?.Map == null || animals == null || animals.Count == 0)
                return null;

            // Create distance buckets for more efficient selection
            List<Pawn>[] buckets = new List<Pawn>[DistanceThresholds.Length + 1];
            for (int i = 0; i < buckets.Length; i++)
            {
                buckets[i] = new List<Pawn>();
            }

            // Sort animals into buckets by distance
            foreach (Pawn animal in animals)
            {
                float distanceSq = (animal.Position - pawn.Position).LengthHorizontalSquared;

                int bucketIndex = 0;
                while (bucketIndex < DistanceThresholds.Length && distanceSq > DistanceThresholds[bucketIndex])
                {
                    bucketIndex++;
                }

                buckets[bucketIndex].Add(animal);
            }

            // Get the reachability cache
            var reachabilityCache = GetOrCreateReachabilityCache(pawn.Map);

            // Find the closest valid animal
            for (int b = 0; b < buckets.Length; b++)
            {
                if (buckets[b].Count == 0)
                    continue;

                // Randomize within each bucket for even distribution
                buckets[b].Shuffle();

                foreach (Pawn animal in buckets[b])
                {
                    // Check if valid and reachable
                    string failReason;
                    bool isValid = CanInteractWithAnimal(pawn, animal, out failReason, forced,
                        CanInteractWhileSleeping, IgnoreSkillRequirements, CanInteractWhileRoaming) &&
                        IsValidForSpecificInteraction(pawn, animal, forced);

                    if (isValid)
                    {
                        // Cache the reachability result
                        reachabilityCache[animal] = true;
                        return animal;
                    }
                }
            }

            return null;
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
        /// Override the base class method for animal handling
        /// </summary>
        protected override bool CanHandleAnimal(Pawn animal, Pawn handler)
        {
            // Use the specialized interaction logic
            return CanInteractWithAnimal(handler, animal, false);
        }

        /// <summary>
        /// Implements the animal-specific validity check
        /// </summary>
        protected override bool IsValidAnimalTarget(Pawn animal, Pawn handler)
        {
            // Use the specialized interaction logic
            string failReason = null;
            return CanInteractWithAnimal(handler, animal, out failReason, false,
                CanInteractWhileSleeping, IgnoreSkillRequirements, CanInteractWhileRoaming);
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
            Job job = JobMaker.MakeJob(WorkJobDef, foodSource);
            job.count = count;
            return job;
        }

        #endregion

        #region Reset

        /// <summary>
        /// Reset the cache - implements IResettableCache
        /// </summary>
        public override void Reset()
        {
            // Call base reset first to handle animal caches
            base.Reset();

            // Now clear interaction-specific caches
            foreach (int mapId in Find.Maps.Select(map => map.uniqueID))
            {
                string cacheKey = this.GetType().Name + INTERACTABLE_CACHE_SUFFIX;
                var animalCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Pawn>>(mapId);

                if (animalCache.ContainsKey(cacheKey))
                {
                    animalCache.Remove(cacheKey);
                }

                // Clear the reachability cache too
                string reachabilityKey = this.GetType().Name + "_Reachability";
                var reachCache = Utility_MapCacheManager.GetOrCreateMapCache<Pawn, bool>(mapId);
                // Just clear the entire cache since we can't easily filter it
                reachCache.Clear();

                // Clear the update tick records
                Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, -1);
                Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, reachabilityKey, -1);
            }

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Reset interaction caches for {this.GetType().Name}");
            }
        }

        /// <summary>
        /// Static method to reset all interaction caches
        /// </summary>
        public static void ResetInteractCache()
        {
            // Clear all interaction caches from all maps
            foreach (Map map in Find.Maps)
            {
                int mapId = map.uniqueID;

                // Clear all interaction-related caches
                foreach (var type in typeof(JobGiver_Handling_InteractAnimal_PawnControl).AllSubclasses())
                {
                    string cacheKey = type.Name + INTERACTABLE_CACHE_SUFFIX;
                    string reachabilityKey = type.Name + "_Reachability";

                    var animalCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Pawn>>(mapId);
                    if (animalCache.ContainsKey(cacheKey))
                    {
                        animalCache.Remove(cacheKey);
                    }

                    // Reset the update tick
                    Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, -1);
                    Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, reachabilityKey, -1);
                }

                // Clear reachability caches
                var reachCache = Utility_MapCacheManager.GetOrCreateMapCache<Pawn, bool>(mapId);
                reachCache.Clear();
            }

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal("Reset all animal interaction caches");
            }
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