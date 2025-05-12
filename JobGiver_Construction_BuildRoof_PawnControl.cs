using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that allows non-humanlike pawns to build roofs in designated areas.
    /// Uses the Construction work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Construction_BuildRoof_PawnControl : JobGiver_Construction_PawnControl
    {
        #region Configuration

        protected override int CacheUpdateInterval => 250; // Update every ~4 seconds
        protected override string DebugName => "roof building assignment";

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        protected override bool RequiresMapZoneorArea => true;

        // Only player faction can build roofs
        protected override bool RequiresPlayerFaction => true;

        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.BuildRoof;

        // Distance thresholds for bucketing - slightly larger for roof building
        protected override float[] DistanceThresholds => new float[] { 225f, 625f, 2500f }; // 15, 25, 50 tiles

        protected override bool ShouldExecuteNow(int mapId)
        {
            // Quick check if there are roof building areas on the map
            var map = Find.Maps.FirstOrDefault(m => m.uniqueID == mapId);
            return map != null && map.areaManager.BuildRoof != null && map.areaManager.BuildRoof.TrueCount > 0;
        }

        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // This job works with IntVec3 cells rather than Things
            return Enumerable.Empty<Thing>();
        }

        /// <summary>
        /// Whether this job uses thing targets or cell-based targets
        /// </summary>
        protected override bool RequiresThingTargets()
        {
            return false; // Uses cell-based targets instead
        }

        /// <summary>
        /// Checks if the map meets requirements for roof building
        /// </summary>
        protected override bool AreMapRequirementsMet(Pawn pawn)
        {
            // Need valid map with build roof area
            return pawn?.Map != null &&
                   pawn.Map.areaManager.BuildRoof != null &&
                   pawn.Map.areaManager.BuildRoof.TrueCount > 0;
        }

        /// <summary>
        /// Creates a roof building job for the pawn
        /// </summary>
        protected override Job CreateConstructionJob(Pawn pawn, bool forced)
        {
            // Get cells that need roofs built
            List<IntVec3> cells = GetRoofBuildCells(pawn.Map);
            if (cells.Count == 0)
                return null;

            // Use distance bucketing for cell selection
            List<IntVec3>[] buckets = CreateDistanceBuckets(pawn, cells);

            // Find best cell to build roof
            IntVec3 bestCell = FindBestCell(buckets, pawn, (cell, p) => ValidateRoofBuildCell(cell, p, forced));

            // Create job for the best cell if found
            if (bestCell.IsValid)
            {
                Job job = JobMaker.MakeJob(WorkJobDef);
                job.targetA = bestCell;
                job.targetB = bestCell;
                return job;
            }

            return null;
        }

        #endregion

        #region Roof-specific helpers

        /// <summary>
        /// Gets cells marked for roof building that need roofs
        /// </summary>
        private List<IntVec3> GetRoofBuildCells(Map map)
        {
            if (map == null || map.areaManager.BuildRoof == null)
                return new List<IntVec3>();

            var cells = new List<IntVec3>();

            // Find all cells marked for roof building
            foreach (IntVec3 cell in map.areaManager.BuildRoof.ActiveCells)
            {
                // Skip if already roofed
                if (cell.Roofed(map))
                    continue;

                // Skip if this would create an unsupported roof
                if (!RoofCollapseUtility.WithinRangeOfRoofHolder(cell, map))
                    continue;

                // Skip if not connected to existing roof/support
                if (!RoofCollapseUtility.ConnectedToRoofHolder(cell, map, assumeRoofAtRoot: true))
                    continue;

                cells.Add(cell);
            }

            // Limit cell count for performance
            return LimitListSize(cells, 500);
        }

        /// <summary>
        /// Validates if a cell is appropriate for roof building
        /// </summary>
        private bool ValidateRoofBuildCell(IntVec3 cell, Pawn pawn, bool forced)
        {
            // First perform base validation from parent class
            if (!ValidateConstructionCell(cell, pawn, forced))
                return false;

            // Special validation for roof building
            if (!pawn.Map.areaManager.BuildRoof[cell] || cell.Roofed(pawn.Map))
                return false;

            // Check for blocking things first
            Thing blocker = RoofUtility.FirstBlockingThing(cell, pawn.Map);
            if (blocker != null)
            {
                // If there's a blocker that can be handled, it will be done automatically
                // when we return the cell as valid.
                Job blockingJob = RoofUtility.HandleBlockingThingJob(blocker, pawn, forced);
                if (blockingJob == null)
                    return false;  // Can't handle the blocker
            }

            // Check for roof stability
            if (!RoofCollapseUtility.WithinRangeOfRoofHolder(cell, pawn.Map) ||
                !RoofCollapseUtility.ConnectedToRoofHolder(cell, pawn.Map, assumeRoofAtRoot: true))
                return false;

            // Check if another pawn is already working on this cell
            if (IsCellReservedByAnother(pawn, cell, WorkJobDef))
                return false;

            return true;
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return "JobGiver_Construction_BuildRoof_PawnControl";
        }

        #endregion
    }
}