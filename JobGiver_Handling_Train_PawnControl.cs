using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to train animals owned by the player's faction.
    /// Uses the Handler work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Handling_Train_PawnControl : JobGiver_Handling_InteractAnimal_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        protected override bool RequiresMapZoneorArea => false;

        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.Train;

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "Train";

        /// <summary>
        /// Cache key suffix specifically for trainable animals
        /// </summary>
        private const string TRAINABLE_CACHE_SUFFIX = "_TrainableAnimals";

        /// <summary>
        /// Training doesn't require the animal to be awake
        /// </summary>
        protected override bool CanInteractWhileSleeping => false;

        /// <summary>
        /// Training can be done while animal is roaming
        /// </summary>
        protected override bool CanInteractWhileRoaming => false;

        /// <summary>
        /// Training requires appropriate skill levels
        /// </summary>
        protected override bool IgnoreSkillRequirements => false;

        /// <summary>
        /// Training usually requires food
        /// </summary>
        protected override bool NeedsFoodForInteraction() => true;

        #endregion

        #region Core flow

        protected override float GetBasePriority(string workTag)
        {
            // Training has moderate priority among work tasks
            return 5.0f;
        }

        #endregion

        #region Target Selection

        /// <summary>
        /// Override to get only animals that need training
        /// </summary>
        protected override bool CanInteractWithAnimalInPrinciple(Pawn animal)
        {
            // Basic animal check from base class
            if (!base.CanInteractWithAnimalInPrinciple(animal))
                return false;

            // Training-specific checks
            return animal.training != null &&
                animal.training.NextTrainableToTrain() != null &&
                animal.RaceProps.animalType != AnimalType.Dryad &&
                !TrainableUtility.TrainedTooRecently(animal);
        }

        /// <summary>
        /// Override to use training-specific cache keys and caching mechanism
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            if (map == null)
                yield break;

            Faction playerFaction = Faction.OfPlayer;
            if (playerFaction == null)
                yield break;

            // Find all animals that need training
            List<Pawn> trainableAnimals = new List<Pawn>();

            // Use map.mapPawns.SpawnedPawnsInFaction for better performance
            foreach (Pawn animal in map.mapPawns.SpawnedPawnsInFaction(playerFaction))
            {
                if (animal == null || !animal.IsNonMutantAnimal)
                    continue;

                // Check if animal needs training
                if (animal.training != null &&
                    animal.training.NextTrainableToTrain() != null &&
                    animal.RaceProps.animalType != AnimalType.Dryad &&
                    !TrainableUtility.TrainedTooRecently(animal) &&
                    !animal.Downed &&
                    animal.CanCasuallyInteractNow())
                {
                    trainableAnimals.Add(animal);
                }
            }

            // Store in the centralized cache
            StoreTrainableAnimalsCache(map, trainableAnimals);

            // Convert to Things for the base class caching system
            foreach (Pawn animal in trainableAnimals)
            {
                yield return animal;
            }
        }

        /// <summary>
        /// Gets or creates a cache of trainable animals for a specific map
        /// </summary>
        protected List<Pawn> GetOrCreateTrainableAnimalsCache(Map map)
        {
            if (map == null)
                return new List<Pawn>();

            int mapId = map.uniqueID;
            string cacheKey = this.GetType().Name + TRAINABLE_CACHE_SUFFIX;

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
                // Cache is invalid or expired, rebuild it using the Update method
                animals = new List<Pawn>();

                foreach (Thing thing in UpdateJobSpecificCache(map))
                {
                    if (thing is Pawn animal)
                    {
                        animals.Add(animal);
                    }
                }
            }

            return animals;
        }

        /// <summary>
        /// Store a list of trainable animals in the centralized cache
        /// </summary>
        private void StoreTrainableAnimalsCache(Map map, List<Pawn> animals)
        {
            if (map == null)
                return;

            int mapId = map.uniqueID;
            string cacheKey = this.GetType().Name + TRAINABLE_CACHE_SUFFIX;
            int currentTick = Find.TickManager.TicksGame;

            var animalCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Pawn>>(mapId);
            animalCache[cacheKey] = animals;
            Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, currentTick);

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Updated trainable animals cache for {this.GetType().Name}, found {animals.Count} animals");
            }
        }

        #endregion

        #region Job Processing

        /// <summary>
        /// Training-specific checks for animal interaction
        /// </summary>
        protected override bool IsValidForSpecificInteraction(Pawn handler, Pawn animal, bool forced)
        {
            // Skip if not of same faction
            if (animal.Faction != handler.Faction)
                return false;

            // Skip if no trainable left or trained too recently
            if (animal.training == null ||
                animal.training.NextTrainableToTrain() == null ||
                TrainableUtility.TrainedTooRecently(animal))
            {
                return false;
            }

            // Skip if animals marked venerated
            if (ModsConfig.IdeologyActive &&
                handler.Ideo != null &&
                handler.Ideo.IsVeneratedAnimal(animal))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Create job for training the animal
        /// </summary>
        protected override Job MakeInteractionJob(Pawn handler, Pawn animal, bool forced)
        {
            if (animal?.training?.NextTrainableToTrain() == null)
                return null;

            // Create the training job
            Job job = JobMaker.MakeJob(WorkJobDef, animal);

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"{handler.LabelShort} created job to train {animal.LabelShort} " +
                    $"({animal.training.NextTrainableToTrain().defName})");
            }

            return job;
        }

        /// <summary>
        /// Custom food check for training animals
        /// </summary>
        protected override bool HasFoodToInteractAnimal(Pawn pawn, Pawn animal)
        {
            // Some animals don't need food for training
            if (!animal.RaceProps.EatsFood || animal.needs?.food == null)
                return true;

            // Use base implementation for standard food check
            return base.HasFoodToInteractAnimal(pawn, animal);
        }

        #endregion

        #region Reset

        /// <summary>
        /// Reset the training-specific cache
        /// </summary>
        public override void Reset()
        {
            // Call base reset first
            base.Reset();

            // Now clear training-specific caches
            foreach (int mapId in Find.Maps.Select(map => map.uniqueID))
            {
                string cacheKey = this.GetType().Name + TRAINABLE_CACHE_SUFFIX;
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
                Utility_DebugManager.LogNormal($"Reset training caches for {this.GetType().Name}");
            }
        }

        #endregion

        #region Utility

        /// <summary>
        /// For debugging purposes
        /// </summary>
        public override string ToString()
        {
            return "JobGiver_Train_PawnControl";
        }

        #endregion
    }
}