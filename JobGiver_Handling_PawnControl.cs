using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Base class for animal handling job givers with specialized cache management
    /// </summary>
    public abstract class JobGiver_Handling_PawnControl : JobGiver_Scan_PawnControl
    {
        #region Configuration

        protected override string WorkTag => "Handling";
        protected virtual float[] DistanceThresholds => new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles
        
        #endregion

        #region Caching
        // Domain-specific caches
        protected static readonly Dictionary<int, List<Pawn>> _animalCache = new Dictionary<int, List<Pawn>>();
        protected static readonly Dictionary<int, Dictionary<Pawn, bool>> _animalReachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        protected static readonly Dictionary<int, int> _lastHandlingCacheUpdate = new Dictionary<int, int>();
        #endregion

        #region Utility

        // Specialized animal handling methods
        protected virtual bool IsValidAnimalTarget(Pawn animal, Pawn handler) { return true;/* Common handling logic */ }

        protected virtual bool CanHandleAnimal(Pawn animal, Pawn handler)
        {
            return true; // Check if the animal is reachable and can be handled by the pawn
        }

        // Reset cache helpers
        public static void ResetHandlingCache()
        {
            _animalCache.Clear();
            _animalReachabilityCache.Clear();
            _lastHandlingCacheUpdate.Clear();
        }

        #endregion
    }
}