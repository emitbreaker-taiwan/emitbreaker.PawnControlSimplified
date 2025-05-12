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
        /// Whether this job giver requires player faction specifically (for jobs like deconstruct)
        /// </summary>
        protected override bool RequiresPlayerFaction => false;

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

        protected static readonly Dictionary<int, int> _lastCacheTick = new Dictionary<int, int>();
        protected static readonly Dictionary<int, List<Thing>> _cachedTargets = new Dictionary<int, List<Thing>>();

        #endregion

        #region Core flow

        /// <summary>
        /// Creates a job for the given pawn using scan-based target selection
        /// </summary>
        protected override Job CreateJobFor(Pawn pawn, bool forced)
        {
            if (pawn?.Map == null)
                return null;

            int mapId = pawn.Map.uniqueID;
            int now = Find.TickManager.TicksGame;

            if (!ShouldExecuteNow(mapId))
                return null;

            // Update cache if needed
            if (!_lastCacheTick.TryGetValue(mapId, out int last)
                || now - last >= CacheUpdateInterval)
            {
                _lastCacheTick[mapId] = now;
                _cachedTargets[mapId] = new List<Thing>(GetTargets(pawn.Map));
            }

            // Check if we have any targets
            var list = _cachedTargets.TryGetValue(mapId, out var targets) ? targets : null;
            if (list == null || list.Count == 0)
                return null;

            // Derived classes must implement the job creation from targets
            return ProcessCachedTargets(pawn, list, forced);
        }

        #endregion

        #region Hooks for derived classes

        /// <summary>
        /// Gets all potential targets on the given map
        /// </summary>
        protected abstract IEnumerable<Thing> GetTargets(Map map);

        /// <summary>
        /// Creates a job for the pawn using the cached targets
        /// </summary>
        protected abstract Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced);

        #endregion

        #region Cache management

        /// <summary>
        /// Reset the shared base class caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            _lastCacheTick.Clear();
            _cachedTargets.Clear();
            Utility_DebugManager.LogNormal("Reset JobGiver_Scan_PawnControl cache");
        }

        #endregion
    }
}