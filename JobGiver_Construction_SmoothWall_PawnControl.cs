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
        /// Creates a construction job for smoothing walls.
        /// </summary>
        protected override Job CreateConstructionJob(Pawn pawn, bool forced)
        {
            // Find the best cell for smoothing
            IntVec3 cell = FindBestCell(
                CreateDistanceBuckets(pawn, _designatedCellsCache[pawn.Map.uniqueID][TargetDesignation]),
                pawn,
                (c, p) => ValidateFloorCell(c, p, forced)
            );

            if (cell.IsValid)
            {
                return JobMaker.MakeJob(WorkJobDef, cell);
            }

            return null;
        }

        #endregion
    }
}