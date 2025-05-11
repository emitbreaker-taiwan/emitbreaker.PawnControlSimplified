using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Abstract base class for all building removal JobGivers in PawnControl.
    /// This allows non-humanlike pawns to remove buildings with appropriate designation.
    /// </summary>
    public abstract class JobGiver_Common_RemoveBuilding_PawnControl : JobGiver_Scan_PawnControl
    {
        #region Configuration

        // Define distance thresholds for bucketing
        protected static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        // Must be implemented by subclasses to specify which designation to target
        protected abstract DesignationDef Designation { get; }

        // Must be implemented by subclasses to specify which job to use for removal
        protected abstract JobDef RemoveBuildingJob { get; }

        #endregion

        #region Overrides

        // Override WorkTag to specify "Construction" work type
        protected override string WorkTag => "Construction";

        // Override cache interval - slightly longer than default since designations don't change as frequently
        protected override int CacheUpdateInterval => 180; // 3 seconds

        // Override debug name for better logging
        protected override string DebugName => $"RemoveBuilding({Designation?.defName ?? "null"})";

        // Override GetTargets to provide buildings that need removal
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null)
                return Enumerable.Empty<Thing>();

            // Find all designated things for removal
            var targets = new List<Thing>();
            var designations = map.designationManager.SpawnedDesignationsOfDef(Designation);
            foreach (Designation designation in designations)
            {
                Thing thing = designation.target.Thing;
                if (thing != null && thing.Spawned)
                {
                    targets.Add(thing);

                    // Limit collection size for performance
                    if (targets.Count >= 100)
                        break;
                }
            }

            return targets;
        }

        // Override TryGiveJob to directly implement job creation logic
        protected override Job TryGiveJob(Pawn pawn)
        {
            return CreateRemovalJob<JobGiver_Common_RemoveBuilding_PawnControl>(pawn);
        }

        #endregion

        #region Validation helpers

        // Add a helper method for target validation
        protected virtual bool ValidateTarget(Thing thing, Pawn pawn)
        {
            // IMPORTANT: Check faction interaction validity first
            if (!Utility_JobGiverManager.IsValidFactionInteraction(thing, pawn, requiresDesignator: true))
                return false;

            // Skip if no longer valid
            if (thing == null || thing.Destroyed || !thing.Spawned)
                return false;

            // Skip if no longer designated
            if (thing.Map.designationManager.DesignationOn(thing, Designation) == null)
                return false;

            // Check for timed explosives - avoid removing things about to explode
            CompExplosive explosive = thing.TryGetComp<CompExplosive>();
            if (explosive != null && explosive.wickStarted)
                return false;

            // Skip if forbidden or unreachable
            if (thing.IsForbidden(pawn) ||
                !pawn.CanReserve(thing, 1, -1) ||
                !pawn.CanReach(thing, PathEndMode.Touch, Danger.Some))
                return false;

            return true;
        }

        #endregion

        #region Common Helpers

        /// <summary>
        /// Generic helper method to create a building removal job that can be used by all subclasses
        /// </summary>
        /// <typeparam name="T">The specific target type to use with JobGiverManager</typeparam>
        /// <param name="pawn">The pawn that will perform the removal job</param>
        /// <param name="workTag">The work tag to use for eligibility checks</param>
        /// <returns>A job to remove a designated building, or null if no valid job could be created</returns>
        protected Job CreateRemovalJob<T>(Pawn pawn) where T : JobGiver_Common_RemoveBuilding_PawnControl
        {
            // Use the StandardTryGiveJob pattern with inline job creation logic
            return Utility_JobGiverManager.StandardTryGiveJob<T>(
                pawn,
                WorkTag,
                (p, forced) => {
                    if (p?.Map == null)
                        return null;

                    // Get all targets from the GetTargets method
                    List<Thing> targets = GetTargets(p.Map).ToList();
                    if (targets.Count == 0)
                        return null;

                    // Use JobGiverManager for distance bucketing
                    var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                        p,
                        targets,
                        (thing) => (thing.Position - p.Position).LengthHorizontalSquared,
                        DISTANCE_THRESHOLDS
                    );

                    // Find the best target to remove using the ValidateTarget method
                    Thing bestTarget = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                        buckets,
                        p,
                        (thing, validator) => ValidateTarget(thing, validator) && p.CanReserve(thing, 1, -1, null, forced),
                        null  // No need for reachability cache as we check in ValidateTarget
                    );

                    // Create job if target found
                    if (bestTarget != null)
                    {
                        Job job = JobMaker.MakeJob(RemoveBuildingJob, bestTarget);
                        Utility_DebugManager.LogNormal($"{p.LabelShort} created job to {RemoveBuildingJob.defName} {bestTarget.LabelCap}");
                        return job;
                    }

                    return null;
                },
                debugJobDesc: DebugName);
        }

        /// <summary>
        /// Helper method that executes the core job creation logic separately from StandardTryGiveJob.
        /// Used by subclasses that need to customize the job after it's created.
        /// </summary>
        protected Job ExecuteJobGiverInternal(Pawn pawn, List<Thing> targets)
        {
            if (pawn?.Map == null || targets.Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                targets,
                (thing) => (thing.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Find the best target to remove using the ValidateTarget method
            Thing bestTarget = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                ValidateTarget,
                null
            );

            // Create job if target found
            if (bestTarget != null)
            {
                Job job = JobMaker.MakeJob(RemoveBuildingJob, bestTarget);
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to {RemoveBuildingJob.defName} {bestTarget.LabelCap}");
                return job;
            }

            return null;
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return $"JobGiver_RemoveBuilding_PawnControl({Designation?.defName ?? "null"})";
        }

        #endregion
    }
}