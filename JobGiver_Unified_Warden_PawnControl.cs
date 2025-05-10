using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Warden-specific implementation of the unified job giver
    /// </summary>
    public class JobGiver_Unified_Warden_PawnControl : JobGiver_Unified_PawnControl<JobModule_Warden, Pawn>
    {
        // Static initializer to register warden modules
        static JobGiver_Unified_Warden_PawnControl()
        {
            // Register modules - exact same as in the original implementation
            RegisterModule(new JobModule_Warden_Execution());
            RegisterModule(new JobModule_Warden_ExecuteGuilty());
            RegisterModule(new JobModule_Warden_FeedPrisoner());
            RegisterModule(new JobModule_Warden_DeliverFood());
            RegisterModule(new JobModule_Warden_TakeToBed());
            RegisterModule(new JobModule_Warden_Chat());
            RegisterModule(new JobModule_Warden_ReleasePrisoner());

            // Register this specific JobGiver with the registry
            Type jobGiverType = typeof(JobGiver_Unified_Warden_PawnControl);
            HashSet<string> workTypes = new HashSet<string> { "Warden" };

            // Add work types from hauling modules
            foreach (var module in _jobModules)
            {
                if (!string.IsNullOrEmpty(module.WorkTypeName) && module.WorkTypeName != "Warden")
                    workTypes.Add(module.WorkTypeName);
            }

            // Register with the appropriate priority for Hauling
            // Register this job giver with the appropriate priority
            RegisterDerivedJobGiver<JobGiver_Unified_Warden_PawnControl>("Warden", 7.2f);
        }

        protected override string WorkTypeName => "Warden";

        /// <summary>
        /// Custom target cache update for wardens that handles prisoners separately
        /// </summary>
        protected override void UpdateTargetCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            // Only update periodically to save performance
            if (currentTick <= _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL &&
                _targetsByTypeCache.ContainsKey(mapId))
                return;

            // Initialize cache structures
            if (!_targetsByTypeCache.ContainsKey(mapId))
                _targetsByTypeCache[mapId] = new Dictionary<string, List<Pawn>>();

            // Clear reachability cache for this map
            _reachabilityCache.ClearMapCache(mapId);

            // Get a reference to the map's reachability cache for easier access
            Dictionary<Pawn, bool> mapReachabilityCache = _reachabilityCache.GetMapCache(mapId);

            // Clear and initialize module target lists
            foreach (var module in _jobModules)
            {
                if (!_targetsByTypeCache[mapId].ContainsKey(module.UniqueID))
                    _targetsByTypeCache[mapId][module.UniqueID] = new List<Pawn>();
                else
                    _targetsByTypeCache[mapId][module.UniqueID].Clear();
            }

            // Handle special modules that process colonists rather than prisoners
            foreach (var module in _jobModules.Where(m => m.HandlesColonists))
            {
                module.UpdateCache(map, _targetsByTypeCache[mapId][module.UniqueID]);
            }

            // Process all prisoners once for efficiency
            foreach (Pawn prisoner in map.mapPawns.PrisonersOfColonySpawned)
            {
                if (prisoner == null || prisoner.Destroyed || !prisoner.Spawned)
                    continue;

                // Let each module filter this prisoner
                foreach (var module in _jobModules.Where(m => !m.HandlesColonists))
                {
                    if (module.ShouldProcessPrisoner(prisoner))
                    {
                        _targetsByTypeCache[mapId][module.UniqueID].Add(prisoner);
                    }
                }
            }

            // Update last cache update tick
            _lastCacheUpdateTick = currentTick;

            // Update map-level target status
            bool mapHasAnyTargets = false;
            foreach (var module in _jobModules)
            {
                bool hasTargets = _targetsByTypeCache[mapId][module.UniqueID].Count > 0;
                module.SetHasTargets(map, hasTargets);

                if (hasTargets)
                    mapHasAnyTargets = true;
            }

            _mapHasTargets[mapId] = mapHasAnyTargets;
        }

        public override string ToString()
        {
            return "JobGiver_Unified_Warden_PawnControl";
        }
    }
}