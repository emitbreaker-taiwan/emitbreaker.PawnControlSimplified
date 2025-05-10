using emitbreaker.PawnControl;
using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

/// <summary>
/// Module for delivering food to prisoners
/// </summary>
public class JobModule_Warden_DeliverFood : JobModule_Warden
{
    public override string UniqueID => "DeliverFoodPrisoner";
    public override float Priority => 5.8f;
    public override string Category => "PrisonerCare"; // Added category for consistency

    // Static caches for map-specific data persistence
    private static readonly Dictionary<int, List<Pawn>> _hungryPrisonersCache = new Dictionary<int, List<Pawn>>();
    private static readonly Dictionary<int, Dictionary<Pawn, bool>> _foodAvailabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
    private static readonly Dictionary<int, Dictionary<Pawn, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
    private static int _lastUpdateCacheTick = -999;

    public override bool ShouldProcessPrisoner(Pawn prisoner)
    {
        try
        {
            return prisoner != null &&
                   prisoner.guest != null &&
                   prisoner.guest.CanBeBroughtFood &&
                   prisoner.Position.IsInPrisonCell(prisoner.Map) &&
                   prisoner.needs?.food != null &&
                   prisoner.needs.food.CurLevelPercentage < prisoner.needs.food.PercentageThreshHungry + 0.02f &&
                   !WardenFeedUtility.ShouldBeFed(prisoner) &&
                   !FoodAvailableInRoomTo(prisoner);
        }
        catch (Exception ex)
        {
            Utility_DebugManager.LogWarning($"Error in ShouldProcessPrisoner for food delivery: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Returns true if there's any ingestible food in the prisoner's room.
    /// Uses map-specific caching for better performance.
    /// </summary>
    private bool FoodAvailableInRoomTo(Pawn prisoner)
    {
        try
        {
            if (prisoner?.Map == null) return false;

            int mapId = prisoner.Map.uniqueID;

            // Check from cache first if available
            if (_foodAvailabilityCache.ContainsKey(mapId) &&
                _foodAvailabilityCache[mapId].TryGetValue(prisoner, out bool hasFood))
            {
                return hasFood;
            }

            // If not in cache, calculate and cache the result
            var room = prisoner.GetRoom();
            if (room == null) return false;

            foreach (var cell in room.Cells)
                foreach (var thing in cell.GetThingList(prisoner.Map))
                    if (thing.def.ingestible != null && FoodUtility.WillEat(prisoner, thing))
                    {
                        // Cache the positive result before returning
                        if (_foodAvailabilityCache.ContainsKey(mapId))
                            _foodAvailabilityCache[mapId][prisoner] = true;

                        return true;
                    }

            // Cache the negative result before returning
            if (_foodAvailabilityCache.ContainsKey(mapId))
                _foodAvailabilityCache[mapId][prisoner] = false;

            return false;
        }
        catch (Exception ex)
        {
            Utility_DebugManager.LogWarning($"Error checking food availability: {ex.Message}");
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

        if (!_foodAvailabilityCache.ContainsKey(mapId))
            _foodAvailabilityCache[mapId] = new Dictionary<Pawn, bool>();

        if (!_reachabilityCache.ContainsKey(mapId))
            _reachabilityCache[mapId] = new Dictionary<Pawn, bool>();

        // Only do a full update if needed
        if (currentTick > _lastUpdateCacheTick + CacheUpdateInterval ||
            !_hungryPrisonersCache.ContainsKey(mapId))
        {
            try
            {
                // Clear existing caches for this map
                _hungryPrisonersCache[mapId].Clear();
                _foodAvailabilityCache[mapId].Clear();
                _reachabilityCache[mapId].Clear();

                // Find all hungry prisoners needing food delivered
                foreach (var prisoner in map.mapPawns.PrisonersOfColony)
                {
                    if (ShouldProcessPrisoner(prisoner))
                    {
                        _hungryPrisonersCache[mapId].Add(prisoner);
                        targetCache.Add(prisoner);
                    }
                }

                if (_hungryPrisonersCache[mapId].Count > 0)
                {
                    Utility_DebugManager.LogNormal(
                        $"Found {_hungryPrisonersCache[mapId].Count} hungry prisoners requiring food delivery on map {map.uniqueID}");
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
                // Skip prisoners who no longer need food
                if (!ShouldProcessPrisoner(prisoner))
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

            // Check if prisoner still needs food
            if (!ShouldProcessPrisoner(prisoner))
                return false;

            // Check faction interaction
            if (!Utility_JobGiverManager.IsValidFactionInteraction(prisoner, warden, requiresDesignator: false))
                return false;

            int mapId = warden.Map.uniqueID;

            // Check if warden can reach and reserve the prisoner
            if (prisoner.IsForbidden(warden) ||
                !warden.CanReserveAndReach(prisoner, PathEndMode.Touch, warden.NormalMaxDanger()))
                return false;

            // Find food for the prisoner
            if (!FoodUtility.TryFindBestFoodSourceFor(
                warden, prisoner,
                prisoner.needs.food.CurCategory == HungerCategory.Starving,
                out Thing foodSource, out ThingDef foodDef,
                false, allowCorpse: false,
                calculateWantedStackCount: true
            )) return false;

            // Ensure the food isn't already in the prisoner's room
            return foodSource.GetRoom() != prisoner.GetRoom();
        }
        catch (Exception ex)
        {
            Utility_DebugManager.LogWarning($"Error validating food delivery job: {ex.Message}");
            return false;
        }
    }

    protected override Job CreateWardenJob(Pawn warden, Pawn prisoner)
    {
        try
        {
            if (FoodUtility.TryFindBestFoodSourceFor(
                warden, prisoner,
                prisoner.needs.food.CurCategory == HungerCategory.Starving,
                out Thing foodSource, out ThingDef foodDef,
                false, allowCorpse: false,
                calculateWantedStackCount: true
            ))
            {
                if (foodSource.GetRoom() == prisoner.GetRoom()) return null;

                float nutrition = FoodUtility.GetNutrition(prisoner, foodSource, foodDef);
                var job = JobMaker.MakeJob(JobDefOf.DeliverFood, foodSource, prisoner);
                job.count = FoodUtility.WillIngestStackCountOf(prisoner, foodDef, nutrition);
                job.targetC = RCellFinder.SpotToChewStandingNear(prisoner, foodSource);

                Utility_DebugManager.LogNormal(
                    $"{warden.LabelShort} created job to deliver food to prisoner {prisoner.LabelShort}");
                return job;
            }
            return null;
        }
        catch (Exception ex)
        {
            Utility_DebugManager.LogWarning($"Error creating food delivery job: {ex.Message}");
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

        foreach (var foodAvailabilityMap in _foodAvailabilityCache.Values)
        {
            foodAvailabilityMap.Clear();
        }
        _foodAvailabilityCache.Clear();

        _lastUpdateCacheTick = -999;
    }
}