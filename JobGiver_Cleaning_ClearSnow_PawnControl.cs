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
    /// JobGiver that assigns tasks to clear snow from designated areas.
    /// Uses the Cleaning work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Cleaning_ClearSnow_PawnControl : JobGiver_Scan_PawnControl
    {
        #region Configuration

        private const float MIN_SNOW_DEPTH = 0.2f; // Minimum snow depth to clear

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 100f, 400f, 1600f }; // 10, 20, 40 tiles

        #endregion

        #region Overrides

        protected override string WorkTag => "Cleaning";

        protected override string DebugName => "SnowClearing";

        protected override int CacheUpdateInterval => 600; // Update every 10 seconds (snow changes slowly)

        protected override float GetBasePriority(string workTag)
        {
            return 4.5f;
        }

        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // Snow clearing doesn't have "Thing" targets - this will return an empty collection
            // but we'll handle the cell-based targets separately
            return Enumerable.Empty<Thing>();
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Use the StandardTryGiveJob pattern that works
            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Cleaning_ClearSnow_PawnControl>(
                pawn,
                WorkTag,  // Use WorkTag property for consistency
                (p, forced) => {
                    // IMPORTANT: Only player pawns and slaves owned by player should clear snow
                    if (p.Faction != Faction.OfPlayer &&
                        !(p.IsSlave && p.HostFaction == Faction.OfPlayer))
                        return null;

                    // Check if map has any designated snow clear areas
                    if (p?.Map == null || p.Map.areaManager.SnowClear.TrueCount == 0)
                        return null;

                    // For snow clearing we need to use our special job creation method
                    return TryCreateSnowClearingJob(p);
                },
                debugJobDesc: "snow clearing assignment");
        }

        /// <summary>
        /// Creates a job for the pawn using the cached targets
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // Note: For snow clearing, we don't use Thing targets since snow is cell-based
            // Instead, we delegate to our specialized method that handles snow cells directly
            return TryCreateSnowClearingJob(pawn);
        }

        #endregion

        #region Snow-clearing helpers

        /// <summary>
        /// Creates a job for clearing snow
        /// </summary>
        private Job TryCreateSnowClearingJob(Pawn pawn)
        {
            if (pawn?.Map == null) return null;

            // Get cells from the snow clear area that have enough snow
            List<IntVec3> snowyCells = GetSnowyCells(pawn.Map);
            if (snowyCells.Count == 0)
                return null;

            // Use distance bucketing for efficient cell selection
            List<IntVec3> cellsInReach = new List<IntVec3>();

            foreach (IntVec3 cell in snowyCells)
            {
                // Skip if the cell is forbidden
                if (cell.IsForbidden(pawn))
                    continue;

                // Skip if snow depth is now below threshold (might have melted)
                if (pawn.Map.snowGrid.GetDepth(cell) < MIN_SNOW_DEPTH)
                    continue;

                // Skip if we can't reserve the cell
                if (!pawn.CanReserve(cell))
                    continue;

                // Calculate distance
                int distanceSq = (cell - pawn.Position).LengthHorizontalSquared;

                // Only include cells within a reasonable range
                if (distanceSq <= DISTANCE_THRESHOLDS[DISTANCE_THRESHOLDS.Length - 1])
                {
                    cellsInReach.Add(cell);
                }
            }

            if (cellsInReach.Count == 0)
                return null;

            // Create distance buckets
            List<IntVec3>[] buckets = new List<IntVec3>[DISTANCE_THRESHOLDS.Length + 1];
            for (int i = 0; i < buckets.Length; i++)
            {
                buckets[i] = new List<IntVec3>();
            }

            // Sort cells into buckets by distance
            foreach (IntVec3 cell in cellsInReach)
            {
                float distanceSq = (cell - pawn.Position).LengthHorizontalSquared;

                int bucketIndex = 0;
                while (bucketIndex < DISTANCE_THRESHOLDS.Length && distanceSq > DISTANCE_THRESHOLDS[bucketIndex])
                {
                    bucketIndex++;
                }

                buckets[bucketIndex].Add(cell);
            }

            // Find the closest valid cell with sufficient snow
            IntVec3 targetCell = IntVec3.Invalid;

            for (int b = 0; b < buckets.Length; b++)
            {
                if (buckets[b].Count == 0)
                    continue;

                // Randomize within each bucket for even distribution
                buckets[b].Shuffle();

                foreach (IntVec3 cell in buckets[b])
                {
                    // Final validation - snow depth might have changed since we cached
                    if (ValidateSnowCell(cell, pawn))
                    {
                        targetCell = cell;
                        break;
                    }
                }

                if (targetCell.IsValid)
                    break;
            }

            if (!targetCell.IsValid)
                return null;

            // Create the snow clearing job
            Job job = JobMaker.MakeJob(JobDefOf.ClearSnow, targetCell);

            Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to clear snow at {targetCell}");
            return job;
        }

        /// <summary>
        /// Gets cells from the snow clear area that actually have enough snow to clear
        /// </summary>
        private List<IntVec3> GetSnowyCells(Map map)
        {
            var result = new List<IntVec3>();

            if (map?.areaManager?.SnowClear == null)
                return result;

            // Get cells from the snow clear area
            List<IntVec3> activeCells = map.areaManager.SnowClear.ActiveCells.ToList();

            // Only include cells that actually have enough snow to clear
            foreach (IntVec3 cell in activeCells)
            {
                if (map.snowGrid.GetDepth(cell) >= MIN_SNOW_DEPTH)
                {
                    result.Add(cell);
                }
            }

            // Limit collection size for performance
            int maxCells = 1000;
            if (result.Count > maxCells)
            {
                result = result.Take(maxCells).ToList();
            }

            return result;
        }

        /// <summary>
        /// Validates if a snow cell can be cleared by the pawn
        /// </summary>
        private bool ValidateSnowCell(IntVec3 cell, Pawn pawn)
        {
            if (!cell.IsValid || pawn?.Map == null)
                return false;

            return pawn.Map.snowGrid.GetDepth(cell) >= MIN_SNOW_DEPTH &&
                   !cell.IsForbidden(pawn) &&
                   pawn.CanReserve(cell);
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return "JobGiver_Cleaning_ClearSnow_PawnControl";
        }

        #endregion
    }
}