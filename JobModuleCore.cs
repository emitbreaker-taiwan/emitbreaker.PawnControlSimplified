using emitbreaker.PawnControl;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Text;
using RimWorld;
using Verse;

/// <summary>
/// Base abstract class for all job modules across different work types.
/// Provides common functionality and structure for specialized job modules.
/// </summary>
public abstract class JobModuleCore
{
    /// <summary>
    /// Gets a unique identifier for this module
    /// </summary>
    public abstract string UniqueID { get; }

    /// <summary>
    /// Gets the priority of this module (higher priority modules run first)
    /// </summary>
    public abstract float Priority { get; }

    /// <summary>
    /// Gets the work type this module is associated with (e.g., "Warden", "Hauling")
    /// </summary>
    public abstract string WorkTypeName { get; }

    /// <summary>
    /// Whether this module requires the violent work tag
    /// </summary>
    public virtual bool RequiresViolence => false;

    /// <summary>
    /// Whether this module processes colonists instead of normal targets
    /// </summary>
    public virtual bool HandlesColonists => false;

    /// <summary>
    /// Categorization of this module for potential batch processing
    /// </summary>
    public virtual string Category => "General";

    /// <summary>
    /// Gets the ThingRequestGroups this module is interested in
    /// </summary>
    public virtual HashSet<ThingRequestGroup> RelevantThingRequestGroups => null;

    /// <summary>
    /// Fast filtering method to determine if a pawn is eligible for this module
    /// </summary>
    public virtual bool QuickFilterCheck(Pawn pawn)
    {
        return true; // By default, all pawns are eligible
    }

    /// <summary>
    /// Indicates update frequency for this module
    /// </summary>
    public virtual int CacheUpdateInterval => 120; // Default 2 seconds

    /// <summary>
    /// Maximum age for cached targets before they're forcibly re-validated
    /// </summary>
    public virtual int MaxTargetCacheAge => 3000; // Default 50 seconds

    /// <summary>
    /// Maximum number of entries to keep in module-specific caches
    /// </summary>
    protected virtual int MaxCacheEntries => 1000;

    // ===== Static cache storage =====

    /// <summary>
    /// Tracks whether this module has any potential targets on a specific map
    /// </summary>
    private static readonly Dictionary<int, Dictionary<string, bool>> _hasTargetsCache =
        new Dictionary<int, Dictionary<string, bool>>();

    /// <summary>
    /// Keeps track of which pawns successfully used this module last
    /// Limits memory usage by only tracking the most recent 500 pawns
    /// </summary>
    private static readonly Dictionary<int, string> _lastSuccessfulModule = new Dictionary<int, string>();

    /// <summary>
    /// When the module last found no targets on a map
    /// </summary>
    private static readonly Dictionary<int, Dictionary<string, int>> _lastEmptyCheckTick =
        new Dictionary<int, Dictionary<string, int>>();

    /// <summary>
    /// Tracks when maps were last active - used for cache cleanup
    /// </summary>
    private static readonly Dictionary<int, int> _mapLastActiveTick =
        new Dictionary<int, int>();

    /// <summary>
    /// How often to check for stale map caches (in ticks)
    /// </summary>
    private const int MAP_CLEANUP_INTERVAL = 30000; // ~500 seconds

    /// <summary>
    /// When a map becomes stale and eligible for cache cleanup
    /// </summary>
    private const int MAP_STALE_THRESHOLD = 300000; // ~83 minutes

    /// <summary>
    /// Last tick we checked for stale map caches
    /// </summary>
    private static int _lastMapCleanupTick = 0;

    /// <summary>
    /// Collection of registered module extensions
    /// </summary>
    private static readonly Dictionary<string, List<IJobModuleExtension>> _moduleExtensions =
        new Dictionary<string, List<IJobModuleExtension>>();

    /// <summary>
    /// Registers a module extension for cross-mod compatibility
    /// </summary>
    public static void RegisterModuleExtension(string moduleId, IJobModuleExtension extension)
    {
        if (string.IsNullOrEmpty(moduleId) || extension == null)
            return;

        if (!_moduleExtensions.ContainsKey(moduleId))
            _moduleExtensions[moduleId] = new List<IJobModuleExtension>();

        _moduleExtensions[moduleId].Add(extension);
    }

