using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Base class for job givers that scan the environment for targets.
    /// Provides caching of target lists for efficient job creation.
    /// </summary>
    public abstract class JobGiver_Scan_PawnControl : JobGiver_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        public override bool RequiresMapZoneorArea => false;

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        public override bool RequiresDesignator
        {
            get
            {
                if (TargetDesignation != null)
                    return true;

                if (RequiresMapZoneorArea)
                    return true;

                return false;
            }
        }

        /// <summary>
        /// Whether this job giver requires player faction specifically (for jobs like deconstruct)
        /// </summary>
        public override bool RequiresPlayerFaction => false;

        /// <summary>
        /// Whether this construction job requires specific tag for non-humanlike pawns
        /// </summary>
        public override PawnEnumTags RequiredTag => PawnEnumTags.Unknown;

        /// <summary>
        /// Checks if a non-humanlike pawn has the required capabilities for this job giver
        /// </summary>
        protected override bool HasRequiredCapabilities(Pawn pawn)
        {
            // For non-humanlike pawns, check for the required mod extension
            var modExtension = Utility_UnifiedCache.GetModExtension(pawn.def);
            if (modExtension == null)
                return false;

            // For humanlike pawns, no additional capability checks
            if (pawn.RaceProps.Humanlike)
                return true;

            if (modExtension.tags == null || modExtension.tags.Count == 0)
                return false;

            // Check for work type enablement
            if (!Utility_TagManager.IsWorkEnabled(pawn, WorkTag))
                return false;

            // Allow if pawn has the AllowAllWork tag
            if (modExtension.tags.Contains(PawnEnumTags.AllowAllWork.ToString()))
                return true;

            if (modExtension.tags.Contains(PawnEnumTags.BlockAllWork.ToString()))
                return false;

            // Check for specific required tag if specified
            if (RequiredTag != PawnEnumTags.Unknown && !modExtension.tags.Contains(RequiredTag.ToString()))
                return false;

            if (modExtension.tags.Contains(ManagedTags.BlockWorkPrefix + WorkTag))
                return false;

            return true;
        }

        /// <summary>
        /// The designation type this job giver handles
        /// </summary>
        protected override DesignationDef TargetDesignation => null;

        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected override JobDef WorkJobDef => null;

        #endregion

        #region Cache System

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_Scan_PawnControl()
        {
            // Initialize the cache using the type-keyed cache manager
            InitializeCache<Thing>();
        }

        #endregion

        #region Core flow

        /// <summary>
        /// Template method for creating a job that handles cache update logic
        /// </summary>
        protected override Job CreateJobFor(Pawn pawn, bool forced)
        {
            int mapId = pawn.Map.uniqueID;

            if (!ShouldExecuteNow(mapId))
                return null;

            return base.CreateJobFor(pawn, forced);
        }

        /// <summary>
        /// Job-specific cache update method - delegates to GetTargets
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            return GetTargets(map);
        }

        #endregion

        #region Hooks for derived classes

        /// <summary>
        /// Gets all potential targets on the given map
        /// </summary>
        protected abstract override IEnumerable<Thing> GetTargets(Map map);

        /// <summary>
        /// Processes cached targets to find a valid job
        /// </summary>
        protected abstract override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced);

        #endregion
    }
}