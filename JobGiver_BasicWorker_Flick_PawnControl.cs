using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Enhanced JobGiver that assigns switch flicking jobs to eligible pawns.
    /// Requires the BasicWorker work tag to be enabled.
    /// </summary>
    public class JobGiver_BasicWorker_Flick_PawnControl : JobGiver_BasicWorker_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Use Hauling work tag
        /// </summary>
        protected override string WorkTag => "Hauling";

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "Flick";

        /// <summary>
        /// The designation this job giver targets
        /// </summary>
        protected override DesignationDef TargetDesignation => DesignationDefOf.Flick;

        /// <summary>
        /// The job to create
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.Flick;

        /// <summary>
        /// This job requires player faction
        /// </summary>
        protected override bool RequiresPlayerFaction => true;

        #endregion

        #region Target Selection

        /// <summary>
        /// Additional validation for flick targets
        /// </summary>
        protected override bool IsValidTarget(Thing thing, Pawn worker)
        {
            // Use base validation first
            if (!base.IsValidTarget(thing, worker))
                return false;

            // Specific validation for flick targets could be added here if needed
            return true;
        }

        #endregion
    }
}