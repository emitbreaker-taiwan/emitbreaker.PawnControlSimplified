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
    public class JobGiver_Cleaning_ClearPollution_PawnControl : JobGiver_Cleaning_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        public override bool RequiresMapZoneorArea => true;

        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.ClearPollution;

        /// <summary>
        /// Human-readable name for debug logging
        /// </summary>
        protected override string DebugName => "PollutionClearing";

        /// <summary>
        /// Update cache every 10 seconds (pollution changes slowly)
        /// </summary>
        protected override int CacheUpdateInterval => base.CacheUpdateInterval;

        /// <summary>
        /// Pollution clearing is slightly higher priority than filth cleaning
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            return 4.8f;
        }

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_Cleaning_ClearPollution_PawnControl() : base()
        {
            // Base constructor already initializes the cache system
        }

        #endregion

        #region Core flow

        /// <summary>
        /// Biotech check - only execute if Biotech is active
        /// </summary>
        protected override bool ShouldExecuteNow(int mapId)
        {
            // First check base conditions
            if (!base.ShouldExecuteNow(mapId))
                return false;

            // Then check Biotech requirement
            return ModLister.CheckBiotech("Clear pollution");
        }

        #endregion

        #region Target Selection

        /// <summary>
        /// Job-specific cache update method for tracking pollution cells
        /// Centralizes the pollution cell tracking logic
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            // Pollution clearing doesn't use Thing targets, so we return an empty collection
            // We handle cell caching separately through our own methods
            return Enumerable.Empty<Thing>();
        }

        /// <summary>
        /// Pollution clearing doesn't use Thing targets
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // Pollution clearing doesn't have "Thing" targets
            return Enumerable.Empty<Thing>();
        }

        /// <summary>
        /// Pollution clearing doesn't use Thing targets
        /// </summary>
        protected override bool RequiresThingTargets()
        {
            return false;
        }

        /// <summary>
        /// Check if map has a pollution clear area with pollution in it
        /// </summary>
        protected override bool AreMapRequirementsMet(Pawn pawn)
        {
            // Check if map has any designated pollution clear areas
            return pawn?.Map != null &&
                   pawn.Map.areaManager.PollutionClear != null &&
                   pawn.Map.areaManager.PollutionClear.TrueCount > 0;
        }

        #endregion

        #region Job Creation

        /// <summary>
        /// Creates a job for clearing pollution
        /// </summary>
        protected override Job CreateCleaningJob(Pawn pawn, bool forced)
        {
            if (pawn?.Map == null)
                return null;

            // Get cells from the pollution clear area that have pollution
            List<IntVec3> pollutedCells = GetPollutedCells(pawn.Map);
            if (pollutedCells.Count == 0)
                return null;

            // Create distance buckets
            var buckets = CreateDistanceBuckets(pawn, pollutedCells);
            if (buckets == null)
                return null;

            // Find the closest valid cell with pollution
            IntVec3 targetCell = FindBestCell(buckets, pawn, (cell, p) => ValidatePollutionCell(cell, p, forced));

            if (!targetCell.IsValid)
                return null;

            // Create the pollution clearing job
            Job job = JobMaker.MakeJob(WorkJobDef, targetCell);

            Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to clear pollution at {targetCell}");
            return job;
        }

        #endregion

        #region Helper Methods

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
            return LimitListSize(result);
        }

        /// <summary>
        /// Validates if a polluted cell can be cleared by the pawn
        /// </summary>
        private bool ValidatePollutionCell(IntVec3 cell, Pawn pawn, bool forced)
        {
            if (!IsValidCell(cell, pawn?.Map))
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
            if (IsCellReservedByAnother(pawn, cell, WorkJobDef))
                return false;

            return true;
        }

        #endregion

        #region Reset

        /// <summary>
        /// Reset the cache - implements IResettableCache
        /// </summary>
        public override void Reset()
        {
            // Use centralized cache reset
            base.Reset();
        }

        #endregion
    }
}