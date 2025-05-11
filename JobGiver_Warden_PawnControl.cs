using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Base class for warden job givers with specialized cache management
    /// </summary>
    public abstract class JobGiver_Warden_PawnControl : JobGiver_Scan_PawnControl
    {
        #region Configuration
        protected override string WorkTag => "Warden";
        protected virtual float[] DistanceThresholds => new float[] { 100f, 225f, 625f }; // 10, 15, 25 tiles
        #endregion

        #region Caching
        // Domain-specific caches
        protected static readonly Dictionary<int, List<Pawn>> _prisonerCache = new Dictionary<int, List<Pawn>>();
        protected static readonly Dictionary<int, Dictionary<Pawn, bool>> _prisonerReachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        protected static readonly Dictionary<int, int> _lastWardenCacheUpdate = new Dictionary<int, int>();
        #endregion

        // Specialized prisoner handling methods
        protected virtual bool IsValidPrisonerTarget(Pawn prisoner, Pawn warden) { /* Common warden logic */ }

        // Reset cache helpers
        public static void ResetWardenCache()
        {
            _prisonerCache.Clear();
            _prisonerReachabilityCache.Clear();
            _lastWardenCacheUpdate.Clear();
        }
    }
}