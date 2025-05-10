using Verse;
using System.Collections.Generic;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Periodically refreshes each JobModule's target cache for a map.
    /// </summary>
    public class MapComponent_JobModuleCache : MapComponent
    {
        private const int UPDATE_INTERVAL = 250; // Ticks between updates (4.16 seconds)
        private int _lastUpdateTick = -UPDATE_INTERVAL;

        public MapComponent_JobModuleCache(Map map) : base(map) 
        { 
            Utility_DebugManager.LogNormal($"MapComponent_JobModuleCache initialized for map {map.uniqueID}");
        }

        public override void MapComponentTick()
        {
            int currentTick = Find.TickManager.TicksGame;
            if (currentTick - _lastUpdateTick < UPDATE_INTERVAL) return;
            _lastUpdateTick = currentTick;

            UpdateAllModuleCaches();
        }
        
        /// <summary>Updates all job module caches for this map.</summary>
        public void UpdateAllModuleCaches()
        {
            int mapId = map.uniqueID;
            
            // Access via non-generic shared base type for simplicity
            var jobModules = JobGiver_Unified_PawnControl<JobModule<Thing>, Thing>._jobModules;
            var targetCache = JobGiver_Unified_PawnControl<JobModule<Thing>, Thing>._targetsByTypeCache;
            
            // Ensure this map's dictionary exists
            if (!targetCache.ContainsKey(mapId))
                targetCache[mapId] = new Dictionary<string, List<Thing>>();
                
            // Update all module caches
            foreach (var module in jobModules)
            {
                // Skip modules that should be skipped
                if (module.ShouldSkipModule(map))
                {
                    module.SetHasTargets(map, false);
                    
                    // Clear existing cache
                    if (targetCache[mapId].ContainsKey(module.UniqueID))
                        targetCache[mapId][module.UniqueID].Clear();
                        
                    continue;
                }
                
                // Get or create this module's target list
                if (!targetCache[mapId].TryGetValue(module.UniqueID, out var targetList))
                {
                    targetList = new List<Thing>();
                    targetCache[mapId][module.UniqueID] = targetList;
                }
                else
                {
                    targetList.Clear();
                }
                
                // Let module update its target cache
                module.UpdateCache(map, targetList);
                
                // Set flag based on whether we found targets
                module.SetHasTargets(map, targetList.Count > 0);
            }
            
            // Update map-level flag
            bool hasAnyTargets = false;
            foreach (var module in jobModules)
            {
                if (module.HasTargets(map))
                {
                    hasAnyTargets = true;
                    break;
                }
            }
            
            JobGiver_Unified_PawnControl<JobModule<Thing>, Thing>._mapHasTargets[mapId] = hasAnyTargets;
            
            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Updated all module caches for map {mapId}");
            }
        }
    }
}