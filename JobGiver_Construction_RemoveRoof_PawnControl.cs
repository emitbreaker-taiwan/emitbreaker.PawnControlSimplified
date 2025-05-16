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
    /// JobGiver that allows non-humanlike pawns to remove roofs in designated areas.
    /// Uses the Construction work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Construction_RemoveRoof_PawnControl : JobGiver_Scan_PawnControl
    {
        #region Overrides

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        public override bool RequiresMapZoneorArea => true;

        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.RemoveRoof;
        public override string WorkTag => "Construction";
        protected override int CacheUpdateInterval => base.CacheUpdateInterval;
        protected override string DebugName => "roof removal assignment";

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 2500f }; // 15, 25, 50 tiles

        protected override bool ShouldExecuteNow(int mapId)
        {
            // Quick check if there are roof removal areas on the map
            var map = Find.Maps.FirstOrDefault(m => m.uniqueID == mapId);
            return map != null && map.areaManager.NoRoof != null && map.areaManager.NoRoof.TrueCount > 0;
        }

        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // This job works with IntVec3 cells rather than Things, so we need to handle it differently
            // We'll return an empty list and handle the actual target collection in TryGiveJob
            return Enumerable.Empty<Thing>();
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (ShouldSkip(pawn))
            {
                return null;
            }

            // Use the StandardTryGiveJob pattern directly
            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Construction_RemoveRoof_PawnControl>(
                pawn,
                WorkTag,  // Use the WorkTag property from the base class
                (p, forced) => {
                    // Get targets from the cache
                    List<Thing> targets = GetTargets(p.Map).ToList();
                    if (targets.Count == 0)
                    {
                        // Direct approach for roof removal since targets are cells, not things
                        List<IntVec3> cells = GetRoofRemovalCells(p.Map);
                        if (cells.Count == 0) return null;

                        // Find the best cell for roof removal
                        IntVec3 bestCell = IntVec3.Invalid;
                        float bestDistSq = float.MaxValue;

                        foreach (IntVec3 cell in cells)
                        {
                            if (!ValidateRoofRemovalCell(cell, p))
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
                            Job job = JobMaker.MakeJob(WorkJobDef);
                            job.targetA = bestCell;
                            job.targetB = bestCell;
                            Utility_DebugManager.LogNormal($"{p.LabelShort} created job to remove roof at {bestCell}");
                            return job;
                        }
                    }

                    return null;
                },
                debugJobDesc: DebugName);
        }

        /// <summary>
        /// Implements the abstract method from JobGiver_Scan_PawnControl to process cached targets
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // This job works with cells, not Things, so we need to implement special handling
            if (pawn?.Map == null)
                return null;

            // Get cells marked for roof removal
            List<IntVec3> cells = GetRoofRemovalCells(pawn.Map);
            if (cells.Count == 0)
                return null;

            // Create distance-based buckets for efficient processing
            List<IntVec3>[] buckets = CreateDistanceBuckets(cells, pawn);

            // Find the best cell for roof removal
            IntVec3 bestCell = FindBestRoofRemovalCell(buckets, pawn);

            // Create job if a valid cell was found
            if (bestCell.IsValid)
            {
                // Create the job
                Job job = JobMaker.MakeJob(WorkJobDef);
                job.targetA = bestCell;
                job.targetB = bestCell;
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to remove roof at {bestCell}");
                return job;
            }

            return null;
        }

        #endregion

        #region Cell-selection helpers

        /// <summary>
        /// Gets cells marked for roof removal that actually have roofs
        /// </summary>
        private List<IntVec3> GetRoofRemovalCells(Map map)
        {
            if (map == null || map.areaManager.NoRoof == null)
                return new List<IntVec3>();

            var cells = new List<IntVec3>();

            // Find all cells marked for roof removal that actually have roofs
            foreach (IntVec3 cell in map.areaManager.NoRoof.ActiveCells)
            {
                // Skip if not roofed - only remove existing roofs
                if (!cell.Roofed(map))
                    continue;

                cells.Add(cell);
            }

            // Limit cell count for performance
            int maxCellCount = 500;
            if (cells.Count > maxCellCount)
            {
                cells = cells.Take(maxCellCount).ToList();
            }

            return cells;
        }

        /// <summary>
        /// Creates distance-based buckets for cells
        /// </summary>
        private List<IntVec3>[] CreateDistanceBuckets(List<IntVec3> cells, Pawn pawn)
        {
            // Create buckets
            List<IntVec3>[] buckets = new List<IntVec3>[DISTANCE_THRESHOLDS.Length + 1];
            for (int i = 0; i < buckets.Length; i++)
                buckets[i] = new List<IntVec3>();

            // Sort cells into buckets by distance
            foreach (IntVec3 cell in cells)
            {
                float distSq = (cell - pawn.Position).LengthHorizontalSquared;
                int bucketIndex = buckets.Length - 1;

                for (int i = 0; i < DISTANCE_THRESHOLDS.Length; i++)
                {
                    if (distSq <= DISTANCE_THRESHOLDS[i])
                    {
                        bucketIndex = i;
                        break;
                    }
                }

                buckets[bucketIndex].Add(cell);
            }

            return buckets;
        }

        /// <summary>
        /// Finds the best cell for roof removal from distance-based buckets
        /// </summary>
        private IntVec3 FindBestRoofRemovalCell(List<IntVec3>[] buckets, Pawn pawn)
        {
            // Process each bucket in order
            for (int i = 0; i < buckets.Length; i++)
            {
                if (buckets[i].Count == 0)
                    continue;

                // Sort by priority within the bucket
                if (buckets[i].Count > 1)
                {
                    buckets[i].Sort((a, b) => GetCellPriority(a, pawn.Map).CompareTo(GetCellPriority(b, pawn.Map)));
                }

                // Randomize cells with same priority
                buckets[i].Shuffle();

                foreach (IntVec3 cell in buckets[i])
                {
                    if (ValidateRoofRemovalCell(cell, pawn))
                        return cell;
                }
            }

            return IntVec3.Invalid;
        }

        /// <summary>
        /// Validates if a cell is appropriate for roof removal
        /// </summary>
        private bool ValidateRoofRemovalCell(IntVec3 cell, Pawn pawn)
        {
            // Filter out invalid cells
            if (!pawn.Map.areaManager.NoRoof[cell] || !cell.Roofed(pawn.Map))
                return false;

            // Skip if forbidden or unreachable
            if (cell.IsForbidden(pawn) ||
                !pawn.CanReserve(cell) ||
                !pawn.CanReach(cell, PathEndMode.Touch, pawn.NormalMaxDanger()))
                return false;

            return true;
        }

        /// <summary>
        /// Calculates roof removal priority based on proximity to other roofed cells
        /// </summary>
        private float GetCellPriority(IntVec3 cell, Map map)
        {
            // Logic from vanilla WorkGiver_RemoveRoof:
            // - Prioritize cells NOT adjacent to support structures
            // - Prioritize cells with fewer adjacent roofed cells

            int adjacentRoofedCellsCount = 0;
            for (int i = 0; i < 8; ++i)
            {
                IntVec3 c = cell + GenAdj.AdjacentCells[i];
                if (c.InBounds(map))
                {
                    Building edifice = c.GetEdifice(map);
                    if (edifice != null && edifice.def.holdsRoof)
                        return -60f; // De-prioritize cells next to roof supports

                    if (c.Roofed(map))
                        ++adjacentRoofedCellsCount;
                }
            }

            // Prioritize cells with fewer adjacent roofed cells
            return -Mathf.Min(adjacentRoofedCellsCount, 3);
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return "JobGiver_RemoveRoof_PawnControl";
        }

        #endregion
    }
}