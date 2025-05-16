using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to clear snow from designated areas.
    /// Uses the Cleaning work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Cleaning_ClearSnow_PawnControl : JobGiver_Cleaning_PawnControl
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
        protected override JobDef WorkJobDef => JobDefOf.ClearSnow;

        private const float MIN_SNOW_DEPTH = 0.2f; // Minimum snow depth to clear

        /// <summary>
        /// Human-readable name for debug logging
        /// </summary>
        protected override string DebugName => "SnowClearing";

        /// <summary>
        /// Update every 10 seconds (snow changes slowly)
        /// </summary>
        protected override int CacheUpdateInterval => base.CacheUpdateInterval;

        /// <summary>
        /// Snow clearing is lowest priority cleaning task
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            return 4.5f;
        }

        /// <summary>
        /// Snow clearing uses designated zone
        /// </summary>
        public override bool RequiresDesignator => true;

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_Cleaning_ClearSnow_PawnControl() : base()
        {
            // Base constructor already initializes the cache system
        }

        #endregion

        #region Target Selection

        /// <summary>
        /// Job-specific cache update method for snow cells
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            // Snow clearing doesn't use Thing targets, so return empty
            return Enumerable.Empty<Thing>();
        }

        /// <summary>
        /// Snow clearing doesn't use Thing targets
        /// </summary>
        protected override bool RequiresThingTargets()
        {
            return false;
        }

        /// <summary>
        /// Snow clearing doesn't use Thing targets
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // Snow clearing doesn't have "Thing" targets
            return Enumerable.Empty<Thing>();
        }

        /// <summary>
        /// Check if map has a snow clear area
        /// </summary>
        protected override bool AreMapRequirementsMet(Pawn pawn)
        {
            // Check if map has any designated snow clear areas
            return pawn?.Map != null &&
                   pawn.Map.areaManager.SnowClear != null &&
                   pawn.Map.areaManager.SnowClear.TrueCount > 0;
        }

        #endregion

        #region Core Flow

        /// <summary>
        /// Only execute if there's actually snow on the map
        /// </summary>
        protected override bool ShouldExecuteNow(int mapId)
        {
            // First check base conditions
            if (!base.ShouldExecuteNow(mapId))
                return false;

            // Check if the map exists and has snow
            Map map = Find.Maps.FirstOrDefault(m => m.uniqueID == mapId);
            if (map == null)
                return false;

            return map.snowGrid.TotalDepth > 0.1f;
        }

        #endregion

        #region Job Creation

        /// <summary>
        /// Creates a job for clearing snow
        /// </summary>
        protected override Job CreateCleaningJob(Pawn pawn, bool forced)
        {
            if (pawn?.Map == null) return null;

            // Get cells from the snow clear area that have enough snow
            List<IntVec3> snowyCells = GetSnowyCells(pawn.Map);
            if (snowyCells.Count == 0)
                return null;

            // Create distance buckets for optimal assignment
            var buckets = CreateDistanceBuckets(pawn, snowyCells);
            if (buckets == null)
                return null;

            // Find the closest valid cell with snow
            IntVec3 targetCell = FindBestCell(buckets, pawn, ValidateSnowCell);

            if (!targetCell.IsValid)
                return null;

            // Create the snow clearing job
            Job job = JobMaker.MakeJob(WorkJobDef, targetCell);

            Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to clear snow at {targetCell}");
            return job;
        }

        #endregion

        #region Helper Methods

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

            return LimitListSize(result);
        }

        /// <summary>
        /// Validates if a snow cell can be cleared by the pawn
        /// </summary>
        private bool ValidateSnowCell(IntVec3 cell, Pawn pawn)
        {
            if (!IsValidCell(cell, pawn?.Map))
                return false;

            return pawn.Map.snowGrid.GetDepth(cell) >= MIN_SNOW_DEPTH &&
                   !cell.IsForbidden(pawn) &&
                   pawn.CanReserve(cell);
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