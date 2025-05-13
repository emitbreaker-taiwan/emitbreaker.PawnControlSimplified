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

    public static void RegisterJobGiver(Type jobGiverType)
    {
        if (!_registeredJobGivers.Contains(jobGiverType))
        {
            _registeredJobGivers.Add(jobGiverType);
        }
    }

    public static bool NeedsUpdate(Type jobGiverType, int mapId, int updateInterval)
    {
        var key = (jobGiverType, mapId);
        return !_lastUpdateTick.TryGetValue(key, out int lastUpdate) ||
               Find.TickManager.TicksGame - lastUpdate >= updateInterval;
    }

    public static void UpdateCache(Type jobGiverType, int mapId, IEnumerable<T> targets)
    {
        var key = (jobGiverType, mapId);
        _targetCache[key] = new List<T>(targets);
        _lastUpdateTick[key] = Find.TickManager.TicksGame;
    }

    public static List<T> GetTargets(Type jobGiverType, int mapId)
    {
        var key = (jobGiverType, mapId);
        return _targetCache.TryGetValue(key, out var targets) ? targets : new List<T>();
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