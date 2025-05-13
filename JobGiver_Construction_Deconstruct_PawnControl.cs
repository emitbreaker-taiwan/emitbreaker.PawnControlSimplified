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
        #region Configuration

        /// <summary>
        /// The designation this job giver targets
        /// </summary>
        protected override DesignationDef TargetDesignation => DesignationDefOf.Deconstruct;

        /// <summary>
        /// The job to create
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.Deconstruct;

        /// <summary>
        /// Human-readable name for debug logging
        /// </summary>
        protected override string DebugName => "Deconstruct";

        /// <summary>
        /// Whether this construction job requires specific tag for non-humanlike pawns
        /// </summary>
        public override PawnEnumTags RequiredTag => PawnEnumTags.AllowWork_Construction;

        /// <summary>
        /// Cache update interval for deconstruct jobs
        /// </summary>
        protected override int CacheUpdateInterval => 160; // ~2.7 seconds

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_Construction_Deconstruct_PawnControl() : base()
        {
            // Base constructor already initializes the cache system
        }

        #endregion

        #region Core Flow

        /// <summary>
        /// Define priority level for deconstruction work
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            // Higher priority than extract tree but lower than most urgent tasks
            return 5.9f;
        }

        #endregion

        #region Target Selection

        /// <summary>
        /// Job-specific cache update method that implements specialized target collection
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            // Use the base implementation that gets removal targets
            return base.UpdateJobSpecificCache(map);
        }

        /// <summary>
        /// Gets all deconstructible targets from the map
        /// </summary>
        protected override IEnumerable<Thing> GetRemovalTargets(Map map)
        {
            // Use the base implementation to get all targets with the Deconstruct designation
            return base.GetRemovalTargets(map);
        }

        #endregion

        #region Thing-Based Helpers

        /// <summary>
        /// Basic validation for construction target things
        /// Override to add deconstruct-specific validation
        /// </summary>
        protected override bool ValidateConstructionTarget(Thing thing, Pawn pawn, bool forced = false)
        {
            // First perform base validation
            if (!base.ValidateConstructionTarget(thing, pawn, forced))
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

        #region Job Creation

        /// <summary>
        /// Creates a construction job for deconstructing buildings
        /// </summary>
        protected override Job CreateConstructionJob(Pawn pawn, bool forced)
        {
            // Use the base implementation that creates jobs for designated targets
            return base.CreateConstructionJob(pawn, forced);
        }

        #endregion

        #region Reset

        /// <summary>
        /// Reset the cache - implements IResettableCache
        /// </summary>
        public override void Reset()
        {
            // Use the centralized cache reset from parent
            base.Reset();
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