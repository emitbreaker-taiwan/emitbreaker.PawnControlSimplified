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

            return true;
        }

        #endregion

        #region Job Creation

        /// <summary>
        /// Creates a construction job for smoothing floors.
        /// </summary>
        protected override Job CreateConstructionJob(Pawn pawn, bool forced)
        {
            // Create a job for smoothing the floor
            return JobMaker.MakeJob(JobDefOf.SmoothFloor);
        }

        #endregion
    }
}