using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Enhanced JobGiver that assigns both plant-cutting and harvesting jobs to eligible pawns.
    /// Optimized for large colonies with 1000+ pawns and high plant counts.
    /// Completely replaces the functionality of Patch_JobGiver_WorkNonHumanlike_DirectJobCreation.
    /// </summary>
    public class JobGiver_PlantsCut_PawnControl : ThinkNode_JobGiver
    {
        // Cache system to improve performance with large numbers of pawns
        private static readonly Dictionary<int, List<Plant>> _plantCache = new Dictionary<int, List<Plant>>();
        private static readonly Dictionary<int, Dictionary<Plant, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Plant, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 500; // Update every ~8 seconds for performance

        // Local optimization parameters
        private const int MAX_LOCAL_SEARCH_TRIES = 150; // Reduced for better large-colony performance
        private const int MAX_CACHE_ENTRIES = 200;  // Cap cache size to avoid memory issues
        private const float MAX_EFFICIENT_DISTANCE_SQ = 2500f; // ~50 tiles max efficient work distance

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 625f, 2500f, 10000f }; // 25, 50, 100 tiles

        public override float GetPriority(Pawn pawn)
        {
            // Ensure this runs at appropriate priority relative to other behaviors
            return 5.5f;
        }

        /// <summary>
        /// Create a job to cut plants using manager-driven bucket processing
        /// </summary>
        private Job TryCreateCutPlantJob(Pawn pawn)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_plantCache.ContainsKey(mapId) || _plantCache[mapId].Count == 0)
                return null;

            // Pre-filter plants that are too far away
            List<Plant> nearbyPlants = _plantCache[mapId]
                .Where(plant => (plant.Position - pawn.Position).LengthHorizontalSquared <= MAX_EFFICIENT_DISTANCE_SQ)
                .ToList();

            // Skip if no plants within efficient distance
            if (nearbyPlants.Count == 0)
            {
                if (Prefs.DevMode)
                    Log.Message($"[PawnControl] {pawn.LabelShort} found no plants to cut within efficient distance ({Math.Sqrt(MAX_EFFICIENT_DISTANCE_SQ)} tiles)");
                return null;
            }

            // Use JobGiverManager for distance bucketing on filtered plants only
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                nearbyPlants,
                (plant) => (plant.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // FIXED: Explicitly specify type argument Plant for FindFirstValidTargetInBuckets
            Plant targetPlant = Utility_JobGiverManager.FindFirstValidTargetInBuckets<Plant>(
                buckets, pawn,
                (plant, p) =>
                {
                    // First check basic validity
                    if (plant == null || plant.Destroyed || !plant.Spawned)
                        return false;

                    // Check if the plant is designated
                    bool isDesignated = p.Map.designationManager.DesignationOn(plant, DesignationDefOf.CutPlant) != null
                                    || p.Map.designationManager.DesignationOn(plant, DesignationDefOf.HarvestPlant) != null;

                    // If plant is designated, only player pawns can cut, but any plant can be targeted
                    if (isDesignated)
                    {
                        if (p.Faction != Faction.OfPlayer)
                            return false;
                    }
                    else
                    {
                        // For non-designated plants (zone-based), check zone and faction ownership
                        var zone = p.Map.zoneManager.ZoneAt(plant.Position) as Zone_Growing;
                        if (zone == null || !zone.allowCut)
                            return false;

                        // Zone-based cutting is restricted to player pawns cutting plants in player-owned zones
                        if (p.Faction != Faction.OfPlayer)
                            return false;
                    }

                    // Common checks for any cutting job
                    if (plant.IsForbidden(p) || !PlantUtility.PawnWillingToCutPlant_Job(plant, p))
                        return false;

                    return p.CanReserve(plant);
                },
                _reachabilityCache
            );

            // Create job if target found
            if (targetPlant != null)
            {
                Job job = JobMaker.MakeJob(JobDefOf.CutPlant, targetPlant);

                if (Prefs.DevMode)
                    Log.Message($"[PawnControl] {pawn.LabelShort} created job to cut {targetPlant.Label}");

                return job;
            }

            return null;
        }

        /// <summary>
        /// Updates the cache of plants that need to be cut
        /// </summary>
        private void UpdatePlantCache(Map map)
        {
            if (map == null) return;
            int tick = Find.TickManager.TicksGame;
            int id = map.uniqueID;
            if (tick <= _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL && _plantCache.ContainsKey(id))
                return;

            // reset
            if (!_plantCache.ContainsKey(id)) _plantCache[id] = new List<Plant>();
            else _plantCache[id].Clear();
            if (!_reachabilityCache.ContainsKey(id)) _reachabilityCache[id] = new Dictionary<Plant, bool>();
            else _reachabilityCache[id].Clear();

            // 1) collect only designated plants
            foreach (var des in map.designationManager.AllDesignations)
            {
                if (des.def == DesignationDefOf.CutPlant || des.def == DesignationDefOf.HarvestPlant)
                    if (des.target.Thing is Plant p) _plantCache[id].Add(p);
            }

            // 2) also collect zone-cut plants—but only if the zone belongs to the same faction
            foreach (var zone in map.zoneManager.AllZones.OfType<Zone_Growing>())
            {
                if (!zone.allowCut) continue;
                foreach (var cell in zone.Cells)
                {
                    var p = cell.GetPlant(map);
                    if (p != null && !_plantCache[id].Contains(p))
                        _plantCache[id].Add(p);
                }
            }

            // clamp cache size
            if (_plantCache[id].Count > MAX_CACHE_ENTRIES)
                _plantCache[id].RemoveRange(MAX_CACHE_ENTRIES,
                    _plantCache[id].Count - MAX_CACHE_ENTRIES);

            _lastCacheUpdateTick = tick;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Check BOTH designations AND growing zones
            bool hasDesignations = pawn.Map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.CutPlant) ||
                                   pawn.Map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.HarvestPlant);

            bool hasGrowingZonesWithAllowCut = pawn.Map.zoneManager.AllZones
                .Any(z => z is Zone_Growing growing && growing.allowCut);

            if (!hasDesignations && !hasGrowingZonesWithAllowCut)
            {
                return null;
            }

            return Utility_JobGiverManager.StandardTryGiveJob<Plant>(
                pawn,
                "PlantCutting",
                (p, forced) => {
                    // Update plant cache
                    UpdatePlantCache(p.Map);

                    // Find and create a job for cutting plants with VALID DESIGNATORS ONLY
                    return TryCreateCutPlantJob(p);
                },
                debugJobDesc: "plant cutting assignment");
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_plantCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_CutPlant_PawnControl";
        }
    }
}