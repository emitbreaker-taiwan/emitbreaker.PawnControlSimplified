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
        public override string WorkTag => "BasicWorker";

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

        /// <summary>
        /// Cache update interval - flick targets don't change often
        /// </summary>
        protected override int CacheUpdateInterval => 300; // Every 5 seconds

        /// <summary>
        /// Distance thresholds for flick targets - typically indoor structures
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        #endregion

        #region Core flow

        /// <summary>
        /// Checks if the map meets requirements for this job giver
        /// </summary>
        protected override bool AreMapRequirementsMet(Pawn pawn)
        {
            // Quick check for flick designations before calling base implementation
            if (pawn?.Map == null || !pawn.Map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.Flick))
                return false;

            return base.AreMapRequirementsMet(pawn);
        }

        #endregion

        #region Target selection

        /// <summary>
        /// Gets all targets with flick designations on the map
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // Use the efficient base implementation that filters by designation
            return base.GetTargets(map);
        }

        /// <summary>
        /// Additional validation for flick targets
        /// </summary>
        protected override bool IsValidTarget(Thing thing, Pawn worker)
        {
            // Use base validation first
            if (!base.IsValidTarget(thing, worker))
                return false;

            // Verify that the target has a CompFlickable component
            if (thing.TryGetComp<CompFlickable>() == null)
                return false;

            // Check if the pawn can operate the target
            if (thing.def.building != null && thing.def.building.wantsHopperAdjacent && !HasAdjacentHopper(thing))
                return false;

            return true;
        }

        /// <summary>
        /// Checks if a hopper is adjacent to the given Thing.
        /// </summary>
        private bool HasAdjacentHopper(Thing thing)
        {
            foreach (IntVec3 cell in GenAdj.CellsAdjacent8Way(thing))
            {
                Building building = thing.Map.thingGrid.ThingAt<Building>(cell);
                if (building != null && building.def.building != null && building.def.building.isHopper)
                {
                    return true;
                }
            }
            return false;
        }

        #endregion

        #region Job creation

        /// <summary>
        /// Creates a job for the specified target
        /// </summary>
        protected override Job CreateJobForTarget(Thing target)
        {
            // Create flick job with proper parameters
            Job job = JobMaker.MakeJob(WorkJobDef, target);

            // Add any flick-specific job parameters if needed

            return job;
        }

        #endregion
    }
}