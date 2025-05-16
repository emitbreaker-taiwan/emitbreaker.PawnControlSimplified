using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Compatibility layer for Utility_MapCacheManager
    /// </summary>
    public static class Utility_MapCacheManager
    {
        // Forward methods to UnifiedCache
        public static Dictionary<TKey, TValue> GetOrCreateMapCache<TKey, TValue>(int mapId) =>
            Utility_UnifiedCache.GetOrCreate(mapId, $"MapCache_{typeof(TKey).Name}_{typeof(TValue).Name}",
                () => new Dictionary<TKey, TValue>(), Utility_UnifiedCache.CachePriority.Normal);

        public static void SetLastCacheUpdateTick(int mapId, string cacheKey, int tick) =>
            Utility_UnifiedCache.Set($"Map_{mapId}_LastUpdate_{cacheKey}", tick);

        public static int GetLastCacheUpdateTick(int mapId, string cacheKey)
        {
            if (Utility_UnifiedCache.TryGetValue($"Map_{mapId}_LastUpdate_{cacheKey}", out int tick))
                return tick;
            return -1;
        }

        public static void ClearAllCaches() => Utility_UnifiedCache.Clear();

        public static void ClearMapCaches(int mapId) => Utility_UnifiedCache.InvalidateMap(mapId);
    }
}