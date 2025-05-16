using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that allows pawns to smooth walls in designated areas.
    /// Uses the Construction work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Construction_SmoothWall_PawnControl : JobGiver_Common_ConstructAffectFloor_PawnControl
    {
        #region Configuration

        /// <summary>
        /// The designation this job giver targets
        /// </summary>
        protected override DesignationDef TargetDesignation => DesignationDefOf.SmoothWall;

        /// <summary>
        /// The job to create
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.SmoothWall;

        /// <summary>
        /// Cache update interval - walls don't change as often
        /// </summary>
        protected override int CacheUpdateInterval => base.CacheUpdateInterval;

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_Construction_SmoothWall_PawnControl() : base()
        {
            // Base constructor already initializes the cache system
        }

        #endregion

        #region Target Selection

        /// <summary>
        /// Job-specific cache update method for wall smoothing cells
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            // Wall smoothing doesn't use Thing targets, so delegate to base implementation
            return base.UpdateJobSpecificCache(map);
        }

        #endregion

        #region Validation

        /// <summary>
        /// Override to add wall-specific validation logic
        /// </summary>
        protected override bool ValidateFloorCell(IntVec3 cell, Pawn pawn, bool forced)
        {
            // First do the base validation
            if (!base.ValidateFloorCell(cell, pawn, forced))
                return false;

            // Additional validation for wall smoothing
            Building edifice = cell.GetEdifice(pawn.Map);

            // Check if the edifice at this cell is smoothable
            if (edifice == null || !edifice.def.IsSmoothable)
                return false;

            return true;
        }

        #endregion

        #region Job Creation

        /// <summary>
        /// Creates a job for smoothing walls using the proper cell target
        /// </summary>
        protected override Job CreateFloorConstructionJob(Pawn pawn, bool forced)
        {
            // Get cells marked for wall smoothing
            List<IntVec3> cells = GetDesignatedCells(pawn.Map);
            if (cells.Count == 0)
                return null;

            // Create buckets and find best cell
            var buckets = CreateDistanceBuckets(pawn, cells);
            if (buckets == null)
                return null;

            // Find the best cell to smooth wall
            IntVec3 targetCell = FindBestCell(buckets, pawn, (cell, p) => ValidateFloorCell(cell, p, forced));

            if (!targetCell.IsValid)
                return null;

            // Create the job with the specific target cell
            Job job = JobMaker.MakeJob(WorkJobDef, targetCell);

            Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to smooth wall at {targetCell}");
            return job;
        }

        /// <summary>
        /// Creates a construction job for smoothing walls.
        /// </summary>
        protected override Job CreateConstructionJob(Pawn pawn, bool forced)
        {
            // Delegate to the specialized floor/wall construction method
            return CreateFloorConstructionJob(pawn, forced);
        }

        #endregion

        #region Reset

        /// <summary>
        /// Reset the cache - implements IResettableCache
        /// </summary>
        public override void Reset()
        {
            // Use centralized cache reset from parent
            base.Reset();
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return "JobGiver_Construction_SmoothWall_PawnControl";
        }

        #endregion
    }
}