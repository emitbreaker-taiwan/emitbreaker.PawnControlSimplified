using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to train animals owned by the player's faction.
    /// Uses the Handler work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Train_PawnControl : ThinkNode_JobGiver
    {
        // Constants
        private const int CACHE_REFRESH_INTERVAL = 250;
        private const int MAX_CACHE_SIZE = 1000;

        // Cache for animals that need training
        private static readonly Dictionary<int, List<Pawn>> _trainableAnimalCache = new Dictionary<int, List<Pawn>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static int _lastCacheUpdateTick = -999;

        // Distance thresholds for bucketing (25, 50, 100 tiles squared)
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 625f, 2500f, 10000f };
        protected static string NoUsableFoodTrans;

        public static void ResetStaticData()
        {
            NoUsableFoodTrans = "NoUsableFood".Translate();
        }

        public override float GetPriority(Pawn pawn)
        {
            // Training has moderate priority among work tasks
            return 5.0f;
        }

        /// <summary>
        /// Updates the cache of animals that need training
        /// </summary>
        private void UpdateTrainableAnimalsCacheSafely(Map map, Faction faction)
        {
            if (map == null || faction == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_REFRESH_INTERVAL ||
                !_trainableAnimalCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_trainableAnimalCache.ContainsKey(mapId))
                    _trainableAnimalCache[mapId].Clear();
                else
                    _trainableAnimalCache[mapId] = new List<Pawn>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Pawn, bool>();

                // Get animals from the faction that can be trained
                foreach (Pawn animal in map.mapPawns.SpawnedPawnsInFaction(faction))
                {
                    if (animal != null && 
                        animal.IsNonMutantAnimal && 
                        animal.training != null && 
                        animal.training.NextTrainableToTrain() != null &&
                        animal.RaceProps.animalType != AnimalType.Dryad &&
                        !TrainableUtility.TrainedTooRecently(animal))
                    {
                        _trainableAnimalCache[mapId].Add(animal);
                    }
                }

                // Limit cache size for performance
                if (_trainableAnimalCache[mapId].Count > MAX_CACHE_SIZE)
                {
                    _trainableAnimalCache[mapId] = _trainableAnimalCache[mapId].Take(MAX_CACHE_SIZE).ToList();
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Basic validation - only player faction pawns can train animals
            if (pawn?.Map == null || pawn.Faction != Faction.OfPlayer)
                return null;

            return Utility_JobGiverManager.StandardTryGiveJob<Pawn>(
                pawn,
                "Handler",
                (p, forced) => {
                    // Update trainable animals cache
                    UpdateTrainableAnimalsCacheSafely(p.Map, p.Faction);

                    // Create training job
                    return TryCreateTrainJob(p);
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
                debugJobDesc: "animal training",
                skipEmergencyCheck: false);
        }

        /// <summary>
        /// Creates a job for training an animal
        /// </summary>
        private Job TryCreateTrainJob(Pawn pawn)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_trainableAnimalCache.ContainsKey(mapId) || _trainableAnimalCache[mapId].Count == 0)
                return null;

            // Create distance buckets for optimized searching
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                _trainableAnimalCache[mapId],
                (animal) => (animal.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // FIXED: Explicitly specify type argument Pawn for FindFirstValidTargetInBuckets
            Pawn targetAnimal = Utility_JobGiverManager.FindFirstValidTargetInBuckets<Pawn>(
                buckets,
                pawn,
                (animal, p) => {
                    // Skip if trying to train self
                    if (animal == p)
                        return false;
                    
                    // Skip if no longer valid
                    if (animal.Destroyed || !animal.Spawned || animal.Map != p.Map)
                        return false;
                    
                    // Skip if not of same faction
                    if (animal.Faction != p.Faction)
                        return false;
                        
                    // Skip if not an animal or doesn't have training
                    if (!animal.IsNonMutantAnimal || 
                        animal.RaceProps.animalType == AnimalType.Dryad ||
                        animal.training == null ||
                        animal.training.NextTrainableToTrain() == null)
                        return false;
                        
                    // Skip if trained too recently
                    if (TrainableUtility.TrainedTooRecently(animal))
                        return false;
                        
                    // Skip if forbidden or unreachable
                    if (animal.IsForbidden(p) ||
                        !p.CanReserve((LocalTargetInfo)animal) ||
                        !p.CanReach((LocalTargetInfo)animal, PathEndMode.Touch, Danger.Some))
                        return false;
                    
                    return true;
                },
                _reachabilityCache
            );

            if (targetAnimal == null)
                return null;

            // Handle food for training
            Thing foodSource = null;
            
            if (targetAnimal.RaceProps.EatsFood && targetAnimal.needs?.food != null &&
                !HasFoodToInteractAnimal(pawn, targetAnimal))
            {
                ThingDef foodDef;
                foodSource = FoodUtility.BestFoodSourceOnMap(
                    pawn, targetAnimal, false, out foodDef, FoodPreferability.RawTasty,
                    false, false, false, false, false,
                    minNutrition: new float?((float)((double)JobDriver_InteractAnimal.RequiredNutritionPerFeed(targetAnimal) * 2.0))
                );

                if (foodSource == null)
                {
                    JobFailReason.Is(NoUsableFoodTrans);
                    return null;
                }
                
                // Create a job to get food first if needed
                Job takeFoodJob = TryCreateTakeFoodJob(pawn, targetAnimal, foodSource, foodDef);
                if (takeFoodJob != null)
                    return takeFoodJob;
            }

            // Create job if target found
            if (targetAnimal != null)
            {
                Job job = JobMaker.MakeJob(JobDefOf.Train, targetAnimal);

                if (Prefs.DevMode)
                    Log.Message($"[PawnControl] {pawn.LabelShort} created job to train {targetAnimal.LabelShort}");

                return job;
            }

            return null;
        }

        /// <summary>
        /// Creates a job to collect food for training an animal
        /// </summary>
        private Job TryCreateTakeFoodJob(Pawn pawn, Pawn targetAnimal, Thing foodSource, ThingDef foodDef)
        {
            int numToTake = FoodUtility.WillIngestStackCountOf(targetAnimal, foodDef, 
                JobDriver_InteractAnimal.RequiredNutritionPerFeed(targetAnimal));
                
            if (numToTake <= 0)
                return null;
                
            numToTake = Math.Min(numToTake, foodSource.stackCount);
            
            Job job = JobMaker.MakeJob(JobDefOf.TakeInventory, foodSource);
            job.count = numToTake;
            
            if (Prefs.DevMode)
                Log.Message($"[PawnControl] {pawn.LabelShort} created job to take food for training {targetAnimal.LabelShort}");
                
            return job;
        }

        /// <summary>
        /// Checks if the pawn has appropriate food for interacting with the animal
        /// </summary>
        private bool HasFoodToInteractAnimal(Pawn pawn, Pawn animal)
        {
            return pawn.inventory.innerContainer.Contains(ThingDefOf.Kibble) ||
                   (animal.RaceProps.foodType & (FoodTypeFlags.Plant | FoodTypeFlags.VegetableOrFruit)) != FoodTypeFlags.None &&
                   pawn.inventory.innerContainer.Any(t => t.def.IsNutritionGivingIngestible &&
                   t.def.ingestible.preferability >= FoodPreferability.RawBad &&
                   (t.def.ingestible.foodType & (FoodTypeFlags.Plant | FoodTypeFlags.VegetableOrFruit)) != FoodTypeFlags.None);
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_trainableAnimalCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_Train_PawnControl";
        }
    }
}