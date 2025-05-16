using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to tame animals with tame designations.
    /// Uses the Handler work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Handling_Tame_PawnControl : JobGiver_Handling_InteractAnimal_PawnControl
    {
        #region Configuration

        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.Tame;

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        public override bool RequiresMapZoneorArea => false;

        /// <summary>
        /// Human-readable name for debug logging
        /// </summary>
        protected override string DebugName => "Tame";

        /// <summary>
        /// Work tag for eligibility checking
        /// </summary>
        public override string WorkTag => "Handling";

        /// <summary>
        /// Cache update interval in ticks (180 ticks = 3 seconds)
        /// </summary>
        protected override int CacheUpdateInterval => base.CacheUpdateInterval;

        /// <summary>
        /// Distance thresholds for bucketing (25, 50, 100 tiles squared)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 625f, 2500f, 10000f };

        /// <summary>
        /// Taming requires the animal to be awake
        /// </summary>
        protected override bool CanInteractWhileSleeping => false;

        /// <summary>
        /// Taming can't be done while animal is roaming
        /// </summary>
        protected override bool CanInteractWhileRoaming => false;

        /// <summary>
        /// Taming doesn't require handling skill levels
        /// </summary>
        protected override bool IgnoreSkillRequirements => true;

        /// <summary>
        /// Taming requires food
        /// </summary>
        protected override bool NeedsFoodForInteraction() => true;

        /// <summary>
        /// Cache key suffix specifically for tameable animals
        /// </summary>
        private const string TAMEABLE_CACHE_SUFFIX = "_TameableAnimals";

        #endregion

        #region Core flow

        protected override float GetBasePriority(string workTag)
        {
            // Taming has moderate priority among work tasks
            return 5.1f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Skip if no tame designations exist on map
            if (pawn?.Map == null || !pawn.Map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.Tame))
                return null;

            if (ShouldSkip(pawn))
            {
                return null;
            }

            // IMPORTANT: Only player faction pawns or pawns slaved to player faction can perform designation jobs
            if (pawn.Faction != Faction.OfPlayer &&
                !(pawn.IsSlave && pawn.HostFaction == Faction.OfPlayer))
                return null;

            // Initialize static data if needed
            InitializeStaticData();

            // Use the parent class job creation logic
            return base.TryGiveJob(pawn);
        }

        #endregion

        #region Target Selection

        /// <summary>
        /// Override to get only animals with tame designation
        /// </summary>
        protected override bool CanInteractWithAnimalInPrinciple(Pawn animal)
        {
            // Basic animal check
            if (animal == null || !animal.RaceProps.Animal || !animal.Spawned || animal.Dead)
                return false;

            // Check for tame designation and tameable status
            return animal.Map?.designationManager.DesignationOn(animal, DesignationDefOf.Tame) != null &&
                   TameUtility.CanTame(animal) &&
                   !TameUtility.TriedToTameTooRecently(animal) &&
                   !animal.InAggroMentalState;
        }

        /// <summary>
        /// Override to use tame-specific cache keys
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            if (map == null)
                yield break;

            // Find all animals with tame designation
            List<Pawn> tameableAnimals = new List<Pawn>();

            // Get animals with tame designations - most efficient approach
            foreach (Designation designation in map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Tame))
            {
                if (designation.target.Thing is Pawn animal && TameUtility.CanTame(animal))
                {
                    tameableAnimals.Add(animal);
                }
            }

            // Store in the centralized cache
            StoreTameableAnimalsCache(map, tameableAnimals);

            // Convert to Things for the base class caching system
            foreach (Pawn animal in tameableAnimals)
            {
                yield return animal;
            }
        }

        /// <summary>
        /// Gets or creates a cache of tameable animals for a specific map
        /// </summary>
        protected List<Pawn> GetOrCreateTameableAnimalsCache(Map map)
        {
            if (map == null)
                return new List<Pawn>();

            int mapId = map.uniqueID;
            string cacheKey = this.GetType().Name + TAMEABLE_CACHE_SUFFIX;

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

                // Find animals with tame designations
                if (map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.Tame))
                {
                    foreach (Designation designation in map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Tame))
                    {
                        if (designation.target.Thing is Pawn animal && TameUtility.CanTame(animal))
                        {
                            animals.Add(animal);
                        }
                    }
                }

                // Store in the central cache
                StoreTameableAnimalsCache(map, animals);
            }

            return animals;
        }

        /// <summary>
        /// Store a list of tameable animals in the centralized cache
        /// </summary>
        private void StoreTameableAnimalsCache(Map map, List<Pawn> animals)
        {
            if (map == null)
                return;

            int mapId = map.uniqueID;
            string cacheKey = this.GetType().Name + TAMEABLE_CACHE_SUFFIX;
            int currentTick = Find.TickManager.TicksGame;

            var animalCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Pawn>>(mapId);
            animalCache[cacheKey] = animals;
            Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, currentTick);

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Updated tameable animals cache for {this.GetType().Name}, found {animals.Count} animals");
            }
        }

        #endregion

        #region Job Processing

        /// <summary>
        /// Taming-specific checks for animal interaction
        /// </summary>
        protected override bool IsValidForSpecificInteraction(Pawn handler, Pawn animal, bool forced)
        {
            // Skip if not wild
            if (animal.Faction != null)
                return false;

            // Skip if no tame designation
            if (handler?.Map == null || animal?.Map == null ||
                handler.Map.designationManager.DesignationOn(animal, DesignationDefOf.Tame) == null)
                return false;

            // Skip if invalid tame target
            if (!TameUtility.CanTame(animal) || TameUtility.TriedToTameTooRecently(animal))
                return false;

            // Skip if in aggressive mental state
            if (animal.InAggroMentalState)
                return false;

            return true;
        }

        /// <summary>
        /// Create job for taming the animal
        /// </summary>
        protected override Job MakeInteractionJob(Pawn handler, Pawn animal, bool forced)
        {
            if (animal == null || handler == null ||
                !IsValidForSpecificInteraction(handler, animal, forced))
                return null;

            // Handle food for taming
            Thing foodSource = null;
            int foodCount = -1;

            if (animal.RaceProps.EatsFood && animal.needs?.food != null &&
                !HasFoodToInteractAnimal(handler, animal))
            {
                ThingDef foodDef;
                float requiredNutrition = JobDriver_InteractAnimal.RequiredNutritionPerFeed(animal) * 2f * 4f;

                foodSource = FoodUtility.BestFoodSourceOnMap(
                    handler,
                    animal,
                    false,
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
                    minNutrition: requiredNutrition
                );

                if (foodSource == null)
                {
                    JobFailReason.Is("NoFood".Translate());
                    return null;
                }

                foodCount = Mathf.CeilToInt(requiredNutrition / FoodUtility.GetNutrition(animal, foodSource, foodDef));
            }

            // Create job if target found
            Job job = JobMaker.MakeJob(WorkJobDef, animal, null, foodSource);
            job.count = foodCount;

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"{handler.LabelShort} created job to tame {animal.LabelShort}");
            }

            return job;
        }

        /// <summary>
        /// Custom food check for taming animals
        /// </summary>
        protected override bool HasFoodToInteractAnimal(Pawn pawn, Pawn animal)
        {
            // Some animals don't need food for taming
            if (!animal.RaceProps.EatsFood || animal.needs?.food == null)
                return true;

            return pawn.inventory.innerContainer.Contains(ThingDefOf.Kibble) ||
                   (animal.RaceProps.foodType & (FoodTypeFlags.Plant | FoodTypeFlags.VegetableOrFruit)) != FoodTypeFlags.None &&
                   pawn.inventory.innerContainer.Any(t => t.def.IsNutritionGivingIngestible &&
                   t.def.ingestible.preferability >= FoodPreferability.RawBad &&
                   (t.def.ingestible.foodType & (FoodTypeFlags.Plant | FoodTypeFlags.VegetableOrFruit)) != FoodTypeFlags.None);
        }

        #endregion

        #region Reset

        /// <summary>
        /// Reset the taming-specific cache
        /// </summary>
        public override void Reset()
        {
            // Call base reset first
            base.Reset();

            // Now clear taming-specific caches
            foreach (int mapId in Find.Maps.Select(map => map.uniqueID))
            {
                string cacheKey = this.GetType().Name + TAMEABLE_CACHE_SUFFIX;
                var animalCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Pawn>>(mapId);

                if (animalCache.ContainsKey(cacheKey))
                {
                    animalCache.Remove(cacheKey);
                }

                // Clear the update tick record too
                Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, -1);
            }

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Reset taming caches for {this.GetType().Name}");
            }
        }

        #endregion

        #region Utility

        /// <summary>
        /// For debugging purposes
        /// </summary>
        public override string ToString()
        {
            return "JobGiver_Tame_PawnControl";
        }

        #endregion
    }
}