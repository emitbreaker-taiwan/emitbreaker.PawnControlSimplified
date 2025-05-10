using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for taming wild animals
    /// </summary>
    public class JobModule_Handling_Tame : JobModule_Handling_InteractAnimal
    {
        public override string UniqueID => "TameAnimal";
        public override float Priority => 6.0f;
        public override string Category => "AnimalHandling";

        // Static caches for map-specific data persistence
        private static readonly Dictionary<int, List<Pawn>> _tamableAnimalsCache = new Dictionary<int, List<Pawn>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _tamabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static int _lastUpdateCacheTick = -999;

        public JobModule_Handling_Tame()
        {
            // Initialize interaction flags from parent class
            canInteractWhileSleeping = false;
            canInteractWhileRoaming = false;
        }

        public override bool ShouldProcessAnimal(Pawn animal, Map map)
        {
            try
            {
                if (animal == null || !animal.Spawned || animal.Dead)
                    return false;

                // Must be an animal
                if (!animal.RaceProps.Animal)
                    return false;

                // Must be wild (not already tamed)
                if (animal.Faction != null)
                    return false;

                // Must be designated for taming
                if (map.designationManager.DesignationOn(animal, DesignationDefOf.Tame) == null)
                    return false;

                // Must not be in a mental state
                if (animal.InMentalState)
                    return false;

                // Must not be downed
                if (animal.Downed)
                    return false;

                return true;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in ShouldProcessAnimal for taming: {ex.Message}");
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
            if (!_tamableAnimalsCache.ContainsKey(mapId))
                _tamableAnimalsCache[mapId] = new List<Pawn>();

            if (!_reachabilityCache.ContainsKey(mapId))
                _reachabilityCache[mapId] = new Dictionary<Pawn, bool>();

            if (!_tamabilityCache.ContainsKey(mapId))
                _tamabilityCache[mapId] = new Dictionary<Pawn, bool>();

            // Only do a full update if needed
            if (currentTick > _lastUpdateCacheTick + CacheUpdateInterval ||
                !_tamableAnimalsCache.ContainsKey(mapId))
            {
                try
                {
                    // Clear existing caches for this map
                    _tamableAnimalsCache[mapId].Clear();
                    _reachabilityCache[mapId].Clear();
                    _tamabilityCache[mapId].Clear();

                    // Find all tamable animals
                    foreach (var animal in map.mapPawns.AllPawnsSpawned)
                    {
                        if (ShouldProcessAnimal(animal, map))
                        {
                            _tamableAnimalsCache[mapId].Add(animal);
                            targetCache.Add(animal);
                        }
                    }

                    if (_tamableAnimalsCache[mapId].Count > 0)
                    {
                        Utility_DebugManager.LogNormal(
                            $"Found {_tamableAnimalsCache[mapId].Count} animals marked for taming on map {map.uniqueID}");
                        hasTargets = true;
                    }

                    _lastUpdateCacheTick = currentTick;
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"Error updating tamable animals cache: {ex}");
                }
            }
            else
            {
                // Just add the cached animals to the target cache
                foreach (Pawn animal in _tamableAnimalsCache[mapId])
                {
                    // Skip animals that are no longer valid for taming
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
                // Call base validation first
                if (!base.ValidateHandlingJob(animal, handler))
                    return false;

                // Check taming-specific requirements
                if (TameUtility.TriedToTameTooRecently(animal))
                {
                    JobFailReason.Is(AnimalInteractedTooRecentlyTrans);
                    return false;
                }

                // Check if food is required and available
                if (animal.RaceProps.EatsFood && animal.needs?.food != null)
                {
                    if (!HasFoodToInteractAnimal(handler, animal))
                    {
                        JobFailReason.Is(NoUsableFoodTrans);
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error validating taming job: {ex.Message}");
                return false;
            }
        }

        protected override Job CreateHandlingJob(Pawn handler, Pawn animal)
        {
            try
            {
                Thing foodThing = null;
                int count = -1;

                // Find food for taming if needed
                if (animal.RaceProps.EatsFood && animal.needs?.food != null && !HasFoodToInteractAnimal(handler, animal))
                {
                    ThingDef foodDef;
                    float requiredNutrition = JobDriver_InteractAnimal.RequiredNutritionPerFeed(animal) * 2f * 4f;

                    foodThing = FoodUtility.BestFoodSourceOnMap(
                        handler, animal, desperate: false, out foodDef,
                        FoodPreferability.RawTasty, allowPlant: false, allowDrug: false, allowCorpse: false,
                        allowDispenserFull: false, allowDispenserEmpty: false, allowForbidden: false,
                        allowSociallyImproper: false, allowHarvest: false, forceScanWholeMap: false,
                        ignoreReservations: false, calculateWantedStackCount: false,
                        FoodPreferability.Undefined, requiredNutrition);

                    if (foodThing != null)
                    {
                        float nutrition = FoodUtility.GetNutrition(animal, foodThing, foodDef);
                        count = FoodUtility.StackCountForNutrition(requiredNutrition, nutrition);
                    }
                }

                // Create the taming job with the food
                Job job = JobMaker.MakeJob(JobDefOf.Tame, animal);
                job.targetC = foodThing;
                job.count = count;

                Utility_DebugManager.LogNormal(
                    $"{handler.LabelShort} created job to tame {animal.LabelShort}" +
                    (foodThing != null ? $" with {foodThing.LabelShort}" : ""));

                return job;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating taming job: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public override void ResetStaticData()
        {
            base.ResetStaticData();

            // Clear all specialized caches
            Utility_CacheManager.ResetJobGiverCache(_tamableAnimalsCache, _reachabilityCache);

            foreach (var tamabilityMap in _tamabilityCache.Values)
            {
                tamabilityMap.Clear();
            }
            _tamabilityCache.Clear();

            _lastUpdateCacheTick = -999;
        }
    }
}