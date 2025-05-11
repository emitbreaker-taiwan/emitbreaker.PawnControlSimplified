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
    public class JobGiver_Construction_BuildRoof_PawnControl : JobGiver_Scan_PawnControl
    {
        #region Overrides

        protected override string WorkTag => "Construction";
        protected override int CacheUpdateInterval => 250; // Update every ~4 seconds
        protected override string DebugName => "roof building assignment";

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 2500f }; // 15, 25, 50 tiles

        protected override bool ShouldExecuteNow(int mapId)
        {
            // Quick check if there are roof building areas on the map
            var map = Find.Maps.FirstOrDefault(m => m.uniqueID == mapId);
            return map != null && map.areaManager.BuildRoof != null && map.areaManager.BuildRoof.TrueCount > 0;
        }

        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // This job works with IntVec3 cells rather than Things, so we need to handle it differently
            // We'll return an empty list and handle the actual target collection in TryGiveJob
            return Enumerable.Empty<Thing>();
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Use the StandardTryGiveJob pattern directly
            return Utility_JobGiverManager.StandardTryGiveJob<Thing>(
                pawn,
                WorkTag,  // Use the WorkTag property from the base class
                (p, forced) => {
                    // Get targets from the cache
                    List<Thing> targets = GetTargets(p.Map).ToList();
                    if (targets.Count == 0)
                    {
                        // Direct approach for roof building since targets are cells, not things
                        List<IntVec3> cells = GetRoofBuildCells(p.Map);
                        if (cells.Count == 0) return null;

                        // Find the best cell for roof building
                        IntVec3 bestCell = IntVec3.Invalid;
                        float bestDistSq = float.MaxValue;

                        foreach (IntVec3 cell in cells)
                        {
                            if (!ValidateRoofBuildCell(cell, p, forced))
                                continue;

                            float distSq = (cell - p.Position).LengthHorizontalSquared;
                            if (distSq < bestDistSq)
                            {
                                bestDistSq = distSq;
                                bestCell = cell;
                            }
                        }

                        if (bestCell.IsValid)
                        {
                            // Create the job
                            Job job = JobMaker.MakeJob(JobDefOf.BuildRoof);
                            job.targetA = bestCell;
                            job.targetB = bestCell;
                            return job;
                        }
                    }

                    return null;
                },
                debugJobDesc: DebugName);
        }

        /// <summary>
        /// Creates a job for the pawn using the cached targets
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // Since roof building targets are cells rather than things, 
            // we need to get the cells directly and find a valid target
            List<IntVec3> cells = GetRoofBuildCells(pawn.Map);
            if (cells.Count == 0)
                return null;

            // Use our existing system to find the best cell for roof building
            IntVec3 bestCell = IntVec3.Invalid;
            float bestDistSq = float.MaxValue;

            foreach (IntVec3 cell in cells)
            {
                if (!ValidateRoofBuildCell(cell, pawn, forced))
                    continue;

                float distSq = (cell - pawn.Position).LengthHorizontalSquared;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestCell = cell;
                }
            }

            // Create job for the best cell if found
            if (bestCell.IsValid)
            {
                Job job = JobMaker.MakeJob(JobDefOf.BuildRoof);
                job.targetA = bestCell;
                job.targetB = bestCell;
                return job;
            }

            return null;
        }

        #endregion

        #region Cell-selection helpers

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
            int maxCacheSize = 500;
            if (cells.Count > maxCacheSize)
            {
                cells = cells.Take(maxCacheSize).ToList();
            }

            return cells;
        }

        /// <summary>
        /// Validates if a cell is appropriate for roof building
        /// </summary>
        private bool ValidateRoofBuildCell(IntVec3 cell, Pawn pawn, bool forced)
        {
            // Filter out invalid cells
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

            // Skip if forbidden or unreachable
            if (cell.IsForbidden(pawn) ||
                !pawn.CanReserve(cell) ||
                !pawn.CanReach(cell, PathEndMode.Touch, pawn.NormalMaxDanger()))
                return false;

            // Check for roof stability
            if (!RoofCollapseUtility.WithinRangeOfRoofHolder(cell, pawn.Map) ||
                !RoofCollapseUtility.ConnectedToRoofHolder(cell, pawn.Map, assumeRoofAtRoot: true))
                return false;

            return true;
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return "JobGiver_BuildRoof_PawnControl";
        }

        #endregion
    }
}