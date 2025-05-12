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
        protected override PawnEnumTags RequiredTag => PawnEnumTags.Unknown;

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

            if (modExtension.tags.Contains(PawnEnumTags.BlockWork_Construction.ToString()))
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

        #region Caching

        /// <summary>
        /// Last update tick for caches
        /// </summary>
        protected static readonly Dictionary<int, int> _lastCacheUpdateTick = new Dictionary<int, int>();

        #endregion

        #region Core flow

        /// <summary>
        /// Creates a job without scanning for targets, using specialized non-scan methods
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
            if (!_lastCacheUpdateTick.TryGetValue(mapId, out int last)
                || now - last >= CacheUpdateInterval)
            {
                _lastCacheUpdateTick[mapId] = now;
                UpdateSpecializedCache(pawn.Map, now);
            }

            // Delegate to derived class to create the job from specialized cache
            return CreateJobFromSpecializedCache(pawn, forced);
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

        #region Cache management

        /// <summary>
        /// Reset all caches for this class
        /// </summary>
        public static void ResetCache()
        {
            _lastCacheUpdateTick.Clear();
        }

        #endregion
    }
}