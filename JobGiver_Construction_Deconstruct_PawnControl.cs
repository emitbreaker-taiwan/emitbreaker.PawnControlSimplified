using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
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

        protected override DesignationDef Designation => DesignationDefOf.Deconstruct;

        protected override JobDef RemoveBuildingJob => JobDefOf.Deconstruct;

        // Override debug name for better logging
        protected override string DebugName => "Deconstruct";

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
            return CreateRemovalJob<JobGiver_Construction_Deconstruct_PawnControl>(pawn);
        }

        /// <summary>
        /// Implements the abstract method from JobGiver_Scan_PawnControl to process cached targets
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            if (pawn == null || targets == null || targets.Count == 0)
                return null;

            // The deconstruct-specific job execution logic is already defined in a helper method
            // Reuse it for processing cached targets
            return ExecuteJobGiverWithDeconstructValidation(pawn, targets, forced);
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

        #region Deconstruct-specific helpers

        /// <summary>
        /// Deconstruct-specific job execution logic
        /// </summary>
        private Job ExecuteJobGiverWithDeconstructValidation(Pawn pawn, List<Thing> targets, bool forced)
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

            // Find the best target with additional deconstruct-specific validation
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
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to deconstruct {bestTarget.LabelCap}");
                return job;
            }

            return null;
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