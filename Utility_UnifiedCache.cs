using RimWorld;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using Verse.AI;
using static emitbreaker.PawnControl.Utility_TargetPrefilteringManager;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Unified caching system that combines all caching functionality in one place
    /// </summary>
    public static class Utility_UnifiedCache
    {
        #region Cache Priority and Entry Types

        /// <summary>
        /// Cache priority levels that determine eviction strategy and timeout
        /// </summary>
        public enum CachePriority
        {
            Critical = 0,   // Critical cache data - longest timeout
            High = 1,       // High priority cache - longer timeout
            Normal = 2,     // Normal priority cache - standard timeout
            Low = 3         // Low priority cache - quick timeout
        }

        /// <summary>
        /// Represents a cached item with metadata
        /// </summary>
        private class CacheEntry
        {
            public object Value { get; set; }
            public int CreationTick { get; set; }
            public CachePriority Priority { get; set; }
            public int TimeoutTicks { get; set; }

            public CacheEntry(object value, CachePriority priority, int timeoutTicks)
            {
                Value = value;
                CreationTick = Find.TickManager?.TicksGame ?? 0;
                Priority = priority;
                TimeoutTicks = timeoutTicks;
            }

            public bool IsExpired(int currentTick)
            {
                return (currentTick - CreationTick) > TimeoutTicks;
            }
        }

        #endregion

        #region Static Cache Collections

        // ===== Specialized cache storage for direct public access =====
        // These retain their public accessibility for compatibility

        // ModExtension caching
        public static readonly Dictionary<ThingDef, NonHumanlikePawnControlExtension> ModExtensions =
            new Dictionary<ThingDef, NonHumanlikePawnControlExtension>();

        // Tag caching
        public static readonly Dictionary<ThingDef, HashSet<string>> Tags =
            new Dictionary<ThingDef, HashSet<string>>();

        // Thread-safe animal caching
        public static readonly ConcurrentDictionary<ThingDef, bool> ForcedAnimals =
            new ConcurrentDictionary<ThingDef, bool>();

        // Colony pawns caching
        private static readonly Dictionary<Map, List<Pawn>> _colonistLikePawns =
            new Dictionary<Map, List<Pawn>>();
        public static readonly Dictionary<Map, int> FrameIndices =
            new Dictionary<Map, int>();

        // Duty caching
        private static readonly Dictionary<string, DutyDef> _dutyDefs =
            new Dictionary<string, DutyDef>();

        // Apparel restriction caching
        private static readonly Dictionary<Pawn, bool> _apparelRestrictions =
            new Dictionary<Pawn, bool>();

        // Work-related caches
        public static readonly Dictionary<ValueTuple<Pawn, string>, bool> WorkEnabled =
            new Dictionary<ValueTuple<Pawn, string>, bool>();
        public static readonly Dictionary<ValueTuple<Pawn, WorkTypeDef>, bool> WorkTypeEnabled =
            new Dictionary<ValueTuple<Pawn, WorkTypeDef>, bool>();
        public static readonly Dictionary<ValueTuple<Pawn, string>, bool> WorkDisabled =
            new Dictionary<ValueTuple<Pawn, string>, bool>();
        public static readonly Dictionary<ValueTuple<Pawn, string>, bool> ForceDraftable =
            new Dictionary<ValueTuple<Pawn, string>, bool>();
        public static readonly Dictionary<ValueTuple<Pawn, string>, bool> ForceEquipWeapon =
            new Dictionary<ValueTuple<Pawn, string>, bool>();
        public static readonly Dictionary<ValueTuple<Pawn, string>, bool> ForceWearApparel =
            new Dictionary<ValueTuple<Pawn, string>, bool>();

        // Race identity caches
        public static readonly Dictionary<ThingDef, bool> IsAnimal =
            new Dictionary<ThingDef, bool>();
        public static readonly Dictionary<ThingDef, bool> IsHumanlike =
            new Dictionary<ThingDef, bool>();
        public static readonly Dictionary<ThingDef, bool> IsMechanoid =
            new Dictionary<ThingDef, bool>();
        public static readonly Dictionary<FlagScopeTarget, bool> FlagOverrides =
            new Dictionary<FlagScopeTarget, bool>();

        // Job caching
        public static readonly Dictionary<Thing, Job> JobCache =
            new Dictionary<Thing, Job>();

        // Work tag caching
        public static readonly Dictionary<Pawn, bool> AllowWorkTag =
            new Dictionary<Pawn, bool>();
        public static readonly Dictionary<Pawn, bool> BlockWorkTag =
            new Dictionary<Pawn, bool>();
        public static readonly Dictionary<Pawn, bool> CombinedWorkTag =
            new Dictionary<Pawn, bool>();

        // UI caching
        public static readonly Dictionary<int, bool> BioTabVisibility =
            new Dictionary<int, bool>();

        // ===== Dynamic cache storage with invalidation =====

        // The main dynamic cache storage - organized by priority level
        private static readonly Dictionary<CachePriority, Dictionary<string, CacheEntry>> _genericCaches =
            new Dictionary<CachePriority, Dictionary<string, CacheEntry>>
            {
                { CachePriority.Critical, new Dictionary<string, CacheEntry>() },
                { CachePriority.High,     new Dictionary<string, CacheEntry>() },
                { CachePriority.Normal,   new Dictionary<string, CacheEntry>() },
                { CachePriority.Low,      new Dictionary<string, CacheEntry>() }
            };

        // Fast lookup for which priority a key lives in
        private static readonly Dictionary<string, CachePriority> _keyToPriority =
            new Dictionary<string, CachePriority>();

        // Map of dependencies between caches
        private static readonly Dictionary<string, HashSet<string>> _dependencies =
            new Dictionary<string, HashSet<string>>();

        // Cache management settings
        private static int _lastMaintenanceTick = 0;
        private static readonly int _maintenanceInterval = 2500;  // Base interval, will be adjusted by colony size

        // Maximum number of entries per cache priority
        private static readonly Dictionary<CachePriority, int> _maxEntriesPerPriority =
            new Dictionary<CachePriority, int>
            {
                { CachePriority.Critical, 200 },
                { CachePriority.High,     150 },
                { CachePriority.Normal,   100 },
                { CachePriority.Low,       50 }
            };

        // Base timeouts by priority (centralized)
        private static readonly Dictionary<CachePriority, int> _baseTimeouts =
            new Dictionary<CachePriority, int>
            {
                { CachePriority.Critical, 18000 },  // 5 minutes
                { CachePriority.High,     12000 },  // 3.3 minutes
                { CachePriority.Normal,    6000 },  // 1.7 minutes
                { CachePriority.Low,       3000 }   // 50 seconds
            };

        // Format for map-specific keys
        private const string MapKeyFormat = "Map_{0}_{1}";

        // Add this to the Static Cache Collections section, after the Tags declaration
        public static readonly Dictionary<ThingDef, PawnTagFlags> TagFlags =
            new Dictionary<ThingDef, PawnTagFlags>();

        #endregion

        #region Public API - Direct Special-Case Caches

        #region ModExtension Caching

        /// <summary>
        /// Gets a cached ModExtension for a ThingDef or creates one if not present
        /// </summary>
        public static NonHumanlikePawnControlExtension GetModExtension(ThingDef def)
        {
            if (def == null)
                return null;

            // Check cache first
            if (ModExtensions.TryGetValue(def, out var cached))
                return cached;

            // Safely try to get the extension
            NonHumanlikePawnControlExtension modExtension = null;
            try
            {
                if (def.modExtensions != null)
                    modExtension = def.GetModExtension<NonHumanlikePawnControlExtension>();
            }
            catch (Exception ex)
            {
                if (Prefs.DevMode)
                    Log.Warning($"[PawnControl] GetModExtension failed for def {def.defName}: {ex.Message}");
            }

            // Cache for future use (even if null)
            ModExtensions[def] = modExtension;
            return modExtension;
        }

        /// <summary>
        /// Updates the cached ModExtension for a ThingDef
        /// </summary>
        public static void UpdateModExtension(ThingDef def, NonHumanlikePawnControlExtension extension)
        {
            if (def == null)
                return;

            ModExtensions[def] = extension;
        }

        /// <summary>
        /// Preloads all ModExtensions from the DefDatabase
        /// </summary>
        public static void PreloadModExtensions()
        {
            int loadedCount = 0;

            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def?.race == null)
                    continue;

                var modExtension = def.GetModExtension<NonHumanlikePawnControlExtension>();
                if (modExtension != null)
                {
                    ModExtensions[def] = modExtension;
                    modExtension.CacheSkillPassions();
                    loadedCount++;
                }
            }

            Utility_DebugManager.LogNormal($"Non Humanlike Pawn Controller refreshed: {loadedCount} extensions loaded.");
        }

        /// <summary>
        /// Preloads the ModExtension for a specific race
        /// </summary>
        public static void PreloadModExtensionForRace(ThingDef def)
        {
            if (def == null)
                return;

            // Look for our extension type in the mod extensions
            if (def.modExtensions != null)
            {
                foreach (var ext in def.modExtensions)
                {
                    if (ext is NonHumanlikePawnControlExtension controlExt)
                    {
                        ModExtensions[def] = controlExt;
                        return;
                    }
                }
            }

            // No extension found, ensure null is cached
            ModExtensions[def] = null;
        }

        /// <summary>
        /// Clears all cached ModExtensions
        /// </summary>
        public static void ClearAllModExtensions()
        {
            ModExtensions.Clear();
            Utility_DebugManager.LogNormal("Cleared all mod extensions in cache.");
        }

        /// <summary>
        /// Clears the cached ModExtension for a specific ThingDef
        /// </summary>
        public static void ClearModExtension(ThingDef def)
        {
            if (def == null)
                return;

            ModExtensions.Remove(def);

            if (Prefs.DevMode)
                Utility_DebugManager.LogNormal($"Cleared mod extension cache for {def.defName}.");
        }

        /// <summary>
        /// Cleans up all runtime ModExtensions when starting a new game
        /// </summary>
        public static void CleanupRuntimeModExtensions()
        {
            Log.Message("[PawnControl] Cleaning up runtime mod extensions for new game");

            int removedCount = 0;

            // Clean all ThingDefs that have our extensions
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def?.modExtensions == null)
                    continue;

                // Look for and remove our extensions (non-XML ones)
                for (int i = def.modExtensions.Count - 1; i >= 0; i--)
                {
                    if (def.modExtensions[i] is NonHumanlikePawnControlExtension ext && !ext.fromXML)
                    {
                        def.modExtensions.RemoveAt(i);
                        removedCount++;
                    }
                }
            }

            // Clear all runtime caches
            ModExtensions.Clear();
            Tags.Clear();
            ForcedAnimals.Clear();
            WorkEnabled.Clear();
            WorkTypeEnabled.Clear();
            WorkDisabled.Clear();
            ForceDraftable.Clear();
            ForceEquipWeapon.Clear();
            ForceWearApparel.Clear();
            IsAnimal.Clear();
            IsHumanlike.Clear();
            IsMechanoid.Clear();

            Log.Message($"[PawnControl] Removed {removedCount} runtime mod extensions when starting new game");
        }

        #endregion

        #region Duty & Colonial Pawn Caching

        /// <summary>
        /// Gets a cached DutyDef by name
        /// </summary>
        public static DutyDef GetDuty(string defName)
        {
            if (string.IsNullOrEmpty(defName))
                return null;

            if (!_dutyDefs.TryGetValue(defName, out var result))
            {
                result = DefDatabase<DutyDef>.GetNamedSilentFail(defName);
                _dutyDefs[defName] = result;
            }

            return result;
        }

        /// <summary>
        /// Returns a list of all colonist-like pawns in the given map and cache them
        /// </summary>
        public static IEnumerable<Pawn> GetColonistLikePawns(Map map)
        {
            if (map == null)
                return Enumerable.Empty<Pawn>();

            int frame = Time.frameCount;
            if (_colonistLikePawns.TryGetValue(map, out var cached) &&
                FrameIndices.TryGetValue(map, out var cachedFrame) &&
                frame - cachedFrame <= 5) // 5-frame TTL
            {
                return cached;
            }

            var result = map.mapPawns.AllPawns.Where(pawn =>
            {
                if (pawn == null || pawn.def == null || pawn.def.race == null)
                    return false;

                if (pawn.Faction != Faction.OfPlayer)
                    return false;

                // Require at least one valid work type to be enabled
                var modExtension = GetModExtension(pawn.def);

                // Fallback: vanilla logic
                if (modExtension == null)
                {
                    foreach (WorkTypeDef workType in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                    {
                        if (pawn.workSettings?.WorkIsActive(workType) == true)
                            return true;
                    }
                    return false;
                }

                // If extension exists, check for at least one tag-allowed work type
                foreach (var workTypeDef in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                {
                    if (IsWorkTypeEnabledForPawn(pawn, workTypeDef))
                        return true;
                }

                return false;
            }).ToList();

            _colonistLikePawns[map] = result;
            FrameIndices[map] = frame;
            return result;
        }

        /// <summary>
        /// Invalidates colonist-like pawns cache for a map
        /// </summary>
        public static void InvalidateColonistCache(Map map)
        {
            if (map == null)
                return;

            _colonistLikePawns.Remove(map);
            FrameIndices.Remove(map);
        }

        #endregion

        #region Work-Related Caching

        /// <summary>
        /// Checks whether a pawn is allowed to perform a work type
        /// </summary>
        public static bool IsWorkTypeEnabledForPawn(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn == null || workType == null)
                return false;

            // Only non-humanlike pawns with valid mod extensions AND allow tags can enable work
            var modExtension = GetModExtension(pawn.def);

            if (modExtension == null)
                return pawn.workSettings?.WorkIsActive(workType) == true; // Vanilla fallback

            if (modExtension.tags == null)
                return false;

            return Utility_TagManager.HasTag(pawn.def, ManagedTags.AllowAllWork) ||
                   Utility_TagManager.HasTag(pawn.def, ManagedTags.AllowWorkPrefix + workType.defName);
        }

        /// <summary>
        /// Checks if a pawn has apparel restrictions
        /// </summary>
        public static bool IsApparelRestricted(Pawn pawn)
        {
            if (pawn == null || pawn.def == null)
                return false;

            if (!_apparelRestrictions.TryGetValue(pawn, out var result))
            {
                var modExtension = GetModExtension(pawn.def);
                result = modExtension != null && modExtension.restrictApparelByBodyType;
                _apparelRestrictions[pawn] = result;
            }

            return result;
        }

        #endregion

        #region Reachability & Designation Caching

        /// <summary>
        /// Gets or creates a reachability dictionary for a map
        /// </summary>
        public static Dictionary<T, bool> GetReachabilityDict<T>(
            Dictionary<int, Dictionary<T, bool>> reachabilityCache,
            int mapId)
        {
            if (!reachabilityCache.ContainsKey(mapId))
                reachabilityCache[mapId] = new Dictionary<T, bool>();

            return reachabilityCache[mapId];
        }

        /// <summary>
        /// Updates a cached collection of things based on designations
        /// </summary>
        public static void UpdateDesignationCache<T>(
            Map map,
            ref int lastUpdateTick,
            int updateInterval,
            Dictionary<int, List<T>> cache,
            Dictionary<int, Dictionary<T, bool>> reachabilityCache,
            DesignationDef designationDef,
            Func<Designation, T> extractFunc,
            int maxCacheEntries = 100) where T : Thing
        {
            if (map == null)
                return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > lastUpdateTick + updateInterval || !cache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (cache.ContainsKey(mapId))
                    cache[mapId].Clear();
                else
                    cache[mapId] = new List<T>();

                // Clear reachability cache too
                if (reachabilityCache.ContainsKey(mapId))
                    reachabilityCache[mapId].Clear();
                else
                    reachabilityCache[mapId] = new Dictionary<T, bool>();

                // Find all things with designations
                List<T> validThings = new List<T>();
                foreach (Designation designation in map.designationManager.SpawnedDesignationsOfDef(designationDef))
                {
                    T thing = extractFunc(designation);
                    if (thing != null && thing.Spawned)
                        validThings.Add(thing);
                }

                // Add items to cache, with size limit
                cache[mapId].AddRange(validThings);
                if (cache[mapId].Count > maxCacheEntries)
                    cache[mapId].RemoveRange(maxCacheEntries, cache[mapId].Count - maxCacheEntries);

                lastUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Updates a generic map-specific cache
        /// </summary>
        public static void UpdateMapCache<T>(
            int mapId,
            Dictionary<int, List<T>> cache,
            IEnumerable<T> newItems,
            int maxCacheEntries = 100)
        {
            // Clear or initialize cache
            if (cache.ContainsKey(mapId))
                cache[mapId].Clear();
            else
                cache[mapId] = new List<T>();

            // Add items with size limit
            if (newItems != null)
            {
                cache[mapId].AddRange(newItems);
                if (cache[mapId].Count > maxCacheEntries)
                    cache[mapId].RemoveRange(maxCacheEntries, cache[mapId].Count - maxCacheEntries);
            }
        }

        #endregion

        #region Spatial Indexing Caching



        #endregion

        #endregion

        #region Public API - Generic Dynamic Caching

        /// <summary>
        /// Builds a consistent key for map-specific cache entries
        /// </summary>
        private static string BuildMapKey(int mapId, string key)
        {
            return string.Format(MapKeyFormat, mapId, key);
        }

        /// <summary>
        /// Gets a value from the cache, or creates it if not present
        /// </summary>
        public static T GetOrCreate<T>(
            string key,
            Func<T> createFunc,
            CachePriority priority = CachePriority.Normal,
            int? timeoutTicks = null)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));
            if (createFunc == null)
                throw new ArgumentNullException(nameof(createFunc));

            // Run maintenance occasionally
            MaybeRunMaintenance();

            // Return existing if valid
            if (TryGetValue<T>(key, out var existing))
                return existing;

            // Otherwise create & store
            var newValue = createFunc();
            Set(key, newValue, priority, timeoutTicks);
            return newValue;
        }

        /// <summary>
        /// Gets a map-specific value from the cache, or creates it
        /// </summary>
        public static T GetOrCreate<T>(
            int mapId,
            string key,
            Func<T> createFunc,
            CachePriority priority = CachePriority.Normal,
            int? timeoutTicks = null)
        {
            return GetOrCreate(BuildMapKey(mapId, key), createFunc, priority, timeoutTicks);
        }

        /// <summary>
        /// Sets a value in the cache
        /// </summary>
        public static void Set(
            string key,
            object value,
            CachePriority priority = CachePriority.Normal,
            int? timeoutTicks = null)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            // Remove from its old priority if existed
            if (_keyToPriority.TryGetValue(key, out var oldPriority))
                _genericCaches[oldPriority].Remove(key);

            // Calculate timeout and store entry
            int timeout = timeoutTicks ?? GetDefaultTimeoutForPriority(priority);
            _genericCaches[priority][key] = new CacheEntry(value, priority, timeout);
            _keyToPriority[key] = priority;
        }

        /// <summary>
        /// Tries to retrieve a value from the cache
        /// </summary>
        public static bool TryGetValue<T>(string key, out T value)
        {
            value = default;

            if (_keyToPriority.TryGetValue(key, out var priority))
            {
                var cache = _genericCaches[priority];
                if (cache.TryGetValue(key, out var entry))
                {
                    int currentTick = Find.TickManager?.TicksGame ?? 0;

                    // Check expiration
                    if (entry.IsExpired(currentTick))
                    {
                        cache.Remove(key);
                        _keyToPriority.Remove(key);
                        return false;
                    }

                    // Check type
                    if (entry.Value is T typed)
                    {
                        value = typed;
                        return true;
                    }

                    // Wrong type, remove entry
                    cache.Remove(key);
                    _keyToPriority.Remove(key);
                }
            }

            return false;
        }

        /// <summary>
        /// Tries to retrieve a map-specific value from the cache
        /// </summary>
        public static bool TryGetValue<T>(int mapId, string key, out T value)
        {
            return TryGetValue(BuildMapKey(mapId, key), out value);
        }

        /// <summary>
        /// Adds a dependency relationship between two cache keys
        /// </summary>
        public static void AddDependency(string sourceKey, string targetKey)
        {
            if (string.IsNullOrEmpty(sourceKey) || string.IsNullOrEmpty(targetKey))
                return;

            if (!_dependencies.ContainsKey(targetKey))
                _dependencies[targetKey] = new HashSet<string>();

            _dependencies[targetKey].Add(sourceKey);
        }

        /// <summary>
        /// Invalidates a specific cache entry and all its dependencies
        /// </summary>
        public static void Invalidate(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;

            var queue = new Queue<string>();
            var seen = new HashSet<string> { key };
            queue.Enqueue(key);

            while (queue.Count > 0)
            {
                var currentKey = queue.Dequeue();

                // Remove entry
                if (_keyToPriority.TryGetValue(currentKey, out var priority))
                {
                    _genericCaches[priority].Remove(currentKey);
                    _keyToPriority.Remove(currentKey);
                }

                // Enqueue dependents
                if (_dependencies.TryGetValue(currentKey, out var dependents))
                {
                    foreach (var dep in dependents)
                        if (seen.Add(dep))
                            queue.Enqueue(dep);
                }
            }
        }

        /// <summary>
        /// Invalidates a map-specific cache entry
        /// </summary>
        public static void Invalidate(int mapId, string key)
        {
            Invalidate(BuildMapKey(mapId, key));
        }

        /// <summary>
        /// Invalidates all cache entries for a specific map
        /// </summary>
        public static void InvalidateMap(int mapId)
        {
            var prefix = string.Format(MapKeyFormat, mapId, string.Empty);

            // Remove from generic caches
            foreach (var priorityLevel in _genericCaches.Keys)
            {
                var toRemove = _genericCaches[priorityLevel].Keys
                                   .Where(k => k.StartsWith(prefix))
                                   .ToList();

                foreach (var k in toRemove)
                {
                    _genericCaches[priorityLevel].Remove(k);
                    _keyToPriority.Remove(k);
                }
            }

            // Also remove from map-specific dictionaries
            InvalidateColonistCache(map: Find.Maps?.FirstOrDefault(m => m.uniqueID == mapId));
        }

        /// <summary>
        /// Checks if a cache entry exists and is valid
        /// </summary>
        public static bool ContainsKey(string key)
        {
            if (_keyToPriority.TryGetValue(key, out var priority))
            {
                var cache = _genericCaches[priority];
                if (cache.TryGetValue(key, out var entry))
                {
                    int currentTick = Find.TickManager?.TicksGame ?? 0;

                    if (entry.IsExpired(currentTick))
                    {
                        cache.Remove(key);
                        _keyToPriority.Remove(key);
                        return false;
                    }

                    return true;
                }
            }

            return false;
        }

        #endregion

        #region Cache Maintenance

        /// <summary>
        /// Runs maintenance if enough time has passed
        /// </summary>
        private static void MaybeRunMaintenance()
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;

            if (currentTick - _lastMaintenanceTick < GetMaintenanceInterval())
                return;

            _lastMaintenanceTick = currentTick;
            RunMaintenance();
        }

        /// <summary>
        /// Performs maintenance on all caches
        /// </summary>
        private static void RunMaintenance()
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;

            // Remove expired and over-capacity entries
            foreach (var priorityLevel in _genericCaches.Keys)
            {
                var cache = _genericCaches[priorityLevel];

                // Remove expired entries
                var expired = cache
                    .Where(kvp => kvp.Value.IsExpired(currentTick))
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in expired)
                {
                    cache.Remove(key);
                    _keyToPriority.Remove(key);
                }

                // If over capacity, remove oldest entries
                int maxEntries = _maxEntriesPerPriority[priorityLevel];
                if (cache.Count > maxEntries)
                {
                    var oldest = cache
                        .OrderBy(kvp => kvp.Value.CreationTick)
                        .Take(cache.Count - maxEntries)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    foreach (var key in oldest)
                    {
                        cache.Remove(key);
                        _keyToPriority.Remove(key);
                    }
                }
            }

            // Clean up dangling dependencies
            CleanupDependencies();

            // Clean up special caches - only remove invalid entries
            CleanupSpecialCaches(currentTick);

            if (Prefs.DevMode)
            {
                var log = new StringBuilder();
                log.AppendLine("Cache maintenance completed:");
                foreach (var level in _genericCaches.Keys)
                    log.AppendLine($"  {level}: {_genericCaches[level].Count} entries");

                Utility_DebugManager.LogNormal(log.ToString());
            }
        }

        /// <summary>
        /// Cleans up dependencies without corresponding cache entries
        /// </summary>
        private static void CleanupDependencies()
        {
            var keysToRemove = new List<string>();

            foreach (var key in _dependencies.Keys)
            {
                bool exists = _genericCaches.Values.Any(dict => dict.ContainsKey(key));
                if (!exists)
                    keysToRemove.Add(key);
            }

            foreach (var key in keysToRemove)
                _dependencies.Remove(key);
        }

        /// <summary>
        /// Cleans up invalid entries in special-purpose caches
        /// </summary>
        private static void CleanupSpecialCaches(int currentTick)
        {
            // Clean job cache
            var invalidJobs = JobCache
                .Where(kvp => kvp.Key == null || !kvp.Key.Spawned || kvp.Key.Destroyed)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in invalidJobs)
                JobCache.Remove(key);

            // Use tool to clean reachability and other map-based caches
            // This is done elsewhere in the game component
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Gets the default timeout for a specific priority level
        /// </summary>
        private static int GetDefaultTimeoutForPriority(CachePriority priority)
        {
            return (int)(_baseTimeouts[priority] * GetColonySizeMultiplier());
        }

        /// <summary>
        /// Gets the maintenance interval adjusted for colony size
        /// </summary>
        private static int GetMaintenanceInterval()
        {
            return (int)(_maintenanceInterval * GetColonySizeMultiplier());
        }

        /// <summary>
        /// Calculates a multiplier based on colony size
        /// </summary>
        private static float GetColonySizeMultiplier()
        {
            if (Find.WorldPawns == null || Find.Maps == null)
                return 1f;

            // Count colonists across all maps
            int colonistCount = Find.Maps
                .SelectMany(map => map?.mapPawns?.FreeColonistsSpawned ?? Enumerable.Empty<Pawn>())
                .Count();

            // Adjust based on colony size - larger colonies need more frequent updates
            if (colonistCount <= 5) return 1.5f;    // Small colonies - longer cache lifetime
            if (colonistCount <= 10) return 1.2f;   // Medium-small colonies
            if (colonistCount <= 20) return 1.0f;   // Medium colonies
            if (colonistCount <= 40) return 0.8f;   // Large colonies
            return 0.6f;                            // Huge colonies
        }

        #endregion

        #region Reset Methods

        /// <summary>
        /// Clears all caches
        /// </summary>
        public static void Clear()
        {
            // Clear generic caches
            foreach (var priorityLevel in _genericCaches.Keys)
                _genericCaches[priorityLevel].Clear();

            _dependencies.Clear();
            _keyToPriority.Clear();

            // Clear specialized caches
            ModExtensions.Clear();
            Tags.Clear();
            ForcedAnimals.Clear();
            _colonistLikePawns.Clear();
            FrameIndices.Clear();
            _dutyDefs.Clear();
            _apparelRestrictions.Clear();
            WorkEnabled.Clear();
            WorkTypeEnabled.Clear();
            WorkDisabled.Clear();
            ForceDraftable.Clear();
            ForceEquipWeapon.Clear();
            ForceWearApparel.Clear();
            IsAnimal.Clear();
            IsHumanlike.Clear();
            IsMechanoid.Clear();
            FlagOverrides.Clear();
            JobCache.Clear();
            AllowWorkTag.Clear();
            BlockWorkTag.Clear();
            CombinedWorkTag.Clear();
            BioTabVisibility.Clear();
            TagFlags.Clear();

            if (Prefs.DevMode)
                Utility_DebugManager.LogNormal("All caches cleared.");
        }

        #endregion
    }
}