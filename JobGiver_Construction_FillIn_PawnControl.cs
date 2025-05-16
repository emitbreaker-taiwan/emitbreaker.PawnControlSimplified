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
        /// Override WorkTag to specify "BasicWorker" work type instead of Construction
        /// </summary>
        public override string WorkTag => "Construction";

        /// <summary>
        /// Whether this construction job requires specific tag for non-humanlike pawns
        /// </summary>
        public override PawnEnumTags RequiredTag => PawnEnumTags.AllowWork_Construction;

        /// <summary>
        /// Use FillIn designation
        /// </summary>
        protected override DesignationDef TargetDesignation => DesignationDefOf.FillIn;

        /// <summary>
        /// Use FillIn job
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.FillIn;

        /// <summary>
        /// Override debug name for better logging
        /// </summary>
        protected override string DebugName => "FillIn";

        /// <summary>
        /// Cache update interval - less often since these targets don't change frequently
        /// </summary>
        protected override int CacheUpdateInterval => base.CacheUpdateInterval;

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_Construction_FillIn_PawnControl() : base()
        {
            // Base constructor already initializes the cache system
        }

        #endregion

        #region Core Flow

        /// <summary>
        /// Fill In is a medium priority task
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            return 6.2f;  // Similar priority to other BasicWorker tasks
        }

        #endregion

        #region Target Selection

        /// <summary>
        /// Job-specific cache update method for fill-in targets
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            // Use the base implementation to get all targets with the FillIn designation
            return base.UpdateJobSpecificCache(map);
        }

        /// <summary>
        /// Gets all pit burrows with the FillIn designation
        /// </summary>
        protected override IEnumerable<Thing> GetRemovalTargets(Map map)
        {
            // Use the base implementation but with additional filtering for PitBurrow type
            foreach (Thing thing in base.GetRemovalTargets(map))
            {
                if (thing is PitBurrow)
                {
                    yield return thing;
                }
            }
        }

        #endregion

        #region Faction Validation

        /// <summary>
        /// Determines if the pawn's faction is allowed to perform this work
        /// Can be overridden by derived classes to customize faction rules
        /// </summary>
        protected override bool IsValidFactionForConstruction(Pawn pawn)
        {
            // BasicWorker tasks have different faction requirements than construction
            return Utility_JobGiverManager.IsValidFactionInteraction(null, pawn, RequiresPlayerFaction);
        }

        #endregion

        #region Thing-Based Helpers

        /// <summary>
        /// Basic validation for construction target things
        /// Override to add PitBurrow-specific validation
        /// </summary>
        protected override bool ValidateConstructionTarget(Thing thing, Pawn pawn, bool forced = false)
        {
            // Use base validation from JobGiver_Common_RemoveBuilding_PawnControl
            if (!base.ValidateConstructionTarget(thing, pawn, forced))
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

        #region Job Creation

        /// <summary>
        /// Creates a construction job for filling in pit burrows
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
            return "JobGiver_Construction_FillIn_PawnControl";
        }

        #endregion
    }
}