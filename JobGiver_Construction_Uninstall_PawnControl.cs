using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that allows non-humanlike pawns to uninstall buildings with the Uninstall designation.
    /// </summary>
    public class JobGiver_Construction_Uninstall_PawnControl : JobGiver_Common_RemoveBuilding_PawnControl
    {
        #region Overrides

        protected override DesignationDef TargetDesignation => DesignationDefOf.Uninstall;

        protected override JobDef WorkJobDef => JobDefOf.Uninstall;

        // Override debug name for better logging
        protected override string DebugName => "Uninstall";

        protected override float GetBasePriority(string workTag)
        {
            // Slightly lower priority than deconstruct
            return 5.8f;
        }

        /// <summary>
        /// Explicitly override TryGiveJob to handle uninstall-specific validation
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            return CreateRemovalJob<JobGiver_Construction_Uninstall_PawnControl>(pawn);
        }

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

        protected override bool ValidateTarget(Thing thing, Pawn pawn)
        {
            // First perform base validation
            if (!base.ValidateTarget(thing, pawn))
                return false;

            // Then check uninstall-specific requirements
            // Check ownership - if claimable, must be owned by pawn's faction
            if (thing.def.Claimable)
            {
                if (thing.Faction != pawn.Faction)
                    return false;
            }
            // If not claimable, pawn must belong to player faction
            else if (pawn.Faction != Faction.OfPlayer)
                return false;

            return true;
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return "JobGiver_Uninstall_PawnControl";
        }

        #endregion
    }
}