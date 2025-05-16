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
    /// JobGiver that assigns sowing tasks to pawns with the Growing work type.
    /// Optimized for large colonies with many growing zones using distance-based bucketing.
    /// </summary>
    public class JobGiver_Growing_GrowerSow_PawnControl : JobGiver_Growing_PawnControl
    {
        #region Configuration

        // Static translation strings
        private static string CantSowCavePlantBecauseOfLightTrans;
        private static string CantSowCavePlantBecauseUnroofedTrans;

        // Cache key suffix for sowable cells - follow same pattern as parent class
        private const string SOW_CACHE_SUFFIX = "_Sow";

        // Extra tracking to avoid recalculating every cell every time
        private Dictionary<int, Dictionary<IntVec3, int>> _cellValidationCache =
            new Dictionary<int, Dictionary<IntVec3, int>>();

        // Track the last tick we purged the validation cache
        private int _lastCachePurgeTick = -1;

        #endregion

        #region Overrides

        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.Sow;

        /// <summary>
        /// Description for logging
        /// </summary>
        protected override string JobDescription => "sowing assignment";

        /// <summary>
        /// Sowing is important for future food production
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            return 5.7f;
        }

        /// <summary>
        /// Constructor to initialize cache
        /// </summary>
        public JobGiver_Growing_GrowerSow_PawnControl() : base()
        {
            // Initialize cache
        }

        /// <summary>
        /// Determines whether to skip this job giver for the pawn
        /// </summary>
        public override bool ShouldSkip(Pawn pawn)
        {
            // First check base conditions efficiently 
            if (base.ShouldSkip(pawn))
                return true;

            // Skip pawns from wrong factions
            if (RequiresPlayerFaction && Utility_Common.PawnIsNotPlayerFaction(pawn))
                return true;

            // IMPORTANT: Check if the pawn is allowed to do Growing work
            if (!IsAllowedGrowingWork(pawn))
            {
                if (Prefs.DevMode)
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} is not allowed to do Growing work (work settings)");
                return true;
            }

            // Occasionally purge cell validation cache to prevent memory growth over time
            PurgeCellValidationCacheIfNeeded();

            return false;
        }

        /// <summary>
        /// Only accept zones/growers that have sowing enabled
        /// </summary>
        protected override bool ExtraRequirements(IPlantToGrowSettable settable, Pawn pawn)
        {
            if (settable is Zone_Growing zone)
            {
                // IMPORTANT: Check if sowing is allowed in this zone
                bool zoneSowingAllowed = zone.allowSow && zone.GetPlantDefToGrow() != null;

                if (!zoneSowingAllowed && Prefs.DevMode)
                {
                    Utility_DebugManager.LogNormal($"Zone {zone.label} does not allow sowing or has no plant def");
                }

                return zoneSowingAllowed;
            }

            if (settable is Building_PlantGrower grower)
            {
                // IMPORTANT: Check if the grower can accept sowing now
                bool growerAllowsSowing = grower.GetPlantDefToGrow() != null && grower.CanAcceptSowNow();

                if (!growerAllowsSowing && Prefs.DevMode)
                {
                    Utility_DebugManager.LogNormal($"Grower {grower.Label} does not accept sowing now");
                }

                return growerAllowsSowing;
            }

            return false;
        }

        /// <summary>
        /// Override TryGiveJob to implement a sequential approach to creating jobs
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (pawn?.Map == null || ShouldSkip(pawn))
            {
                return null;
            }

            // Use the SequentialTryGiveJob method with our custom job creation logic
            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Growing_GrowerSow_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) => {
                    // IMPORTANT: Double-check work settings at job creation time
                    if (!IsAllowedGrowingWork(p))
                    {
                        if (Prefs.DevMode)
                            Utility_DebugManager.LogNormal($"{p.LabelShort} is not allowed to do Growing work (work disabled)");
                        return null;
                    }

                    // First get growing cells from the base method
                    List<IntVec3> growingCells = GetGrowingWorkCells(p);
                    if (growingCells.Count == 0)
                    {
                        if (Prefs.DevMode)
                            Utility_DebugManager.LogNormal($"{p.LabelShort}: No growing cells found");
                        return null;
                    }

                    if (Prefs.DevMode)
                        Utility_DebugManager.LogNormal($"{p.LabelShort}: Found {growingCells.Count} growing cells");

                    // Filter for sowable cells
                    List<IntVec3> sowableCells = FilterSowableCells(p, growingCells);
                    if (sowableCells.Count == 0)
                    {
                        if (Prefs.DevMode)
                            Utility_DebugManager.LogNormal($"{p.LabelShort}: No sowable cells found");
                        return null;
                    }

                    if (Prefs.DevMode)
                        Utility_DebugManager.LogNormal($"{p.LabelShort}: Found {sowableCells.Count} sowable cells");

                    // Create sowing job
                    Job job = CreateSowingJob(p, sowableCells);
                    if (job == null)
                    {
                        if (Prefs.DevMode)
                            Utility_DebugManager.LogNormal($"{p.LabelShort}: Failed to create sowing job");
                        return null;
                    }

                    // IMPORTANT: Final verification of zone/cell to make sure sowing is allowed
                    if (!VerifySowingAllowed(job.targetA.Cell, p))
                    {
                        if (Prefs.DevMode)
                            Utility_DebugManager.LogNormal($"{p.LabelShort}: Sowing not allowed at {job.targetA.Cell}");
                        return null;
                    }

                    if (Prefs.DevMode)
                        Utility_DebugManager.LogNormal($"{p.LabelShort}: Created sowing job at {job.targetA.Cell}");

                    return job;
                },
                skipEmergencyCheck: false,
                jobGiverType: GetType()
            );
        }

        /// <summary>
        /// Final verification that sowing is allowed at the target cell
        /// </summary>
        private bool VerifySowingAllowed(IntVec3 cell, Pawn pawn)
        {
            Map map = pawn.Map;
            if (map == null)
                return false;

            // Check zone settings
            Zone_Growing zone = cell.GetZone(map) as Zone_Growing;
            if (zone != null)
            {
                // Verify zone allows sowing
                if (!zone.allowSow)
                {
                    if (Prefs.DevMode)
                        Utility_DebugManager.LogNormal($"Zone {zone.label} does not allow sowing");
                    return false;
                }

                // Verify zone has a plant def
                ThingDef plantDefToGrow = zone.GetPlantDefToGrow();
                if (plantDefToGrow == null)
                {
                    if (Prefs.DevMode)
                        Utility_DebugManager.LogNormal($"Zone {zone.label} has no plant def to grow");
                    return false;
                }
            }
            else
            {
                // Check if there's a plant grower at this location
                Building_PlantGrower grower = map.thingGrid.ThingAt<Building_PlantGrower>(cell);
                if (grower != null)
                {
                    // Verify the grower allows sowing
                    if (!grower.CanAcceptSowNow())
                    {
                        if (Prefs.DevMode)
                            Utility_DebugManager.LogNormal($"Plant grower {grower.Label} cannot accept sowing now");
                        return false;
                    }

                    // Verify the grower has a plant def
                    ThingDef plantDefToGrow = grower.GetPlantDefToGrow();
                    if (plantDefToGrow == null)
                    {
                        if (Prefs.DevMode)
                            Utility_DebugManager.LogNormal($"Plant grower {grower.Label} has no plant def to grow");
                        return false;
                    }
                }
                else
                {
                    // No zone or grower found
                    if (Prefs.DevMode)
                        Utility_DebugManager.LogNormal($"No growing zone or plant grower found at {cell}");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Process the cached targets efficiently
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // IMPORTANT: Double-check work settings at job creation time
            if (!IsAllowedGrowingWork(pawn))
            {
                if (Prefs.DevMode)
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} is not allowed to do Growing work (work disabled)");
                return null;
            }

            if (pawn?.Map == null || targets == null || targets.Count == 0)
                return null;

            // Get valid growing cells from parent method
            List<IntVec3> growingCells = GetGrowingWorkCells(pawn);
            if (growingCells.Count == 0)
                return null;

            // Find sowable cells efficiently
            List<IntVec3> sowableCells = FilterSowableCells(pawn, growingCells);
            if (sowableCells.Count == 0)
                return null;

            // Create sowing job by finding the best cell
            Job job = CreateSowingJob(pawn, sowableCells);

            // IMPORTANT: Final verification of zone/cell to make sure sowing is allowed
            if (job != null && !VerifySowingAllowed(job.targetA.Cell, pawn))
            {
                if (Prefs.DevMode)
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort}: Sowing not allowed at {job.targetA.Cell}");
                return null;
            }

            return job;
        }

        #endregion

        #region Sowing-specific implementation

        /// <summary>
        /// Filters cells that can be sown from all growing cells
        /// Uses a two-level cache system for better performance
        /// </summary>
        private List<IntVec3> FilterSowableCells(Pawn pawn, List<IntVec3> growingCells)
        {
            if (pawn?.Map == null || growingCells == null || growingCells.Count == 0)
                return new List<IntVec3>();

            int mapId = pawn.Map.uniqueID;
            int currentTick = Find.TickManager.TicksGame;
            Map map = pawn.Map;

            // Create a result list with a reasonable capacity
            List<IntVec3> result = new List<IntVec3>(Math.Min(growingCells.Count, 200));

            // Get or create the cell validation cache for this map
            if (!_cellValidationCache.TryGetValue(mapId, out var validationCache))
            {
                validationCache = new Dictionary<IntVec3, int>(1000);
                _cellValidationCache[mapId] = validationCache;
            }

            // Process cells in batches for better performance
            int cellsChecked = 0;
            int cellsValid = 0;
            foreach (IntVec3 cell in growingCells)
            {
                // Limit processing to avoid excessive CPU usage
                if (cellsChecked++ > 1000)
                    break;

                // IMPORTANT: Check zone settings for this cell
                Zone_Growing zone = cell.GetZone(map) as Zone_Growing;
                if (zone != null && (!zone.allowSow || zone.GetPlantDefToGrow() == null))
                {
                    // Skip cells in zones that don't allow sowing or have no plant def
                    continue;
                }

                // Check if cell was recently validated and is still valid
                if (validationCache.TryGetValue(cell, out int lastValidatedTick) &&
                    currentTick - lastValidatedTick < 600) // Valid for 10 seconds
                {
                    // Cell was recently validated, add it to results
                    result.Add(cell);
                    cellsValid++;
                    continue;
                }

                // Cell needs validation, check if it can be sown
                if (CanSowAtCellGeneric(cell, map))
                {
                    // Update validation cache and add to results
                    validationCache[cell] = currentTick;
                    result.Add(cell);
                    cellsValid++;

                    // Limit number of valid cells to prevent performance issues
                    if (result.Count >= 200)
                        break;
                }
            }

            if (Prefs.DevMode && cellsChecked > 0)
            {
                Utility_DebugManager.LogNormal($"Checked {cellsChecked} cells, found {cellsValid} valid sowable cells");
            }

            return result;
        }

        /// <summary>
        /// Check if a cell can be sown (generic checks without pawn-specific validation)
        /// </summary>
        private bool CanSowAtCellGeneric(IntVec3 cell, Map map)
        {
            // IMPORTANT: First check zone settings
            Zone_Growing zone = cell.GetZone(map) as Zone_Growing;
            if (zone != null)
            {
                if (!zone.allowSow)
                    return false;

                if (zone.GetPlantDefToGrow() == null)
                    return false;
            }
            else
            {
                // Check for plant grower
                Building_PlantGrower grower = map.thingGrid.ThingAt<Building_PlantGrower>(cell);
                if (grower != null)
                {
                    if (!grower.CanAcceptSowNow())
                        return false;

                    if (grower.GetPlantDefToGrow() == null)
                        return false;
                }
                else
                {
                    // No zone or grower found
                    return false;
                }
            }

            // Check if the cell is in the growing season
            if (!PlantUtility.GrowthSeasonNow(cell, map, true))
                return false;

            // Get plant def to grow - CRITICAL CHECK
            ThingDef plantDef = CalculateWantedPlantDef(cell, map);
            if (plantDef == null)
            {
                if (Prefs.DevMode)
                    Utility_DebugManager.LogNormal($"Can't sow at {cell}: No plant def to grow");
                return false;
            }

            // Check for existing plants
            Plant existingPlant = map.thingGrid.ThingAt<Plant>(cell);
            if (existingPlant != null)
            {
                if (existingPlant.def == plantDef && !existingPlant.IsDessicated())
                    return false;

                // If there's already a different plant, we might be able to sow after it's cleared
                if (!existingPlant.IsDessicated() && map.designationManager.DesignationOn(existingPlant, DesignationDefOf.CutPlant) == null)
                    return false;
            }

            List<Thing> thingList = cell.GetThingList(map);

            // Check for things blocking planting
            for (int i = 0; i < thingList.Count; i++)
            {
                Thing thing = thingList[i];

                // Check if the cell already has the desired plant
                if (thing.def == plantDef)
                    return false;

                // Check for blueprints, frames, or other blockers
                if (thing.def.BlocksPlanting())
                    return false;

                if (thing is Blueprint || thing is Frame)
                {
                    // Additional check for non-fertile blueprints/frames
                    Thing edifice = cell.GetEdifice(map);
                    if (edifice == null || edifice.def.fertility < 0.0f)
                        return false;
                }
            }

            // Check if the cell is fertile for the plant
            if (!plantDef.CanEverPlantAt(cell, map))
                return false;

            // Check cave plant requirements
            if (plantDef.plant.cavePlant)
            {
                if (!cell.Roofed(map))
                    return false;

                if (map.glowGrid.GroundGlowAt(cell, true) > 0.0f)
                    return false;
            }

            // Check if plant interferes with roof
            if (plantDef.plant.interferesWithRoof && cell.Roofed(map))
                return false;

            // Final check for immediate plantability
            return plantDef.CanNowPlantAt(cell, map);
        }

        /// <summary>
        /// Calculates what plant should be grown in a specific cell
        /// </summary>
        /// <param name="c">The cell to check</param>
        /// <param name="map">The map</param>
        /// <returns>The PlantDef to grow, or null if none</returns>
        protected override ThingDef CalculateWantedPlantDef(IntVec3 c, Map map)
        {
            Zone_Growing zone = c.GetZone(map) as Zone_Growing;
            if (zone != null)
            {
                return zone.GetPlantDefToGrow();
            }

            Building_PlantGrower plantGrower = map.thingGrid.ThingAt<Building_PlantGrower>(c);
            if (plantGrower != null)
            {
                return plantGrower.GetPlantDefToGrow();
            }

            return null;
        }

        /// <summary>
        /// Creates a sowing job for the pawn at the best available cell
        /// </summary>
        private Job CreateSowingJob(Pawn pawn, List<IntVec3> sowableCells)
        {
            if (pawn?.Map == null || sowableCells == null || sowableCells.Count == 0)
                return null;

            // Filter cells for this specific pawn
            List<IntVec3> validCells = new List<IntVec3>(sowableCells.Count);
            Map map = pawn.Map;

            // Pre-filter a reasonable number of cells
            int cellsChecked = 0;
            int cellsValid = 0;

            foreach (IntVec3 cell in sowableCells)
            {
                if (cellsChecked++ > 200) // Limit checking to first 200 cells
                    break;

                if (CanSowAtCellForPawn(cell, pawn))
                {
                    validCells.Add(cell);
                    cellsValid++;
                }

                // Early out if we found enough valid cells
                if (validCells.Count >= 50)
                    break;
            }

            if (validCells.Count == 0)
            {
                if (Prefs.DevMode)
                    Utility_DebugManager.LogNormal($"No valid cells for {pawn.LabelShort} after pawn-specific filtering");
                return null;
            }

            // Find best cell using distance bucketing
            IntVec3 targetCell = FindBestCell(pawn, validCells);
            if (!targetCell.IsValid)
            {
                if (Prefs.DevMode)
                    Utility_DebugManager.LogNormal($"Failed to find best cell for {pawn.LabelShort}");
                return null;
            }

            // Determine what to plant
            ThingDef plantDefToSow = CalculateWantedPlantDef(targetCell, map);
            if (plantDefToSow == null)
            {
                if (Prefs.DevMode)
                    Utility_DebugManager.LogNormal($"No plant def to sow at {targetCell} for {pawn.LabelShort}");
                return null;
            }

            // Skill check
            if (plantDefToSow.plant.sowMinSkill > 0)
            {
                int skillLevel = pawn.skills?.GetSkill(SkillDefOf.Plants).Level
                               ?? pawn.RaceProps.mechFixedSkillLevel;

                if (skillLevel < plantDefToSow.plant.sowMinSkill)
                {
                    if (Prefs.DevMode)
                        Utility_DebugManager.LogNormal($"{pawn.LabelShort} lacks skill to plant {plantDefToSow} (needs {plantDefToSow.plant.sowMinSkill}, has {skillLevel})");
                    return null;
                }
            }

            // Create and return the sow job
            var job = JobMaker.MakeJob(WorkJobDef, targetCell);
            job.plantDefToSow = plantDefToSow;

            // Double-check that the job can be performed
            if (!job.TryMakePreToilReservations(pawn, false))
            {
                if (Prefs.DevMode)
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} couldn't reserve cell {targetCell} for sowing");
                return null;
            }

            return job;
        }

        /// <summary>
        /// Check if a cell can be sown by a specific pawn
        /// </summary>
        private bool CanSowAtCellForPawn(IntVec3 cell, Pawn pawn)
        {
            if (cell.IsForbidden(pawn))
                return false;

            if (!pawn.CanReserve(cell, 1, -1))
                return false;

            // Check if pawn can reach the cell
            if (!pawn.CanReach(cell, PathEndMode.Touch, Danger.Deadly))
                return false;

            // Get plant def to grow
            ThingDef plantDef = CalculateWantedPlantDef(cell, pawn.Map);
            if (plantDef == null)
                return false;

            // Check cave plant requirements
            if (plantDef.plant.cavePlant)
            {
                if (!cell.Roofed(pawn.Map))
                    return false;

                if (pawn.Map.glowGrid.GroundGlowAt(cell, true) > 0.0f)
                    return false;
            }

            // Check if the cell needs to be cleared first
            Plant existingPlant = pawn.Map.thingGrid.ThingAt<Plant>(cell);
            if (existingPlant != null && existingPlant.def != plantDef)
            {
                // Plant must have cut designation to be valid for sowing
                if (pawn.Map.designationManager.DesignationOn(existingPlant, DesignationDefOf.CutPlant) == null)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Periodically purge the cell validation cache to prevent memory build-up
        /// </summary>
        private void PurgeCellValidationCacheIfNeeded()
        {
            int currentTick = Find.TickManager.TicksGame;

            // Purge every 2000 ticks (33 seconds)
            if (currentTick - _lastCachePurgeTick < 2000)
                return;

            _lastCachePurgeTick = currentTick;

            // Purge old entries from the cache
            foreach (var mapCache in _cellValidationCache.Values)
            {
                // Remove entries older than 600 ticks (10 seconds)
                List<IntVec3> keysToRemove = new List<IntVec3>();
                foreach (var pair in mapCache)
                {
                    if (currentTick - pair.Value > 600)
                        keysToRemove.Add(pair.Key);
                }

                // Remove old keys
                foreach (var key in keysToRemove)
                    mapCache.Remove(key);
            }
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

            // Clear sowing-specific caches
            _cellValidationCache.Clear();
            _lastCachePurgeTick = -1;

            // Also clear any map-specific sowable caches using the centralized system
            foreach (int mapId in Find.Maps.Select(map => map.uniqueID))
            {
                string sowCacheKey = this.GetType().Name + SOW_CACHE_SUFFIX;
                var sowCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<IntVec3>>(mapId);

                if (sowCache.ContainsKey(sowCacheKey))
                {
                    sowCache.Remove(sowCacheKey);
                }

                Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, sowCacheKey, -1);
            }

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Reset sowing caches for {this.GetType().Name}");
            }
        }

        #endregion

        #region Utility

        /// <summary>
        /// Initialize the static translation strings
        /// </summary>
        public static void ResetStaticData()
        {
            CantSowCavePlantBecauseOfLightTrans = "CantSowCavePlantBecauseOfLight".Translate();
            CantSowCavePlantBecauseUnroofedTrans = "CantSowCavePlantBecauseUnroofed".Translate();
        }

        public override string ToString()
        {
            return "JobGiver_Growing_GrowerSow_PawnControl";
        }

        #endregion
    }
}