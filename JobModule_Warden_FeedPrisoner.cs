using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for feeding prisoners
    /// </summary>
    public class JobModule_Warden_FeedPrisoner : JobModule_Warden
    {
        public override string UniqueID => "FeedPrisoner";
        public override float Priority => 6.2f;
        public override string Category => "PrisonerCare"; // Added category for consistency

        // Static caches for map-specific data persistence
        private static readonly Dictionary<int, List<Pawn>> _hungryPrisonersCache = new Dictionary<int, List<Pawn>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static int _lastUpdateCacheTick = -999;

        // Cache for food source information - map specific
        private static readonly Dictionary<int, Dictionary<Pawn, Thing>> _prisonerToFoodSourceMap = new Dictionary<int, Dictionary<Pawn, Thing>>();
        private static readonly Dictionary<int, Dictionary<Pawn, ThingDef>> _prisonerToFoodDefMap = new Dictionary<int, Dictionary<Pawn, ThingDef>>();

        public override bool ShouldProcessPrisoner(Pawn prisoner)
        {
            try
            {
                return prisoner != null &&
                       prisoner.guest != null &&
                       WardenFeedUtility.ShouldBeFed(prisoner) &&
                       prisoner.needs?.food != null &&
                       prisoner.needs.food.CurLevelPercentage < prisoner.needs.food.PercentageThreshHungry + 0.02f;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in ShouldProcessPrisoner for feed job: {ex.Message}");
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
            if (!_hungryPrisonersCache.ContainsKey(mapId))
                _hungryPrisonersCache[mapId] = new List<Pawn>();

            if (!_reachabilityCache.ContainsKey(mapId))
                _reachabilityCache[mapId] = new Dictionary<Pawn, bool>();

            if (!_prisonerToFoodSourceMap.ContainsKey(mapId))
                _prisonerToFoodSourceMap[mapId] = new Dictionary<Pawn, Thing>();

            if (!_prisonerToFoodDefMap.ContainsKey(mapId))
                _prisonerToFoodDefMap[mapId] = new Dictionary<Pawn, ThingDef>();

            // Only do a full update if needed
            if (currentTick > _lastUpdateCacheTick + CacheUpdateInterval ||
                !_hungryPrisonersCache.ContainsKey(mapId))
            {
                try
                {
                    // Clear existing caches for this map
                    _hungryPrisonersCache[mapId].Clear();
                    _reachabilityCache[mapId].Clear();
                    _prisonerToFoodSourceMap[mapId].Clear();
                    _prisonerToFoodDefMap[mapId].Clear();

                    // Find all hungry prisoners
                    foreach (Pawn prisoner in map.mapPawns.PrisonersOfColonySpawned)
                    {
                        if (ShouldProcessPrisoner(prisoner))
                        {
                            _hungryPrisonersCache[mapId].Add(prisoner);
                            targetCache.Add(prisoner);

                            // Pre-cache best food sources when possible - don't worry about the pawn yet
                            if (FoodUtility.TryFindBestFoodSourceFor(
                                null, prisoner,
                                prisoner.needs.food.CurCategory == HungerCategory.Starving,
                                out Thing foodSource, out ThingDef foodDef,
                                false, allowCorpse: false))
                            {
                                _prisonerToFoodSourceMap[mapId][prisoner] = foodSource;
                                _prisonerToFoodDefMap[mapId][prisoner] = foodDef;
                            }
                        }
                    }

                    if (_hungryPrisonersCache[mapId].Count > 0)
                    {
                        Utility_DebugManager.LogNormal(
                            $"Found {_hungryPrisonersCache[mapId].Count} hungry prisoners on map {map.uniqueID}");
                    }

                    _lastUpdateCacheTick = currentTick;
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"Error updating hungry prisoner cache: {ex}");
                }
            }
            else
            {
                // Just add the cached prisoners to the target cache
                foreach (Pawn prisoner in _hungryPrisonersCache[mapId])
                {
                    // Skip prisoners who are no longer hungry (may have been fed)
                    if (prisoner.needs?.food != null &&
                        prisoner.needs.food.CurLevelPercentage >= prisoner.needs.food.PercentageThreshHungry + 0.02f)
                        continue;

                    targetCache.Add(prisoner);
                }
            }

            SetHasTargets(map, hasTargets || (targetCache != null && targetCache.Count > 0));
        }

        public override bool ValidateTarget(Pawn prisoner, Pawn warden)
        {
            try
            {
                if (prisoner == null || warden == null || !prisoner.Spawned || !warden.Spawned)
                    return false;

                // Check faction interaction
                if (!Utility_JobGiverManager.IsValidFactionInteraction(prisoner, warden, requiresDesignator: false))
                    return false;

                // Check if prisoner still needs food
                if (!ShouldProcessPrisoner(prisoner))
                    return false;

                // Check food restrictions
                if (prisoner.foodRestriction != null)
                {
                    var policy = prisoner.foodRestriction.GetCurrentRespectedRestriction(warden);
                    if (policy != null && policy.filter.AllowedDefCount == 0) return false;
                }

                // Check if warden can reach the prisoner
                if (!warden.CanReserveAndReach(prisoner, PathEndMode.ClosestTouch, warden.NormalMaxDanger()))
                    return false;

                // Try to find valid food
                return FoodUtility.TryFindBestFoodSourceFor(
                    warden, prisoner,
                    prisoner.needs.food.CurCategory == HungerCategory.Starving,
                    out Thing foodSource, out ThingDef foodDef,
                    false, allowCorpse: false);
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error validating prisoner feed job: {ex.Message}");
                return false;
            }
        }

        protected override Job CreateWardenJob(Pawn warden, Pawn prisoner)
        {
            try
            {
                int mapId = warden.Map.uniqueID;
                Thing foodSource = null;
                ThingDef foodDef = null;
                bool foundFood = false;

                // Try to use cached food source first - validate it's still valid for this warden
                if (_prisonerToFoodSourceMap.ContainsKey(mapId) &&
                    _prisonerToFoodSourceMap[mapId].TryGetValue(prisoner, out Thing cachedFood) &&
                    _prisonerToFoodDefMap[mapId].TryGetValue(prisoner, out ThingDef cachedDef))
                {
                    // Validate the cached food is still valid
                    if (cachedFood != null && !cachedFood.Destroyed && cachedFood.Spawned &&
                        warden.CanReserveAndReach(cachedFood, PathEndMode.ClosestTouch, warden.NormalMaxDanger()))
                    {
                        foodSource = cachedFood;
                        foodDef = cachedDef;
                        foundFood = true;
                    }
                }

                // If no valid cached food or cache miss, try to find food now
                if (!foundFood && FoodUtility.TryFindBestFoodSourceFor(
                    warden, prisoner,
                    prisoner.needs.food.CurCategory == HungerCategory.Starving,
                    out foodSource, out foodDef,
                    false, allowCorpse: false))
                {
                    foundFood = true;

                    // Cache the new food source for future use
                    if (_prisonerToFoodSourceMap.ContainsKey(mapId))
                    {
                        _prisonerToFoodSourceMap[mapId][prisoner] = foodSource;
                        _prisonerToFoodDefMap[mapId][prisoner] = foodDef;
                    }
                }

                if (foundFood)
                {
                    float nutrition = FoodUtility.GetNutrition(prisoner, foodSource, foodDef);
                    Job job = JobMaker.MakeJob(JobDefOf.FeedPatient, foodSource, prisoner);
                    job.count = FoodUtility.WillIngestStackCountOf(prisoner, foodDef, nutrition);

                    bool disliked = FoodUtility.MoodFromIngesting(prisoner, foodSource,
                        FoodUtility.GetFinalIngestibleDef(foodSource)) < 0;
                    string foodInfo = disliked ? " (disliked food)" : "";

                    Utility_DebugManager.LogNormal(
                        $"{warden.LabelShort} created job to feed prisoner {prisoner.LabelShort}{foodInfo}");
                    return job;
                }

                return null;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating prisoner feed job: {ex.Message}");
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
            Utility_CacheManager.ResetJobGiverCache(_hungryPrisonersCache, _reachabilityCache);

            foreach (var foodSourceMap in _prisonerToFoodSourceMap.Values)
            {
                foodSourceMap.Clear();
            }
            _prisonerToFoodSourceMap.Clear();

            foreach (var foodDefMap in _prisonerToFoodDefMap.Values)
            {
                foodDefMap.Clear();
            }
            _prisonerToFoodDefMap.Clear();

            _lastUpdateCacheTick = -999;
        }
    }
}