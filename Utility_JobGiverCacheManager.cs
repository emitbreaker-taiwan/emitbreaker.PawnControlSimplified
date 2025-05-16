using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using RimWorld;
using Verse;
using Verse.AI;

public static class Utility_JobGiverCacheManager<T> where T : Thing
{
    // Key structure: (Type jobGiverType, int mapId)
    private static readonly Dictionary<(Type, int), List<T>> _targetCache = new Dictionary<(Type, int), List<T>>();
    private static readonly Dictionary<(Type, int), int> _lastUpdateTick = new Dictionary<(Type, int), int>();
    private static readonly Dictionary<(Type, int), Dictionary<T, bool>> _reachabilityCache = new Dictionary<(Type, int), Dictionary<T, bool>>();

    // Registry of all job giver types
    private static readonly HashSet<Type> _registeredJobGivers = new HashSet<Type>();

    private static readonly object _cacheLock = new object();
    private static Dictionary<Type, Dictionary<int, List<T>>> _cacheSystem = new Dictionary<Type, Dictionary<int, List<T>>>();

    // Implement proper type checking before casting

    public static void RegisterJobGiver(Type jobGiverType)
    {
        if (!_registeredJobGivers.Contains(jobGiverType))
        {
            _registeredJobGivers.Add(jobGiverType);
        }
    }

    public static bool NeedsUpdate(Type jobGiverType, int mapId, int interval)
    {
        int ticksGame = Find.TickManager.TicksGame;
        int lastUpdate = GetLastUpdateTick(jobGiverType, mapId);

        // Add stagger based on job giver type to prevent all caches updating at once
        int staggerOffset = Math.Abs(jobGiverType.GetHashCode() % 60);

        return ticksGame - lastUpdate >= interval ||
               (ticksGame + staggerOffset) % interval == 0;
    }

    public static void UpdateCache(Type jobGiverType, int mapId, IEnumerable<T> targets)
    {
        // Limit to 100 targets maximum per job giver
        var limitedTargets = targets.Take(100).ToList();

        var key = (jobGiverType, mapId);
        _targetCache[key] = new List<T>(targets);
        _lastUpdateTick[key] = Find.TickManager.TicksGame;
    }

    public static List<T> GetTargets(Type jobGiverType, int mapId)
    {
        // If cache doesn't exist, initialize it but don't populate yet
        if (!_cacheSystem.TryGetValue(jobGiverType, out var typeCache) ||
            !typeCache.TryGetValue(mapId, out var mapCache)) // Fixed: Removed incorrect 'MapCaches' reference
        {
            return new List<T>();
        }

        return mapCache; // Fixed: Directly return the mapCache as it is already a List<T>

        //lock (_cacheLock)
        //{
        //    if (_cacheSystem.TryGetValue(jobGiverType, out var mapCache) &&
        //        mapCache.TryGetValue(mapId, out var targets))
        //    {
        //        return targets;
        //    }
        //    return new List<T>();
        //}
    }

    private static int GetLastUpdateTick(Type jobGiverType, int mapId)
    {
        var key = (jobGiverType, mapId);
        if (_lastUpdateTick.TryGetValue(key, out int lastUpdate))
        {
            return lastUpdate;
        }
        return 0; // Default to 0 if no update has been recorded
    }

    public static bool TryGetReachabilityResult(Type jobGiverType, int mapId, T target, out bool result)
    {
        var key = (jobGiverType, mapId);
        if (_reachabilityCache.TryGetValue(key, out var mapCache) &&
            mapCache.TryGetValue(target, out result))
        {
            return true;
        }
        result = default;
        return false;
    }

    public static void CacheReachabilityResult(Type jobGiverType, int mapId, T target, bool result)
    {
        var key = (jobGiverType, mapId);
        if (!_reachabilityCache.TryGetValue(key, out var mapCache))
        {
            mapCache = new Dictionary<T, bool>();
            _reachabilityCache[key] = mapCache;
        }
        mapCache[target] = result;
    }

    // Reset a specific job giver's cache for all maps
    public static void ResetJobGiverCache(Type jobGiverType)
    {
        var keysToRemove = _targetCache.Keys.Where(k => k.Item1 == jobGiverType).ToList();
        foreach (var key in keysToRemove)
        {
            _targetCache.Remove(key);
            _lastUpdateTick.Remove(key);
        }

        keysToRemove = _reachabilityCache.Keys.Where(k => k.Item1 == jobGiverType).ToList();
        foreach (var key in keysToRemove)
        {
            _reachabilityCache.Remove(key);
        }
    }

    // Reset all job giver caches for a specific map
    public static void ResetMapCache(int mapId)
    {
        var keysToRemove = _targetCache.Keys.Where(k => k.Item2 == mapId).ToList();
        foreach (var key in keysToRemove)
        {
            _targetCache.Remove(key);
            _lastUpdateTick.Remove(key);
        }

        keysToRemove = _reachabilityCache.Keys.Where(k => k.Item2 == mapId).ToList();
        foreach (var key in keysToRemove)
        {
            _reachabilityCache.Remove(key);
        }
    }

    // Reset all caches completely
    public static void Reset()
    {
        _targetCache.Clear();
        _lastUpdateTick.Clear();
        _reachabilityCache.Clear();
    }
}