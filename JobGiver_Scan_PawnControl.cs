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