using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that allows pawns to remove floors in designated areas.
    /// Uses the Construction work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Construction_RemoveFloor_PawnControl : JobGiver_Common_ConstructAffectFloor_PawnControl
    {
        #region Configuration

        /// <summary>
        /// The designation this job giver targets
        /// </summary>
        protected override DesignationDef TargetDesignation => DesignationDefOf.RemoveFloor;

        /// <summary>
        /// The job to create
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.RemoveFloor;

        #endregion

        #region Validation

        /// <summary>
        /// Override to add any additional validation specific to floor removal
        /// </summary>
        protected override bool ValidateFloorCell(IntVec3 cell, Pawn pawn, bool forced)
        {
            // Use base class validation first
            if (!base.ValidateFloorCell(cell, pawn, forced))
                return false;

            // Add RemoveFloor-specific validation if needed

            return true;
        }

        #endregion

        #region Job Creation

        /// <summary>
        /// Creates a construction job for removing floors.
        /// </summary>
        protected override Job CreateConstructionJob(Pawn pawn, bool forced)
        {
            // Create a job for removing floors
            return JobMaker.MakeJob(WorkJobDef);
        }

        #endregion
    }
}