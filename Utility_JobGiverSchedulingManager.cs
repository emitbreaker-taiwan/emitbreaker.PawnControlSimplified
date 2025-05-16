using RimWorld;
using System;
using System.Collections.Generic;
using Verse;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Manager for configuring and monitoring progressive JobGiver scheduling
    /// </summary>
    public static class Utility_JobGiverSchedulingManager
    {
        // Default tick groups for different colony sizes
        private const int SMALL_COLONY_TICK_GROUPS = 2;      // < 10 pawns
        private const int MEDIUM_COLONY_TICK_GROUPS = 4;     // 10-50 pawns
        private const int LARGE_COLONY_TICK_GROUPS = 8;      // 50-100 pawns
        private const int HUGE_COLONY_TICK_GROUPS = 12;      // 100+ pawns
        
        // Track when to recalculate tick groups
        private static int _lastRecalculationTick = -1;
        private const int RECALCULATION_INTERVAL = 2500;  // About 40 seconds of game time
        
        // Store job giver specific configuration
        private static readonly Dictionary<Type, int> _customTickGroupConfig = new Dictionary<Type, int>();
        private static readonly Dictionary<string, int> _workTypeTickGroupConfig = new Dictionary<string, int>();
        
        /// <summary>
        /// Updates tick group settings for all registered JobGivers based on colony size
        /// </summary>
        public static void RecalculateTickGroups()
        {
            int currentTick = Find.TickManager.TicksGame;
            if (currentTick - _lastRecalculationTick < RECALCULATION_INTERVAL)
                return;
                
            _lastRecalculationTick = currentTick;
            
            // Count total pawns across all maps
            int totalPawns = 0;
            if (Current.Game?.Maps != null)
            {
                foreach (var map in Current.Game.Maps)
                {
                    totalPawns += map.mapPawns.AllPawnsSpawnedCount;
                }
            }
            
            // Select appropriate base tick groups based on colony size
            int baseTickGroups;
            if (totalPawns < 10)
                baseTickGroups = SMALL_COLONY_TICK_GROUPS;
            else if (totalPawns < 50)
                baseTickGroups = MEDIUM_COLONY_TICK_GROUPS;
            else if (totalPawns < 100)
                baseTickGroups = LARGE_COLONY_TICK_GROUPS;
            else
                baseTickGroups = HUGE_COLONY_TICK_GROUPS;
                
            // Apply base settings to all work types
            foreach (string workType in Utility_JobGiverTickManager.GetAllWorkTypes())
            {
                // Use any custom configuration if specified, otherwise use the base setting
                int tickGroups = _workTypeTickGroupConfig.GetValueSafe(workType, baseTickGroups);
                
                // Update all JobGivers associated with this work type
                foreach (Type jobGiverType in Utility_JobGiverTickManager.GetJobGiversForWorkType(workType))
                {
                    // Give precedence to custom settings per JobGiver
                    int finalTickGroups = _customTickGroupConfig.GetValueSafe(jobGiverType, tickGroups);
                    Utility_JobGiverTickManager.SetTickGroups(jobGiverType, finalTickGroups);
                }
            }

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Recalculated progressive scheduling with {baseTickGroups} base tick groups for {totalPawns} pawns");
            }
        }
        
        /// <summary>
        /// Sets custom tick groups for a specific JobGiver type
        /// </summary>
        public static void SetCustomTickGroups(Type jobGiverType, int tickGroups)
        {
            if (jobGiverType == null || tickGroups < 1)
                return;
                
            _customTickGroupConfig[jobGiverType] = tickGroups;
            Utility_JobGiverTickManager.SetTickGroups(jobGiverType, tickGroups);
            
            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Set custom tick groups for {jobGiverType.Name}: {tickGroups}");
            }
        }
        
        /// <summary>
        /// Sets custom tick groups for all JobGivers of a specific work type
        /// </summary>
        public static void SetWorkTypeTickGroups(string workType, int tickGroups)
        {
            if (string.IsNullOrEmpty(workType) || tickGroups < 1)
                return;
                
            _workTypeTickGroupConfig[workType] = tickGroups;
            
            // Apply to all JobGivers of this work type
            foreach (Type jobGiverType in Utility_JobGiverTickManager.GetJobGiversForWorkType(workType))
            {
                // Only apply if no custom setting exists for this specific JobGiver
                if (!_customTickGroupConfig.ContainsKey(jobGiverType))
                {
                    Utility_JobGiverTickManager.SetTickGroups(jobGiverType, tickGroups);
                }
            }
            
            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Set tick groups for work type '{workType}': {tickGroups}");
            }
        }
        
        /// <summary>
        /// Force recalculation of tick groups on the next update
        /// </summary>
        public static void ForceRecalculation()
        {
            _lastRecalculationTick = -9999;
        }
    }
}