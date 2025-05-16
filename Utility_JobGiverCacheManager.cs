using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace emitbreaker.PawnControl
{
    public static class Utility_JobGiverCacheManager<T> where T : Thing
    {
        #region Data Structures

        // Work tag-based caching systems
        // Key structure: (string workTag, int mapId)
        private static readonly Dictionary<(string, int), List<T>> _workTagTargetCache = new Dictionary<(string, int), List<T>>();
        private static readonly Dictionary<(string, int), int> _workTagLastUpdateTick = new Dictionary<(string, int), int>();
        private static readonly Dictionary<(string, int), Dictionary<T, bool>> _workTagReachabilityCache = new Dictionary<(string, int), Dictionary<T, bool>>();

        // Registry of all work tags
        private static readonly HashSet<string> _registeredWorkTags = new HashSet<string>();

        // Legacy caching systems (for backward compatibility)
        // Key structure: (Type jobGiverType, int mapId)
        private static readonly Dictionary<(Type, int), List<T>> _targetCache = new Dictionary<(Type, int), List<T>>();
        private static readonly Dictionary<(Type, int), int> _lastUpdateTick = new Dictionary<(Type, int), int>();
        private static readonly Dictionary<(Type, int), Dictionary<T, bool>> _reachabilityCache = new Dictionary<(Type, int), Dictionary<T, bool>>();

        // Registry of all job giver types
        private static readonly HashSet<Type> _registeredJobGivers = new HashSet<Type>();

        // Thread safety
        private static readonly object _cacheLock = new object();

        #endregion

        #region Work Tag Registration

        /// <summary>
        /// Registers a work tag for caching
        /// </summary>
        public static void RegisterWorkTag(string workTag)
        {
            if (string.IsNullOrEmpty(workTag))
                return;

            if (!_registeredWorkTags.Contains(workTag))
            {
                _registeredWorkTags.Add(workTag);
            }
        }

        /// <summary>
        /// Registers a job giver type for caching (legacy method)
        /// </summary>
        public static void RegisterJobGiver(Type jobGiverType)
        {
            if (jobGiverType == null)
                return;

            if (!_registeredJobGivers.Contains(jobGiverType))
            {
                _registeredJobGivers.Add(jobGiverType);

                // Also register corresponding work tag if available
                string workTag = Utility_JobGiverManager.GetWorkTagForJobGiverType(jobGiverType);
                if (!string.IsNullOrEmpty(workTag))
                {
                    RegisterWorkTag(workTag);
                }
            }
        }

        #endregion

        #region Cache Update Logic

        /// <summary>
        /// Checks if a work tag cache needs updating
        /// </summary>
        public static bool NeedsWorkTagUpdate(string workTag, int mapId, int interval)
        {
            if (string.IsNullOrEmpty(workTag))
                return true;

            int ticksGame = Find.TickManager.TicksGame;
            int lastUpdate = GetWorkTagLastUpdateTick(workTag, mapId);

            // Add stagger based on work tag to prevent all caches updating at once
            int staggerOffset = Math.Abs(workTag.GetHashCode() % 60);

            return ticksGame - lastUpdate >= interval ||
                   (ticksGame + staggerOffset) % interval == 0;
        }

        /// <summary>
        /// Checks if a job giver cache needs updating (legacy method)
        /// </summary>
        public static bool NeedsUpdate(Type jobGiverType, int mapId, int interval)
        {
            if (jobGiverType == null)
                return true;

            // Try to use work tag-based method if available
            string workTag = Utility_JobGiverManager.GetWorkTagForJobGiverType(jobGiverType);
            if (!string.IsNullOrEmpty(workTag))
            {
                return NeedsWorkTagUpdate(workTag, mapId, interval);
            }

            // Legacy fallback
            int ticksGame = Find.TickManager.TicksGame;
            int lastUpdate = GetLastUpdateTick(jobGiverType, mapId);

            // Add stagger based on job giver type to prevent all caches updating at once
            int staggerOffset = Math.Abs(jobGiverType.GetHashCode() % 60);

            return ticksGame - lastUpdate >= interval ||
                   (ticksGame + staggerOffset) % interval == 0;
        }

        /// <summary>
        /// Updates the target cache for a work tag
        /// </summary>
        public static void UpdateWorkTagCache(string workTag, int mapId, IEnumerable<T> targets)
        {
            if (string.IsNullOrEmpty(workTag))
                return;

            // Limit to 100 targets maximum per work tag
            var limitedTargets = targets.Take(100).ToList();

            var key = (workTag, mapId);
            _workTagTargetCache[key] = new List<T>(limitedTargets);
            _workTagLastUpdateTick[key] = Find.TickManager.TicksGame;
        }

        /// <summary>
        /// Updates the target cache for a job giver type (legacy method)
        /// </summary>
        public static void UpdateCache(Type jobGiverType, int mapId, IEnumerable<T> targets)
        {
            if (jobGiverType == null)
                return;

            // Also update the work tag cache if available
            string workTag = Utility_JobGiverManager.GetWorkTagForJobGiverType(jobGiverType);
            if (!string.IsNullOrEmpty(workTag))
            {
                UpdateWorkTagCache(workTag, mapId, targets);
            }

            // Legacy update
            // Limit to 100 targets maximum per job giver
            var limitedTargets = targets.Take(100).ToList();

            var key = (jobGiverType, mapId);
            _targetCache[key] = new List<T>(limitedTargets);
            _lastUpdateTick[key] = Find.TickManager.TicksGame;
        }

        #endregion

        #region Cache Access

        /// <summary>
        /// Gets cached targets for a work tag
        /// </summary>
        public static List<T> GetWorkTagTargets(string workTag, int mapId)
        {
            if (string.IsNullOrEmpty(workTag))
                return new List<T>();

            var key = (workTag, mapId);
            if (_workTagTargetCache.TryGetValue(key, out var targets))
            {
                return targets;
            }

            return new List<T>();
        }

        /// <summary>
        /// Gets cached targets for a job giver type (legacy method)
        /// </summary>
        public static List<T> GetTargets(Type jobGiverType, int mapId)
        {
            if (jobGiverType == null)
                return new List<T>();

            // Try to use work tag-based cache if available
            string workTag = Utility_JobGiverManager.GetWorkTagForJobGiverType(jobGiverType);
            if (!string.IsNullOrEmpty(workTag))
            {
                var workTagTargets = GetWorkTagTargets(workTag, mapId);
                if (workTagTargets.Count > 0)
                    return workTagTargets;
            }

            // Legacy fallback
            var key = (jobGiverType, mapId);
            if (_targetCache.TryGetValue(key, out var targets))
            {
                return targets;
            }

            return new List<T>();
        }

        /// <summary>
        /// Gets the last update tick for a work tag
        /// </summary>
        private static int GetWorkTagLastUpdateTick(string workTag, int mapId)
        {
            var key = (workTag, mapId);
            if (_workTagLastUpdateTick.TryGetValue(key, out int lastUpdate))
            {
                return lastUpdate;
            }
            return 0; // Default to 0 if no update has been recorded
        }

        /// <summary>
        /// Gets the last update tick for a job giver type (legacy method)
        /// </summary>
        private static int GetLastUpdateTick(Type jobGiverType, int mapId)
        {
            var key = (jobGiverType, mapId);
            if (_lastUpdateTick.TryGetValue(key, out int lastUpdate))
            {
                return lastUpdate;
            }
            return 0; // Default to 0 if no update has been recorded
        }

        #endregion

        #region Reachability Caching

        /// <summary>
        /// Tries to get a cached reachability result for a work tag
        /// </summary>
        public static bool TryGetReachabilityResult(string workTag, int mapId, T target, out bool result)
        {
            if (string.IsNullOrEmpty(workTag))
            {
                result = default;
                return false;
            }

            var key = (workTag, mapId);
            if (_workTagReachabilityCache.TryGetValue(key, out var mapCache) &&
                mapCache.TryGetValue(target, out result))
            {
                return true;
            }

            result = default;
            return false;
        }

        /// <summary>
        /// Tries to get a cached reachability result for a job giver type (legacy method)
        /// </summary>
        public static bool TryGetReachabilityResult(Type jobGiverType, int mapId, T target, out bool result)
        {
            if (jobGiverType == null)
            {
                result = default;
                return false;
            }

            // Try to use work tag-based cache if available
            string workTag = Utility_JobGiverManager.GetWorkTagForJobGiverType(jobGiverType);
            if (!string.IsNullOrEmpty(workTag))
            {
                if (TryGetReachabilityResult(workTag, mapId, target, out result))
                    return true;
            }

            // Legacy fallback
            var key = (jobGiverType, mapId);
            if (_reachabilityCache.TryGetValue(key, out var mapCache) &&
                mapCache.TryGetValue(target, out result))
            {
                return true;
            }

            result = default;
            return false;
        }

        /// <summary>
        /// Caches a reachability result for a work tag
        /// </summary>
        public static void CacheReachabilityResult(string workTag, int mapId, T target, bool result)
        {
            if (string.IsNullOrEmpty(workTag))
                return;

            var key = (workTag, mapId);
            if (!_workTagReachabilityCache.TryGetValue(key, out var mapCache))
            {
                mapCache = new Dictionary<T, bool>();
                _workTagReachabilityCache[key] = mapCache;
            }

            // Limit cache size per work tag
            if (mapCache.Count >= 1000)
            {
                // Simple strategy: remove random entries when cache gets too big
                var keysToRemove = mapCache.Keys.Take(200).ToList();
                foreach (var k in keysToRemove)
                    mapCache.Remove(k);
            }

            mapCache[target] = result;
        }

        /// <summary>
        /// Caches a reachability result for a job giver type (legacy method)
        /// </summary>
        public static void CacheReachabilityResult(Type jobGiverType, int mapId, T target, bool result)
        {
            if (jobGiverType == null)
                return;

            // Also cache for work tag if available
            string workTag = Utility_JobGiverManager.GetWorkTagForJobGiverType(jobGiverType);
            if (!string.IsNullOrEmpty(workTag))
            {
                CacheReachabilityResult(workTag, mapId, target, result);
            }

            // Legacy caching
            var key = (jobGiverType, mapId);
            if (!_reachabilityCache.TryGetValue(key, out var mapCache))
            {
                mapCache = new Dictionary<T, bool>();
                _reachabilityCache[key] = mapCache;
            }

            // Limit cache size per job giver
            if (mapCache.Count >= 1000)
            {
                // Simple strategy: remove random entries when cache gets too big
                var keysToRemove = mapCache.Keys.Take(200).ToList();
                foreach (var k in keysToRemove)
                    mapCache.Remove(k);
            }

            mapCache[target] = result;
        }

        #endregion

        #region Cache Reset Methods

        /// <summary>
        /// Resets the cache for a specific work tag across all maps
        /// </summary>
        public static void ResetWorkTagCache(string workTag)
        {
            if (string.IsNullOrEmpty(workTag))
                return;

            var keysToRemove = _workTagTargetCache.Keys.Where(k => k.Item1 == workTag).ToList();
            foreach (var key in keysToRemove)
            {
                _workTagTargetCache.Remove(key);
                _workTagLastUpdateTick.Remove(key);
            }

            keysToRemove = _workTagReachabilityCache.Keys.Where(k => k.Item1 == workTag).ToList();
            foreach (var key in keysToRemove)
            {
                _workTagReachabilityCache.Remove(key);
            }
        }

        /// <summary>
        /// Resets the cache for a specific job giver type across all maps (legacy method)
        /// </summary>
        public static void ResetJobGiverCache(Type jobGiverType)
        {
            if (jobGiverType == null)
                return;

            // Also reset work tag cache if available
            string workTag = Utility_JobGiverManager.GetWorkTagForJobGiverType(jobGiverType);
            if (!string.IsNullOrEmpty(workTag))
            {
                ResetWorkTagCache(workTag);
            }

            // Legacy reset
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

        /// <summary>
        /// Resets all caches for a specific map
        /// </summary>
        public static void ResetMapCache(int mapId)
        {
            // Reset work tag-based caches
            var workTagKeysToRemove = _workTagTargetCache.Keys.Where(k => k.Item2 == mapId).ToList();
            foreach (var key in workTagKeysToRemove)
            {
                _workTagTargetCache.Remove(key);
                _workTagLastUpdateTick.Remove(key);
            }

            workTagKeysToRemove = _workTagReachabilityCache.Keys.Where(k => k.Item2 == mapId).ToList();
            foreach (var key in workTagKeysToRemove)
            {
                _workTagReachabilityCache.Remove(key);
            }

            // Reset legacy caches
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

        /// <summary>
        /// Resets all caches completely
        /// </summary>
        public static void Reset()
        {
            // Reset work tag-based caches
            _workTagTargetCache.Clear();
            _workTagLastUpdateTick.Clear();
            _workTagReachabilityCache.Clear();

            // Reset legacy caches
            _targetCache.Clear();
            _lastUpdateTick.Clear();
            _reachabilityCache.Clear();
        }

        #endregion
    }
}