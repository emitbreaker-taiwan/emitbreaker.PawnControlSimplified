using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for training animals owned by the player's faction
    /// </summary>
    public class JobModule_Handling_Train : JobModule_Handling_InteractAnimal
    {
        public override string UniqueID => "TrainAnimal";
        public override float Priority => 5.0f; // Same as original JobGiver
        public override string Category => "AnimalHandling";
        public override int CacheUpdateInterval => 250; // Same as original (every 4.16 seconds)

        // Static caches for map-specific data persistence
        private static readonly Dictionary<int, List<Pawn>> _trainableAnimalCache = new Dictionary<int, List<Pawn>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _trainabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static int _lastUpdateCacheTick = -999;

        // Constants from original class
        private const int MAX_CACHE_SIZE = 1000;

        /// <summary>
        /// Constructor to initialize animal interaction settings
        /// </summary>
        public JobModule_Handling_Train()
        {
            // Initialize interaction flags from parent class
            canInteractWhileSleeping = false;
            canInteractWhileRoaming = false;
        }

        public override bool ShouldProcessAnimal(Pawn animal, Map map)
        {
            try
            {
                if (animal == null || map == null || !animal.Spawned)
                    return false;

                return animal.IsNonMutantAnimal &&
                       animal.Faction == Faction.OfPlayer &&
                       animal.RaceProps.animalType != AnimalType.Dryad &&
                       animal.training != null &&
                       animal.training.NextTrainableToTrain() != null &&
                       !TrainableUtility.TrainedTooRecently(animal);
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in ShouldProcessAnimal for training: {ex}");
                return false;
            }
        }

        public override void UpdateCache(Map map, List<Pawn> targetCache)
        {
            base.UpdateCache(map, targetCache);

            if (map == null) return;
            int mapId = map.uniqueID;
            bool hasTargets = false;

            int currentTick = Find.TickManager.TicksGame;

            // Initialize caches if needed
            if (!_trainableAnimalCache.ContainsKey(mapId))
                _trainableAnimalCache[mapId] = new List<Pawn>();

            if (!_reachabilityCache.ContainsKey(mapId))
                _reachabilityCache[mapId] = new Dictionary<Pawn, bool>();

            if (!_trainabilityCache.ContainsKey(mapId))
                _trainabilityCache[mapId] = new Dictionary<Pawn, bool>();

            // Only do a full update if needed
            if (currentTick > _lastUpdateCacheTick + CacheUpdateInterval ||
                !_trainableAnimalCache.ContainsKey(mapId))
            {
                try
                {
                    // Clear existing caches for this map
                    _trainableAnimalCache[mapId].Clear();
                    _reachabilityCache[mapId].Clear();
                    _trainabilityCache[mapId].Clear();

                    // Get animals from the faction that can be trained
                    foreach (Pawn animal in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
                    {
                        if (ShouldProcessAnimal(animal, map))
                        {
                            _trainableAnimalCache[mapId].Add(animal);
                            targetCache.Add(animal);
                        }
                    }

                    // Limit cache size for performance
                    if (_trainableAnimalCache[mapId].Count > MAX_CACHE_SIZE)
                    {
                        _trainableAnimalCache[mapId] = _trainableAnimalCache[mapId].Take(MAX_CACHE_SIZE).ToList();
                    }

                    if (_trainableAnimalCache[mapId].Count > 0)
                    {
                        Utility_DebugManager.LogNormal(
                            $"Found {_trainableAnimalCache[mapId].Count} animals that need training on map {map.uniqueID}");
                        hasTargets = true;
                    }

                    _lastUpdateCacheTick = currentTick;
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"Error updating trainable animals cache: {ex}");
                }
            }
            else
            {
                // Just add the cached animals to the target cache
                foreach (Pawn animal in _trainableAnimalCache[mapId])
                {
                    // Skip animals that are no longer valid for training
                    if (!animal.Spawned || !ShouldProcessAnimal(animal, map))
                        continue;

                    targetCache.Add(animal);
                    hasTargets = true;
                }
            }

            SetHasTargets(map, hasTargets);
        }

        public override bool ValidateHandlingJob(Pawn animal, Pawn handler)
        {
            try
            {
                // Skip if trying to train self
                if (animal == handler)
                    return false;

                // Use base class validation for common animal interaction checks
                if (!base.ValidateHandlingJob(animal, handler))
                    return false;

                // Skip if no longer needs training
                if (animal.training == null || animal.training.NextTrainableToTrain() == null)
                    return false;

                // Skip if trained too recently
                if (TrainableUtility.TrainedTooRecently(animal))
                {
                    JobFailReason.Is(AnimalInteractedTooRecentlyTrans);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error validating training job: {ex}");
                return false;
            }
        }

        protected override Job CreateHandlingJob(Pawn handler, Pawn animal)
        {
            try
            {
                // Check if handler needs food for training
                if (animal.RaceProps.EatsFood && animal.needs?.food != null &&
                    !HasFoodToInteractAnimal(handler, animal))
                {
                    // Create a job to get food first using parent class method
                    Job takeFoodJob = TakeFoodForAnimalInteractJob(handler, animal);
                    if (takeFoodJob != null)
                    {
                        Utility_DebugManager.LogNormal($"{handler.LabelShort} created job to take food for training {animal.LabelShort}");
                        return takeFoodJob;
                    }
                    else
                    {
                        JobFailReason.Is(NoUsableFoodTrans);
                        return null;
                    }
                }

                // Create the training job
                Job job = JobMaker.MakeJob(JobDefOf.Train, animal);
                Utility_DebugManager.LogNormal($"{handler.LabelShort} created job to train {animal.LabelShort}");
                return job;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating training job: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public override void ResetStaticData()
        {
            base.ResetStaticData();

            Utility_CacheManager.ResetJobGiverCache(_trainableAnimalCache, _reachabilityCache);

            foreach (var trainabilityMap in _trainabilityCache.Values)
            {
                trainabilityMap.Clear();
            }
            _trainabilityCache.Clear();

            _lastUpdateCacheTick = -999;
        }
    }
}