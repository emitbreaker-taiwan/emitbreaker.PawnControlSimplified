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
    /// JobGiver that assigns harvesting tasks to pawns with the Growing work type.
    /// Optimized for large colonies with many plants using distance-based bucketing.
    /// </summary>
    public class JobGiver_Growing_GrowerHarvest_PawnControl : JobGiver_Growing_PawnControl
    {
        #region Configuration

        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.Harvest;

        // Maximum harvest work amount per job
        private const float MAX_HARVEST_WORK_PER_JOB = 2400f;

        // Maximum plants per job
        private const int MAX_PLANTS_PER_JOB = 40;

        // Cache key for harvestable plants
        private const string HARVEST_CACHE_KEY_SUFFIX = "_HarvestablePlants";

        #endregion

        #region Overrides

        /// <summary>
        /// Description for logging
        /// </summary>
        protected override string JobDescription => "harvest assignment";

        /// <summary>
        /// Harvesting is important for obtaining food and resources
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            return 5.8f;
        }

        public override bool ShouldSkip(Pawn pawn)
        {
            base.ShouldSkip(pawn);

            if (Utility_Common.PawnIsNotPlayerFaction(pawn))
                return true;

            return false;
        }

        /// <summary>
        /// Override TryGiveJob to use the common helper method with a custom processor for harvesting
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            // Use the CreateGrowingJob helper with a custom processor for harvesting
            return CreateGrowingJob<JobGiver_Growing_GrowerHarvest_PawnControl>(pawn, ProcessCellsForHarvesting);
        }

        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            if (targets == null || targets.Count == 0)
                return null;

            List<IntVec3> targetCells = targets
                .Where(t => t is Plant)
                .Select(t => t.Position)
                .ToList();

            return ProcessCellsForHarvesting(pawn, targetCells);
        }

        /// <summary>
        /// Only accept zones with plants ready to harvest
        /// </summary>
        protected override bool ExtraRequirements(IPlantToGrowSettable settable, Pawn pawn)
        {
            // For harvesting, we check for actual plants in the cell filtering step
            // Always return true here to include all growing zones and plant growers
            return true;
        }

        #endregion

        #region Harvesting-specific implementation

        /// <summary>
        /// Get or create a cache of harvestable plants for a specific map
        /// </summary>
        private List<Plant> GetOrCreateHarvestablePlantsCache(Pawn pawn, List<IntVec3> cells)
        {
            if (pawn?.Map == null || cells == null || cells.Count == 0)
                return new List<Plant>();

            int mapId = pawn.Map.uniqueID;
            string cacheKey = this.GetType().Name + HARVEST_CACHE_KEY_SUFFIX;

            // Try to get cached plants from the central cache manager
            var plantCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Plant>>(mapId);

            // Check if we need to update the cache
            int currentTick = Find.TickManager.TicksGame;
            int lastUpdateTick = Utility_MapCacheManager.GetLastCacheUpdateTick(mapId, cacheKey);

            if (currentTick - lastUpdateTick > CacheUpdateInterval ||
                !plantCache.TryGetValue(cacheKey, out List<Plant> result) ||
                result == null ||
                result.Any(p => p == null || p.Destroyed))
            {
                // Cache is invalid or expired, rebuild it
                result = new List<Plant>();
                Map map = pawn.Map;

                // Find harvestable plants in the cells
                foreach (IntVec3 cell in cells)
                {
                    Plant plant = cell.GetPlant(map);
                    if (plant == null)
                        continue;

                    // Check if harvestable - don't check pawn-specific conditions yet
                    // as those may change between different pawns using the same cache
                    if (IsPlantGenericHarvestable(plant, map))
                    {
                        result.Add(plant);
                    }
                }

                // Store in the central cache
                plantCache[cacheKey] = result;
                Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, currentTick);

                if (Prefs.DevMode)
                {
                    Utility_DebugManager.LogNormal($"Updated harvest cache for {this.GetType().Name}, found {result.Count} harvestable plants");
                }
            }

            return result;
        }

        /// <summary>
        /// Check if a plant is harvestable in general (no pawn-specific checks)
        /// </summary>
        private bool IsPlantGenericHarvestable(Plant plant, Map map)
        {
            if (plant == null || plant.Destroyed || !plant.Spawned)
                return false;

            if (!plant.HarvestableNow || plant.LifeStage != PlantLifeStage.Mature || !plant.CanYieldNow())
                return false;

            // Check if plant is designated for harvest
            if (map.designationManager.DesignationOn(plant, DesignationDefOf.HarvestPlant) != null)
                return true;

            // Check if plant is in a growing zone
            Zone_Growing zone = plant.Position.GetZone(map) as Zone_Growing;
            if (zone == null)
                return plant.def.plant.autoHarvestable; // Only wild plants with autoHarvestable

            // Check if the plant matches what the zone wants to grow
            ThingDef plantDef = zone.GetPlantDefToGrow();
            return plantDef != null && plant.def == plantDef;
        }

        /// <summary>
        /// Filter plants for a specific pawn from the cached harvestable plants
        /// </summary>
        private List<Plant> FilterPlantsForPawn(Pawn pawn, List<Plant> harvestable)
        {
            if (pawn?.Map == null || harvestable == null || harvestable.Count == 0)
                return new List<Plant>();

            List<Plant> result = new List<Plant>();

            foreach (Plant plant in harvestable)
            {
                // Skip destroyed or invalid plants
                if (plant == null || plant.Destroyed || !plant.Spawned)
                    continue;

                // Check pawn-specific conditions
                if (!plant.IsForbidden(pawn) &&
                    pawn.CanReserve(plant, 1, -1) &&
                    PlantUtility.PawnWillingToCutPlant_Job(plant, pawn))
                {
                    result.Add(plant);
                }
            }

            return result;
        }

        /// <summary>
        /// Custom processor for harvesting jobs
        /// </summary>
        private Job ProcessCellsForHarvesting(Pawn pawn, List<IntVec3> cells)
        {
            if (pawn?.Map == null || cells == null || cells.Count == 0)
                return null;

            // Get harvestable plants from cache
            List<Plant> cachedHarvestable = GetOrCreateHarvestablePlantsCache(pawn, cells);

            // Filter for pawn-specific conditions
            List<Plant> harvestablePlants = FilterPlantsForPawn(pawn, cachedHarvestable);

            if (harvestablePlants.Count == 0)
                return null;

            Map map = pawn.Map;

            // Sort by distance to pawn
            harvestablePlants.Sort((a, b) =>
            {
                return (a.Position - pawn.Position).LengthHorizontalSquared.CompareTo(
                        (b.Position - pawn.Position).LengthHorizontalSquared);
            });

            // Create harvesting job with primary plant
            Plant targetPlant = harvestablePlants[0];
            Job job = JobMaker.MakeJob(WorkJobDef);
            job.AddQueuedTarget(TargetIndex.A, targetPlant);

            // Add additional nearby harvestable plants of the same type
            Room startingRoom = targetPlant.Position.GetRoom(map);
            ThingDef wantedPlantDef = CalculateWantedPlantDef(targetPlant.Position, map);
            float totalHarvestWork = targetPlant.def.plant.harvestWork;
            int plantsAdded = 1;

            // Try to find additional plants in the same room
            for (int i = 1; i < harvestablePlants.Count; i++)
            {
                Plant plant = harvestablePlants[i];

                // Skip if we've reached our limits
                if (plantsAdded >= MAX_PLANTS_PER_JOB || totalHarvestWork > MAX_HARVEST_WORK_PER_JOB)
                    break;

                // Skip if plant is not in the same room
                if (plant.Position.GetRoom(map) != startingRoom)
                    continue;

                // Only add plants of the wanted type in growing zones
                Zone_Growing zone = plant.Position.GetZone(map) as Zone_Growing;
                if (zone != null && zone.GetPlantDefToGrow() != plant.def && zone.GetPlantDefToGrow() != null)
                    continue;

                // Add this plant to the job
                job.AddQueuedTarget(TargetIndex.A, plant);
                totalHarvestWork += plant.def.plant.harvestWork;
                plantsAdded++;
            }

            // Sort targets by distance if there are enough of them
            if (job.targetQueueA != null && job.targetQueueA.Count >= 3)
            {
                job.targetQueueA.SortBy((targ) => targ.Cell.DistanceToSquared(pawn.Position));
            }

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to harvest {plantsAdded} plants starting with {targetPlant.Label}");
            }

            return job;
        }

        /// <summary>
        /// Check if a plant is valid for harvesting for a specific pawn
        /// </summary>
        private bool IsPlantHarvestable(Plant plant, Pawn pawn, Map map)
        {
            if (plant == null || plant.Destroyed || !plant.Spawned)
                return false;

            if (!plant.HarvestableNow || plant.LifeStage != PlantLifeStage.Mature || !plant.CanYieldNow())
                return false;

            if (plant.IsForbidden(pawn) || !pawn.CanReserve(plant, 1, -1))
                return false;

            if (!PlantUtility.PawnWillingToCutPlant_Job(plant, pawn))
                return false;

            // Check if plant is designated for harvest
            if (map.designationManager.DesignationOn(plant, DesignationDefOf.HarvestPlant) != null)
                return true;

            // Check if plant is in a growing zone
            Zone_Growing zone = plant.Position.GetZone(map) as Zone_Growing;
            if (zone == null)
                return plant.def.plant.autoHarvestable; // Only wild plants with autoHarvestable

            // Check if the plant matches what the zone wants to grow
            ThingDef plantDef = zone.GetPlantDefToGrow();
            return plantDef != null && plant.def == plantDef;
        }

        #endregion

        #region Reset

        /// <summary>
        /// Reset the cache - extends base Reset implementation
        /// </summary>
        public override void Reset()
        {
            // Call base reset first to handle growing cell caches
            base.Reset();

            // Now clear harvesting-specific caches
            foreach (int mapId in Find.Maps.Select(map => map.uniqueID))
            {
                string cacheKey = this.GetType().Name + HARVEST_CACHE_KEY_SUFFIX;
                var plantCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Plant>>(mapId);

                if (plantCache.ContainsKey(cacheKey))
                {
                    plantCache.Remove(cacheKey);
                }

                // Clear the update tick record too
                Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, -1);
            }

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Reset harvest caches for {this.GetType().Name}");
            }
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return "JobGiver_Growing_GrowerHarvest_PawnControl";
        }

        #endregion
    }
}