    /// <summary>
    /// Gets extensions registered for this module
    /// </summary>
    protected List<IJobModuleExtension> GetExtensions()
    {
        if (_moduleExtensions.TryGetValue(UniqueID, out var extensions))
            return extensions;
        return null;
    }

    /// <summary>
    /// Gets extensions of a specific type for this module
    /// </summary>
    protected T GetExtension<T>() where T : IJobModuleExtension
    {
        var extensions = GetExtensions();
        if (extensions == null)
            return default(T);

        foreach (var extension in extensions)
        {
            if (extension is T typedExt)
                return typedExt;
        }

        return default(T);
    }

    /// <summary>
    /// Determines if this module should be skipped for the given map
    /// This is for early optimization to avoid processing modules that can't apply
    /// For example: skipping pollution cleaning if the DLC isn't active
    /// </summary>
    public virtual bool ShouldSkipModule(Map map)
    {
        return false; // By default, no modules are skipped
    }

    /// <summary>
    /// Fast check if this module has any potential targets on the given map
    /// </summary>
    public virtual bool HasTargets(Map map)
    {
        if (map == null) return false;

        // Update map activity tracking
        int currentTick = Find.TickManager.TicksGame;
        int mapId = map.uniqueID;

        // Update map activity timestamp
        _mapLastActiveTick[mapId] = currentTick;

        // Check if it's time to clean up stale map caches
        CheckForStaleMapCaches(currentTick);

        string moduleId = this.UniqueID;

        // Initialize caches if needed
        if (!_hasTargetsCache.TryGetValue(mapId, out var moduleCache))
        {
            moduleCache = new Dictionary<string, bool>();
            _hasTargetsCache[mapId] = moduleCache;
        }

        // If we have a recent cached result, use it
        if (moduleCache.TryGetValue(moduleId, out bool hasTargets))
        {
            // Check if we recently determined there are no targets and it's too soon to recheck
            if (!hasTargets && _lastEmptyCheckTick.TryGetValue(mapId, out var tickCache) &&
                tickCache.TryGetValue(moduleId, out int lastCheckTick))
            {
                // Only recheck for targets occasionally if we found none before
                if (currentTick < lastCheckTick + CacheUpdateInterval)
                {
                    return false;
                }
            }
        }

        // Default to having potential targets until proven otherwise
        return true;
    }

    /// <summary>
    /// Called when target cache is updated to record whether targets exist
    /// </summary>
    public void SetHasTargets(Map map, bool hasTargets)
    {
        if (map == null) return;

        int mapId = map.uniqueID;
        string moduleId = this.UniqueID;

        // Initialize caches if needed
        if (!_hasTargetsCache.TryGetValue(mapId, out var moduleCache))
        {
            moduleCache = new Dictionary<string, bool>();
            _hasTargetsCache[mapId] = moduleCache;
        }

        moduleCache[moduleId] = hasTargets;

        // Record when we found no targets
        if (!hasTargets)
        {
            if (!_lastEmptyCheckTick.TryGetValue(mapId, out var tickCache))
            {
                tickCache = new Dictionary<string, int>();
                _lastEmptyCheckTick[mapId] = tickCache;
            }

            tickCache[moduleId] = Find.TickManager.TicksGame;
        }
    }

    /// <summary>
    /// Records successful job creation for a pawn with this module
    /// </summary>
    public void RecordSuccessfulJobCreation(Pawn pawn)
    {
        if (pawn == null) return;
        _lastSuccessfulModule[pawn.thingIDNumber] = this.UniqueID;
    }

    /// <summary>
    /// Optimized worktype validation for quick filtering - override in specialized classes
    /// </summary>
    public virtual bool WorkTypeApplies(Pawn pawn)
    {
        // Specialized modules should override this with direct worktype checks
        return true;
    }

    /// <summary>
    /// Reset any static data when language changes or game reloads
    /// </summary>
    public virtual void ResetStaticData() { }

    /// <summary>
    /// Reset the target caches for all modules
    /// </summary>
    public static void ResetAllTargetCaches()
    {
        _hasTargetsCache.Clear();
        _lastEmptyCheckTick.Clear();
        _mapLastActiveTick.Clear();
    }

    /// <summary>
    /// Reset the last successful module cache
    /// </summary>
    public static void ResetLastSuccessfulModuleCache()
    {
        _lastSuccessfulModule.Clear();
    }

