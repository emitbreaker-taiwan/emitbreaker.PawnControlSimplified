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
    /// </summary>
    public class JobGiver_PlantCutting_PlantsCut_PawnControl : ThinkNode_JobGiver
    {
        // Cache for plants that need to be cut
        private static readonly Dictionary<int, List<Plant>> _plantCache = new Dictionary<int, List<Plant>>();
        private static readonly Dictionary<int, Dictionary<Plant, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Plant, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 500; // Update every ~8 seconds for performance

        // Local optimization parameters
        private const int MAX_CACHE_ENTRIES = 200; // Cap cache size to avoid memory issues
        private const float MAX_EFFICIENT_DISTANCE_SQ = 2500f; // ~50 tiles max efficient work distance

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 625f, 2500f, 10000f }; // 25, 50, 100 tiles

        public override float GetPriority(Pawn pawn)
        {
            // Plant cutting runs at slightly higher than default priority
            return 6.0f;
        }

        /// <summary>
        /// Main entry point for job assignment using the standard approach
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            // IMPORTANT: Only player pawns can cut plants
            if (pawn.Faction != Faction.OfPlayer &&
                !(pawn.IsSlave && pawn.HostFaction == Faction.OfPlayer))
                return null;

            return Utility_JobGiverManager.StandardTryGiveJob<Plant>(
                pawn,
                "PlantCutting",
                (p, forced) => {
                    // Update plant cache
                    UpdatePlantCache(p.Map);

                    // Find and create a job for cutting plants
                    return TryCreateCutPlantJob(p);
                },
                debugJobDesc: "plant cutting assignment");
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
                {
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} found no plants to cut within efficient distance ({Math.Sqrt(MAX_EFFICIENT_DISTANCE_SQ)} tiles)");
                }
                return null;
            }

            // Build distance buckets
            var buckets = CreateDistanceBucketsForPlants(
                pawn,
                nearbyPlants,
                DISTANCE_THRESHOLDS
            );

            // Find a valid plant to cut
            Plant targetPlant = FindFirstValidPlant(buckets, pawn);

            // Create job if target found
            if (targetPlant != null)
            {
                Job job = JobMaker.MakeJob(JobDefOf.CutPlant, targetPlant);
                if (Prefs.DevMode)
                {
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to cut {targetPlant.Label}");
                }
                return job;
            }

            return null;
        }

        private List<Plant>[] CreateDistanceBucketsForPlants(Pawn pawn, IEnumerable<Plant> plants, float[] distanceThresholds)
        {
            if (pawn == null || plants == null || distanceThresholds == null)
                return null;

            // Initialize buckets
            List<Plant>[] buckets = new List<Plant>[distanceThresholds.Length + 1];
            for (int i = 0; i < buckets.Length; i++)
                buckets[i] = new List<Plant>();

            foreach (Plant plant in plants)
            {
                // Get distance squared between pawn and plant
                float distSq = (plant.Position - pawn.Position).LengthHorizontalSquared;

                // Assign to appropriate bucket
                int bucketIndex = distanceThresholds.Length; // Default to last bucket (furthest)
                for (int i = 0; i < distanceThresholds.Length; i++)
                {
                    if (distSq < distanceThresholds[i])
                    {
                        bucketIndex = i;
                        break;
                    }
                }

                buckets[bucketIndex].Add(plant);
            }

            return buckets;
        }

        /// <summary>
        /// Find the first valid plant for cutting
        /// </summary>
        private Plant FindFirstValidPlant(List<Plant>[] buckets, Pawn pawn, bool forced = false)
        {
            if (buckets == null || pawn?.Map == null) return null;

            // Process buckets from closest to farthest
            for (int b = 0; b < buckets.Length; b++)
            {
                if (buckets[b] == null || buckets[b].Count == 0)
                    continue;

                // Randomize within each distance band for better distribution
                buckets[b].Shuffle();

                // Check each plant in this distance band
                foreach (Plant plant in buckets[b])
                {
                    // Skip validation for invalid plants
                    if (plant == null || plant.Destroyed || !plant.Spawned)
                        continue;

                    // Check reachability cache
                    int mapId = pawn.Map.uniqueID;
                    if (!_reachabilityCache.TryGetValue(mapId, out var mapReachability))
                    {
                        mapReachability = new Dictionary<Plant, bool>();
                        _reachabilityCache[mapId] = mapReachability;
                    }

                    if (mapReachability.TryGetValue(plant, out bool canReach))
                    {
                        if (!canReach) continue;
                    }
                    else
                    {
                        canReach = pawn.CanReserveAndReach(plant, PathEndMode.Touch, pawn.NormalMaxDanger());
                        mapReachability[plant] = canReach;
                        if (!canReach) continue;
                    }

                    // Check if the plant is valid for cutting
                    if (ValidatePlantTarget(plant, pawn, forced))
                        return plant;
                }
            }

            return null;
        }

        /// <summary>
        /// Validates if a plant is a valid cutting target for a pawn
        /// </summary>
        private bool ValidatePlantTarget(Plant plant, Pawn pawn, bool forced = false)
        {
            // First check basic validity
            if (plant == null || plant.Destroyed || !plant.Spawned)
                return false;

            // Check if the plant is designated
            bool isDesignated = pawn.Map.designationManager.DesignationOn(plant, DesignationDefOf.CutPlant) != null ||
                              pawn.Map.designationManager.DesignationOn(plant, DesignationDefOf.HarvestPlant) != null;

            // If plant is designated, only player pawns can cut, but any plant can be targeted
            if (isDesignated)
            {
                if (pawn.Faction != Faction.OfPlayer)
                    return false;
            }
            else
            {
                // For non-designated plants (zone-based), check zone and faction ownership
                var zone = pawn.Map.zoneManager.ZoneAt(plant.Position) as Zone_Growing;
                if (zone == null || !zone.allowCut)
                    return false;

                // Zone-based cutting is restricted to player pawns cutting plants in player-owned zones
                if (pawn.Faction != Faction.OfPlayer)
                    return false;
            }

            // Common checks for any cutting job
            if (plant.IsForbidden(pawn) || !PlantUtility.PawnWillingToCutPlant_Job(plant, pawn))
                return false;

            return pawn.CanReserve(plant, 1, -1, null, forced);
        }

        /// <summary>
        /// Updates the cache of plants that need to be cut
        /// </summary>
        private void UpdatePlantCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_plantCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (!_plantCache.TryGetValue(mapId, out var plantList))
                {
                    plantList = new List<Plant>();
                    _plantCache[mapId] = plantList;
                }
                else
                {
                    plantList.Clear();
                }

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Plant, bool>();

                // 1) Collect designated plants
                foreach (var des in map.designationManager.AllDesignations)
                {
                    if (des.def == DesignationDefOf.CutPlant || des.def == DesignationDefOf.HarvestPlant)
                    {
                        if (des.target.Thing is Plant plant)
                        {
                            plantList.Add(plant);
                        }
                    }
                }

                // 2) Collect zone-cut plants
                foreach (var zone in map.zoneManager.AllZones.OfType<Zone_Growing>())
                {
                    if (!zone.allowCut) continue;

                    // Get the plant type intended for this zone
                    ThingDef plantDefToGrow = zone.GetPlantDefToGrow();

                    foreach (var cell in zone.Cells)
                    {
                        Plant plant = cell.GetPlant(map);
                        // Only add plants that don't match the zone's intended crop
                        if (plant != null && !_plantCache[mapId].Contains(plant) &&
                            (plantDefToGrow == null || plant.def != plantDefToGrow))
                        {
                            _plantCache[mapId].Add(plant);
                        }
                    }
                }

                // Limit cache size
                if (plantList.Count > MAX_CACHE_ENTRIES)
                {
                    plantList.RemoveRange(MAX_CACHE_ENTRIES, plantList.Count - MAX_CACHE_ENTRIES);
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            // Clear plant cache
            foreach (var mapCache in _plantCache)
            {
                mapCache.Value.Clear();
            }
            _plantCache.Clear();

            // Clear reachability cache
            foreach (var mapCache in _reachabilityCache)
            {
                mapCache.Value.Clear();
            }
            _reachabilityCache.Clear();

            _lastCacheUpdateTick = -999;
        }

        /// <summary>
        /// Provides a readable string representation of this job giver
        /// </summary>
        public override string ToString()
        {
            return "JobGiver_PlantCutting_PlantsCut_PawnControl";
        }
    }
}