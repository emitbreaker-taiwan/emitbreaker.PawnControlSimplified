using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using RimWorld;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Centralized scanner for finding jobs across all job givers
    /// </summary>
    public static class JobGiverScanner
    {
        // Cache of available work types per pawn
        private static readonly Dictionary<int, HashSet<string>> _pawnWorkTypeCache = 
            new Dictionary<int, HashSet<string>>();
            
        // Cache last updated time
        private static readonly Dictionary<int, int> _pawnCacheLastUpdateTick = 
            new Dictionary<int, int>();
            
        // Cache update interval (5 seconds)
        private const int CACHE_UPDATE_INTERVAL = 300;
        
        /// <summary>
        /// Get all work types a pawn can perform
        /// </summary>
        public static HashSet<string> GetAvailableWorkTypes(Pawn pawn)
        {
            if (pawn == null) return new HashSet<string>();

            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null) return new HashSet<string>();

            if (!Utility_TagManager.HasTagSet(pawn.def, modExtension)) return new HashSet<string>();

            if (Utility_TagManager.HasTag(pawn.def, ManagedTags.BlockAllWork)) return new HashSet<string>();

            int currentTick = Find.TickManager.TicksGame;
            int pawnId = pawn.thingIDNumber;
            
            // Check if cache exists and is recent
            if (_pawnWorkTypeCache.TryGetValue(pawnId, out var workTypes) &&
                _pawnCacheLastUpdateTick.TryGetValue(pawnId, out var lastUpdate) &&
                currentTick - lastUpdate < CACHE_UPDATE_INTERVAL)
            {
                return workTypes;
            }
            
            // Create or refresh cache
            var newWorkTypes = new HashSet<string>();
            
            // Collect all work types the pawn can do
            if (pawn.workSettings != null && pawn.workSettings.EverWork)
            {
                foreach (WorkTypeDef workType in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                {
                    if (Utility_JobGiverManager.PawnCanDoWorkType(pawn, workType.defName))
                    {
                        newWorkTypes.Add(workType.defName);
                    }
                }
            }
           
            // Update cache
            _pawnWorkTypeCache[pawnId] = newWorkTypes;
            _pawnCacheLastUpdateTick[pawnId] = currentTick;
            
            return newWorkTypes;
        }
        
        /// <summary>
        /// Clear work type cache for a specific pawn
        /// </summary>
        public static void ClearPawnCache(Pawn pawn)
        {
            if (pawn == null) return;
            
            int pawnId = pawn.thingIDNumber;
            _pawnWorkTypeCache.Remove(pawnId);
            _pawnCacheLastUpdateTick.Remove(pawnId);
        }
        
        /// <summary>
        /// Clear all cached work type data
        /// </summary>
        public static void ClearAllCaches()
        {
            _pawnWorkTypeCache.Clear();
            _pawnCacheLastUpdateTick.Clear();
        }
    }
}