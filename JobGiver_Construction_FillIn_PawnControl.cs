using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns pit filling tasks to pawns with the BasicWorker work tag.
    /// This allows non-humanlike pawns to fill in pit burrows with the FillIn designation.
    /// </summary>
    public class JobGiver_Construction_FillIn_PawnControl : JobGiver_Common_RemoveBuilding_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Whether this construction job requires specific tag for non-humanlike pawns
        /// </summary>
        protected override PawnEnumTags RequiredTag => PawnEnumTags.AllowWork_Construction;

        /// <summary>
        /// Use FillIn designation
        /// </summary>
        protected override DesignationDef TargetDesignation => DesignationDefOf.FillIn;

        /// <summary>
        /// Use FillIn job
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.FillIn;

        #endregion

        #region Overrides

        /// <summary>
        /// Override WorkTag to specify "BasicWorker" work type instead of Construction
        /// </summary>
        protected override string WorkTag => "BasicWorker";

        /// <summary>
        /// Override debug name for better logging
        /// </summary>
        protected override string DebugName => "FillIn";

        /// <summary>
        /// Fill In is a medium priority task
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            return 6.2f;  // Similar priority to other BasicWorker tasks
        }

        /// <summary>
        /// Explicitly override TryGiveJob to use the common helper with the correct type
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            return CreateRemovalJob<JobGiver_Construction_FillIn_PawnControl>(pawn);
        }

        /// <summary>
        /// Implements the abstract method from JobGiver_Scan_PawnControl to process cached targets
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            if (pawn == null || targets == null || targets.Count == 0)
                return null;

            // Extra faction validation to ensure only allowed pawns can perform this job
            if (!IsPawnValidFaction(pawn))
                return null;

            // Use the parent class's ExecuteJobGiverInternal method for consistent behavior
            return ExecuteJobGiverInternal(pawn, LimitListSize(targets));
        }

        /// <summary>
        /// Override ValidateTarget to add PitBurrow-specific validation
        /// </summary>
        protected override bool ValidateTarget(Thing thing, Pawn pawn)
        {
            // First perform base validation
            if (!base.ValidateTarget(thing, pawn))
                return false;

            // Additional validation for PitBurrow
            PitBurrow pitBurrow = thing as PitBurrow;
            if (pitBurrow == null)
                return false;

            // Check if pawn can safely approach
            if (!pawn.CanReach(thing, PathEndMode.Touch, pawn.NormalMaxDanger()))
                return false;

            return true;
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return "JobGiver_Construction_FillIn_PawnControl";
        }

        #endregion
    }
}