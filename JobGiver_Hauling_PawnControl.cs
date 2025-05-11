using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Base class for hauling job givers with specialized cache management
    /// </summary>
    public abstract class JobGiver_Hauling_PawnControl : JobGiver_Scan_PawnControl
    {
        #region Configuration
        protected override string WorkTag => "Hauling";
        protected virtual float[] DistanceThresholds => new float[] { 225f, 625f, 2500f }; // 15, 25, 50 tiles
        #endregion

        #region Caching
        // Domain-specific caches
        protected static readonly Dictionary<int, List<Thing>> _haulableCache = new Dictionary<int, List<Thing>>();
        protected static readonly Dictionary<int, Dictionary<Thing, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Thing, bool>>();
        protected static readonly Dictionary<int, int> _lastHaulingCacheUpdate = new Dictionary<int, int>();
        #endregion

        #region Utility

        // Specialized hauling methods
        protected virtual bool CanHaulThing(Thing t, Pawn p) { return true;/* Common hauling logic */ }

        // Reset cache helpers
        public static void ResetHaulingCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_haulableCache, _reachabilityCache);
            _lastHaulingCacheUpdate.Clear();
        }

        #endregion
    }
}