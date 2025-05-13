using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Base class for job givers that don't scan the environment,
    /// but find jobs through other mechanisms like lords or special utilities.
    /// </summary>
    public abstract class JobGiver_Noscan_PawnControl : JobGiver_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        protected override bool RequiresDesignator
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
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        protected override bool RequiresMapZoneorArea => false;

        /// <summary>
        /// Whether this job giver requires player faction specifically (for jobs like deconstruct)
        /// </summary>
        protected override bool RequiresPlayerFaction => true;

        /// <summary>
        /// Whether this construction job requires specific tag for non-humanlike pawns
        /// </summary>
        public override PawnEnumTags RequiredTag => PawnEnumTags.Unknown;

        /// <summary>
        /// Checks if a non-humanlike pawn has the required capabilities for this job giver
        /// </summary>
        protected override bool HasRequiredCapabilities(Pawn pawn)
        {
            // For humanlike pawns, no additional capability checks
            if (pawn.RaceProps.Humanlike)
                return true;

            // For non-humanlike pawns, check for the required mod extension
            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null)
                return false;

            if (modExtension.tags == null || modExtension.tags.Count == 0)
                return false;

            // Check for work type enablement
            if (!Utility_TagManager.WorkTypeEnabled(pawn.def, WorkTag))
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
        public JobGiver_Noscan_PawnControl()
        {
            // Register this job giver type with the cache manager
            InitializeCache<Thing>();
        }

        /// <summary>
        /// Reset the cache - implements IResettableCache
        /// </summary>
        public override void Reset()
        {
            // Use base class Reset implementation, which will use the job giver type
            // to reset only this job giver's cache
            base.Reset();
        }

        #endregion

        #region Core flow

        /// <summary>
        /// Template method for creating a job that handles cache update logic
        /// </summary>
        protected override Job CreateJobFor(Pawn pawn, bool forced)
        {
            if (pawn?.Map == null)
                return null;

            int mapId = pawn.Map.uniqueID;
            int now = Find.TickManager.TicksGame;

            if (!ShouldExecuteNow(mapId))
                return null;

            // Update specialized caches if needed
            if (ShouldUpdateCache(mapId))
            {
                // Call specialized cache update method
                UpdateSpecializedCache(pawn.Map, now);

                // Mark cache as updated via parent class mechanism
                UpdateCache(mapId, pawn.Map);
            }

            // Delegate to derived class to create the job from specialized cache
            return CreateJobFromSpecializedCache(pawn, forced);
        }

        /// <summary>
        /// For no-scan job givers, returns empty since we don't use the standard cache system
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            // Non-scan job givers don't use the standard cache method
            // but they do need to mark the cache as updated
            return Enumerable.Empty<Thing>();
        }

        /// <summary>
        /// Gets targets for this job giver - for backward compatibility with base class
        /// For no-scan job givers, returns empty since we don't use the standard GetTargets approach
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // Non-scan job givers don't use the standard GetTargets method
            return Enumerable.Empty<Thing>();
        }

        /// <summary>
        /// Processes cached targets to find a valid job - not used in no-scan job givers
        /// but required by base class. We delegate to CreateJobFromSpecializedCache instead.
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // Non-scan job givers use CreateJobFromSpecializedCache instead
            return null;
        }

        #endregion

        #region Hooks for derived classes

        /// <summary>
        /// Updates any specialized caches that the non-scan job giver uses
        /// </summary>
        protected abstract void UpdateSpecializedCache(Map map, int currentTick);

        /// <summary>
        /// Creates a job using specialized caches or non-scan methods
        /// </summary>
        protected abstract Job CreateJobFromSpecializedCache(Pawn pawn, bool forced);

        #endregion
    }
}