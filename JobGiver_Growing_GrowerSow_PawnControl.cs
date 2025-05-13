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

        // Cache key for sowable cells
        private const string SOW_CACHE_KEY_SUFFIX = "_SowableCells";

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

        public override bool ShouldSkip(Pawn pawn)
        {
            if (base.ShouldSkip(pawn))
                return true;

            if (Utility_Common.PawnIsNotPlayerFaction(pawn))
                return true;

            return false;
        }

        /// <summary>
        /// Override TryGiveJob to use the common helper method with a custom processor for sowing
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            // Use the CreateGrowingJob helper with a custom processor for sowing
            return CreateGrowingJob<JobGiver_Growing_GrowerSow_PawnControl>(pawn, ProcessCellsForSowing);
        }

        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // Implement logic to process cached targets for sowing jobs
            if (targets == null || targets.Count == 0)
                return null;

            // Use the sowable cells cache instead of filtering targets directly
            List<IntVec3> sowableCells = GetSowableCells(pawn);

            if (sowableCells.Count == 0)
                return null;

            // Use the existing sowing logic to create a job
            return ProcessCellsForSowing(pawn, sowableCells);
        }

        /// <summary>
        /// Only accept zones/growers that have sowing enabled
        /// </summary>
        protected override bool ExtraRequirements(IPlantToGrowSettable settable, Pawn pawn)
        {
            if (settable is Zone_Growing zone)
                return zone.allowSow && zone.GetPlantDefToGrow() != null;

            if (settable is Building_PlantGrower grower)
                return grower.GetPlantDefToGrow() != null && grower.CanAcceptSowNow();

            return false;
        }

        #endregion

        #region Sowing-specific implementation

        /// <summary>
        /// Gets or creates a cache of cells that can be sown
        /// </summary>
        private List<IntVec3> GetSowableCells(Pawn pawn)
        {
            if (pawn?.Map == null)
                return new List<IntVec3>();

            int mapId = pawn.Map.uniqueID;
            string cacheKey = this.GetType().Name + SOW_CACHE_KEY_SUFFIX;

            // Try to get sowable cells from the map cache manager
            var cellCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<IntVec3>>(mapId);

            // Check if we need to update the cache
            int currentTick = Find.TickManager.TicksGame;
            int lastUpdateTick = Utility_MapCacheManager.GetLastCacheUpdateTick(mapId, cacheKey);

            if (currentTick - lastUpdateTick > CacheUpdateInterval ||
                !cellCache.TryGetValue(cacheKey, out List<IntVec3> sowableCells) ||
                sowableCells == null)
            {
                // Cache is invalid or expired, rebuild it
                List<IntVec3> growingCells = GetGrowingWorkCells(pawn);
                Map map = pawn.Map;

                // Pre-filter cells that can be sown in general (not pawn-specific yet)
                sowableCells = FindTargetCells(
                    pawn,
                    growingCells,
                    (cell, p, m) => CanSowAtCellGeneric(cell, m)
                );

                // Store in the central cache
                cellCache[cacheKey] = sowableCells;
                Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, currentTick);

                if (Prefs.DevMode)
                {
                    Utility_DebugManager.LogNormal($"Updated sowable cells cache for {this.GetType().Name}, found {sowableCells.Count} cells");
                }
            }

            return sowableCells;
        }

        /// <summary>
        /// Check if a cell can be sown (generic checks without pawn-specific validation)
        /// </summary>
        private bool CanSowAtCellGeneric(IntVec3 cell, Map map)
        {
            if (!PlantUtility.GrowthSeasonNow(cell, map, true))
                return false;

            // Get plant def to grow
            ThingDef plantDef = CalculateWantedPlantDef(cell, map);
            if (plantDef == null)
                return false;

            List<Thing> thingList = cell.GetThingList(map);

            // Check if the cell already has the desired plant
            for (int i = 0; i < thingList.Count; i++)
            {
                Thing thing = thingList[i];
                if (thing.def == plantDef)
                    return false;
            }

            // Check for blueprints or frames
            bool hasStructure = false;
            for (int i = 0; i < thingList.Count; i++)
            {
                Thing thing = thingList[i];
                if ((thing is Blueprint || thing is Frame))
                {
                    hasStructure = true;
                    break;
                }
            }

            // If there's a structure, ensure fertility
            if (hasStructure)
            {
                Thing edifice = cell.GetEdifice(map);
                if (edifice == null || edifice.def.fertility < 0.0f)
                    return false;
            }

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

            // Check for things blocking planting
            for (int i = 0; i < thingList.Count; i++)
            {
                Thing thing = thingList[i];
                if (thing.def.BlocksPlanting())
                    return false;
            }

            // Final checks for sowing
            return plantDef.CanNowPlantAt(cell, map);
        }

        /// <summary>
        /// Custom processor for sowing jobs
        /// </summary>
        private Job ProcessCellsForSowing(Pawn pawn, List<IntVec3> cells)
        {
            if (pawn?.Map == null || cells == null || cells.Count == 0)
                return null;

            // Filter out cells that don't pass pawn-specific checks
            List<IntVec3> validCells = cells.Where(cell => CanSowAtCellForPawn(cell, pawn)).ToList();

            if (validCells.Count == 0)
                return null;

            // Find best cell using distance bucketing
            IntVec3 targetCell = FindBestCell(pawn, validCells);
            if (!targetCell.IsValid)
                return null;

            // Determine what to plant
            ThingDef plantDefToSow = CalculateWantedPlantDef(targetCell, pawn.Map);
            if (plantDefToSow == null)
                return null;

            // Skill check
            if (plantDefToSow.plant.sowMinSkill > 0)
            {
                int skillLevel = pawn.skills?.GetSkill(SkillDefOf.Plants).Level
                                 ?? pawn.RaceProps.mechFixedSkillLevel;
                if (skillLevel < plantDefToSow.plant.sowMinSkill)
                {
                    JobFailReason.Is("UnderAllowedSkill".Translate(plantDefToSow.plant.sowMinSkill));
                    return null;
                }
            }

            // Create and return the sow job
            var job = JobMaker.MakeJob(WorkJobDef, targetCell);
            job.plantDefToSow = plantDefToSow;

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created sow job for {plantDefToSow.label} at {targetCell}");
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

            // Get plant def to grow
            ThingDef plantDef = CalculateWantedPlantDef(cell, pawn.Map);
            if (plantDef == null)
                return false;

            // Check cave plant requirements - add specific error messages
            if (plantDef.plant.cavePlant)
            {
                if (!cell.Roofed(pawn.Map))
                {
                    JobFailReason.Is(CantSowCavePlantBecauseUnroofedTrans);
                    return false;
                }

                if (pawn.Map.glowGrid.GroundGlowAt(cell, true) > 0.0f)
                {
                    JobFailReason.Is(CantSowCavePlantBecauseOfLightTrans);
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Full check if a cell can be sown (for backwards compatibility)
        /// </summary>
        private bool CanSowAtCell(IntVec3 cell, Pawn pawn, Map map)
        {
            if (cell.IsForbidden(pawn))
                return false;

            if (!pawn.CanReserve(cell, 1, -1))
                return false;

            if (!PlantUtility.GrowthSeasonNow(cell, map, true))
                return false;

            // Get plant def to grow
            ThingDef plantDef = CalculateWantedPlantDef(cell, map);
            if (plantDef == null)
                return false;

            List<Thing> thingList = cell.GetThingList(map);

            // Check if the cell already has the desired plant
            for (int i = 0; i < thingList.Count; i++)
            {
                Thing thing = thingList[i];
                if (thing.def == plantDef)
                    return false;
            }

            // Check for blueprints or frames
            bool hasStructure = false;
            for (int i = 0; i < thingList.Count; i++)
            {
                Thing thing = thingList[i];
                if ((thing is Blueprint || thing is Frame) && thing.Faction == pawn.Faction)
                {
                    hasStructure = true;
                    break;
                }
            }

            // If there's a structure, ensure fertility
            if (hasStructure)
            {
                Thing edifice = cell.GetEdifice(map);
                if (edifice == null || edifice.def.fertility < 0.0f)
                    return false;
            }

            // Check cave plant requirements
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

            // Check if plant interferes with roof
            if (plantDef.plant.interferesWithRoof && cell.Roofed(map))
                return false;

            // Check for things blocking planting
            for (int i = 0; i < thingList.Count; i++)
            {
                Thing thing = thingList[i];
                if (thing.def.BlocksPlanting())
                    return false;
            }

            // Final checks for sowing
            return plantDef.CanNowPlantAt(cell, map);
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

            // Now clear sowing-specific caches
            foreach (int mapId in Find.Maps.Select(map => map.uniqueID))
            {
                string cacheKey = this.GetType().Name + SOW_CACHE_KEY_SUFFIX;
                var cellCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<IntVec3>>(mapId);

                if (cellCache.ContainsKey(cacheKey))
                {
                    cellCache.Remove(cacheKey);
                }

                // Clear the update tick record too
                Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, -1);
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