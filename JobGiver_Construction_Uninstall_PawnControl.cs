using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
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

        protected override DesignationDef Designation => DesignationDefOf.Uninstall;

        protected override JobDef RemoveBuildingJob => JobDefOf.Uninstall;

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
            return ExecuteJobGiverWithUninstallValidation(pawn, targets, forced);
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

        #region Uninstall-specific helpers

        /// <summary>
        /// Uninstall-specific job execution logic
        /// </summary>
        private Job ExecuteJobGiverWithUninstallValidation(Pawn pawn, List<Thing> targets, bool forced)
        {
            if (pawn?.Map == null || targets.Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing and target selection
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                targets,
                (thing) => (thing.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Find the best target with additional uninstall-specific validation
            Thing bestTarget = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (thing, p) => ValidateTarget(thing, p) && p.CanReserve(thing, 1, -1, null, forced),
                null  // No need for reachability cache as base class already handles caching
            );

            // Create job if target found
            if (bestTarget != null)
            {
                Job job = JobMaker.MakeJob(RemoveBuildingJob, bestTarget);
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to uninstall {bestTarget.LabelCap}");
                return job;
            }

            return null;
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