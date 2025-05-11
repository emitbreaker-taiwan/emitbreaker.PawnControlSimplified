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
    public class JobGiver_Growing_GrowerSow_PawnControl : JobGiver_Common_Growing_PawnControl
    {
        #region Configuration

        // Static translation strings
        private static string CantSowCavePlantBecauseOfLightTrans;
        private static string CantSowCavePlantBecauseUnroofedTrans;

        #endregion

        #region Overrides

        /// <summary>
        /// Description for logging
        /// </summary>
        protected override string JobDescription => "sowing assignment";

        /// <summary>
        /// Sowing is important for future food production
        /// </summary>
        public override float GetPriority(Pawn pawn)
        {
            return 5.7f;
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

            // Filter targets to find valid sowing cells
            List<IntVec3> validCells = targets
                .OfType<Zone_Growing>()
                .SelectMany(zone => zone.Cells)
                .Where(cell => CanSowAtCell(cell, pawn, pawn.Map))
                .ToList();

            if (validCells.Count == 0)
                return null;

            // Use the existing sowing logic to create a job
            return ProcessCellsForSowing(pawn, validCells);
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
        /// Custom processor for sowing jobs
        /// </summary>
        private Job ProcessCellsForSowing(Pawn pawn, List<IntVec3> cells)
        {
            if (pawn?.Map == null || cells == null || cells.Count == 0)
                return null;

            // Find valid cells for sowing
            List<IntVec3> validCells = FindTargetCells(
                pawn,
                cells,
                (cell, actor, map) => CanSowAtCell(cell, actor, map)
            );

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
            var job = JobMaker.MakeJob(JobDefOf.Sow, targetCell);
            job.plantDefToSow = plantDefToSow;
            Utility_DebugManager.LogNormal($"{pawn.LabelShort} created sow job for {plantDefToSow.label} at {targetCell}");
            return job;
        }

        /// <summary>
        /// Check if a cell can be sown
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