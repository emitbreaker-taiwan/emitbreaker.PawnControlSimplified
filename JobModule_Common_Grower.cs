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
    /// Common abstract base class for modules that handle growing operations.
    /// Provides shared functionality for sowing, harvesting, and plant maintenance.
    /// </summary>
    public abstract class JobModule_Common_Grower : JobModuleCore
    {
        // Cache for growing cells that need work
        protected static readonly Dictionary<int, List<IntVec3>> _growingCellCache = new Dictionary<int, List<IntVec3>>();
        protected static readonly Dictionary<int, Dictionary<Plant, bool>> _plantReachabilityCache = new Dictionary<int, Dictionary<Plant, bool>>();
        protected static readonly Dictionary<int, Dictionary<IntVec3, bool>> _cellReachabilityCache = new Dictionary<int, Dictionary<IntVec3, bool>>();
        protected static int _lastCacheUpdateTick = -999;

        // Define constants for cache management
        public override int CacheUpdateInterval => 240; // 4 seconds
        protected const int MAX_CACHE_SIZE = 200; // Grow zones can be large

        // Define distance thresholds for bucketing
        protected static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        // Cache for plant def lookups
        protected static readonly Dictionary<IntVec3, ThingDef> _plantDefCache = new Dictionary<IntVec3, ThingDef>();

        // Translation strings
        protected static string CantSowCavePlantBecauseOfLightTrans;
        protected static string CantSowCavePlantBecauseUnroofedTrans;
        protected static string MissingSkillTrans;
        protected static string NoGrowingZoneTrans;

        // Configuration properties that can be overridden by derived classes
        protected virtual bool RequiresGrowingSkill => true;
        protected virtual bool AllowHarvestingWildPlants => false;
        protected virtual bool CheckGrowthSeasonNow => true;

        /// <summary>
        /// Initialize or reset translation strings and caches
        /// </summary>
        public override void ResetStaticData()
        {
            ClearCaches();

            // Initialize translation strings
            CantSowCavePlantBecauseOfLightTrans = "CantSowCavePlantBecauseOfLight".Translate();
            CantSowCavePlantBecauseUnroofedTrans = "CantSowCavePlantBecauseUnroofed".Translate();
            MissingSkillTrans = "UnderAllowedSkill".Translate();
            NoGrowingZoneTrans = "NoGrowingZone".Translate();
        }

        /// <summary>
        /// Clear all caches
        /// </summary>
        protected void ClearCaches()
        {
            _growingCellCache.Clear();
            _plantReachabilityCache.Clear();
            _cellReachabilityCache.Clear();
            _plantDefCache.Clear();
            _lastCacheUpdateTick = -999;
        }

        #region Growing Cell Management

        /// <summary>
        /// Check if a cell is valid for the specific growing operation
        /// </summary>
        protected abstract bool ShouldProcessGrowingCell(IntVec3 cell, Map map);

        /// <summary>
        /// Check if a cell should be included in the growing cache based on its contents
        /// </summary>
        protected virtual bool ValidateCellContents(IntVec3 cell, Map map, ThingDef plantDef)
        {
            return true; // Override in concrete classes
        }

        /// <summary>
        /// Validate that the specific grower can work on this cell
        /// </summary>
        public virtual bool ValidateGrowerForCell(IntVec3 cell, Pawn grower)
        {
            if (!cell.InBounds(grower.Map) || cell.IsForbidden(grower))
                return false;

            // Basic checks
            if (!grower.CanReserve(cell) || !grower.CanReach(cell, PathEndMode.Touch, grower.NormalMaxDanger()))
                return false;

            // Get the plant def for this cell
            ThingDef plantDef = GetPlantDefForCell(cell, grower.Map);
            if (plantDef == null)
                return false;

            // Check grower's plant skill if needed
            if (RequiresGrowingSkill && plantDef.plant.sowMinSkill > 0)
            {
                int skillLevel = grower.skills?.GetSkill(SkillDefOf.Plants).Level ?? 0;
                if (skillLevel < plantDef.plant.sowMinSkill)
                {
                    // FIX: Pass the sowMinSkill as a string, not an int
                    JobFailReason.Is(MissingSkillTrans, plantDef.plant.sowMinSkill.ToString());
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Create the specific job for the grower at this cell
        /// </summary>
        public abstract Job CreateGrowerJob(Pawn grower, IntVec3 cell);

        #endregion

        #region Plant Target Management

        /// <summary>
        /// Check if a plant is valid for the specific growing operation
        /// </summary>
        protected virtual bool ShouldProcessPlant(Plant plant, Map map)
        {
            return false; // Override in concrete subclasses that process plants
        }

        /// <summary>
        /// Validate that the specific grower can work on this plant
        /// </summary>
        public virtual bool ValidateGrowerForPlant(Plant plant, Pawn grower)
        {
            return false; // Override in concrete subclasses that process plants
        }

        /// <summary>
        /// Create the specific job for the grower to work on this plant
        /// </summary>
        public virtual Job CreateGrowerJob(Pawn grower, Plant plant)
        {
            return null; // Override in concrete subclasses that process plants
        }

        #endregion

        #region Caching and Targeting

        /// <summary>
        /// Gets the plant def that should be grown at a specific cell
        /// </summary>
        protected ThingDef GetPlantDefForCell(IntVec3 cell, Map map)
        {
            // Check cache first
            if (_plantDefCache.TryGetValue(cell, out ThingDef cachedDef))
                return cachedDef;

            // Calculate the plant def for this cell
            ThingDef plantDef = CalculatePlantDefForCell(cell, map);

            // Cache the result
            if (plantDef != null)
                _plantDefCache[cell] = plantDef;

            return plantDef;
        }

        /// <summary>
        /// Calculate what plant def should be grown at this cell
        /// </summary>
        protected ThingDef CalculatePlantDefForCell(IntVec3 cell, Map map)
        {
            // Check for growing zones
            Zone_Growing growZone = map.zoneManager.ZoneAt(cell) as Zone_Growing;
            if (growZone != null)
            {
                return growZone.GetPlantDefToGrow();
            }

            // Check for planter buildings (hydroponics, etc)
            Building_PlantGrower plantGrower = map.edificeGrid[cell] as Building_PlantGrower;
            if (plantGrower != null)
            {
                return plantGrower.GetPlantDefToGrow();
            }

            return null;
        }

        /// <summary>
        /// Check if a cell is suitable for planting the specified plant
        /// </summary>
        protected bool IsGoodSowingCell(IntVec3 cell, Map map, ThingDef plantDef)
        {
            if (plantDef == null)
                return false;

            // Check growth season if required
            if (CheckGrowthSeasonNow && !PlantUtility.GrowthSeasonNow(cell, map, true))
                return false;

            // Check for existing plants or obstacles
            List<Thing> thingList = cell.GetThingList(map);
            for (int i = 0; i < thingList.Count; i++)
            {
                // Skip if exact plant type already exists
                if (thingList[i].def == plantDef)
                    return false;

                // Check for obstacles
                if (thingList[i].def.BlocksPlanting())
                    return false;
            }

            // Special checks for cave plants
            if (plantDef.plant.cavePlant)
            {
                if (!cell.Roofed(map))
                {
                    JobFailReason.Is(CantSowCavePlantBecauseUnroofedTrans);
                    return false;
                }

                if (map.glowGrid.GroundGlowAt(cell, true) > 0.0f)
                {
                    JobFailReason.Is(CantSowCavePlantBecauseOfLightTrans);
                    return false;
                }
            }

            // Check roof interference
            if (plantDef.plant.interferesWithRoof && cell.Roofed(map))
                return false;

            // Final check - can this plant be planted here right now?
            return plantDef.CanNowPlantAt(cell, map);
        }

        /// <summary>
        /// Checks if a plant can be harvested
        /// </summary>
        protected bool CanHarvestPlant(Plant plant, Pawn pawn)
        {
            if (plant == null || !plant.Spawned || plant.IsForbidden(pawn))
                return false;

            if (!plant.HarvestableNow)
                return false;

            // Check for special plant flags
            if (PlantUtility.TreeMarkedForExtraction(plant))
                return false;

            // Check if it's in a growing zone or hydroponic (if we require that)
            if (!AllowHarvestingWildPlants)
            {
                Zone_Growing growZone = plant.Map.zoneManager.ZoneAt(plant.Position) as Zone_Growing;
                Building_PlantGrower plantGrower = plant.Map.edificeGrid[plant.Position] as Building_PlantGrower;

                if (growZone == null && plantGrower == null)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Updates the cache of growing cells
        /// </summary>
        public void UpdateGrowingCellCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CacheUpdateInterval ||
                !_growingCellCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_growingCellCache.ContainsKey(mapId))
                    _growingCellCache[mapId].Clear();
                else
                    _growingCellCache[mapId] = new List<IntVec3>();

                // Clear reachability cache too
                if (_cellReachabilityCache.ContainsKey(mapId))
                    _cellReachabilityCache[mapId].Clear();
                else
                    _cellReachabilityCache[mapId] = new Dictionary<IntVec3, bool>();

                int cellsProcessed = 0;

                // First add all hydroponic basins
                foreach (Building building in map.listerBuildings.allBuildingsColonist)
                {
                    if (building is Building_PlantGrower plantGrower)
                    {
                        ThingDef plantDef = plantGrower.GetPlantDefToGrow();
                        if (plantDef == null) continue;

                        foreach (IntVec3 cell in plantGrower.OccupiedRect())
                        {
                            if (ShouldProcessGrowingCell(cell, map) && ValidateCellContents(cell, map, plantDef))
                            {
                                _growingCellCache[mapId].Add(cell);
                                cellsProcessed++;

                                if (cellsProcessed >= MAX_CACHE_SIZE)
                                    break;
                            }
                        }

                        if (cellsProcessed >= MAX_CACHE_SIZE)
                            break;
                    }
                }

                // Then add growing zones if we haven't hit the limit
                if (cellsProcessed < MAX_CACHE_SIZE)
                {
                    foreach (Zone zone in map.zoneManager.AllZones)
                    {
                        if (zone is Zone_Growing growZone)
                        {
                            ThingDef plantDef = growZone.GetPlantDefToGrow();
                            if (plantDef == null) continue;

                            foreach (IntVec3 cell in growZone.cells)
                            {
                                if (ShouldProcessGrowingCell(cell, map) && ValidateCellContents(cell, map, plantDef))
                                {
                                    _growingCellCache[mapId].Add(cell);
                                    cellsProcessed++;

                                    if (cellsProcessed >= MAX_CACHE_SIZE)
                                        break;
                                }
                            }

                            if (cellsProcessed >= MAX_CACHE_SIZE)
                                break;
                        }
                    }
                }

                _lastCacheUpdateTick = currentTick;

                // Record whether we found any targets
                SetHasTargets(map, _growingCellCache[mapId].Count > 0);
            }
        }

        /// <summary>
        /// Gets the cached list of growing cells from the given map
        /// </summary>
        public List<IntVec3> GetGrowingCells(Map map)
        {
            if (map == null)
                return new List<IntVec3>();

            int mapId = map.uniqueID;

            if (_growingCellCache.TryGetValue(mapId, out var cachedCells))
                return cachedCells;

            return new List<IntVec3>();
        }

        /// <summary>
        /// Find a valid growing cell from the cache
        /// </summary>
        public IntVec3 FindValidGrowingCell(Pawn grower)
        {
            if (grower?.Map == null) return IntVec3.Invalid;

            // Update the cache first
            UpdateGrowingCellCache(grower.Map);

            int mapId = grower.Map.uniqueID;
            if (!_growingCellCache.ContainsKey(mapId) || _growingCellCache[mapId].Count == 0)
                return IntVec3.Invalid;

            // FIX: Instead of using CreateDistanceBuckets, manually create buckets for IntVec3
            var cellsByDistance = new List<IntVec3>[DISTANCE_THRESHOLDS.Length + 1];
            for (int i = 0; i < cellsByDistance.Length; i++)
            {
                cellsByDistance[i] = new List<IntVec3>();
            }

            // Sort cells into distance buckets
            foreach (IntVec3 cell in _growingCellCache[mapId])
            {
                float distanceSquared = (cell - grower.Position).LengthHorizontalSquared;
                int bucketIndex = DISTANCE_THRESHOLDS.Length; // Default to last bucket

                // Find the appropriate bucket
                for (int i = 0; i < DISTANCE_THRESHOLDS.Length; i++)
                {
                    if (distanceSquared <= DISTANCE_THRESHOLDS[i])
                    {
                        bucketIndex = i;
                        break;
                    }
                }

                cellsByDistance[bucketIndex].Add(cell);
            }

            // Find the best cell for growing
            for (int bucketIndex = 0; bucketIndex < cellsByDistance.Length; bucketIndex++)
            {
                foreach (IntVec3 cell in cellsByDistance[bucketIndex])
                {
                    // Check cache first
                    if (_cellReachabilityCache[mapId].TryGetValue(cell, out bool cached))
                    {
                        if (!cached) continue;
                    }

                    // Validate if not cached
                    if (ValidateGrowerForCell(cell, grower))
                    {
                        _cellReachabilityCache[mapId][cell] = true;
                        return cell;
                    }
                    else
                    {
                        _cellReachabilityCache[mapId][cell] = false;
                    }
                }
            }

            return IntVec3.Invalid;
        }

        /// <summary>
        /// Create a job for the grower from cached cells
        /// </summary>
        public Job CreateJobFor(Pawn grower)
        {
            try
            {
                if (grower == null) return null;

                // Find a valid growing cell
                IntVec3 targetCell = FindValidGrowingCell(grower);
                if (!targetCell.IsValid)
                    return null;

                // Create the job
                return CreateGrowerJob(grower, targetCell);
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating grower job: {ex}");
                return null;
            }
        }

        #endregion
    }
}