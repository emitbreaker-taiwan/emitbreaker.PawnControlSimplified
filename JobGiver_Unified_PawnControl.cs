using emitbreaker.PawnControl;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Verse;
using Verse.AI;

/// <summary>
/// A generic unified JobGiver that handles tasks through a pluggable module system.
/// This significantly reduces ThinkNode overhead while maintaining clear separation of concerns.
/// </summary>
/// <typeparam name="TModule">The type of job module this job giver manages</typeparam>
/// <typeparam name="TTarget">The type of target these modules work with</typeparam>
public abstract class JobGiver_Unified_PawnControl<TModule, TTarget> : ThinkNode_JobGiver
    where TModule : JobModule<TTarget>
    where TTarget : Thing
{
    // Cache of work settings last checked time
    private static readonly Dictionary<int, int> _lastWorkSettingsCheckTick = new Dictionary<int, int>();
    private const int WORK_SETTINGS_CHECK_INTERVAL = 300; // 5 seconds in ticks

    // Simple static module list sorted by priority (set once at registration)
    public static readonly List<TModule> _jobModules = new List<TModule>();

    // Target cache (refreshed periodically)
    public static readonly Dictionary<int, Dictionary<string, List<TTarget>>> _targetsByTypeCache = new Dictionary<int, Dictionary<string, List<TTarget>>>();

    // Cache of available work types per pawn
    protected static readonly Dictionary<int, HashSet<string>> _pawnWorkTypeCache = new Dictionary<int, HashSet<string>>();

    // Cache last updated time
    protected static readonly Dictionary<int, int> _pawnCacheLastUpdateTick = new Dictionary<int, int>();

    // Change from instance property to static property
    protected virtual string WorkTypeName { get; }

    // Memory-managed reachability cache with automatic cleanup
    protected static readonly ReachabilityCache<TTarget> _reachabilityCache = new ReachabilityCache<TTarget>(1500); // Limit to 1500 entries per map

    protected static int _lastCacheUpdateTick = -999;
    protected const int CACHE_UPDATE_INTERVAL = 120; // Update every 2 seconds

    // A quick lookup for maps with any valid targets
    public static readonly Dictionary<int, bool> _mapHasTargets = new Dictionary<int, bool>();

    // Map activity tracking to help with cache cleanup
    private static readonly Dictionary<int, int> _mapLastUsedTick = new Dictionary<int, int>();
    private static readonly HashSet<int> _activeMaps = new HashSet<int>();

    private static readonly Dictionary<string, string> _availableWorkTypeNames = new Dictionary<string, string>();
    private static readonly Dictionary<string, float> _workTypePriority = new Dictionary<string, float>();

    // Distance thresholds for bucketing
    protected static readonly float[] DISTANCE_THRESHOLDS = new float[] { 400f, 900f, 1600f }; // 20, 30, 40 tiles

    // Lookup dictionary for faster module retrieval by ID
    private static readonly Dictionary<string, TModule> _moduleById = new Dictionary<string, TModule>();

    // Priority tracking for adaptive module updates
    private static readonly Dictionary<string, PriorityTracker> _modulePriorityTracking = new Dictionary<string, PriorityTracker>();

    // Add at the bottom of the constructor or in a static constructor
    static JobGiver_Unified_PawnControl()
    {
        // Don't register anything in the base class constructor
        // Each derived class will handle its own registration
        if (typeof(JobGiver_Unified_PawnControl<TModule, TTarget>) == typeof(TModule).DeclaringType)
        {
            Utility_DebugManager.LogNormal($"Base JobGiver type initialized: {typeof(JobGiver_Unified_PawnControl<TModule, TTarget>).Name}");
        }
    }

    /// <summary>
    /// Register this job giver with the central registry
    /// </summary>
    protected static void RegisterWithRegistry()
    {
        // This just logs that registration should happen in derived classes
        if (typeof(JobGiver_Unified_PawnControl<TModule, TTarget>) == typeof(TModule).DeclaringType)
        {
            Utility_DebugManager.LogNormal($"Base JobGiver type registered: {typeof(JobGiver_Unified_PawnControl<TModule, TTarget>).Name}");
        }
    }

    /// <summary>
    /// Registers a new job module with improved priority handling and caching
    /// </summary>
    public static void RegisterModule(TModule module)
    {
        if (module == null) return;

        // Add to list
        _jobModules.Add(module);

        // Sort by priority (just once when registered)
        _jobModules.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        // Initialize priority tracking for this module
        if (!_modulePriorityTracking.ContainsKey(module.UniqueID))
        {
            _modulePriorityTracking[module.UniqueID] = new PriorityTracker();
        }

        // Log registration
        Utility_DebugManager.LogNormal($"Registered {module.WorkTypeName} job module: {module.UniqueID} with priority {module.Priority}");
    }

    /// <summary>
    /// Fast check to determine if a map has any targets from any modules
    /// </summary>
    protected static bool MapHasAnyTargets(Map map)
    {
        if (map == null) return false;
        int mapId = map.uniqueID;
        return _mapHasTargets.TryGetValue(mapId, out bool hasTargets) && hasTargets;
    }

    /// <summary>
    /// Gets the priority for this job giver, dynamically adjusting based on module availability and success rates
    /// Required for proper functioning in ThinkNode_PrioritySorter
    /// </summary>
    /// <summary>
    /// Gets the priority for this job giver, dynamically adjusting based on module availability and success rates
    /// Required for proper functioning in ThinkNode_PrioritySorter
    /// </summary>
    public override float GetPriority(Pawn pawn)
    {
        EnsureDictionariesInitialized();

        // Exit early if no pawn or map
        if (pawn == null || pawn.Map == null)
            return -100f;

        var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
        if (modExtension == null) return -100f;

        if (!Utility_TagManager.HasTagSet(pawn.def, modExtension)) return -100f;

        if (Utility_Common.WorkTypeDefNamed(WorkTypeName) == null || !Utility_TagManager.WorkTypeSettingEnabled(pawn, Utility_Common.WorkTypeDefNamed(WorkTypeName))) return -100f;

        // Get all work types this JobGiver handles
        foreach (var module in _jobModules)
        {
            if (string.IsNullOrEmpty(module.UniqueID) || string.IsNullOrEmpty(module.WorkTypeName))
                continue;

            if (!CanModuleBeUsedByPawn(module, pawn))
                continue;

            // FIXED: Use WorkTypeName as the key for both dictionaries
            if (!_availableWorkTypeNames.ContainsKey(module.WorkTypeName))
            {
                _availableWorkTypeNames.Add(module.WorkTypeName, module.UniqueID);
            }

            if (!_workTypePriority.ContainsKey(module.WorkTypeName))
            {
                // FIXED: Use WorkTypeName as key, not UniqueID
                _workTypePriority.Add(module.WorkTypeName, module.Priority);
            }
        }

        // Check if the dictionary is empty
        if (_workTypePriority == null || _workTypePriority.Count == 0)
            return GetBasePriority(WorkTypeName);  // Default priority if no entries

        // Find the maximum value in the dictionary
        return _workTypePriority.Values.Max();
    }

    // A method to ensure initialization
    private static void EnsureDictionariesInitialized()
    {
        if (_workTypePriority.Count > 0)
            return;  // Already initialized

        foreach (var module in _jobModules)
        {
            if (string.IsNullOrEmpty(module.UniqueID) || string.IsNullOrEmpty(module.WorkTypeName))
                continue;

            if (!_availableWorkTypeNames.ContainsKey(module.WorkTypeName))
            {
                _availableWorkTypeNames.Add(module.WorkTypeName, module.UniqueID);
            }

            if (!_workTypePriority.ContainsKey(module.WorkTypeName))
            {
                _workTypePriority.Add(module.WorkTypeName, module.Priority);
            }
        }
    }

    private bool CanModuleBeUsedByPawn(TModule module, Pawn pawn)
    {
        if (pawn == null || pawn.Map == null)
            return false;

        var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
        if (modExtension == null) return false;

        if (!Utility_TagManager.WorkEnabled(pawn.def, module.WorkTypeName)) return false;

        // 1. Check if the pawn can do this work type
        if (!module.WorkTypeApplies(pawn))
            return false;

        // 2. Check quick filter rules (skills, policies, etc)
        if (!module.QuickFilterCheck(pawn))
            return false;

        // 3. Check if the module has any targets on the map
        if (!module.HasTargets(pawn.Map))
            return false;

        // 4. Check if this module's work type has high enough priority
        if (_workTypePriority != null && _workTypePriority.Count > 0)
        {
            float maxPriority = _workTypePriority.Values.Max();
            var highPriorityWorkTypes = _workTypePriority
                .Where(kv => kv.Value == maxPriority)
                .Select(kv => kv.Key)
                .ToHashSet();

            if (!highPriorityWorkTypes.Contains(module.WorkTypeName))
                return false;
        }

        // All checks passed
        return true;
    }

    /// <summary>
    /// Gets the base priority value for this job giver type
    /// Can be overridden in subclasses to provide work-type specific priorities
    /// </summary>
    private static float GetBasePriority(string WorkTypeName)
    {
        switch (WorkTypeName)
        {
            // Emergency/Critical (naturalPriority 1300+)
            case "Firefighter": return 9.0f;  // naturalPriority 1400
            case "Patient": return 8.8f;  // naturalPriority 1350
            case "Doctor": return 8.5f;  // naturalPriority 1300

            // High Priority (naturalPriority 1000-1300)
            case "PatientBedRest": return 8.0f;  // naturalPriority 1200
            case "BasicWorker": return 7.8f;  // naturalPriority 1150
            case "Childcare": return 7.5f;  // naturalPriority 1175 (Biotech DLC)
            case "Warden": return 7.2f;  // naturalPriority 1100
            case "Handling": return 7.0f;  // naturalPriority 1050
            case "Cooking": return 6.8f;  // naturalPriority 1000

            // Medium-High Priority (naturalPriority 600-950)
            case "Hunting": return 6.5f;  // naturalPriority 950
            case "Construction": return 6.2f;  // naturalPriority 900
            case "Growing": return 5.8f;  // naturalPriority 700
            case "Mining": return 5.5f;  // naturalPriority 600

            // Medium Priority (naturalPriority 400-500)
            case "PlantCutting": return 5.2f;  // naturalPriority 500
            case "Smithing": return 4.9f;  // naturalPriority 470
            case "Tailoring": return 4.7f;  // naturalPriority 450
            case "Art": return 4.5f;  // naturalPriority 430
            case "Crafting": return 4.3f;  // naturalPriority 400

            // Low Priority (naturalPriority <400)
            case "Hauling": return 3.9f;  // naturalPriority 300
            case "Cleaning": return 3.5f;  // naturalPriority 200
            case "Research": return 3.2f;  // naturalPriority 100
            case "DarkStudy": return 3.0f;  // naturalPriority 150 (Ideology DLC)

            // Default for any unspecified work types
            default: return 5.0f;
        }
    }

    protected override Job TryGiveJob(Pawn pawn)
    {
        // Early validation - fail fast
        if (pawn == null || pawn.Map == null) return null;
        int mapId = pawn.Map.uniqueID;

        // Quick check: if this map has no targets at all, quit early
        if (!MapHasAnyTargets(pawn.Map)) return null;

        // Get the modules this pawn is allowed to run based on work settings and tags
        var eligibleModules = Utility_PawnModuleFilter
            .GetAllowedModules<TTarget>(pawn)
            .Where(m => m.WorkTypeApplies(pawn)) // Apply dynamic check (skills, etc)
            .ToList();

        // If no modules are eligible, exit early
        if (eligibleModules.Count == 0) return null;

        // Sort by priority (most important first)
        eligibleModules.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        // Try each module in priority order
        foreach (var module in eligibleModules)
        {
            // Skip if no targets for this module
            if (!_targetsByTypeCache.TryGetValue(mapId, out var mapCache) ||
                !mapCache.TryGetValue(module.UniqueID, out var targets) ||
                targets.Count == 0)
                continue;

            // Skip if module-specific quick filter fails
            if (!module.QuickFilterCheck(pawn))
                continue;

            // Find valid target using distance bucketing
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                targets,
                t => (t.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS);

            // Find first valid target using bucketing & reachability cache
            TTarget target = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (t, p) => module.ValidateJob(t, p),
                _reachabilityCache.GetMapCache(mapId));

            // If valid target found, create job
            if (target != null)
            {
                try
                {
                    Job job = module.CreateJob(pawn, target);
                    if (job != null)
                    {
                        // Track success for adaptive prioritization
                        JobModuleCore.SetLastSuccessfulModule(pawn, module.UniqueID);

                        // Record success in tracker
                        if (_modulePriorityTracking.TryGetValue(module.UniqueID, out var tracker))
                        {
                            tracker.RecordSuccess();
                        }

                        return job;
                    }
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"Error creating job from module {module.UniqueID}: {ex}");
                }
            }

            // Record failure for this module
            if (_modulePriorityTracking.TryGetValue(module.UniqueID, out var failTracker))
            {
                failTracker.RecordFailure();
            }
        }

        // No job found from any eligible module
        return null;
    }

    /// <summary>
    /// Updates the cache of work types available for this pawn based on their skills, settings, and available modules
    /// </summary>
    private void UpdatePawnWorkTypeCache(Pawn pawn)
    {
        if (pawn == null) return;

        int pawnId = pawn.thingIDNumber;
        int currentTick = Find.TickManager.TicksGame;

        // Create a new set for this pawn if it doesn't exist
        if (!_pawnWorkTypeCache.ContainsKey(pawnId))
        {
            _pawnWorkTypeCache[pawnId] = new HashSet<string>();
        }
        else
        {
            // Clear existing work types to rebuild
            _pawnWorkTypeCache[pawnId].Clear();
        }

        // Get pawn's mod extension
        var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
        if (modExtension == null) return;

        // If pawn has no tag set, they can't work
        if (!Utility_TagManager.HasTagSet(pawn.def, modExtension)) return;

        // Cache all work types this pawn can perform
        foreach (var module in _jobModules)
        {
            // Skip modules with invalid work types
            if (string.IsNullOrEmpty(module.WorkTypeName))
                continue;

            // Check if this work type is enabled for this pawn's race in general
            if (!Utility_TagManager.WorkEnabled(pawn.def, module.WorkTypeName))
                continue;

            // Check if this specific pawn has this work type enabled in their work tab
            var workTypeDef = Utility_Common.WorkTypeDefNamed(module.WorkTypeName);
            if (workTypeDef == null || !Utility_TagManager.WorkTypeSettingEnabled(pawn, workTypeDef))
                continue;

            // Check if the module itself thinks the pawn can do this work type
            if (!module.WorkTypeApplies(pawn))
                continue;

            // All checks passed, add to cache
            _pawnWorkTypeCache[pawnId].Add(module.WorkTypeName);
        }

        // Record when we last updated this pawn's cache
        _pawnCacheLastUpdateTick[pawnId] = currentTick;

        // Log detailed info in dev mode
        if (Prefs.DevMode)
        {
            int workTypeCount = _pawnWorkTypeCache[pawnId].Count;
            string workTypes = string.Join(", ", _pawnWorkTypeCache[pawnId]);
            Utility_DebugManager.LogNormal($"Updated work types for {pawn.LabelShort}: {workTypeCount} types [{workTypes}]");
        }
    }

    private List<TModule> FilterEligibleModules(Pawn pawn)
    {
        // Create a thread-safe container for results
        var eligibleModules = new List<TModule>();
        var moduleArray = _jobModules.ToArray(); // Capture for thread safety

        // Perform parallel filtering of modules
        Parallel.ForEach(moduleArray, module => {
            // Quick reject: Check work settings and tags
            if (!Utility_TagManager.WorkEnabled(pawn.def, module.WorkTypeName) ||
                !module.WorkTypeApplies(pawn))
                return;

            // Quick reject: Check if module should be skipped for this map
            if (module.ShouldSkipModule(pawn.Map))
                return;

            // Quick reject: Check other fast-fail conditions
            if (!module.QuickFilterCheck(pawn))
                return;

            // Check if the module has any targets
            if (!module.HasTargets(pawn.Map))
                return;

            // Passed all filters, add to eligible list (with lock)
            lock (eligibleModules)
            {
                eligibleModules.Add(module);
            }
        });

        return eligibleModules;
    }

    private Job FindOptimalJob(Pawn pawn)
    {
        // Get eligible modules in parallel
        var eligibleModules = FilterEligibleModules(pawn);

        // If no eligible modules, return null
        if (eligibleModules.Count == 0) return null;

        // Sort by absolute priority (ignoring work type)
        eligibleModules.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        // Try modules in priority order
        foreach (var module in eligibleModules)
        {
            Job job = TryGetJobFromModule(pawn, module);
            if (job != null)
            {
                // Track success for adaptive prioritization
                JobModuleCore.SetLastSuccessfulModule(pawn, module.UniqueID);

                // Update success rate tracking
                if (!_modulePriorityTracking.ContainsKey(module.UniqueID))
                    _modulePriorityTracking[module.UniqueID] = new PriorityTracker();

                _modulePriorityTracking[module.UniqueID].RecordSuccess();

                return job;
            }

            // Record failure for this module
            if (_modulePriorityTracking.ContainsKey(module.UniqueID))
                _modulePriorityTracking[module.UniqueID].RecordFailure();
        }

        // No job found
        return null;
    }

    protected static void RegisterDerivedJobGiver<TDerived>(string workTypeName, float priority)
    where TDerived : JobGiver_Unified_PawnControl<TModule, TTarget>
    {
        Type jobGiverType = typeof(TDerived);
        HashSet<string> workTypes = new HashSet<string> { workTypeName };

        // Add work types from modules
        foreach (var module in _jobModules)
        {
            if (!string.IsNullOrEmpty(module.WorkTypeName) && module.WorkTypeName != workTypeName)
                workTypes.Add(module.WorkTypeName);
        }

        // Register with the registry
        JobGiverRegistry.Register(jobGiverType, priority, workTypes);

        Utility_DebugManager.LogNormal($"Registered {jobGiverType.Name} for work type {workTypeName} with priority {priority}");
    }

    /// <summary>
    /// Try to get a job from a specific module
    /// </summary>
    private Job TryGetJobFromModule(Pawn pawn, TModule module)
    {
        int mapId = pawn.Map.uniqueID;
        string moduleId = module.UniqueID;

        // Skip if no targets for this module
        if (!_targetsByTypeCache.TryGetValue(mapId, out var mapCache) ||
            !mapCache.TryGetValue(moduleId, out var targets) ||
            targets.Count == 0)
            return null;

        // Create distance buckets for efficient selection
        var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
            pawn,
            targets,
            t => (t.Position - pawn.Position).LengthHorizontalSquared,
            DISTANCE_THRESHOLDS);

        // Find first valid target using bucketing and caching
        TTarget target = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
            buckets,
            pawn,
            (t, p) => module.ValidateJob(t, p),
            _reachabilityCache.GetMapCache(mapId));

        // Create job if target found
        if (target != null)
        {
            try
            {
                return module.CreateJob(pawn, target);
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error creating job from module {moduleId}: {ex}");
            }
        }

        // No valid job from this module
        return null;
    }

    /// <summary>
    /// Updates the shared cache for all targets
    /// </summary>
    protected virtual void UpdateTargetCache(Map map)
    {
        if (map == null) return;

        int currentTick = Find.TickManager.TicksGame;
        int mapId = map.uniqueID;

        // Only update periodically to save performance
        if (currentTick <= _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL &&
            _targetsByTypeCache.ContainsKey(mapId))
            return;

        // Initialize target cache for this map if needed
        if (!_targetsByTypeCache.ContainsKey(mapId))
            _targetsByTypeCache[mapId] = new Dictionary<string, List<TTarget>>();

        // Update cache for each module, skipping those that should be skipped
        foreach (var module in _jobModules)
        {
            // Skip this module if it should be skipped for this map
            if (module.ShouldSkipModule(map))
            {
                // Make sure we don't think this module has targets
                module.SetHasTargets(map, false);

                // Clear any existing targets for this module
                string moduleId2 = module.UniqueID;
                if (_targetsByTypeCache[mapId].ContainsKey(moduleId2))
                    _targetsByTypeCache[mapId][moduleId2].Clear();

                continue;
            }

            // Initialize module's target list if needed
            string moduleId = module.UniqueID;
            if (!_targetsByTypeCache[mapId].ContainsKey(moduleId))
            {
                _targetsByTypeCache[mapId][moduleId] = new List<TTarget>();
            }
            else
            {
                _targetsByTypeCache[mapId][moduleId].Clear();
            }

            // Update this module's targets
            module.UpdateCache(map, _targetsByTypeCache[mapId][moduleId]);
        }

        // Update last cache update tick
        _lastCacheUpdateTick = currentTick;

        // Update map-level target status
        bool mapHasAnyTargets = false;
        foreach (var module in _jobModules)
        {
            if (module.HasTargets(map))
            {
                mapHasAnyTargets = true;
                break;
            }
        }

        _mapHasTargets[mapId] = mapHasAnyTargets;

        // Cleanup stale reachability data occasionally
        if (currentTick % 6000 == 0) // Every 100 seconds
        {
            CleanupStaleCaches();
        }
    }

    /// <summary>
    /// Clean up stale caches from maps that are no longer active
    /// </summary>
    private void CleanupStaleCaches()
    {
        int currentTick = Find.TickManager.TicksGame;
        List<int> mapsToRemove = new List<int>();

        // Identify maps that haven't been accessed in a long time
        foreach (var kvp in _mapLastUsedTick)
        {
            if (currentTick - kvp.Value > 300000) // ~5 minutes
            {
                mapsToRemove.Add(kvp.Key);
            }
        }

        // Clean up those maps' caches
        foreach (int mapId in mapsToRemove)
        {
            // Skip if this is still an active map
            if (_activeMaps.Contains(mapId))
                continue;

            // Clean up caches
            _targetsByTypeCache.Remove(mapId);
            _mapHasTargets.Remove(mapId);
            _mapLastUsedTick.Remove(mapId);
            _reachabilityCache.ClearMapCache(mapId);

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Cleaned up stale caches for map ID {mapId}");
            }
        }
    }

    public static void ClearPawnCache(Pawn pawn)
    {
        if (pawn == null) return;

        int pawnId = pawn.thingIDNumber;
        _pawnWorkTypeCache.Remove(pawnId);
        _pawnCacheLastUpdateTick.Remove(pawnId);
    }

    /// <summary>
    /// Reset caches when loading game or changing maps
    /// </summary>
    public static void ResetCache()
    {
        // Clear all caches
        _targetsByTypeCache.Clear();
        _reachabilityCache.ClearAllCaches();
        _mapHasTargets.Clear();
        _lastCacheUpdateTick = -999;
        _activeMaps.Clear();
        _mapLastUsedTick.Clear();
        _pawnWorkTypeCache.Clear();
        _pawnCacheLastUpdateTick.Clear();

        // Clear module filter cache
        Utility_PawnModuleFilter.ClearAllCaches();

        // Reset static data for modules
        foreach (var module in _jobModules)
        {
            module.ResetStaticData();
        }
    }

    /// <summary>
    /// Reset job module cache when forced to update for dynamic injections
    /// </summary>
    public static void ResetJobModuleCache()  // Removed the 'new' keyword
    {
        _lastCacheUpdateTick = -999;

        if (Current.Game?.Maps != null)
        {
            foreach (var map in Current.Game.Maps)
            {
                int mapId = map.uniqueID;
                if (_mapHasTargets.ContainsKey(mapId))
                {
                    _mapHasTargets[mapId] = true; // Force recheck for all maps
                }
            }
        }

        Utility_DebugManager.LogNormal("Reset PlantCutting job module cache");
    }

    // Add to JobGiver_Unified_PawnControl class
    public static void DebugLogModules()
    {
        if (!Prefs.DevMode) return;

        Utility_DebugManager.LogNormal($"JobGiver {typeof(JobGiver_Unified_PawnControl<TModule, TTarget>).Name} has {_jobModules.Count} modules:");

        foreach (var module in _jobModules)
        {
            Utility_DebugManager.LogNormal($"- {module.UniqueID} (WorkType: {module.WorkTypeName}, Priority: {module.Priority})");
        }
    }

    /// <summary>
    /// Memory-managed reachability cache with size limits
    /// </summary>
    protected class ReachabilityCache<T> where T : Thing
    {
        private readonly Dictionary<int, LRUCache<T, bool>> _mapCaches =
            new Dictionary<int, LRUCache<T, bool>>();
        private readonly int _maxEntriesPerMap;

        public ReachabilityCache(int maxEntriesPerMap)
        {
            _maxEntriesPerMap = maxEntriesPerMap;
        }

        /// <summary>
        /// Get cache for a specific map
        /// </summary>
        public Dictionary<T, bool> GetMapCache(int mapId)
        {
            if (!_mapCaches.TryGetValue(mapId, out var cache))
            {
                cache = new LRUCache<T, bool>(_maxEntriesPerMap);
                _mapCaches[mapId] = cache;
            }

            return cache;
        }

        /// <summary>
        /// Clear cache for a specific map
        /// </summary>
        public void ClearMapCache(int mapId)
        {
            if (_mapCaches.TryGetValue(mapId, out var cache))
            {
                cache.Clear();
            }
        }

        /// <summary>
        /// Clear all caches
        /// </summary>
        public void ClearAllCaches()
        {
            foreach (var cache in _mapCaches.Values)
            {
                cache.Clear();
            }

            _mapCaches.Clear();
        }
    }

    /// <summary>
    /// Tracks module success rate for dynamic prioritization
    /// </summary>
    private class PriorityTracker
    {
        private int _totalChecks = 0;
        private int _successes = 0;
        private int _recentTotal = 0;
        private int _recentSuccesses = 0;
        private readonly Queue<bool> _recentResults = new Queue<bool>();
        private const int RECENT_HISTORY_SIZE = 50;

        public void RecordSuccess()
        {
            _totalChecks++;
            _successes++;

            // Add to recent results
            _recentResults.Enqueue(true);
            _recentTotal++;
            _recentSuccesses++;

            // Maintain history size
            if (_recentResults.Count > RECENT_HISTORY_SIZE)
            {
                bool oldest = _recentResults.Dequeue();
                if (oldest)
                    _recentSuccesses--;
                _recentTotal--;
            }
        }

        public void RecordFailure()
        {
            _totalChecks++;

            // Add to recent results
            _recentResults.Enqueue(false);
            _recentTotal++;

            // Maintain history size
            if (_recentResults.Count > RECENT_HISTORY_SIZE)
            {
                bool oldest = _recentResults.Dequeue();
                if (oldest)
                    _recentSuccesses--;
                _recentTotal--;
            }
        }

        public float GetSuccessRate()
        {
            // Prioritize recent success rate with a minimum check count
            if (_recentTotal >= 10)
                return (float)_recentSuccesses / _recentTotal;

            // Fall back to overall rate with a minimum
            if (_totalChecks >= 5)
                return (float)_successes / _totalChecks;

            // Default neutral rate for new modules
            return 0.5f;
        }
    }

    /// <summary>
    /// Generic LRU cache with size limits that automatically evicts oldest entries
    /// </summary>
    public class LRUCache<TKey, TValue> : Dictionary<TKey, TValue>
    {
        private readonly LinkedList<TKey> _lruList = new LinkedList<TKey>();
        private readonly int _capacity;

        public LRUCache(int capacity)
        {
            _capacity = capacity;
        }

        public new TValue this[TKey key]
        {
            get
            {
                TValue value = base[key];

                // Move to front of LRU list
                _lruList.Remove(key);
                _lruList.AddFirst(key);

                return value;
            }
            set
            {
                // If key already exists, remove it from LRU tracking
                if (ContainsKey(key))
                {
                    _lruList.Remove(key);
                }
                else if (Count >= _capacity) // Need to remove oldest item
                {
                    // Remove least recently used item
                    TKey oldest = _lruList.Last.Value;
                    _lruList.RemoveLast();
                    Remove(oldest);
                }

                // Add/update value and add to front of LRU list
                base[key] = value;
                _lruList.AddFirst(key);
            }
        }

        public new bool TryGetValue(TKey key, out TValue value)
        {
            if (base.TryGetValue(key, out value))
            {
                // Move to front of LRU list
                _lruList.Remove(key);
                _lruList.AddFirst(key);
                return true;
            }

            return false;
        }

        public new void Clear()
        {
            base.Clear();
            _lruList.Clear();
        }
    }
}