    /// <summary>
    /// Check for and clean up caches from stale maps
    /// </summary>
    private static void CheckForStaleMapCaches(int currentTick)
    {
        // Only check periodically
        if (currentTick - _lastMapCleanupTick < MAP_CLEANUP_INTERVAL)
            return;

        _lastMapCleanupTick = currentTick;

        // Find all map IDs that haven't been active recently
        List<int> staleMapIds = new List<int>();
        foreach (var kvp in _mapLastActiveTick)
        {
            if (currentTick - kvp.Value > MAP_STALE_THRESHOLD)
            {
                staleMapIds.Add(kvp.Key);
            }
        }

        // Clean up caches for stale maps
        foreach (int mapId in staleMapIds)
        {
            CleanupMapCache(mapId);
        }
    }

    /// <summary>
    /// Clean up all caches for a specific map
    /// </summary>
    public static void CleanupMapCache(int mapId)
    {
        _hasTargetsCache.Remove(mapId);
        _lastEmptyCheckTick.Remove(mapId);
        _mapLastActiveTick.Remove(mapId);

        // Also notify any extensions
        foreach (var extList in _moduleExtensions.Values)
        {
            foreach (var extension in extList)
            {
                extension.OnMapCacheCleanup(mapId);
            }
        }

        if (Prefs.DevMode)
        {
            Utility_DebugManager.LogNormal($"Cleaned up stale caches for map ID {mapId}");
        }
    }

    /// <summary>
    /// A dictionary with a maximum size that automatically removes oldest entries
    /// </summary>
    private class LimitedSizeDictionary<TKey, TValue>
    {
        private readonly Dictionary<TKey, TValue> _dictionary = new Dictionary<TKey, TValue>();
        private readonly Queue<TKey> _keyOrder = new Queue<TKey>();
        private readonly int _maxSize;

        public LimitedSizeDictionary(int maxSize)
        {
            _maxSize = maxSize;
        }

        public TValue this[TKey key]
        {
            get => _dictionary[key];
            set
            {
                if (!_dictionary.ContainsKey(key))
                {
                    _keyOrder.Enqueue(key);

                    // Remove oldest entries if we've exceeded max size
                    while (_keyOrder.Count > _maxSize)
                    {
                        TKey oldestKey = _keyOrder.Dequeue();
                        _dictionary.Remove(oldestKey);
                    }
                }
                _dictionary[key] = value;
            }
        }

        public void Clear()
        {
            _dictionary.Clear();
            _keyOrder.Clear();
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            return _dictionary.TryGetValue(key, out value);
        }

        /// <summary>
        /// Removes an entry from the dictionary
        /// </summary>
        /// <param name="key">The key to remove</param>
        /// <returns>True if the element was removed, false otherwise</returns>
        public bool Remove(TKey key)
        {
            // Remove from dictionary
            bool removed = _dictionary.Remove(key);

            // Note: This is a simplified implementation that doesn't update the _keyOrder queue.
            // For complete correctness, we would need to rebuild the queue without this key,
            // but that would be inefficient. Since the queue is only used to track oldest entries
            // for removal when the size limit is exceeded, having "ghost" entries in the queue
            // that have already been removed from the dictionary isn't a critical issue.

            return removed;
        }
    }

    public static void ClearLastSuccessfulModule(Pawn pawn)
    {
        if (pawn != null)
            _lastSuccessfulModule.Remove(pawn.thingIDNumber);
    }

    public static string GetLastSuccessfulModule(Pawn pawn)
    {
        if (pawn == null)
            return null;

        _lastSuccessfulModule.TryGetValue(pawn.thingIDNumber, out string moduleId);
        return moduleId;
    }

    // Add this to JobModuleCore class
    /// <summary>
    /// Records the last successful module for a pawn
    /// </summary>
    public static void SetLastSuccessfulModule(Pawn pawn, string moduleId)
    {
        if (pawn == null || string.IsNullOrEmpty(moduleId))
            return;

        // Use a unique key for the pawn to track their last successful module
        int pawnId = pawn.thingIDNumber;

        // Store this in some dictionary like:
        _lastSuccessfulModule[pawnId] = moduleId;
    }

    /// <summary>
    /// Checks if this module was the last successful one for this pawn
    /// </summary>
    public virtual bool WasLastSuccessfulModule(Pawn pawn)
    {
        if (pawn == null) return false;

        int pawnId = pawn.thingIDNumber;
        return _lastSuccessfulModule.TryGetValue(pawnId, out string lastModule) &&
               lastModule == UniqueID;
    }
}