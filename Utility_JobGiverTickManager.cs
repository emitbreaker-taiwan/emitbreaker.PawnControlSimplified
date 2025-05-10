using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Manages the scheduling of JobGivers across ticks to optimize performance
    /// with large numbers of pawns and job processors.
    /// </summary>
    public static class Utility_JobGiverTickManager
    {
        // Map JobGiver types to their configured update intervals (in ticks)
        private static readonly Dictionary<Type, int> _updateIntervals = new Dictionary<Type, int>();
        
        // Map JobGiver types to the last tick they were executed
        private static readonly Dictionary<Type, int> _lastExecutionTicks = new Dictionary<Type, int>();

        // Tracks JobGivers by their work types for grouped execution
        private static readonly Dictionary<string, List<Type>> _jobGiversByWorkType = new Dictionary<string, List<Type>>();
        
        // Staggered offset per JobGiver to prevent execution clustering
        private static readonly Dictionary<Type, int> _executionOffsets = new Dictionary<Type, int>();
        
        // Priority levels used to determine execution frequency
        private static readonly Dictionary<Type, int> _priorityLevels = new Dictionary<Type, int>();
        
        // Map-specific tracking for conditional JobGiver activation
        private static readonly Dictionary<int, HashSet<Type>> _activeJobGiversByMapId = 
            new Dictionary<int, HashSet<Type>>();

        // Base update interval constants
        public const int HIGH_PRIORITY_INTERVAL = 30;   // Critical tasks like firefighting (0.5 sec)
        public const int MEDIUM_PRIORITY_INTERVAL = 60;  // Important tasks like doctoring (1 sec)
        public const int LOW_PRIORITY_INTERVAL = 120;    // Standard tasks like hauling (2 sec)
        public const int BACKGROUND_PRIORITY_INTERVAL = 250; // Low urgency tasks (4 sec+)

        /// <summary>
        /// Registers a JobGiver with the tick manager
        /// </summary>
        /// <param name="jobGiverType">Type of JobGiver to register</param>
        /// <param name="workTypeName">Associated work type for grouping</param>
        /// <param name="priority">Execution priority (higher runs more frequently)</param>
        /// <param name="customInterval">Optional custom update interval</param>
        public static void RegisterJobGiver(Type jobGiverType, string workTypeName, int priority, int? customInterval = null)
        {
            if (jobGiverType == null) return;
            
            // Set priority for this JobGiver
            _priorityLevels[jobGiverType] = priority;
            
            // Calculate execution interval based on priority
            int interval = customInterval ?? GetIntervalFromPriority(priority);
            _updateIntervals[jobGiverType] = interval;
            
            // Generate a stable but unique offset for this JobGiver to stagger execution
            _executionOffsets[jobGiverType] = Math.Abs(jobGiverType.GetHashCode() % interval);
            
            // Group by work type
            if (!string.IsNullOrEmpty(workTypeName))
            {
                if (!_jobGiversByWorkType.TryGetValue(workTypeName, out var list))
                {
                    list = new List<Type>();
                    _jobGiversByWorkType[workTypeName] = list;
                }
                
                if (!list.Contains(jobGiverType))
                {
                    list.Add(jobGiverType);
                }
            }
            
            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Registered JobGiver {jobGiverType.Name} (WorkType: {workTypeName}, Priority: {priority}, Interval: {interval} ticks)");
            }
        }

        /// <summary>
        /// Determines if a JobGiver should execute on the current tick
        /// </summary>
        /// <param name="jobGiverType">Type of JobGiver to check</param>
        /// <param name="mapId">Current map ID</param>
        /// <param name="forceExecute">Force execution regardless of schedule</param>
        /// <returns>True if JobGiver should execute now</returns>
        public static bool ShouldExecute(Type jobGiverType, int mapId, bool forceExecute = false)
        {
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
            
            // Apply scaled intervals to all registered JobGivers
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
                Utility_DebugManager.LogNormal($"Adjusted JobGiver intervals for {totalPawns} pawns across {totalMaps} maps (scale: {scaleFactor:F2}x)");
            }
        }
        
        /// <summary>
        /// Sets a JobGiver as active or inactive for a specific map
        /// </summary>
        public static void SetJobGiverActiveForMap(Type jobGiverType, int mapId, bool active)
        {
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
        /// Checks if a JobGiver is active on a specific map
        /// </summary>
        public static bool IsJobGiverActiveOnMap(Type jobGiverType, int mapId)
        {
            // If not explicitly tracked, assume active
            if (!_activeJobGiversByMapId.TryGetValue(mapId, out var activeJobGivers))
            {
                return true;
            }
            
            // If tracked but not in set, it's inactive
            return activeJobGivers.Contains(jobGiverType);
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
        /// Resets all tracking data (for loading a new game)
        /// </summary>
        public static void ResetAll()
        {
            _lastExecutionTicks.Clear();
            _activeJobGiversByMapId.Clear();
        }
        
        /// <summary>
        /// Gets the next scheduled execution tick for a JobGiver
        /// </summary>
        public static int GetNextExecutionTick(Type jobGiverType)
        {
            int currentTick = Find.TickManager.TicksGame;
            int lastTick = _lastExecutionTicks.GetValueSafe(jobGiverType, currentTick);
            int interval = _updateIntervals.GetValueSafe(jobGiverType, MEDIUM_PRIORITY_INTERVAL);
            int offset = _executionOffsets.GetValueSafe(jobGiverType, 0);
            
            // Calculate next execution tick based on interval and offset
            return lastTick + interval - ((currentTick + offset) % interval);
        }
        
        /// <summary>
        /// Gets all JobGiver types associated with a specific work type
        /// </summary>
        public static IEnumerable<Type> GetJobGiversForWorkType(string workType)
        {
            if (string.IsNullOrEmpty(workType) || !_jobGiversByWorkType.TryGetValue(workType, out var list))
            {
                return Enumerable.Empty<Type>();
            }
            
            return list;
        }
    }
}