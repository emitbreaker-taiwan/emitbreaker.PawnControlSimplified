using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to clear pollution from designated areas.
    /// Uses the Cleaning work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Cleaning_ClearPollution_PawnControl : JobGiver_Scan_PawnControl
    {
        #region Configuration

        // Constants from vanilla JobDriver_ClearPollution
        private const int MAX_DISTANCE_TO_CLEAR = 10;
        private const int MAX_DISTANCE_TO_CLEAR_SQUARED = 100;
        private const int POLLUTION_CELLS_TO_CLEAR_PER_JOB = 6;

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 100f, 400f, 1600f }; // 10, 20, 40 tiles

        #endregion

        #region Overrides

        protected override string WorkTag => "Cleaning";

        protected override string DebugName => "PollutionClearing";

        protected override int CacheUpdateInterval => 600; // Update every 10 seconds (pollution changes slowly)

        public override float GetPriority(Pawn pawn)
        {
            // Pollution clearing is higher priority than regular cleaning but lower than most tasks
            return 4.8f;
        }

        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // Pollution clearing doesn't have "Thing" targets - this will return an empty collection
            // but we'll handle the cell-based targets separately
            return Enumerable.Empty<Thing>();
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Use the StandardTryGiveJob pattern that works
            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Cleaning_ClearPollution_PawnControl>(
                pawn,
                WorkTag,  // Use WorkTag property for consistency
                (p, forced) => {
                    // IMPORTANT: Only player pawns and slaves owned by player should clear pollution
                    if (p.Faction != Faction.OfPlayer &&
                        !(p.IsSlave && p.HostFaction == Faction.OfPlayer))
                        return null;

                    // Check if map has any designated pollution clear areas
                    if (p?.Map == null || p.Map.areaManager.PollutionClear.TrueCount == 0)
                        return null;

                    // Get pollution cells to clear (our target cells)
                    return TryCreatePollutionClearingJob(p, forced);
                },
                debugJobDesc: "pollution clearing assignment");
        }

        protected override bool ShouldExecuteNow(int mapId)
        {
            return ModLister.CheckBiotech("Clear pollution");
        }

        /// <summary>
        /// Creates a job for the pawn using the cached targets
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // Note: For pollution clearing, we don't actually use the Thing targets
            // because pollution is cell-based, not thing-based

            // Delegate to our specialized method that handles pollution cells
            return TryCreatePollutionClearingJob(pawn, forced);
        }

        #endregion

        #region Pollution-clearing helpers

        /// <summary>
        /// Creates a job for clearing pollution
        /// </summary>
        private Job TryCreatePollutionClearingJob(Pawn pawn, bool forced)
        {
            if (pawn?.Map == null) return null;

            // Get cells from the pollution clear area that have pollution
            List<IntVec3> pollutedCells = GetPollutedCells(pawn.Map);
            if (pollutedCells.Count == 0)
                return null;

            // Create distance buckets for efficient cell selection
            List<IntVec3>[] buckets = new List<IntVec3>[DISTANCE_THRESHOLDS.Length + 1];
            for (int i = 0; i < buckets.Length; i++)
            {
                buckets[i] = new List<IntVec3>();
            }

            // Sort cells into buckets by distance
            foreach (IntVec3 cell in pollutedCells)
            {
                float distanceSq = (cell - pawn.Position).LengthHorizontalSquared;

                int bucketIndex = 0;
                while (bucketIndex < DISTANCE_THRESHOLDS.Length && distanceSq > DISTANCE_THRESHOLDS[bucketIndex])
                {
                    bucketIndex++;
                }

                buckets[bucketIndex].Add(cell);
            }

            // Find the closest valid cell with pollution
            IntVec3 targetCell = IntVec3.Invalid;

            for (int b = 0; b < buckets.Length; b++)
            {
                if (buckets[b].Count == 0)
                    continue;

                // Randomize within each bucket for even distribution
                buckets[b].Shuffle();

                foreach (IntVec3 cell in buckets[b])
                {
                    if (ValidatePollutionCell(cell, pawn, forced))
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

            // Create the pollution clearing job
            Job job = JobMaker.MakeJob(JobDefOf.ClearPollution, targetCell);

            Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to clear pollution at {targetCell}");
            return job;
        }

        /// <summary>
        /// Gets cells from the pollution clear area that actually have pollution
        /// </summary>
        private List<IntVec3> GetPollutedCells(Map map)
        {
            var result = new List<IntVec3>();

            if (map?.areaManager?.PollutionClear == null)
                return result;

            // Get cells from the pollution clear area
            List<IntVec3> activeCells = map.areaManager.PollutionClear.ActiveCells.ToList();

            // Only include cells that actually have pollution
            foreach (IntVec3 cell in activeCells)
            {
                if (map.pollutionGrid.IsPolluted(cell))
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
        /// Validates if a polluted cell can be cleared by the pawn
        /// </summary>
        private bool ValidatePollutionCell(IntVec3 cell, Pawn pawn, bool forced)
        {
            if (!cell.IsValid || pawn?.Map == null)
                return false;

            // Must be polluted
            if (!pawn.Map.pollutionGrid.IsPolluted(cell))
                return false;

            // Must be in the designated pollution clear area
            if (!pawn.Map.areaManager.PollutionClear[cell])
                return false;

            // Must not be forbidden
            if (cell.IsForbidden(pawn))
                return false;

            // Must be able to reserve
            if (!pawn.CanReserve(cell, 1, -1, null, forced))
                return false;

            // Check if anyone else is already working on this cell
            if (AnyOtherPawnCleaning(pawn, cell))
                return false;

            return true;
        }

        /// <summary>
        /// Checks if any other pawn is already clearing this cell
        /// </summary>
        private bool AnyOtherPawnCleaning(Pawn pawn, IntVec3 cell)
        {
            List<Pawn> freeColonistsSpawned = pawn.Map.mapPawns.FreeColonistsSpawned;

            for (int i = 0; i < freeColonistsSpawned.Count; i++)
            {
                if (freeColonistsSpawned[i] != pawn &&
                    freeColonistsSpawned[i].CurJobDef == JobDefOf.ClearPollution)
                {
                    LocalTargetInfo target = freeColonistsSpawned[i].CurJob.GetTarget(TargetIndex.A);
                    if (target.IsValid && target.Cell == cell)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return "JobGiver_Cleaning_ClearPollution_PawnControl";
        }

        #endregion
    }
}