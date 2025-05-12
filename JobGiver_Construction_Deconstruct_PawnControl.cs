using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that allows non-humanlike pawns to deconstruct buildings with the Deconstruct designation.
    /// </summary>
    public class JobGiver_Construction_Deconstruct_PawnControl : JobGiver_Common_RemoveBuilding_PawnControl
    {
        #region Overrides

        protected override DesignationDef TargetDesignation => DesignationDefOf.Deconstruct;

        protected override JobDef WorkJobDef => JobDefOf.Deconstruct;

        // Override debug name for better logging
        protected override string DebugName => "Deconstruct";

        /// <summary>
        /// Whether this construction job requires specific tag for non-humanlike pawns
        /// </summary>
        protected override PawnEnumTags RequiredTag => PawnEnumTags.AllowWork_Construction;

        protected override float GetBasePriority(string workTag)
        {
            // Higher priority than extract tree but lower than most urgent tasks
            return 5.9f;
        }

        /// <summary>
        /// Override TryGiveJob to use the common helper method with the specific type
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            // Use the type-specific CreateRemovalJob to ensure proper job creation
            return CreateRemovalJob<JobGiver_Construction_Deconstruct_PawnControl>(pawn);
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

        // Override ValidateTarget to add deconstruct-specific validation
        protected override bool ValidateTarget(Thing thing, Pawn pawn)
        {
            // First perform base validation
            if (!base.ValidateTarget(thing, pawn))
                return false;

            // Then check deconstruct-specific requirements
            Building building = thing.GetInnerIfMinified() as Building;
            if (building == null)
                return false;

            if (!building.DeconstructibleBy(pawn.Faction))
                return false;

            return true;
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return "JobGiver_Construction_Deconstruct_PawnControl";
        }

        #endregion
    }
}