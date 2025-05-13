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
        #region Configuration

        /// <summary>
        /// The designation this job giver targets
        /// </summary>
        protected override DesignationDef TargetDesignation => DesignationDefOf.Uninstall;

        /// <summary>
        /// The job to create
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.Uninstall;

        /// <summary>
        /// Human-readable name for debug logging
        /// </summary>
        protected override string DebugName => "Uninstall";

        /// <summary>
        /// Cache update interval - slightly less often for uninstall tasks
        /// </summary>
        protected override int CacheUpdateInterval => 180; // 3 seconds

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_Construction_Uninstall_PawnControl() : base()
        {
            // Base constructor already initializes the cache system
        }

        #endregion

        #region Core Flow

        /// <summary>
        /// Define priority level for uninstall work
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            // Slightly lower priority than deconstruct
            return 5.8f;
        }

        #endregion

        #region Target Selection

        /// <summary>
        /// Job-specific cache update method that implements specialized target collection
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            // Use the base implementation for uninstall targets
            return base.UpdateJobSpecificCache(map);
        }

        /// <summary>
        /// Gets all targets with uninstall designations
        /// </summary>
        protected override IEnumerable<Thing> GetRemovalTargets(Map map)
        {
            // Use the base implementation to get targets with the Uninstall designation
            return base.GetRemovalTargets(map);
        }

        #endregion

        #region Thing-Based Helpers

        /// <summary>
        /// Basic validation for construction target things
        /// Override to add uninstall-specific validation
        /// </summary>
        protected override bool ValidateConstructionTarget(Thing thing, Pawn pawn, bool forced = false)
        {
            // First perform base validation from parent class
            if (!base.ValidateConstructionTarget(thing, pawn, forced))
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

        #region Job Creation

        /// <summary>
        /// Creates a construction job for uninstalling buildings
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
            // Use centralized cache reset from parent
            base.Reset();
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