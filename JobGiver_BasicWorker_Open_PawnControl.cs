using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Enhanced JobGiver that assigns container opening jobs to eligible pawns.
    /// </summary>
    public class JobGiver_BasicWorker_Open_PawnControl : JobGiver_BasicWorker_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Use Hauling work tag
        /// </summary>
        public override string WorkTag => "BasicWorker";

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "Open";

        /// <summary>
        /// The designation this job giver targets
        /// </summary>
        protected override DesignationDef TargetDesignation => DesignationDefOf.Open;

        /// <summary>
        /// The job to create
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.Open;

        /// <summary>
        /// This job requires player faction
        /// </summary>
        protected override bool RequiresPlayerFaction => true;

        /// <summary>
        /// Cache update interval - open targets don't change often
        /// </summary>
        protected override int CacheUpdateInterval => 240; // Every 4 seconds

        /// <summary>
        /// Distance thresholds for open targets - typically indoor objects
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_BasicWorker_Open_PawnControl() : base()
        {
            // Base constructor already initializes the cache system
        }

        #endregion

        #region Core flow

        /// <summary>
        /// Checks if the map meets requirements for this job giver
        /// </summary>
        protected override bool AreMapRequirementsMet(Pawn pawn)
        {
            // Quick check for open designations before calling base implementation
            if (pawn?.Map == null || !pawn.Map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.Open))
                return false;

            return base.AreMapRequirementsMet(pawn);
        }

        #endregion

        #region Target selection

        /// <summary>
        /// Gets all targets with open designations on the map
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // Use the efficient base implementation that filters by designation
            return base.GetTargets(map);
        }

        /// <summary>
        /// Additional validation for open targets
        /// </summary>
        protected override bool IsValidTarget(Thing thing, Pawn worker)
        {
            // Use base validation first
            if (!base.IsValidTarget(thing, worker))
                return false;

            // Specific validation for open targets could be added here if needed
            return true;
        }

        #endregion

        #region Job creation

        /// <summary>
        /// Creates a job for the specified target
        /// </summary>
        protected override Job CreateJobForTarget(Thing target)
        {
            // Create open job with proper parameters
            Job job = JobMaker.MakeJob(WorkJobDef, target);

            // Add any open-specific job parameters if needed

            return job;
        }

        #endregion

        #region Reset

        /// <summary>
        /// Reset the cache - implements IResettableCache
        /// </summary>
        public override void Reset()
        {
            base.Reset();
        }

        #endregion
    }
}