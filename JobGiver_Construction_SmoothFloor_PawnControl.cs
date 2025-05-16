using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that allows pawns to smooth floors in designated areas.
    /// Uses the Construction work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Construction_SmoothFloor_PawnControl : JobGiver_Common_ConstructAffectFloor_PawnControl
    {
        #region Configuration

        /// <summary>
        /// The designation this job giver targets
        /// </summary>
        protected override DesignationDef TargetDesignation => DesignationDefOf.SmoothFloor;

        /// <summary>
        /// The job to create
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.SmoothFloor;

        /// <summary>
        /// Cache update interval - update slightly less often for floor smoothing
        /// </summary>
        protected override int CacheUpdateInterval => base.CacheUpdateInterval;

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_Construction_SmoothFloor_PawnControl() : base()
        {
            // Base constructor already initializes the cache system
        }

        #endregion

        #region Target Selection

        /// <summary>
        /// Job-specific cache update method for floor smoothing cells
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            // Floor smoothing doesn't use Thing targets, so delegate to base implementation
            return base.UpdateJobSpecificCache(map);
        }

        #endregion

        #region Validation

        /// <summary>
        /// Override to add any additional validation specific to floor smoothing
        /// </summary>
        protected override bool ValidateFloorCell(IntVec3 cell, Pawn pawn, bool forced)
        {
            // Use base class validation first
            if (!base.ValidateFloorCell(cell, pawn, forced))
                return false;

            // Add SmoothFloor-specific validation if needed
            // For now, no additional validation is needed

            return true;
        }

        #endregion

        #region Job Creation

        /// <summary>
        /// Creates a job for smoothing floors using the proper cell target
        /// </summary>
        protected override Job CreateFloorConstructionJob(Pawn pawn, bool forced)
        {
            // Get cells marked for floor smoothing
            List<IntVec3> cells = GetDesignatedCells(pawn.Map);
            if (cells.Count == 0)
                return null;

            // Create buckets and find best cell
            var buckets = CreateDistanceBuckets(pawn, cells);
            if (buckets == null)
                return null;

            // Find the best cell to smooth floor
            IntVec3 targetCell = FindBestCell(buckets, pawn, (cell, p) => ValidateFloorCell(cell, p, forced));

            if (!targetCell.IsValid)
                return null;

            // Create the job with the specific target cell
            Job job = JobMaker.MakeJob(WorkJobDef, targetCell);

            Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to smooth floor at {targetCell}");
            return job;
        }

        /// <summary>
        /// Creates a construction job for smoothing floors.
        /// </summary>
        protected override Job CreateConstructionJob(Pawn pawn, bool forced)
        {
            // For floor smoothing, we delegate to the specialized floor construction job method
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
            return "JobGiver_Construction_SmoothFloor_PawnControl";
        }

        #endregion
    }
}