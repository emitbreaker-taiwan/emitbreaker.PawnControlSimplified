using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Manages cached work permissions to avoid redundant calculations
    /// </summary>
    public static class Utility_WorkPermissionManager
    {
        #region Cache Data Structures

        // Cache structure: Map ID → Pawn → Work Type → Permission
        private static readonly Dictionary<int, Dictionary<Pawn, Dictionary<string, bool>>> _workPermissionCache = new Dictionary<int, Dictionary<Pawn, Dictionary<string, bool>>>();

        // Last tick when each map's cache was updated
        private static readonly Dictionary<int, int> _lastCacheUpdateTick = new Dictionary<int, int>();

        // Default cache invalidation time (200 ticks = ~3.3 seconds)
        private const int CACHE_INVALIDATION_INTERVAL = 200;

        // Cache for non-player work type permissions
        private static readonly Dictionary<string, bool> _nonPlayerWorkTypeCache = new Dictionary<string, bool>();

        #endregion

        #region Public API

        /// <summary>
        /// Checks if a pawn can do a specific work type, using cached results when available
        /// </summary>
        /// <param name="pawn">The pawn to check</param>
        /// <param name="workTag">The work type to check (e.g., "Warden", "Doctor", "Construction")</param>
        /// <returns>True if the pawn can do the work type</returns>
        public static bool CanDoWorkType(Pawn pawn, string workTag)
        {
            if (pawn == null || string.IsNullOrEmpty(workTag))
                return false;

            int mapId = pawn.Map?.uniqueID ?? -1;
            
            // If not on a map or dead, calculate directly without caching
            if (mapId < 0 || pawn.Dead)
                return CalculateWorkPermission(pawn, workTag);

            // Check for stale cache and refresh if needed
            EnsureCacheIsValid(mapId);

            // Try to get from cache
            if (_workPermissionCache.TryGetValue(mapId, out var pawnDict) &&
                pawnDict.TryGetValue(pawn, out var workDict) &&
                workDict.TryGetValue(workTag, out bool result))
            {
                return result;
            }

            // Calculate and store in cache
            bool canWork = CalculateWorkPermission(pawn, workTag);
            StoreInCache(pawn, workTag, canWork);
            
            return canWork;
        }

        /// <summary>
        /// Reset the cache for a specific pawn
        /// </summary>
        public static void ResetCacheForPawn(Pawn pawn)
        {
            if (pawn?.Map == null) return;
            
            int mapId = pawn.Map.uniqueID;
            
            if (_workPermissionCache.TryGetValue(mapId, out var pawnDict))
            {
                if (pawnDict.ContainsKey(pawn))
                {
                    pawnDict.Remove(pawn);
                    if (Prefs.DevMode)
                        Utility_DebugManager.LogNormal($"Reset work permissions cache for {pawn.LabelShort}");
                }
            }
        }

        /// <summary>
        /// Reset the entire permissions cache
        /// </summary>
        public static void ResetAllCaches()
        {
            _workPermissionCache.Clear();
            _lastCacheUpdateTick.Clear();
            
            if (Prefs.DevMode)
                Utility_DebugManager.LogNormal("Reset all work permissions caches");
        }

        /// <summary>
        /// Pre-compute work permissions for commonly used work types for a pawn
        /// </summary>
        public static void PrecomputeCommonWorkPermissions(Pawn pawn)
        {
            if (pawn?.Map == null) return;
            
            // List of common work types that should be pre-computed
            string[] commonWorkTypes = new[] { 
                "Warden", "Doctor", "Patient", "Firefighter", "Construction", 
                "Growing", "Mining", "Hauling", "Cleaning" 
            };

            foreach (string workType in commonWorkTypes)
            {
                // This will automatically calculate and cache
                CanDoWorkType(pawn, workType);
            }
        }

        #endregion

        #region Cache Management

        /// <summary>
        /// Checks if the cache for a map is still valid, refreshes if not
        /// </summary>
        private static void EnsureCacheIsValid(int mapId)
        {
            int currentTick = Find.TickManager.TicksGame;
            
            // Initialize or check last update time
            if (!_lastCacheUpdateTick.TryGetValue(mapId, out int lastUpdate) || 
                currentTick > lastUpdate + CACHE_INVALIDATION_INTERVAL)
            {
                // Cache is stale, refresh it
                RefreshCacheForMap(mapId);
                _lastCacheUpdateTick[mapId] = currentTick;
            }
        }

        /// <summary>
        /// Refreshes the cache for an entire map
        /// </summary>
        private static void RefreshCacheForMap(int mapId)
        {
            // Clear existing cache for this map
            _workPermissionCache.Remove(mapId);
            
            // Initialize empty cache structure
            _workPermissionCache[mapId] = new Dictionary<Pawn, Dictionary<string, bool>>();
            
            // Find the map
            Map map = Find.Maps.Find(m => m.uniqueID == mapId);
            if (map == null) return;

            // Pre-compute for pawns that are likely to need it
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn.Faction == Faction.OfPlayer || 
                    pawn.HostFaction == Faction.OfPlayer ||
                    (pawn.Faction != null && !pawn.Faction.HostileTo(Faction.OfPlayer)))
                {
                    PrecomputeCommonWorkPermissions(pawn);
                }
            }
        }

        /// <summary>
        /// Store a permission result in the cache
        /// </summary>
        private static void StoreInCache(Pawn pawn, string workTag, bool permission)
        {
            if (pawn?.Map == null) return;
            
            int mapId = pawn.Map.uniqueID;
            
            // Ensure the map dictionary exists
            if (!_workPermissionCache.TryGetValue(mapId, out var pawnDict))
            {
                pawnDict = new Dictionary<Pawn, Dictionary<string, bool>>();
                _workPermissionCache[mapId] = pawnDict;
            }
            
            // Ensure the pawn dictionary exists
            if (!pawnDict.TryGetValue(pawn, out var workDict))
            {
                workDict = new Dictionary<string, bool>();
                pawnDict[pawn] = workDict;
            }
            
            // Store the permission
            workDict[workTag] = permission;
        }

        #endregion

        #region Permission Calculation

        /// <summary>
        /// Calculate if a pawn can perform a specific work type
        /// </summary>
        private static bool CalculateWorkPermission(Pawn pawn, string workTag)
        {
            if (!Utility_Common.PawnChecker(pawn) || string.IsNullOrEmpty(workTag))
                return false;

            var modExtension = Utility_UnifiedCache.GetModExtension(pawn.def);
            if (modExtension == null)
                return false;

            if (!Utility_ThinkTreeManager.HasAllowWorkTag(pawn))
                return false;

            // For player faction pawns, check work tab settings
            if (pawn.Faction == Faction.OfPlayer)
            {
                // Check tag-based permissions first
                if (!Utility_TagManager.WorkTypeSettingEnabled(pawn, workTag))
                    return false;
            }

            // Finally check mod-extension or global-state rules
            return Utility_JobGiverManager.IsEligibleForSpecializedJobGiver(pawn, workTag);
        }

        /// <summary>
        /// Checks if non-player pawns can do work of a specific type
        /// by examining requirements of the job giver
        /// </summary>
        public static bool CanNonPlayerPawnDoWorkType(object jobGiver, string workTag)
        {
            // Try to get from cache first
            if (_nonPlayerWorkTypeCache.TryGetValue(workTag, out bool canDo))
                return canDo;

            // Default to true (permissive)
            canDo = true;

            try
            {
                // If jobGiver is provided and is a JobGiver_PawnControl instance, use it directly
                if (jobGiver is JobGiver_PawnControl pawnControlJobGiver)
                {
                    // Check if this job giver has player-specific requirements
                    bool requiresDesignator = pawnControlJobGiver.RequiresDesignator;
                    bool requiresMapZoneorArea = pawnControlJobGiver.RequiresMapZoneorArea;
                    bool requiresPlayerFaction = pawnControlJobGiver.RequiresPlayerFaction;

                    // If the job giver requires player-specific elements, block non-player pawns
                    if (requiresDesignator || requiresMapZoneorArea || requiresPlayerFaction)
                    {
                        canDo = false;

                        if (Prefs.DevMode)
                        {
                            Utility_DebugManager.LogNormal(
                                $"JobGiver {pawnControlJobGiver.GetType().Name} blocks non-player pawns from {workTag} work: " +
                                $"RequiresDesignator={requiresDesignator}, " +
                                $"RequiresMapZoneorArea={requiresMapZoneorArea}, " +
                                $"RequiresPlayerFaction={requiresPlayerFaction}");
                        }
                    }
                }
                else
                {
                    // No valid job giver provided
                    canDo = false;
                    if (Prefs.DevMode)
                    {
                        Utility_DebugManager.LogNormal($"No valid JobGiver_PawnControl provided for work type {workTag}, defaulting to restricted for non-player pawns");
                    }
                }
            }
            catch (Exception ex)
            {
                canDo = false;
                Utility_DebugManager.LogError($"Error in CanNonPlayerPawnDoWorkType for {workTag}: {ex.Message}");
            }

            // Store in cache and return
            _nonPlayerWorkTypeCache[workTag] = canDo;
            return canDo;
        }

        #endregion
    }
}