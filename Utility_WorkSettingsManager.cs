using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace emitbreaker.PawnControl
{
    public static class Utility_WorkSettingsManager
    {
        private static readonly HashSet<Pawn> _workgiverReplacementDone = new HashSet<Pawn>();

        // Add map-level cache for spawned pawns with tick-based invalidation
        private static readonly Dictionary<int, List<Pawn>> _mapPawnCache = new Dictionary<int, List<Pawn>>();
        private static readonly Dictionary<int, int> _mapPawnCacheLastTick = new Dictionary<int, int>();

        // Add precomputed WorkGiverDef arrays for faster lookup
        private static readonly Dictionary<WorkTypeDef, WorkGiverDef[]> _workTypeGiversCache = new Dictionary<WorkTypeDef, WorkGiverDef[]>();

        // NEW: Per-pawn cache of enabled WorkGiverDefs
        private static readonly Dictionary<int, WorkGiverDef[]> _pawnEnabledWorkGiversCache = new Dictionary<int, WorkGiverDef[]>();

        // Replace with these tracking dictionaries
        private static readonly Dictionary<int, int[]> _workPrioritySnapshots = new Dictionary<int, int[]>();
        private static readonly Dictionary<int, int> _lastCheckTick = new Dictionary<int, int>();
        private static readonly HashSet<int> _dirtyPawns = new HashSet<int>();

        // Constants for check intervals
        private const int NORMAL_CHECK_INTERVAL = 600; // 10 seconds for normal checking
        private const int AFTER_WORKTAB_CHECK_INTERVAL = 10; // Quick check after work tab events
        private const int MAX_PRIORITY = 4; // RimWorld's max work priority

        // Store current window state to detect Work Tab closing
        private static bool _workTabWasOpen = false;
        private static int _workTabCloseTime = 0;

        // Track whether a pawn was recently spawned
        private static readonly HashSet<int> _recentlySpawnedPawns = new HashSet<int>();

        // NEW: Track pawns that have had listeners attached to avoid duplicate attachments
        private static readonly HashSet<int> _pawnsWithListeners = new HashSet<int>();

        /// <summary>
        /// Initialize the WorkGiver cache for better performance
        /// </summary>
        public static void InitializeWorkGiverCache()
        {
            _workTypeGiversCache.Clear();

            foreach (WorkTypeDef workType in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                // Find all WorkGiverDefs with priority > 0 for this work type
                List<WorkGiverDef> givers = new List<WorkGiverDef>();

                // Replace LINQ with manual loop
                foreach (WorkGiverDef def in DefDatabase<WorkGiverDef>.AllDefsListForReading)
                {
                    if (def.workType == workType && def.priorityInType > 0)
                    {
                        givers.Add(def);
                    }
                }

                // Sort by priority descending (manually instead of using LINQ)
                givers.Sort((a, b) => b.priorityInType.CompareTo(a.priorityInType));

                _workTypeGiversCache[workType] = givers.ToArray();
            }

            Utility_DebugManager.LogNormal($"Initialized WorkGiver cache for {_workTypeGiversCache.Count} work types");
        }

        /// <summary>
        /// Gets WorkGivers for a specific work type from cache
        /// </summary>
        public static WorkGiverDef[] GetCachedWorkGivers(WorkTypeDef workType)
        {
            if (workType == null)
                return Array.Empty<WorkGiverDef>();

            // Initialize cache if needed
            if (_workTypeGiversCache.Count == 0)
                InitializeWorkGiverCache();

            if (_workTypeGiversCache.TryGetValue(workType, out var givers))
                return givers;

            return Array.Empty<WorkGiverDef>();
        }

        /// <summary>
        /// Gets all enabled WorkGiverDefs for a pawn, using cached values when possible
        /// </summary>
        public static WorkGiverDef[] GetEnabledWorkGiversForPawn(Pawn pawn)
        {
            if (pawn == null || pawn.workSettings == null)
                return Array.Empty<WorkGiverDef>();

            int pawnId = pawn.thingIDNumber;

            // Return from cache if available
            if (_pawnEnabledWorkGiversCache.TryGetValue(pawnId, out var cachedGivers))
                return cachedGivers;

            // Attach listener if not already attached
            AttachWorkSettingsChangeListener(pawn);

            // Otherwise build the array of enabled work givers
            RebuildEnabledWorkGiversForPawn(pawn);

            // Return the newly built cache (or empty if something went wrong)
            return _pawnEnabledWorkGiversCache.TryGetValue(pawnId, out var builtGivers)
                ? builtGivers
                : Array.Empty<WorkGiverDef>();
        }

        /// <summary>
        /// Attaches a change listener to the pawn's work settings using priority snapshotting
        /// </summary>
        public static void AttachWorkSettingsChangeListener(Pawn pawn)
        {
            // Guard against null pawn or workSettings
            if (pawn == null || pawn.workSettings == null || _pawnsWithListeners.Contains(pawn.thingIDNumber))
                return;

            try
            {
                // Take initial snapshot
                TakeWorkPrioritySnapshot(pawn);

                // Mark pawn as having a listener
                _pawnsWithListeners.Add(pawn.thingIDNumber);

                if (Prefs.DevMode)
                {
                    Utility_DebugManager.LogNormal($"Started tracking work settings for {pawn.LabelShort}");
                }
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error attaching work settings tracking: {ex.Message}");
            }
        }

        /// <summary>
        /// Takes a snapshot of the pawn's current work priorities to detect future changes
        /// </summary>
        private static void TakeWorkPrioritySnapshot(Pawn pawn)
        {
            if (pawn?.workSettings == null) return;

            int pawnId = pawn.thingIDNumber;
            var workTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;
            int[] priorities = new int[workTypes.Count];

            // Store current priorities
            for (int i = 0; i < workTypes.Count; i++)
            {
                priorities[i] = pawn.workSettings.GetPriority(workTypes[i]);
            }

            // Save snapshot
            _workPrioritySnapshots[pawnId] = priorities;
            _lastCheckTick[pawnId] = Find.TickManager.TicksGame;
        }

        /// <summary>
        /// Checks if work priorities have changed since the last snapshot
        /// </summary>
        private static bool HaveWorkPrioritiesChanged(Pawn pawn)
        {
            if (pawn?.workSettings == null) return false;

            int pawnId = pawn.thingIDNumber;
            if (!_workPrioritySnapshots.TryGetValue(pawnId, out var oldPriorities))
                return false; // No snapshot to compare against

            var workTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;

            // Skip unnecessary checks if we have a size mismatch (mod change?)
            if (oldPriorities.Length != workTypes.Count)
            {
                TakeWorkPrioritySnapshot(pawn); // Re-baseline
                return false;
            }

            // Compare priorities
            for (int i = 0; i < workTypes.Count; i++)
            {
                if (pawn.workSettings.GetPriority(workTypes[i]) != oldPriorities[i])
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Call this method from your tick/update system to detect work setting changes
        /// </summary>
        public static void CheckForWorkSettingsChanges()
        {
            int currentTick = Find.TickManager.TicksGame;
            bool workTabIsOpen = Find.WindowStack?.Windows?.Any(w => w.GetType().Name.Contains("MainTabWindow_Work")) ?? false;

            // Track Work Tab closing
            if (!workTabIsOpen && _workTabWasOpen)
            {
                _workTabCloseTime = currentTick;
                foreach (var pawn in PawnsFinder.AllMapsCaravansAndTravelingTransportPods_Alive_FreeColonistsAndPrisoners)
                {
                    if (pawn?.workSettings != null)
                    {
                        _dirtyPawns.Add(pawn.thingIDNumber);
                    }
                }
            }

            // Update tracking
            _workTabWasOpen = workTabIsOpen;

            // Process recently spawned pawns without delay
            if (_recentlySpawnedPawns.Count > 0)
            {
                foreach (int pawnId in _recentlySpawnedPawns.ToList())
                {
                    _dirtyPawns.Add(pawnId);
                }
                _recentlySpawnedPawns.Clear();
            }

            // Process all pawns with listeners for periodic checks
            List<int> pawnIdsToCheck = new List<int>(_pawnsWithListeners);

            foreach (int pawnId in pawnIdsToCheck)
            {
                // Check if we need to evaluate this pawn
                bool shouldCheck = false;
                int checkInterval = NORMAL_CHECK_INTERVAL;

                // Check more frequently after work tab closes
                if (_dirtyPawns.Contains(pawnId))
                {
                    shouldCheck = true;
                    _dirtyPawns.Remove(pawnId);
                }
                else if (currentTick - _workTabCloseTime < 300) // Within 5 seconds of tab closing
                {
                    checkInterval = AFTER_WORKTAB_CHECK_INTERVAL;
                }

                // Check if it's time to evaluate
                if (!shouldCheck && _lastCheckTick.TryGetValue(pawnId, out int lastCheck))
                {
                    shouldCheck = (currentTick - lastCheck) >= checkInterval;
                }

                if (shouldCheck)
                {
                    // Find the pawn
                    Pawn pawn = null;
                    foreach (var map in Find.Maps)
                    {
                        pawn = map.mapPawns.AllPawns.FirstOrDefault(p => p.thingIDNumber == pawnId);
                        if (pawn != null) break;
                    }

                    // Check caravans
                    if (pawn == null)
                    {
                        foreach (var caravan in Find.WorldObjects.Caravans)
                        {
                            pawn = caravan.pawns.InnerListForReading.FirstOrDefault(p => p.thingIDNumber == pawnId);
                            if (pawn != null) break;
                        }
                    }

                    // If pawn is found, check for changes
                    if (pawn != null && pawn.workSettings != null)
                    {
                        if (HaveWorkPrioritiesChanged(pawn))
                        {
                            // Work settings changed, update cache and notify
                            TakeWorkPrioritySnapshot(pawn);
                            RebuildEnabledWorkGiversForPawn(pawn);
                            ResetJobModuleCache(pawn);

                            if (Prefs.DevMode)
                            {
                                Utility_DebugManager.LogNormal($"Detected work settings change for {pawn.LabelShort}");
                            }
                        }
                        else
                        {
                            // Just update the last check time
                            _lastCheckTick[pawnId] = currentTick;
                        }
                    }
                    else
                    {
                        // Pawn no longer exists or no longer has work settings, clean up
                        _pawnsWithListeners.Remove(pawnId);
                        _workPrioritySnapshots.Remove(pawnId);
                        _lastCheckTick.Remove(pawnId);
                        _dirtyPawns.Remove(pawnId);

                        if (Prefs.DevMode)
                        {
                            Utility_DebugManager.LogNormal($"Cleaned up work settings tracking for removed pawn (ID: {pawnId})");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Call this when a pawn is spawned to mark its work settings for immediate checking
        /// </summary>
        public static void NotifyPawnSpawned(Pawn pawn)
        {
            if (pawn?.thingIDNumber > 0)
            {
                _recentlySpawnedPawns.Add(pawn.thingIDNumber);

                // Also, if this is the first time we're seeing this pawn, attach tracking
                if (!_pawnsWithListeners.Contains(pawn.thingIDNumber) && pawn.workSettings != null)
                {
                    AttachWorkSettingsChangeListener(pawn);
                }
            }
        }

        /// <summary>
        /// Resets job module caches for a specific pawn when their work settings change
        /// </summary>
        private static void ResetJobModuleCache(Pawn pawn)
        {
            if (pawn == null) return;

            // Clear the disabled work type cache for this pawn
            Utility_JobGiverManager.ClearDisabledWorkTypeCache(pawn);

            // ENHANCED: Use aggressive cache reset to ensure fresh job evaluation
            Utility_CacheManager.ResetAllJobModuleCaches(
                pawn,
                JobModuleCacheResetScope.WorkSettings | JobModuleCacheResetScope.SpawnSetup
            );

            // Force immediate evaluation on next tick
            if (pawn.jobs != null && pawn.mindState != null)
            {
                pawn.mindState.Active = true;

                if (Prefs.DevMode)
                {
                    Utility_DebugManager.LogNormal($"Force activated mindState for {pawn.LabelShort} after work settings change");
                }
            }
        }

        /// <summary>
        /// Rebuilds the cached array of enabled work givers for a pawn
        /// </summary>
        private static void RebuildEnabledWorkGiversForPawn(Pawn pawn)
        {
            if (pawn == null || pawn.workSettings == null)
                return;

            int pawnId = pawn.thingIDNumber;
            List<WorkGiverDef> enabledGivers = new List<WorkGiverDef>();

            // Find all work types this pawn has enabled
            foreach (WorkTypeDef workType in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                // Skip if pawn doesn't have this work type enabled
                if (!pawn.workSettings.WorkIsActive(workType))
                    continue;

                // Get all work givers for this work type
                WorkGiverDef[] workGivers = GetCachedWorkGivers(workType);

                // Add all enabled work givers
                foreach (WorkGiverDef giver in workGivers)
                {
                    enabledGivers.Add(giver);
                }
            }

            // Store in cache
            _pawnEnabledWorkGiversCache[pawnId] = enabledGivers.ToArray();

            if (Prefs.DevMode && pawn.RaceProps.Humanlike)
            {
                Utility_DebugManager.LogNormal($"Rebuilt enabled WorkGivers for {pawn.LabelShort} - found {enabledGivers.Count} givers");
            }
        }

        /// <summary>
        /// Gets a cached list of all spawned pawns for a map
        /// </summary>
        public static List<Pawn> GetCachedMapPawns(Map map)
        {
            if (map == null)
                return new List<Pawn>();

            int mapId = map.uniqueID;
            int currentTick = Find.TickManager.TicksGame;

            // Return from cache if recent
            if (_mapPawnCache.TryGetValue(mapId, out var pawns) &&
                _mapPawnCacheLastTick.TryGetValue(mapId, out var lastTick) &&
                currentTick - lastTick < 180) // Refresh every 3 seconds
            {
                return pawns;
            }

            // Update cache
            if (!_mapPawnCache.ContainsKey(mapId))
                _mapPawnCache[mapId] = new List<Pawn>();
            else
                _mapPawnCache[mapId].Clear();

            // Manually add pawns instead of using AddRange to avoid temporary allocations
            List<Pawn> spawnedPawns = map.mapPawns.AllPawnsSpawned.ToList();
            for (int i = 0; i < spawnedPawns.Count; i++)
            {
                _mapPawnCache[mapId].Add(spawnedPawns[i]);

                // Ensure pawn has listener attached if needed
                if (spawnedPawns[i].workSettings != null && !_pawnsWithListeners.Contains(spawnedPawns[i].thingIDNumber))
                {
                    AttachWorkSettingsChangeListener(spawnedPawns[i]);
                }
            }

            _mapPawnCacheLastTick[mapId] = currentTick;

            return _mapPawnCache[mapId];
        }

        /// <summary>
        /// Removes a pawn's cached work givers when despawned or destroyed
        /// </summary>
        public static void CleanupPawnCache(Pawn pawn)
        {
            if (pawn == null) return;

            int pawnId = pawn.thingIDNumber;
            _pawnEnabledWorkGiversCache.Remove(pawnId);
            _pawnsWithListeners.Remove(pawnId);
            _workPrioritySnapshots.Remove(pawnId);
            _lastCheckTick.Remove(pawnId);
            _dirtyPawns.Remove(pawnId);
            _workgiverReplacementDone.Remove(pawn);
        }

        /// <summary>
        /// Fully initializes a pawn's work settings, think tree, and workgivers for modded behavior.
        /// - Ensures WorkSettings are created and initialized if missing or invalid.
        /// - Injects a modded think tree subtree for custom behavior.
        /// - Replaces WorkGivers with modded versions, optionally locking the cache.
        /// - Logs detailed debug information in developer mode.
        /// </summary>
        // Fully initialize a pawn without destroying existing priorities
        public static void FullInitializePawn(Pawn pawn, bool forceLock = true, string subtreeDefName = null)
        {
            if (pawn == null || pawn.def == null || pawn.def.race == null)
            {
                return;
            }

            // ✅ New Safe Check: Only inject subtree if mainWorkThinkTreeDefName was NOT injected statically
            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);

            if (modExtension == null)
            {
                return;
            }

            if (!Utility_ThinkTreeManager.HasAllowOrBlockWorkTag(pawn.def))
            {
                return;
            }

            // ✅ Ensure WorkSettings exist
            EnsureWorkSettingsInitialized(pawn);

            Utility_DebugManager.LogNormal($"Completed FullInitializePawn for {pawn.LabelShortCap} (forceLock={forceLock})");

            // Add at the end of the method
            if (Prefs.DevMode && modExtension.debugMode && pawn?.thinker?.MainThinkNodeRoot != null)
            {
                Utility_ThinkTreeManager.ValidateThinkTree(pawn);
            }
        }

        /// <summary>
        /// Utility class for managing pawn work settings and workgivers in the PawnControl mod.
        /// - Provides methods to initialize, replace, and lock work settings for modded pawns.
        /// - Supports backup and restoration of work priorities.
        /// - Includes safe methods to ensure pawns are ready for work with modded behavior.
        /// - Logs detailed debug information in developer mode for troubleshooting.
        /// </summary>
        public static void FullInitializeAllEligiblePawns(Map map, bool forceLock = true, string subtreeDefName = null)
        {
            if (map == null || map.mapPawns == null)
            {
                return;
            }

            // Use cached pawn list for better performance
            List<Pawn> pawns = GetCachedMapPawns(map);

            foreach (var pawn in pawns)
            {
                if (pawn == null || pawn.def == null || pawn.def.race == null)
                    continue;

                if (!Utility_ThinkTreeManager.HasAllowOrBlockWorkTag(pawn.def))
                    continue;

                var modExtension = Utility_CacheManager.GetModExtension(pawn.def);

                if (modExtension == null)
                    continue; // No mod extension found, nothing to do

                // ✅ Skip full reinitialization if pawn already has static ThinkTree assigned
                if (modExtension != null && !string.IsNullOrEmpty(modExtension.mainWorkThinkTreeDefName))
                {
                    Utility_DebugManager.LogNormal($"Skipping FullInitialize for {pawn.LabelShortCap} (static ThinkTree '{modExtension.mainWorkThinkTreeDefName}' already assigned).");
                    continue;
                }

                try
                {
                    FullInitializePawn(pawn, forceLock, subtreeDefName);
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogWarning($"FullInitializePawn failed for {pawn?.LabelShortCap ?? "unknown pawn"}: {ex.Message}");
                }
            }

            Utility_DebugManager.LogNormal($"Completed FullInitializeAllEligiblePawns on map {map.Index} (forceLock={forceLock}).");
        }

        /// <summary>
        /// Ensure that a pawn's WorkSettings are initialized if missing or invalid.
        /// Only applies to pawns tagged for PawnControl work injection.
        /// </summary>
        public static void EnsureWorkSettingsInitialized(Pawn pawn)
        {
            if (pawn == null || pawn.workSettings == null)
            {
                var modExtension = Utility_CacheManager.GetModExtension(pawn?.def);
                if (modExtension == null)
                {
                    return; // No mod extension found, nothing to do
                }

                Utility_DebugManager.LogWarning($"WorkSettings missing for {pawn?.LabelShort ?? "null pawn"}. Attempting to create.");

                // First ensure skills exist if needed (skills should be initialized before work settings)
                if (Utility_SkillManager.ShouldHaveSkills(pawn))
                {
                    Utility_SkillManager.ForceAttachSkillTrackerIfMissing(pawn);
                }

                pawn.workSettings = new Pawn_WorkSettings(pawn);
                pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
            }
        }

        /// <summary>
        /// Clear all caches when the map changes
        /// </summary>
        public static void ClearCaches()
        {
            _workgiverReplacementDone.Clear();
            _mapPawnCache.Clear();
            _mapPawnCacheLastTick.Clear();
            // Don't clear the per-pawn caches - they're still valid across map changes
        }

        /// <summary>
        /// Reset all caches (for game load)
        /// </summary>
        public static void ResetAllCaches()
        {
            _workgiverReplacementDone.Clear();
            _mapPawnCache.Clear();
            _mapPawnCacheLastTick.Clear();
            _pawnEnabledWorkGiversCache.Clear();
            _pawnsWithListeners.Clear();
            _workTypeGiversCache.Clear();
            _workPrioritySnapshots.Clear();
            _lastCheckTick.Clear();
            _dirtyPawns.Clear();
            _recentlySpawnedPawns.Clear();
            _workTabWasOpen = false;
            _workTabCloseTime = 0;
        }

        /// <summary>
        /// Ensures that a pawn has valid WorkSettings and proper WorkGivers.
        /// Can be safely called during runtime to rescue pawns whose WorkSettings got reset or corrupted.
        /// Only operates on tagged pawns.
        /// </summary>
        public static void SafeEnsurePawnReadyForWork(Pawn pawn)
        {
            if (pawn == null || pawn.def == null || pawn.def.race == null)
            {
                return;
            }

            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null)
            {
                return; // No mod extension found, nothing to do
            }

            if (!Utility_ThinkTreeManager.HasAllowOrBlockWorkTag(pawn.def))
            {
                return;
            }

            // ENHANCED: Only skip if already done and the pawn actually has valid work givers
            bool alreadyDone = _workgiverReplacementDone.Contains(pawn);
            bool hasWorkGivers = pawn?.workSettings?.WorkGiversInOrderNormal?.Count > 0;

            if (alreadyDone && hasWorkGivers)
            {
                return; // ✅ Already successfully initialized, skip
            }

            // ✅ Ensure workSettings exists
            if (pawn.workSettings == null || !pawn.workSettings.EverWork)
            {
                EnsureWorkSettingsInitialized(pawn);
                Utility_DebugManager.LogWarning($"SafeEnsurePawnReadyForWork: WorkSettings recreated for {pawn.LabelCap}.");
            }

            // ENHANCED: Always force work givers to populate properly
            EnsureWorkGiversPopulated(pawn);

            // ENHANCED: Force a snapshot for work settings change detection
            if (!_pawnsWithListeners.Contains(pawn.thingIDNumber))
            {
                AttachWorkSettingsChangeListener(pawn);
            }

            // ENHANCED: Force mind state active to ensure proper job scanning
            if (pawn.mindState != null)
            {
                pawn.mindState.Active = true;
            }

            Utility_DebugManager.LogNormal($"SafeEnsurePawnReadyForWork completed for {pawn.LabelCap}.");

            // ✅ Mark as completed
            _workgiverReplacementDone.Add(pawn);
        }

        /// <summary>
        /// Force the WorkGiversInOrder lists to be populated for a pawn if they're null or empty.
        /// This is necessary for pawns with statically assigned ThinkTrees that might skip normal initialization.
        /// </summary>
        public static void EnsureWorkGiversPopulated(Pawn pawn)
        {
            if (pawn?.workSettings == null)
            {
                return;
            }

            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null)
            {
                return; // No mod extension found, nothing to do
            }

            try
            {
                // Check if work lists are empty
                List<WorkGiver> normalList = pawn.workSettings.WorkGiversInOrderNormal;
                List<WorkGiver> emergencyList = pawn.workSettings.WorkGiversInOrderEmergency;

                // ENHANCED: Always rebuild for modded pawns
                bool needsRebuild = true;

                if (needsRebuild)
                {
                    // First try regular initialization
                    if (!pawn.workSettings.Initialized)
                    {
                        Utility_DebugManager.LogWarning($"WorkSettings not initialized for {pawn.LabelCap}. Initializing...");
                        pawn.workSettings.EnableAndInitialize();
                    }

                    // Force rebuild via reflection
                    MethodInfo cacheMethod = AccessTools.Method(typeof(Pawn_WorkSettings), "CacheWorkGiversInOrder");
                    if (cacheMethod != null)
                    {
                        cacheMethod.Invoke(pawn.workSettings, null);
                        Utility_DebugManager.LogNormal($"Forced WorkGiver cache rebuild for {pawn.LabelCap}");

                        // Force list refresh by accessing them
                        normalList = pawn.workSettings.WorkGiversInOrderNormal;
                        emergencyList = pawn.workSettings.WorkGiversInOrderEmergency;

                        // If still null or empty, create them manually
                        if (normalList == null || normalList.Count == 0 ||
                            emergencyList == null || emergencyList.Count == 0)
                        {
                            Utility_DebugManager.LogWarning($"Creating new WorkGiver lists for {pawn.LabelCap}");

                            // Create new lists if needed
                            var normalField = AccessTools.Field(typeof(Pawn_WorkSettings), "workGiversInOrderNormal");
                            var emergencyField = AccessTools.Field(typeof(Pawn_WorkSettings), "workGiversInOrderEmergency");

                            if (normalField != null && emergencyField != null)
                            {
                                var newNormalList = new List<WorkGiver>();
                                var newEmergencyList = new List<WorkGiver>();

                                // Populate with workgivers for ALL enabled work types
                                foreach (WorkTypeDef workTypeDef in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                                {
                                    if (pawn.workSettings.GetPriority(workTypeDef) > 0)
                                    {
                                        // Get all work givers for this work type
                                        foreach (WorkGiverDef def in workTypeDef.workGiversByPriority)
                                        {
                                            newNormalList.Add(def.Worker);
                                            if (def.emergency)
                                                newEmergencyList.Add(def.Worker);
                                        }
                                    }
                                }

                                normalField.SetValue(pawn.workSettings, newNormalList);
                                emergencyField.SetValue(pawn.workSettings, newEmergencyList);
                                Utility_DebugManager.LogNormal($"Created new WorkGiver lists with {newNormalList.Count} normal and {newEmergencyList.Count} emergency givers");
                            }
                        }

                        // Verify the counts to debug
                        if (normalList != null && emergencyList != null)
                        {
                            Utility_DebugManager.LogNormal($"After rebuild: Normal list count: {normalList.Count}, Emergency list count: {emergencyList.Count}");
                        }
                    }

                    // Force an immediate rebuild of the enabled workgivers cache
                    RebuildEnabledWorkGiversForPawn(pawn);
                }
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Exception in EnsureWorkGiversPopulated: {ex}");
            }
        }

        /// <summary>
        /// Forces the pawn to take an available job from the appropriate job module
        /// This bypasses normal job selection when a pawn appears "stuck"
        /// </summary>
        public static void ForceJobAssignmentIfNeeded(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed)
                return;

            // Only do this for pawns that should be working but aren't
            if (pawn.jobs?.curJob != null || pawn.CurJobDef != null)
                return;

            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null)
                return;

            // Get all enabled work types for this pawn
            List<WorkTypeDef> enabledWorkTypes = new List<WorkTypeDef>();
            foreach (WorkTypeDef workType in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                if (pawn.workSettings != null && pawn.workSettings.WorkIsActive(workType))
                {
                    enabledWorkTypes.Add(workType);
                }
            }

            if (enabledWorkTypes.Count == 0)
                return;

            // Try job modules for each enabled work type
            foreach (WorkTypeDef workType in enabledWorkTypes)
            {
                // Get the appropriate job giver class name for this work type
                string jobGiverClassName = $"JobGiver_Unified_{workType.defName}_PawnControl";

                // Try to find the corresponding class using reflection
                Type jobGiverType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => a.GetTypes())
                    .FirstOrDefault(t => t.Name == jobGiverClassName);

                if (jobGiverType == null)
                    continue;

                // Try to get a static method for manual job assignment
                MethodInfo tryAssignJobMethod = jobGiverType.GetMethod("TryAssignJobToPawn",
                    BindingFlags.Public | BindingFlags.Static);

                if (tryAssignJobMethod != null)
                {
                    try
                    {
                        object result = tryAssignJobMethod.Invoke(null, new object[] { pawn });
                        if (result is bool success && success)
                        {
                            Utility_DebugManager.LogNormal($"Successfully forced job assignment for {pawn.LabelShort} using {jobGiverClassName}");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Utility_DebugManager.LogError($"Error forcing job for {pawn.LabelShort}: {ex.Message}");
                    }
                }
            }

            // If we got here, we couldn't assign any jobs
            Utility_DebugManager.LogWarning($"Failed to force job assignment for {pawn.LabelShort}");
        }

        /// <summary>
        /// Event raised when a pawn's work settings are changed
        /// </summary>
        public static event Action<Pawn> OnWorkSettingsChanged;

        // Track if event listeners are properly registered
        private static bool _listenersRegistered = false;

        /// <summary>
        /// Notify that a pawn's work settings have changed
        /// </summary>
        public static void NotifyWorkSettingsChanged(Pawn pawn)
        {
            if (pawn != null)
            {
                OnWorkSettingsChanged?.Invoke(pawn);

                // Also clear cached data in relevant managers
                Utility_JobGiverManager.ClearDisabledWorkTypeCache(pawn);
                JobGiverScanner.ClearPawnCache(pawn);

                Utility_DebugManager.LogNormal($"Work settings changed for {pawn.LabelShort}");
            }
        }

        /// <summary>
        /// Make sure listeners are registered
        /// </summary>
        public static void EnsureWorkSettingsListenersRegistered()
        {
            if (!_listenersRegistered)
            {
                _listenersRegistered = true;
                Utility_DebugManager.LogNormal("Work settings change listeners registered");
            }
        }

        /// <summary>
        /// Clears all cached work settings data
        /// </summary>
        public static void ClearAllWorkSettingsCache()
        {
            // Signal a cache reset to all relevant systems
            try
            {
                JobGiverScanner.ClearAllCaches();
                Utility_DebugManager.LogNormal("Cleared all work settings caches");
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error clearing work settings caches: {ex}");
            }
        }
    }
}
