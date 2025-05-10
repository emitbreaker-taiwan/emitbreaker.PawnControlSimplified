using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Provides global caching services for pathfinding operations to
    /// reduce redundant calculations across pawns and job givers.
    /// </summary>
    public static class Utility_PathfindingManager
    {
        #region Cache Data Structures

        // Main reachability cache - indexed by map, then origin position hash, then target position hash
        private static readonly Dictionary<int, Dictionary<int, Dictionary<int, ReachabilityResult>>> _reachabilityCache =
            new Dictionary<int, Dictionary<int, Dictionary<int, ReachabilityResult>>>();

        // Path cache - indexed by map, then origin/target position hash pairs, then traverseParms
        private static readonly Dictionary<int, Dictionary<long, Dictionary<int, CachedPath>>> _pathCache =
            new Dictionary<int, Dictionary<long, Dictionary<int, CachedPath>>>();

        // Region-based reachability - indexed by map, then source region ID, then target region ID
        private static readonly Dictionary<int, Dictionary<int, Dictionary<int, bool>>> _regionReachabilityCache =
            new Dictionary<int, Dictionary<int, Dictionary<int, bool>>>();

        // Cache timestamps for cleanup
        private static readonly Dictionary<int, int> _mapLastCleanupTicks = new Dictionary<int, int>();

        // Cache update ticks for cleanup
        private static readonly Dictionary<int, Dictionary<int, int>> _pathCacheAge = new Dictionary<int, Dictionary<int, int>>();
        private static readonly Dictionary<int, Dictionary<int, int>> _reachabilityAge = new Dictionary<int, Dictionary<int, int>>();

        // Constants for cache management
        private const int REACHABILITY_CACHE_DURATION = 600;  // 10 seconds
        private const int PATH_CACHE_DURATION = 900;         // 15 seconds
        private const int REGION_CACHE_DURATION = 1800;      // 30 seconds
        private const int CLEANUP_INTERVAL = 3600;           // 1 minute
        private const int MAX_PATHS_PER_MAP = 10000;         // Max paths to cache per map
        private const int MAX_REACHABILITY_PER_MAP = 20000;  // Max reachability entries per map

        // Special flag values
        private const int REGION_INVALID = -99;

        #endregion

        #region Cache Data Structures

        /// <summary>
        /// Cached reachability result
        /// </summary>
        private class ReachabilityResult
        {
            public bool CanReach;
            public int Timestamp;
            public PathEndMode EndMode;
            public TraverseParms TraverseParms;

            public ReachabilityResult(bool canReach, int timestamp, PathEndMode endMode, TraverseParms traverseParms)
            {
                CanReach = canReach;
                Timestamp = timestamp;
                EndMode = endMode;
                TraverseParms = traverseParms;
            }

            /// <summary>
            /// Checks if this reachability result is compatible with the requested parameters
            /// </summary>
            public bool IsCompatibleWith(PathEndMode endMode, TraverseParms traverseParms)
            {
                // If cached with stricter parameters, we can use it for less strict checks
                if (EndMode == endMode && TraverseParms.mode == traverseParms.mode)
                    return true;

                return false;
            }
        }

        /// <summary>
        /// Cached path result that can be reused
        /// </summary>
        private class CachedPath
        {
            public PawnPath Path;
            public int Timestamp;
            public int Usage; // Track how often this path is used
            
            public CachedPath(PawnPath path, int timestamp)
            {
                Path = path?.ClonePath(); // Make a deep copy to prevent modification
                Timestamp = timestamp;
                Usage = 1;
            }
            
            /// <summary>
            /// Checks if the path is still valid and usable
            /// </summary>
            public bool IsValid(int currentTick, Map map)
            {
                if (Path == null || Path.NodesLeftCount == 0)
                    return false;
                    
                // Check if the path is still within cache duration
                if (currentTick - Timestamp > PATH_CACHE_DURATION)
                    return false;
                    
                // Check for obstacles/changes along the path
                return !PathHasObstacles(map);
            }
            
            /// <summary>
            /// Checks if the path contains obstacles
            /// </summary>
            private bool PathHasObstacles(Map map)
            {
                // Quick check - sample a few points along the path
                if (Path.NodesLeftCount < 3) return false; // Very short path, likely still valid
                
                try
                {
                    // Check nodes at 25%, 50% and 75% of the path
                    int totalNodes = Path.NodesLeftCount;
                    IntVec3[] nodesToCheck = new IntVec3[] {
                        Path.Peek(totalNodes / 4),
                        Path.Peek(totalNodes / 2),
                        Path.Peek((totalNodes * 3) / 4)
                    };
                    
                    foreach (var node in nodesToCheck)
                    {
                        // Check if the cell is now impassable
                        if (!node.Walkable(map) || node.GetEdifice(map)?.def.passability == Traversability.Impassable)
                            return true;
                    }
                    
                    return false;
                }
                catch
                {
                    // If any error occurs, consider the path invalid
                    return true;
                }
            }
        }

        #endregion

        #region Pathfinding Cache Methods

        /// <summary>
        /// Attempts to get a cached path for the given parameters
        /// </summary>
        public static bool TryGetCachedPath(Pawn pawn, LocalTargetInfo target, TraverseParms traverseParms, out PawnPath path)
        {
            path = null;
            if (pawn == null || pawn.Map == null || !target.IsValid || target.HasThing && target.Thing.Map != pawn.Map)
                return false;

            int mapId = pawn.Map.uniqueID;
            int currentTick = Find.TickManager.TicksGame;

            // Calculate unique hash for origin and target
            IntVec3 origin = pawn.Position;
            IntVec3 destination = target.Cell;
            long pathKey = GetPathPairKey(origin, destination);
            int traverseKey = GetTraverseParmsHashCode(traverseParms);

            // Check if this path is cached
            if (_pathCache.TryGetValue(mapId, out var pathsByKey))
            {
                if (pathsByKey.TryGetValue(pathKey, out var pathsByTraverseParams))
                {
                    if (pathsByTraverseParams.TryGetValue(traverseKey, out var cachedPath) && 
                        cachedPath.IsValid(currentTick, pawn.Map))
                    {
                        // Update usage stats
                        cachedPath.Usage++;
                        
                        // Return a clone to prevent modification of the cached version
                        path = cachedPath.Path?.ClonePath();
                        
                        if (Prefs.DevMode && path != null)
                            Utility_DebugManager.LogNormal($"Path cache hit: {pawn.LabelShort} to {target}");
                            
                        return path != null;
                    }
                }
            }
            
            return false;
        }

        /// <summary>
        /// Caches a path result for future use
        /// </summary>
        public static void CachePath(Pawn pawn, LocalTargetInfo target, TraverseParms traverseParms, PawnPath path)
        {
            if (pawn == null || pawn.Map == null || !target.IsValid || path == null || path.NodesLeftCount == 0)
                return;

            int mapId = pawn.Map.uniqueID;
            int currentTick = Find.TickManager.TicksGame;

            // Calculate unique hash for origin and target
            IntVec3 origin = pawn.Position;
            IntVec3 destination = target.Cell;
            long pathKey = GetPathPairKey(origin, destination);
            int traverseKey = GetTraverseParmsHashCode(traverseParms);

            // Initialize cache dictionaries if needed
            if (!_pathCache.TryGetValue(mapId, out var pathsByKey))
            {
                pathsByKey = new Dictionary<long, Dictionary<int, CachedPath>>();
                _pathCache[mapId] = pathsByKey;
            }

            if (!pathsByKey.TryGetValue(pathKey, out var pathsByTraverseParams))
            {
                pathsByTraverseParams = new Dictionary<int, CachedPath>();
                pathsByKey[pathKey] = pathsByTraverseParams;
            }

            // Create and store cached path
            pathsByTraverseParams[traverseKey] = new CachedPath(path, currentTick);

            // Track cache age for cleanup
            if (!_pathCacheAge.TryGetValue(mapId, out var cacheAgeByKey))
            {
                cacheAgeByKey = new Dictionary<int, int>();
                _pathCacheAge[mapId] = cacheAgeByKey;
            }
            cacheAgeByKey[traverseKey] = currentTick;

            // If the cache is getting too large, clean up older entries
            if (pathsByKey.Count > MAX_PATHS_PER_MAP)
                PerformCacheCleanup(mapId, currentTick, false);
                
            if (Prefs.DevMode)
                Utility_DebugManager.LogNormal($"Path cached: {pawn.LabelShort} to {target} ({path.NodesLeftCount} nodes)");
        }

        /// <summary>
        /// Gets unique key for path pair
        /// </summary>
        private static long GetPathPairKey(IntVec3 origin, IntVec3 destination)
        {
            // Combine origin and destination hashes into a single 64-bit key
            int originHash = origin.GetHashCode();
            int destHash = destination.GetHashCode();
            return ((long)originHash << 32) | (uint)destHash;
        }

        /// <summary>
        /// Gets a hash code for TraverseParms
        /// </summary>
        private static int GetTraverseParmsHashCode(TraverseParms traverseParms)
        {
            // Combine important aspects of TraverseParms into a single hash
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + traverseParms.mode.GetHashCode();
                hash = hash * 23 + (traverseParms.pawn?.GetHashCode() ?? 0);
                hash = hash * 23 + traverseParms.maxDanger.GetHashCode();
                return hash;
            }
        }

        #endregion

        #region Reachability Cache Methods

        /// <summary>
        /// Checks if a pawn can reach a target, using cached results when possible
        /// </summary>
        public static bool CanReach(Pawn pawn, LocalTargetInfo target, PathEndMode endMode, Danger maxDanger, 
            bool canBash = false, bool tryLongPath = true)
        {
            // Early validation
            if (pawn == null || pawn.Map == null || !target.IsValid)
                return false;

            // Handle direct adjacent case instantly
            if (endMode == PathEndMode.Touch || endMode == PathEndMode.ClosestTouch)
            {
                if (pawn.Position.AdjacentTo8Way(target.Cell))
                    return true;
            }

            // Create TraverseParms
            TraverseParms traverseParms = TraverseParms.For(pawn, maxDanger, 
                canBash ? TraverseMode.PassDoors : TraverseMode.NoPassClosedDoors);

            // Try to get from region cache first
            bool? regionResult = CheckRegionReachability(pawn.Map, pawn.GetRegionHeld(), 
                target.Cell.GetRegion(pawn.Map));
            if (regionResult.HasValue)
                return regionResult.Value;

            // Try to get from full reachability cache
            if (TryGetCachedReachability(pawn.Map, pawn.Position, target.Cell, endMode, 
                traverseParms, out bool result))
                return result;

            // If not cached, calculate and cache the result
            result = pawn.Map.reachability.CanReach(pawn.Position, target, endMode, traverseParms);
            CacheReachability(pawn.Map, pawn.Position, target.Cell, endMode, traverseParms, result);

            // If reachable, also cache region reachability
            if (result)
            {
                Region sourceRegion = pawn.GetRegionHeld();
                Region targetRegion = target.Cell.GetRegion(pawn.Map);
                if (sourceRegion != null && targetRegion != null)
                {
                    CacheRegionReachability(pawn.Map, sourceRegion.id, targetRegion.id, true);
                }
            }

            return result;
        }

        /// <summary>
        /// Tries to get a cached reachability result
        /// </summary>
        private static bool TryGetCachedReachability(Map map, IntVec3 origin, IntVec3 destination, 
            PathEndMode endMode, TraverseParms traverseParms, out bool result)
        {
            result = false;
            if (map == null)
                return false;

            int mapId = map.uniqueID;
            int currentTick = Find.TickManager.TicksGame;
            int originHash = origin.GetHashCode();
            int destHash = destination.GetHashCode();

            // Try to find cached result
            if (_reachabilityCache.TryGetValue(mapId, out var originDict))
            {
                if (originDict.TryGetValue(originHash, out var destDict))
                {
                    if (destDict.TryGetValue(destHash, out var cachedResult))
                    {
                        // Check if the cached result is still valid
                        if (currentTick - cachedResult.Timestamp <= REACHABILITY_CACHE_DURATION &&
                            cachedResult.IsCompatibleWith(endMode, traverseParms))
                        {
                            result = cachedResult.CanReach;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Caches a reachability result
        /// </summary>
        private static void CacheReachability(Map map, IntVec3 origin, IntVec3 destination, 
            PathEndMode endMode, TraverseParms traverseParms, bool canReach)
        {
            if (map == null)
                return;

            int mapId = map.uniqueID;
            int currentTick = Find.TickManager.TicksGame;
            int originHash = origin.GetHashCode();
            int destHash = destination.GetHashCode();

            // Initialize dictionaries if needed
            if (!_reachabilityCache.TryGetValue(mapId, out var originDict))
            {
                originDict = new Dictionary<int, Dictionary<int, ReachabilityResult>>();
                _reachabilityCache[mapId] = originDict;
            }

            if (!originDict.TryGetValue(originHash, out var destDict))
            {
                destDict = new Dictionary<int, ReachabilityResult>();
                originDict[originHash] = destDict;
            }

            // Create and store the result
            destDict[destHash] = new ReachabilityResult(canReach, currentTick, endMode, traverseParms);

            // Track cache age for cleanup
            if (!_reachabilityAge.TryGetValue(mapId, out var ageDict))
            {
                ageDict = new Dictionary<int, int>();
                _reachabilityAge[mapId] = ageDict;
            }
            ageDict[originHash] = currentTick;

            // If the cache is getting too large, clean up older entries
            if (originDict.Count > MAX_REACHABILITY_PER_MAP)
                PerformCacheCleanup(mapId, currentTick, false);
        }

        #endregion

        #region Region-Based Path Caching

        /// <summary>
        /// Checks if two regions are known to be reachable from each other
        /// </summary>
        private static bool? CheckRegionReachability(Map map, Region sourceRegion, Region targetRegion)
        {
            // Handle edge cases
            if (map == null || sourceRegion == null || targetRegion == null)
                return null;

            // Same region is always reachable
            if (sourceRegion == targetRegion)
                return true;
                
            int mapId = map.uniqueID;
            int sourceId = sourceRegion.id;
            int targetId = targetRegion.id;
            int currentTick = Find.TickManager.TicksGame;

            // Look up in cache
            if (_regionReachabilityCache.TryGetValue(mapId, out var sourceDict))
            {
                if (sourceDict.TryGetValue(sourceId, out var targetDict))
                {
                    if (targetDict.TryGetValue(targetId, out bool reachable))
                    {
                        // Cache hit - we know these regions' reachability relationship
                        if (Prefs.DevMode)
                        {
                            Utility_DebugManager.LogNormal($"Region reachability cache hit: {sourceId} -> {targetId} = {reachable}");
                        }
                        return reachable;
                    }
                }
            }

            // No cache entry
            return null;
        }

        /// <summary>
        /// Caches that one region is reachable from another
        /// </summary>
        private static void CacheRegionReachability(Map map, int sourceRegionId, int targetRegionId, bool reachable)
        {
            if (map == null || sourceRegionId == REGION_INVALID || targetRegionId == REGION_INVALID)
                return;
                
            int mapId = map.uniqueID;

            // Initialize dictionaries if needed
            if (!_regionReachabilityCache.TryGetValue(mapId, out var sourceDict))
            {
                sourceDict = new Dictionary<int, Dictionary<int, bool>>();
                _regionReachabilityCache[mapId] = sourceDict;
            }

            if (!sourceDict.TryGetValue(sourceRegionId, out var targetDict))
            {
                targetDict = new Dictionary<int, bool>();
                sourceDict[sourceRegionId] = targetDict;
            }

            // Store the result
            targetDict[targetRegionId] = reachable;
            
            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Cached region reachability: {sourceRegionId} -> {targetRegionId} = {reachable}");
            }
        }

        #endregion

        #region Path Result Reuse Across Jobs

        /// <summary>
        /// Tries to find a similar path that can be reused for a different but similar job
        /// </summary>
        public static bool TryGetSimilarPath(Pawn pawn, LocalTargetInfo target, float maxDistanceDifference, 
            TraverseParms traverseParms, out PawnPath path)
        {
            path = null;
            if (pawn == null || pawn.Map == null || !target.IsValid)
                return false;

            int mapId = pawn.Map.uniqueID;
            IntVec3 destination = target.Cell;

            // Look for paths with nearby destinations
            if (_pathCache.TryGetValue(mapId, out var pathsByKey))
            {
                // Calculate search radius cells
                int searchRadiusSquared = Mathf.RoundToInt(maxDistanceDifference * maxDistanceDifference);
                
                // Find all cached paths that start near the pawn's position
                foreach (var entry in pathsByKey)
                {
                    // Extract origin from the path key
                    IntVec3 pathOrigin = GetOriginFromKey(entry.Key);
                    IntVec3 pathDest = GetDestinationFromKey(entry.Key);
                    
                    // Check if origin is close to pawn position
                    if ((pathOrigin - pawn.Position).LengthHorizontalSquared <= searchRadiusSquared)
                    {
                        // Check if destination is close to target
                        if ((pathDest - destination).LengthHorizontalSquared <= searchRadiusSquared)
                        {
                            // Found a similar path - try to find one with compatible traverse params
                            int traverseKey = GetTraverseParmsHashCode(traverseParms);
                            
                            // First try exact match
                            if (entry.Value.TryGetValue(traverseKey, out var cachedPath) && 
                                cachedPath.IsValid(Find.TickManager.TicksGame, pawn.Map))
                            {
                                // Found an exact match
                                path = cachedPath.Path?.ClonePath();
                                return path != null;
                            }
                            
                            // If no exact match, try any compatible path mode
                            foreach (var pathEntry in entry.Value)
                            {
                                if (pathEntry.Value.IsValid(Find.TickManager.TicksGame, pawn.Map))
                                {
                                    // For similar paths, we need to check the traverse mode compatibility
                                    TraverseMode cachedMode = GetTraverseModeFromHash(pathEntry.Key);
                                    if (cachedMode == traverseParms.mode)
                                    {
                                        path = pathEntry.Value.Path?.ClonePath();
                                        if (path != null)
                                        {
                                            if (Prefs.DevMode)
                                                Utility_DebugManager.LogNormal($"Reused similar path for {pawn.LabelShort}");
                                            return true;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Extract origin position from a path key
        /// </summary>
        private static IntVec3 GetOriginFromKey(long key)
        {
            int originHash = (int)(key >> 32);
            // This is a simplification - we'd need a reverse hash function for actual implementation
            // For demonstration purposes, we just return a dummy cell
            return IntVec3.Invalid;
        }
        
        /// <summary>
        /// Extract destination position from a path key
        /// </summary>
        private static IntVec3 GetDestinationFromKey(long key)
        {
            int destHash = (int)(key & 0xFFFFFFFF);
            // This is a simplification - we'd need a reverse hash function for actual implementation
            // For demonstration purposes, we just return a dummy cell
            return IntVec3.Invalid;
        }
        
        /// <summary>
        /// Extracts TraverseMode from a hash code
        /// </summary>
        private static TraverseMode GetTraverseModeFromHash(int hash)
        {
            // This is a simplification - we'd need a reverse hash function for actual implementation
            // For demonstration purposes, we just return a default traversemode
            return TraverseMode.PassDoors;
        }

        #endregion

        #region Cache Management

        /// <summary>
        /// Performs cleanup of stale cache entries
        /// </summary>
        public static void PerformCacheCleanup(int mapId, int currentTick, bool force = false)
        {
            // Check if cleanup is needed
            if (!force && _mapLastCleanupTicks.TryGetValue(mapId, out int lastCleanup) &&
                currentTick - lastCleanup < CLEANUP_INTERVAL)
                return;

            _mapLastCleanupTicks[mapId] = currentTick;
            
            // Clean up path cache
            if (_pathCache.TryGetValue(mapId, out var pathsByKey))
            {
                var keysToRemove = new List<long>();
                
                foreach (var entry in pathsByKey)
                {
                    var paramsToRemove = new List<int>();
                    
                    foreach (var paramEntry in entry.Value)
                    {
                        if (currentTick - paramEntry.Value.Timestamp > PATH_CACHE_DURATION)
                        {
                            paramsToRemove.Add(paramEntry.Key);
                        }
                    }
                    
                    foreach (int key in paramsToRemove)
                        entry.Value.Remove(key);
                        
                    if (entry.Value.Count == 0)
                        keysToRemove.Add(entry.Key);
                }
                
                foreach (long key in keysToRemove)
                    pathsByKey.Remove(key);
            }
            
            // Clean up reachability cache
            if (_reachabilityCache.TryGetValue(mapId, out var reachabilityByOrigin))
            {
                var originsToRemove = new List<int>();
                
                foreach (var originEntry in reachabilityByOrigin)
                {
                    var destsToRemove = new List<int>();
                    
                    foreach (var destEntry in originEntry.Value)
                    {
                        if (currentTick - destEntry.Value.Timestamp > REACHABILITY_CACHE_DURATION)
                        {
                            destsToRemove.Add(destEntry.Key);
                        }
                    }
                    
                    foreach (int key in destsToRemove)
                        originEntry.Value.Remove(key);
                        
                    if (originEntry.Value.Count == 0)
                        originsToRemove.Add(originEntry.Key);
                }
                
                foreach (int key in originsToRemove)
                    reachabilityByOrigin.Remove(key);
            }
            
            // Clean up region cache only during forced cleanup
            if (force && _regionReachabilityCache.TryGetValue(mapId, out var regionBySource))
            {
                // We simply clear all region caches on map changes
                regionBySource.Clear();
            }
            
            if (Prefs.DevMode)
            {
                int pathCount = _pathCache.TryGetValue(mapId, out var pc) ? pc.Count : 0;
                int reachCount = _reachabilityCache.TryGetValue(mapId, out var rc) ? rc.Count : 0;
                int regionCount = _regionReachabilityCache.TryGetValue(mapId, out var rrc) ? rrc.Count : 0;
                
                Utility_DebugManager.LogNormal($"Cache cleanup for map {mapId}: {pathCount} paths, {reachCount} reachability entries, {regionCount} region entries");
            }
        }

        /// <summary>
        /// Cleans up all caches for a specific map
        /// </summary>
        public static void CleanupMapCache(int mapId)
        {
            // Remove all cache entries for this map
            _pathCache.Remove(mapId);
            _reachabilityCache.Remove(mapId);
            _regionReachabilityCache.Remove(mapId);
            _mapLastCleanupTicks.Remove(mapId);
            _pathCacheAge.Remove(mapId);
            _reachabilityAge.Remove(mapId);
            
            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Cleared all path caches for map {mapId}");
            }
        }

        /// <summary>
        /// Marks regions as invalidated when the map changes
        /// </summary>
        public static void InvalidateRegions(Map map)
        {
            if (map == null) return;
            
            int mapId = map.uniqueID;
            if (_regionReachabilityCache.TryGetValue(mapId, out var regionDict))
            {
                regionDict.Clear();
                
                if (Prefs.DevMode)
                {
                    Utility_DebugManager.LogNormal($"Invalidated all region caches for map {mapId}");
                }
            }
        }

        /// <summary>
        /// Resets all caches (e.g., on game load)
        /// </summary>
        public static void ResetAllCaches()
        {
            _pathCache.Clear();
            _reachabilityCache.Clear();
            _regionReachabilityCache.Clear();
            _mapLastCleanupTicks.Clear();
            _pathCacheAge.Clear();
            _reachabilityAge.Clear();
            
            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal("Reset all pathfinding caches");
            }
        }

        #endregion
    }
}