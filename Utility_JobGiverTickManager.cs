using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Manages the scheduling of job executions by work tags and JobGivers across ticks
    /// to optimize performance with large numbers of pawns and job processors.
    /// </summary>
    public static class Utility_JobGiverTickManager
    {
        #region Data Structures

        // Work tag-based scheduling data
        // Map work tags to their configured update intervals (in ticks)
        private static readonly Dictionary<string, int> _workTagUpdateIntervals = new Dictionary<string, int>();

        // Map work tags to the last tick they were executed
        private static readonly Dictionary<string, int> _workTagLastExecutionTicks = new Dictionary<string, int>();

        // Staggered offset per work tag to prevent execution clustering
        private static readonly Dictionary<string, int> _workTagExecutionOffsets = new Dictionary<string, int>();

        // Priority levels used to determine execution frequency
        private static readonly Dictionary<string, int> _workTagPriorityLevels = new Dictionary<string, int>();

        // Map-specific tracking for conditional work tag activation
        private static readonly Dictionary<int, HashSet<string>> _activeWorkTagsByMapId = new Dictionary<int, HashSet<string>>();

        // Store base intervals and dynamic intervals for work tags
        private static readonly Dictionary<string, int> _workTagBaseIntervals = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _workTagDynamicIntervals = new Dictionary<string, int>();

        // Number of tick groups to spread pawns across for each work tag
        private static readonly Dictionary<string, int> _tickGroupsPerWorkTag = new Dictionary<string, int>();

        // Tracks work tags to their associated JobGiver types
        private static readonly Dictionary<string, List<Type>> _jobGiversByWorkTag = new Dictionary<string, List<Type>>();

        // Legacy JobGiver-based scheduling data (for backward compatibility)
        private static readonly Dictionary<Type, int> _updateIntervals = new Dictionary<Type, int>();
        private static readonly Dictionary<Type, int> _lastExecutionTicks = new Dictionary<Type, int>();
        private static readonly Dictionary<Type, int> _executionOffsets = new Dictionary<Type, int>();
        private static readonly Dictionary<Type, int> _priorityLevels = new Dictionary<Type, int>();
        private static readonly Dictionary<int, HashSet<Type>> _activeJobGiversByMapId = new Dictionary<int, HashSet<Type>>();
        private static readonly Dictionary<Type, int> _baseIntervals = new Dictionary<Type, int>();
        private static readonly Dictionary<Type, int> _dynamicIntervals = new Dictionary<Type, int>();
        private static readonly Dictionary<Type, int> _tickGroupsPerJobGiver = new Dictionary<Type, int>();

        #endregion

        #region Constants

        // Default number of tick groups for progressive scheduling
        public const int DEFAULT_TICK_GROUPS = 4;

        // Base update interval constants
        public const int HIGH_PRIORITY_INTERVAL = 30;   // Critical tasks like firefighting (0.5 sec)
        public const int MEDIUM_PRIORITY_INTERVAL = 60;  // Important tasks like doctoring (1 sec)
        public const int LOW_PRIORITY_INTERVAL = 120;    // Standard tasks like hauling (2 sec)
        public const int BACKGROUND_PRIORITY_INTERVAL = 250; // Low urgency tasks (4 sec+)

        #endregion

        #region Work Tag Registration

        /// <summary>
        /// Registers a work tag with the tick manager
        /// </summary>
        /// <param name="workTag">Work tag to register</param>
        /// <param name="priority">Execution priority (higher runs more frequently)</param>
        /// <param name="customInterval">Optional custom update interval</param>
        /// <param name="tickGroups">Number of groups to distribute pawns across (default: 4)</param>
        public static void RegisterWorkTag(string workTag, int priority, int? customInterval = null, int tickGroups = DEFAULT_TICK_GROUPS)
        {
            if (string.IsNullOrEmpty(workTag)) return;

            // Set priority for this work tag
            _workTagPriorityLevels[workTag] = priority;

            // Calculate execution interval based on priority
            int interval = customInterval ?? GetIntervalFromPriority(priority);
            _workTagUpdateIntervals[workTag] = interval;

            // Store base interval for future reference
            _workTagBaseIntervals[workTag] = interval;

            // Generate a stable but unique offset for this work tag to stagger execution
            _workTagExecutionOffsets[workTag] = Math.Abs(workTag.GetHashCode() % interval);

            // Set the number of tick groups for progressive scheduling
            _tickGroupsPerWorkTag[workTag] = Math.Max(1, tickGroups);

            // Initialize the JobGiver list for this work tag if not already present
            if (!_jobGiversByWorkTag.ContainsKey(workTag))
            {
                _jobGiversByWorkTag[workTag] = new List<Type>();
            }

            if (Utility_DebugManager.ShouldLogDetailed())
            {
                Utility_DebugManager.LogNormal($"Registered work tag '{workTag}' (Priority: {priority}, Interval: {interval} ticks, Tick Groups: {tickGroups})");
            }
        }

        /// <summary>
        /// Registers a JobGiver with a specific work tag
        /// </summary>
        /// <param name="jobGiverType">Type of JobGiver to register</param>
        /// <param name="workTag">Associated work tag</param>
        public static void RegisterJobGiverForWorkTag(Type jobGiverType, string workTag)
        {
            if (jobGiverType == null || string.IsNullOrEmpty(workTag)) return;

            // Ensure the work tag is initialized with default values if not already registered
            if (!_workTagPriorityLevels.ContainsKey(workTag))
            {
                RegisterWorkTag(workTag, 5);  // Default medium priority
            }

            // Add the JobGiver to the work tag's list
            if (!_jobGiversByWorkTag.TryGetValue(workTag, out var jobGivers))
            {
                jobGivers = new List<Type>();
                _jobGiversByWorkTag[workTag] = jobGivers;
            }

            if (!jobGivers.Contains(jobGiverType))
            {
                jobGivers.Add(jobGiverType);
            }

            if (Utility_DebugManager.ShouldLogDetailed())
            {
                Utility_DebugManager.LogNormal($"Registered JobGiver {jobGiverType.Name} with work tag '{workTag}'");
            }
        }

        /// <summary>
        /// Legacy method for backward compatibility
        /// </summary>
        public static void RegisterJobGiver(Type jobGiverType, string workTypeName, int priority, int? customInterval = null, int tickGroups = DEFAULT_TICK_GROUPS)
        {
            if (jobGiverType == null) return;

            // First register the work tag if specified
            if (!string.IsNullOrEmpty(workTypeName))
            {
                RegisterWorkTag(workTypeName, priority, customInterval, tickGroups);
                RegisterJobGiverForWorkTag(jobGiverType, workTypeName);
            }

            // Also register legacy data for backward compatibility
            _priorityLevels[jobGiverType] = priority;

            // Calculate execution interval based on priority
            int interval = customInterval ?? GetIntervalFromPriority(priority);
            _updateIntervals[jobGiverType] = interval;

            // Store base interval
            _baseIntervals[jobGiverType] = interval;

            // Generate a stable but unique offset for this JobGiver to stagger execution
            _executionOffsets[jobGiverType] = Math.Abs(jobGiverType.GetHashCode() % interval);

            // Set the number of tick groups for progressive scheduling
            _tickGroupsPerJobGiver[jobGiverType] = Math.Max(1, tickGroups);

            if (Utility_DebugManager.ShouldLogDetailed())
            {
                Utility_DebugManager.LogNormal($"Registered JobGiver {jobGiverType.Name} (WorkType: {workTypeName}, Priority: {priority}, Interval: {interval} ticks, Tick Groups: {tickGroups})");
            }
        }

        #endregion

        #region Work Tag Execution Scheduling

        /// <summary>
        /// Determines if a work tag should execute on the current tick for a specific pawn
        /// </summary>
        /// <param name="workTag">Work tag to check</param>
        /// <param name="pawn">The pawn requesting a job</param>
        /// <param name="forceExecute">Force execution regardless of schedule</param>
        /// <returns>True if the work tag should execute now for this pawn</returns>
        public static bool ShouldExecuteForPawn(string workTag, Pawn pawn, bool forceExecute = false)
        {
            if (pawn == null || string.IsNullOrEmpty(workTag))
                return false;

            int mapId = pawn.Map?.uniqueID ?? -1;
            if (mapId < 0)
                return false;

            // Force execution if requested
            if (forceExecute)
                return true;

            // Get the number of tick groups for this work tag
            int tickGroups = _tickGroupsPerWorkTag.GetValueSafe(workTag, DEFAULT_TICK_GROUPS);

            // Get the pawn's tick group based on a hash of its ThingID
            // This ensures consistent assignment across game sessions
            int pawnTickGroup = Math.Abs(pawn.thingIDNumber % tickGroups);

            // Get the current tick and see if it's this pawn's turn
            int currentTick = Find.TickManager.TicksGame;
            bool isTickForThisPawn = (currentTick % tickGroups) == pawnTickGroup;

            // Check if the work tag should execute this tick
            bool workTagShouldExecute = ShouldExecuteWorkTag(workTag, mapId, forceExecute);

            // Work tag should execute if it's scheduled AND it's this pawn's turn in the rotation
            return workTagShouldExecute && isTickForThisPawn;
        }

        /// <summary>
        /// Determines if a work tag should execute on the current tick
        /// </summary>
        /// <param name="workTag">Work tag to check</param>
        /// <param name="mapId">Current map ID</param>
        /// <param name="forceExecute">Force execution regardless of schedule</param>
        /// <returns>True if the work tag should execute now</returns>
        public static bool ShouldExecuteWorkTag(string workTag, int mapId, bool forceExecute = false)
        {
            if (string.IsNullOrEmpty(workTag))
                return false;

            int currentTick = Find.TickManager.TicksGame;

            // Force execution if requested
            if (forceExecute)
            {
                _workTagLastExecutionTicks[workTag] = currentTick;
                return true;
            }

            // Get configured update interval (default to medium if not set)
            int interval = _workTagUpdateIntervals.GetValueSafe(workTag, MEDIUM_PRIORITY_INTERVAL);

            // Get offset for this work tag (ensures staggered execution)
            int offset = _workTagExecutionOffsets.GetValueSafe(workTag, 0);

            // Get last execution tick (default to far past to ensure first run)
            int lastTick = _workTagLastExecutionTicks.GetValueSafe(workTag, -interval);

            // Determine if enough time has passed since last execution
            bool intervalElapsed = (currentTick - lastTick) >= interval;

            // Check if this is the right tick within the interval based on offset
            bool offsetMatches = (currentTick + offset) % interval == 0;

            // Special handling for dynamic work tag activation per map
            bool isActiveOnMap = IsWorkTagActiveOnMap(workTag, mapId);

            // Work tag should execute if interval elapsed AND it's active on this map
            bool shouldExecute = (intervalElapsed && offsetMatches && isActiveOnMap);

            // Update last execution time if executing now
            if (shouldExecute)
            {
                _workTagLastExecutionTicks[workTag] = currentTick;
            }

            return shouldExecute;
        }

        /// <summary>
        /// Legacy method for backward compatibility - 
        /// Determines if a JobGiver should execute for a specific pawn
        /// </summary>
        public static bool ShouldExecuteForPawn(Type jobGiverType, Pawn pawn, bool forceExecute = false)
        {
            if (pawn == null || jobGiverType == null)
                return false;

            // Try to use work tag-based scheduling if available
            string workTag = Utility_JobGiverManager.GetWorkTagForJobGiverType(jobGiverType);
            if (!string.IsNullOrEmpty(workTag))
            {
                return ShouldExecuteForPawn(workTag, pawn, forceExecute);
            }

            // Legacy fallback implementation
            int mapId = pawn.Map?.uniqueID ?? -1;
            if (mapId < 0)
                return false;

            // Force execution if requested
            if (forceExecute)
                return true;

            // Get the number of tick groups for this JobGiver
            int tickGroups = _tickGroupsPerJobGiver.GetValueSafe(jobGiverType, DEFAULT_TICK_GROUPS);

            // Get the pawn's tick group based on a hash of its ThingID
            int pawnTickGroup = Math.Abs(pawn.thingIDNumber % tickGroups);

            // Get the current tick and see if it's this pawn's turn
            int currentTick = Find.TickManager.TicksGame;
            bool isTickForThisPawn = (currentTick % tickGroups) == pawnTickGroup;

            // Check if the base JobGiver should execute this tick
            bool baseJobGiverShouldExecute = ShouldExecute(jobGiverType, mapId, forceExecute);

            return baseJobGiverShouldExecute && isTickForThisPawn;
        }

        /// <summary>
        /// Legacy method for backward compatibility - 
        /// Determines if a JobGiver should execute on the current tick
        /// </summary>
        public static bool ShouldExecute(Type jobGiverType, int mapId, bool forceExecute = false)
        {
            if (jobGiverType == null)
                return false;

            // Try to use work tag-based scheduling if available
            string workTag = Utility_JobGiverManager.GetWorkTagForJobGiverType(jobGiverType);
            if (!string.IsNullOrEmpty(workTag))
            {
                return ShouldExecuteWorkTag(workTag, mapId, forceExecute);
            }

            // Legacy fallback implementation
            int currentTick = Find.TickManager.TicksGame;

            // Force execution if requested
            if (forceExecute)
            {
                _lastExecutionTicks[jobGiverType] = currentTick;
                return true;
            }

            // Get configured update interval (default to medium if not set)
            int interval = _updateIntervals.GetValueSafe(jobGiverType, MEDIUM_PRIORITY_INTERVAL);

            // Get offset for this JobGiver (ensures staggered execution)
            int offset = _executionOffsets.GetValueSafe(jobGiverType, 0);

            // Get last execution tick (default to far past to ensure first run)
            int lastTick = _lastExecutionTicks.GetValueSafe(jobGiverType, -interval);

            // Determine if enough time has passed since last execution
            bool intervalElapsed = (currentTick - lastTick) >= interval;

            // Check if this is the right tick within the interval based on offset
            bool offsetMatches = (currentTick + offset) % interval == 0;

            // Special handling for dynamic JobGiver activation per map
            bool isActiveOnMap = IsJobGiverActiveOnMap(jobGiverType, mapId);

            // JobGiver should execute if interval elapsed AND it's active on this map
            bool shouldExecute = (intervalElapsed && offsetMatches && isActiveOnMap);

            // Update last execution time if executing now
            if (shouldExecute)
            {
                _lastExecutionTicks[jobGiverType] = currentTick;
            }

            return shouldExecute;
        }

        #endregion

        #region Tick Group Management

        /// <summary>
        /// Sets the number of tick groups for a work tag's progressive scheduling
        /// </summary>
        /// <param name="workTag">Work tag</param>
        /// <param name="tickGroups">Number of groups to distribute pawns across</param>
        public static void SetWorkTagTickGroups(string workTag, int tickGroups)
        {
            if (string.IsNullOrEmpty(workTag) || tickGroups < 1)
                return;

            _tickGroupsPerWorkTag[workTag] = tickGroups;

            if (Utility_DebugManager.ShouldLogDetailed())
            {
                Utility_DebugManager.LogNormal($"Updated tick groups for work tag '{workTag}': {tickGroups}");
            }
        }

        /// <summary>
        /// Legacy method - Sets the number of tick groups for a JobGiver's progressive scheduling
        /// </summary>
        public static void SetTickGroups(Type jobGiverType, int tickGroups)
        {
            if (jobGiverType == null || tickGroups < 1)
                return;

            // Update for work tag if available
            string workTag = Utility_JobGiverManager.GetWorkTagForJobGiverType(jobGiverType);
            if (!string.IsNullOrEmpty(workTag))
            {
                SetWorkTagTickGroups(workTag, tickGroups);
            }

            // Also update legacy data
            _tickGroupsPerJobGiver[jobGiverType] = tickGroups;

            if (Utility_DebugManager.ShouldLogDetailed())
            {
                Utility_DebugManager.LogNormal($"Updated tick groups for {jobGiverType.Name}: {tickGroups}");
            }
        }

        #endregion

        #region Interval Management

        /// <summary>
        /// Gets the appropriate interval value for a priority level
        /// </summary>
        public static int GetIntervalForPriority(int priority)
        {
            return GetIntervalFromPriority(priority);
        }

        /// <summary>
        /// Gets the appropriate execution interval based on priority level
        /// </summary>
        private static int GetIntervalFromPriority(int priority)
        {
            if (priority >= 8) return HIGH_PRIORITY_INTERVAL;      // Critical tasks
            if (priority >= 5) return MEDIUM_PRIORITY_INTERVAL;    // Important tasks
            if (priority >= 3) return LOW_PRIORITY_INTERVAL;       // Standard tasks
            return BACKGROUND_PRIORITY_INTERVAL;                   // Low priority tasks
        }

        /// <summary>
        /// Gets the base interval for a work tag
        /// </summary>
        public static int GetWorkTagBaseInterval(string workTag)
        {
            if (string.IsNullOrEmpty(workTag))
                return MEDIUM_PRIORITY_INTERVAL;

            if (_workTagBaseIntervals.TryGetValue(workTag, out int interval))
                return interval;

            // If no specific base interval is set, calculate from priority
            int priority = GetWorkTagPriority(workTag);
            return GetIntervalFromPriority(priority);
        }

        /// <summary>
        /// Sets the base interval for a work tag
        /// </summary>
        public static void SetWorkTagBaseInterval(string workTag, int interval)
        {
            if (string.IsNullOrEmpty(workTag) || interval <= 0)
                return;

            _workTagBaseIntervals[workTag] = interval;

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Set base interval for work tag '{workTag}': {interval} ticks");
            }
        }

        /// <summary>
        /// Sets a dynamic interval for a work tag based on runtime profiling
        /// </summary>
        public static void SetWorkTagDynamicInterval(string workTag, int interval)
        {
            if (string.IsNullOrEmpty(workTag) || interval <= 0)
                return;

            _workTagDynamicIntervals[workTag] = interval;

            // Update current interval
            _workTagUpdateIntervals[workTag] = interval;

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Set dynamic interval for work tag '{workTag}': {interval} ticks");
            }
        }

        /// <summary>
        /// Gets the current interval for a work tag, considering dynamic adjustments
        /// </summary>
        public static int GetWorkTagCurrentInterval(string workTag)
        {
            if (string.IsNullOrEmpty(workTag))
                return MEDIUM_PRIORITY_INTERVAL;

            // Prefer dynamic interval if available
            if (_workTagDynamicIntervals.TryGetValue(workTag, out int dynInterval))
                return dynInterval;

            // Fall back to base interval
            return GetWorkTagBaseInterval(workTag);
        }

        /// <summary>
        /// Gets the priority level for a work tag
        /// </summary>
        private static int GetWorkTagPriority(string workTag)
        {
            // Look up the priority from our registry
            if (_workTagPriorityLevels.TryGetValue(workTag, out int priority))
                return priority;

            return 5; // Default medium priority
        }

        /// <summary>
        /// Legacy method - Gets the base interval for a JobGiver
        /// </summary>
        public static int GetBaseInterval(Type jobGiverType)
        {
            if (jobGiverType == null)
                return MEDIUM_PRIORITY_INTERVAL;

            // Try to use work tag-based approach if available
            string workTag = Utility_JobGiverManager.GetWorkTagForJobGiverType(jobGiverType);
            if (!string.IsNullOrEmpty(workTag))
            {
                return GetWorkTagBaseInterval(workTag);
            }

            // Legacy fallback
            if (_baseIntervals.TryGetValue(jobGiverType, out int interval))
                return interval;

            // If no specific base interval is set, calculate from priority
            return GetIntervalFromPriority(GetJobGiverPriority(jobGiverType));
        }

        /// <summary>
        /// Legacy method - Sets the base interval for a JobGiver
        /// </summary>
        public static void SetBaseInterval(Type jobGiverType, int interval)
        {
            if (jobGiverType == null || interval <= 0)
                return;

            // Set for work tag if available
            string workTag = Utility_JobGiverManager.GetWorkTagForJobGiverType(jobGiverType);
            if (!string.IsNullOrEmpty(workTag))
            {
                SetWorkTagBaseInterval(workTag, interval);
            }

            // Also set legacy data
            _baseIntervals[jobGiverType] = interval;

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Set base interval for {jobGiverType.Name}: {interval} ticks");
            }
        }

        /// <summary>
        /// Legacy method - Sets a dynamic interval for a JobGiver based on runtime profiling
        /// </summary>
        public static void SetDynamicInterval(Type jobGiverType, int interval)
        {
            if (jobGiverType == null || interval <= 0)
                return;

            // Set for work tag if available
            string workTag = Utility_JobGiverManager.GetWorkTagForJobGiverType(jobGiverType);
            if (!string.IsNullOrEmpty(workTag))
            {
                SetWorkTagDynamicInterval(workTag, interval);
            }

            // Also set legacy data
            _dynamicIntervals[jobGiverType] = interval;

            // Update current interval
            _updateIntervals[jobGiverType] = interval;

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Set dynamic interval for {jobGiverType.Name}: {interval} ticks");
            }
        }

        /// <summary>
        /// Legacy method - Gets the current interval for a JobGiver, considering dynamic adjustments
        /// </summary>
        public static int GetCurrentInterval(Type jobGiverType)
        {
            if (jobGiverType == null)
                return MEDIUM_PRIORITY_INTERVAL;

            // Try to use work tag-based approach if available
            string workTag = Utility_JobGiverManager.GetWorkTagForJobGiverType(jobGiverType);
            if (!string.IsNullOrEmpty(workTag))
            {
                return GetWorkTagCurrentInterval(workTag);
            }

            // Legacy fallback
            // Prefer dynamic interval if available
            if (_dynamicIntervals.TryGetValue(jobGiverType, out int dynInterval))
                return dynInterval;

            // Fall back to base interval
            return GetBaseInterval(jobGiverType);
        }

        /// <summary>
        /// Legacy method - Gets the priority level for a JobGiver
        /// </summary>
        private static int GetJobGiverPriority(Type jobGiverType)
        {
            // Try to use work tag priority if available
            string workTag = Utility_JobGiverManager.GetWorkTagForJobGiverType(jobGiverType);
            if (!string.IsNullOrEmpty(workTag) && _workTagPriorityLevels.TryGetValue(workTag, out int workTagPriority))
            {
                return workTagPriority;
            }

            // Look up the priority from our legacy registry
            if (_priorityLevels.TryGetValue(jobGiverType, out int priority))
                return priority;

            return 5; // Default medium priority
        }

        #endregion

        #region Dynamic Colony Size Adjustment

        /// <summary>
        /// Dynamically adjusts execution intervals based on colony size and performance
        /// </summary>
        public static void AdjustIntervalsBasedOnColonySize()
        {
            // Count total pawns across all maps to gauge complexity
            int totalPawns = 0;
            int totalMaps = 0;

            if (Current.Game?.Maps != null)
            {
                foreach (var map in Current.Game.Maps)
                {
                    totalPawns += map.mapPawns.AllPawnsSpawnedCount;
                    totalMaps++;
                }
            }

            // Scale factor based on pawn count (more pawns = longer intervals)
            float scaleFactor = 1f;

            if (totalPawns > 50) scaleFactor = 1.5f;
            if (totalPawns > 100) scaleFactor = 2f;
            if (totalPawns > 200) scaleFactor = 2.5f;
            if (totalPawns > 500) scaleFactor = 3f;
            if (totalPawns > 1000) scaleFactor = 4f;

            // Apply scaled intervals to all registered work tags
            foreach (var workTag in _workTagPriorityLevels.Keys.ToList())
            {
                int priority = _workTagPriorityLevels[workTag];
                int baseInterval = GetIntervalFromPriority(priority);
                int scaledInterval = (int)(baseInterval * scaleFactor);

                // Update interval
                _workTagUpdateIntervals[workTag] = scaledInterval;
            }

            // Also apply to legacy JobGivers for backward compatibility
            foreach (var jobGiverType in _priorityLevels.Keys.ToList())
            {
                int priority = _priorityLevels[jobGiverType];
                int baseInterval = GetIntervalFromPriority(priority);
                int scaledInterval = (int)(baseInterval * scaleFactor);

                // Update interval
                _updateIntervals[jobGiverType] = scaledInterval;
            }

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Adjusted job intervals for {totalPawns} pawns across {totalMaps} maps (scale: {scaleFactor:F2}x)");
            }
        }

        #endregion

        #region Activation Management

        /// <summary>
        /// Sets a work tag as active or inactive for a specific map
        /// </summary>
        public static void SetWorkTagActiveForMap(string workTag, int mapId, bool active)
        {
            if (string.IsNullOrEmpty(workTag))
                return;

            if (!_activeWorkTagsByMapId.TryGetValue(mapId, out var activeWorkTags))
            {
                activeWorkTags = new HashSet<string>();
                _activeWorkTagsByMapId[mapId] = activeWorkTags;
            }

            if (active)
            {
                activeWorkTags.Add(workTag);
            }
            else
            {
                activeWorkTags.Remove(workTag);
            }
        }

        /// <summary>
        /// Checks if a work tag is active on a specific map
        /// </summary>
        public static bool IsWorkTagActiveOnMap(string workTag, int mapId)
        {
            if (string.IsNullOrEmpty(workTag))
                return false;

            // If not explicitly tracked, assume active
            if (!_activeWorkTagsByMapId.TryGetValue(mapId, out var activeWorkTags))
            {
                return true;
            }

            // If tracked but not in set, it's inactive
            return activeWorkTags.Contains(workTag);
        }

        /// <summary>
        /// Legacy method - Sets a JobGiver as active or inactive for a specific map
        /// </summary>
        public static void SetJobGiverActiveForMap(Type jobGiverType, int mapId, bool active)
        {
            if (jobGiverType == null)
                return;

            // Set for work tag if available
            string workTag = Utility_JobGiverManager.GetWorkTagForJobGiverType(jobGiverType);
            if (!string.IsNullOrEmpty(workTag))
            {
                SetWorkTagActiveForMap(workTag, mapId, active);
            }

            // Legacy implementation
            if (!_activeJobGiversByMapId.TryGetValue(mapId, out var activeJobGivers))
            {
                activeJobGivers = new HashSet<Type>();
                _activeJobGiversByMapId[mapId] = activeJobGivers;
            }

            if (active)
            {
                activeJobGivers.Add(jobGiverType);
            }
            else
            {
                activeJobGivers.Remove(jobGiverType);
            }
        }

        /// <summary>
        /// Legacy method - Checks if a JobGiver is active on a specific map
        /// </summary>
        public static bool IsJobGiverActiveOnMap(Type jobGiverType, int mapId)
        {
            if (jobGiverType == null)
                return false;

            // Check work tag first if available
            string workTag = Utility_JobGiverManager.GetWorkTagForJobGiverType(jobGiverType);
            if (!string.IsNullOrEmpty(workTag))
            {
                return IsWorkTagActiveOnMap(workTag, mapId);
            }

            // Legacy fallback
            // If not explicitly tracked, assume active
            if (!_activeJobGiversByMapId.TryGetValue(mapId, out var activeJobGivers))
            {
                return true;
            }

            // If tracked but not in set, it's inactive
            return activeJobGivers.Contains(jobGiverType);
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Gets the next scheduled execution tick for a work tag
        /// </summary>
        public static int GetWorkTagNextExecutionTick(string workTag)
        {
            if (string.IsNullOrEmpty(workTag))
                return 0;

            int currentTick = Find.TickManager.TicksGame;
            int lastTick = _workTagLastExecutionTicks.GetValueSafe(workTag, currentTick);
            int interval = _workTagUpdateIntervals.GetValueSafe(workTag, MEDIUM_PRIORITY_INTERVAL);
            int offset = _workTagExecutionOffsets.GetValueSafe(workTag, 0);

            // Calculate next execution tick based on interval and offset
            return lastTick + interval - ((currentTick + offset) % interval);
        }

        /// <summary>
        /// Legacy method - Gets the next scheduled execution tick for a JobGiver
        /// </summary>
        public static int GetNextExecutionTick(Type jobGiverType)
        {
            if (jobGiverType == null)
                return 0;

            // Try to use work tag-based method if available
            string workTag = Utility_JobGiverManager.GetWorkTagForJobGiverType(jobGiverType);
            if (!string.IsNullOrEmpty(workTag))
            {
                return GetWorkTagNextExecutionTick(workTag);
            }

            // Legacy fallback
            int currentTick = Find.TickManager.TicksGame;
            int lastTick = _lastExecutionTicks.GetValueSafe(jobGiverType, currentTick);
            int interval = _updateIntervals.GetValueSafe(jobGiverType, MEDIUM_PRIORITY_INTERVAL);
            int offset = _executionOffsets.GetValueSafe(jobGiverType, 0);

            // Calculate next execution tick based on interval and offset
            return lastTick + interval - ((currentTick + offset) % interval);
        }

        /// <summary>
        /// Gets all JobGiver types associated with a specific work tag
        /// </summary>
        public static IEnumerable<Type> GetJobGiversForWorkTag(string workTag)
        {
            if (string.IsNullOrEmpty(workTag) || !_jobGiversByWorkTag.TryGetValue(workTag, out var list))
            {
                return Enumerable.Empty<Type>();
            }

            return list;
        }

        /// <summary>
        /// Legacy method - Gets all JobGiver types associated with a specific work type
        /// </summary>
        public static IEnumerable<Type> GetJobGiversForWorkType(string workType)
        {
            // This is now an alias for GetJobGiversForWorkTag for consistency
            return GetJobGiversForWorkTag(workType);
        }

        /// <summary>
        /// Gets all work tags that have registered JobGivers
        /// </summary>
        public static IEnumerable<string> GetAllWorkTags()
        {
            return _workTagPriorityLevels.Keys;
        }

        /// <summary>
        /// Legacy method - Gets all work types that have registered JobGivers
        /// </summary>
        public static IEnumerable<string> GetAllWorkTypes()
        {
            // This is now an alias for GetAllWorkTags for consistency
            return GetAllWorkTags();
        }

        #endregion

        #region Data Management

        /// <summary>
        /// Resets all tracking data (for loading a new game)
        /// </summary>
        public static void ResetAll()
        {
            // Reset work tag data
            _workTagLastExecutionTicks.Clear();
            _activeWorkTagsByMapId.Clear();

            // Reset legacy data
            _lastExecutionTicks.Clear();
            _activeJobGiversByMapId.Clear();
        }

        /// <summary>
        /// Cleans up data for a specific map that is no longer needed
        /// </summary>
        public static void CleanupMap(int mapId)
        {
            // Remove work tag data for this map
            if (_activeWorkTagsByMapId.TryGetValue(mapId, out _))
            {
                _activeWorkTagsByMapId.Remove(mapId);
            }

            // Reset execution ticks for all work tags
            foreach (var workTag in _workTagUpdateIntervals.Keys.ToList())
            {
                if (_workTagLastExecutionTicks.ContainsKey(workTag))
                {
                    _workTagLastExecutionTicks[workTag] = -999;
                }
            }

            // Remove legacy JobGiver data for this map
            if (_activeJobGiversByMapId.TryGetValue(mapId, out _))
            {
                _activeJobGiversByMapId.Remove(mapId);
            }

            // Reset execution ticks for all legacy JobGivers
            foreach (var jobGiverType in _updateIntervals.Keys.ToList())
            {
                if (_lastExecutionTicks.ContainsKey(jobGiverType))
                {
                    _lastExecutionTicks[jobGiverType] = -999;
                }
            }

            if (Prefs.DevMode)
            {
                int workTagCount = _jobGiversByWorkTag.Count;
                int jobGiverCount = _jobGiversByWorkTag.Values.Sum(list => list.Count);
                Utility_DebugManager.LogNormal($"Cleaned up tick manager data for map {mapId} with {workTagCount} work tags and {jobGiverCount} registered JobGivers");
            }
        }

        #endregion
    }
}