using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Manages caches for map-specific data
    /// </summary>
    public static class Utility_MapCacheManager
    {
        // Cache for each map, storing various dictionary types
        private static readonly Dictionary<int, Dictionary<string, object>> _mapCaches = new Dictionary<int, Dictionary<string, object>>();
        
        // Track the last update tick for each cache entry
        private static readonly Dictionary<int, Dictionary<string, int>> _lastUpdateTicks = new Dictionary<int, Dictionary<string, int>>();

        /// <summary>
        /// Gets or creates a map-specific cache of the specified type
        /// </summary>
        /// <typeparam name="TKey">The key type for the dictionary</typeparam>
        /// <typeparam name="TValue">The value type for the dictionary</typeparam>
        /// <param name="mapId">The map ID to get the cache for</param>
        /// <returns>A dictionary cache for the specified map</returns>
        public static Dictionary<TKey, TValue> GetOrCreateMapCache<TKey, TValue>(int mapId)
        {
            // Initialize map dictionary if it doesn't exist
            if (!_mapCaches.TryGetValue(mapId, out Dictionary<string, object> mapCache))
            {
                mapCache = new Dictionary<string, object>();
                _mapCaches[mapId] = mapCache;
            }

            // Generate cache key based on types
            string cacheKey = typeof(TKey).Name + "_" + typeof(TValue).Name;

            // Get or create the specific dictionary type
            if (!mapCache.TryGetValue(cacheKey, out object specificCache))
            {
                specificCache = new Dictionary<TKey, TValue>();
                mapCache[cacheKey] = specificCache;
            }
            else
            {
                // Check if the object is of the expected type before casting
                if (!(specificCache is Dictionary<TKey, TValue>))
                {
                    // If not the expected type, remove the old cache and create a new one
                    Utility_DebugManager.LogWarning($"Cache type mismatch for key {cacheKey} - expected Dictionary<{typeof(TKey).Name}, {typeof(TValue).Name}> but got {specificCache.GetType()}. Creating new cache.");
                    specificCache = new Dictionary<TKey, TValue>();
                    mapCache[cacheKey] = specificCache;
                }
            }

            return (Dictionary<TKey, TValue>)specificCache;
        }

        /// <summary>
        /// Gets the last update tick for a specific cache key
        /// </summary>
        /// <param name="mapId">The map ID</param>
        /// <param name="cacheKey">The cache key</param>
        /// <returns>The last tick this cache was updated, or -1 if never updated</returns>
        public static int GetLastCacheUpdateTick(int mapId, string cacheKey)
        {
            if (!_lastUpdateTicks.TryGetValue(mapId, out Dictionary<string, int> tickMap))
                return -1;

            if (!tickMap.TryGetValue(cacheKey, out int lastTick))
                return -1;

            return lastTick;
        }

        /// <summary>
        /// Sets the last update tick for a specific cache key
        /// </summary>
        /// <param name="mapId">The map ID</param>
        /// <param name="cacheKey">The cache key</param>
        /// <param name="tick">The tick value</param>
        public static void SetLastCacheUpdateTick(int mapId, string cacheKey, int tick)
        {
            if (!_lastUpdateTicks.TryGetValue(mapId, out Dictionary<string, int> tickMap))
            {
                tickMap = new Dictionary<string, int>();
                _lastUpdateTicks[mapId] = tickMap;
            }

            tickMap[cacheKey] = tick;
        }

        /// <summary>
        /// Clears all caches with keys starting with a specific prefix for a map
        /// </summary>
        /// <param name="mapId">The map ID</param>
        /// <param name="keyPrefix">The prefix to match</param>
        public static void ClearPrefixedCaches(int mapId, string keyPrefix)
        {
            if (_mapCaches.TryGetValue(mapId, out var mapCache))
            {
                // Find all keys starting with the prefix
                var keysToRemove = mapCache.Keys
                    .Where(k => k.StartsWith(keyPrefix))
                    .ToList();

                // Remove them from the cache
                foreach (var key in keysToRemove)
                {
                    mapCache.Remove(key);
                }
            }

            if (_lastUpdateTicks.TryGetValue(mapId, out var tickMap))
            {
                // Find all keys starting with the prefix
                var keysToRemove = tickMap.Keys
                    .Where(k => k.StartsWith(keyPrefix))
                    .ToList();

                // Remove them from the update ticks map
                foreach (var key in keysToRemove)
                {
                    tickMap.Remove(key);
                }
            }
        }

        /// <summary>
        /// Clears all caches for a specific map
        /// </summary>
        /// <param name="mapId">The map ID to clear</param>
        public static void ClearMapCache(int mapId)
        {
            if (_mapCaches.ContainsKey(mapId))
            {
                _mapCaches.Remove(mapId);
            }

            if (_lastUpdateTicks.ContainsKey(mapId))
            {
                _lastUpdateTicks.Remove(mapId);
            }
        }

        /// <summary>
        /// Clear all caches
        /// </summary>
        public static void ClearAllCaches()
        {
            _mapCaches.Clear();
            _lastUpdateTicks.Clear();
        }

        /// <summary>
        /// Resets all caches on game load or when requested by other systems
        /// </summary>
        public static void ResetAllCaches()
        {
            ClearAllCaches();

            // Log that caches were cleared
            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal("Reset all map caches");
            }
        }
    }
}