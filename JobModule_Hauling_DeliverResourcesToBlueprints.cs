using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for hauling tasks specifically for delivering resources to construction blueprints
    /// </summary>
    public class JobModule_Hauling_DeliverResourcesToBlueprints : JobModule_Hauling
    {
        // Reference to the common resources implementation
        private readonly JobModule_Common_DeliverResources _commonImpl;

        // Static caches for map-specific data persistence
        private static readonly Dictionary<int, List<Blueprint>> _blueprintCache = new Dictionary<int, List<Blueprint>>();
        private static readonly Dictionary<int, Dictionary<Blueprint, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Blueprint, bool>>();
        private static int _lastBlueprintCacheUpdateTick = -999;

        // Module metadata
        public override string UniqueID => "Hauling_DeliverToBP";
        public override float Priority => 5.7f; // Same priority as original JobGiver
        public override string Category => "Construction";
        public override int CacheUpdateInterval => 180; // Update every 3 seconds

        // Constructor initializes the common implementation directly
        public JobModule_Hauling_DeliverResourcesToBlueprints()
        {
            _commonImpl = new JobModule_Common_DeliverResources_Adapter(this);
        }

        /// <summary>
        /// These ThingRequestGroups are what this module cares about
        /// </summary>
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups =>
            new HashSet<ThingRequestGroup> { ThingRequestGroup.Blueprint };

        /// <summary>
        /// Reset any static data when language changes or game reloads
        /// </summary>
        public override void ResetStaticData()
        {
            base.ResetStaticData();
            _commonImpl.ResetStaticData();
            Utility_CacheManager.ResetJobGiverCache(_blueprintCache, _reachabilityCache);
            _lastBlueprintCacheUpdateTick = -999;
        }

        /// <summary>
        /// Update the cache of blueprints that need resources
        /// </summary>
        public override void UpdateCache(Map map, List<Thing> targetCache)
        {
            if (map == null) return;
            int mapId = map.uniqueID;
            bool hasTargets = false;

            int currentTick = Find.TickManager.TicksGame;

            // Initialize caches if needed
            if (!_blueprintCache.ContainsKey(mapId))
                _blueprintCache[mapId] = new List<Blueprint>();

            if (!_reachabilityCache.ContainsKey(mapId))
                _reachabilityCache[mapId] = new Dictionary<Blueprint, bool>();

            // Only do a full update if needed
            if (currentTick > _lastBlueprintCacheUpdateTick + CacheUpdateInterval ||
                !_blueprintCache.ContainsKey(mapId))
            {
                try
                {
                    // Clear existing caches for this map
                    _blueprintCache[mapId].Clear();
                    _reachabilityCache[mapId].Clear();

                    // Find all blueprints on the map
                    foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint))
                    {
                        Blueprint blueprint = thing as Blueprint;
                        if (blueprint != null && blueprint.Spawned &&
                            GenConstruct.CanConstruct(blueprint, null, false, false))
                        {
                            _blueprintCache[mapId].Add(blueprint);

                            // Also add to the target cache provided by the job giver
                            targetCache.Add(blueprint);
                        }
                    }

                    if (_blueprintCache[mapId].Count > 0)
                    {
                        Utility_DebugManager.LogNormal(
                            $"Found {_blueprintCache[mapId].Count} blueprints needing resources on map {map.uniqueID}");
                    }

                    _lastBlueprintCacheUpdateTick = currentTick;
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"Error updating blueprint cache: {ex}");
                }
            }
            else
            {
                // Just add the cached targets to the target cache
                foreach (Blueprint blueprint in _blueprintCache[mapId])
                {
                    // Skip if no longer valid
                    if (!blueprint.Spawned || blueprint.Destroyed)
                        continue;

                    targetCache.Add(blueprint);
                }
            }

            SetHasTargets(map, hasTargets || (targetCache != null && targetCache.Count > 0));
        }

        /// <summary>
        /// Check if this item should be hauled (resources to blueprint)
        /// </summary>
        public override bool ShouldHaulItem(Thing thing, Map map)
        {
            try
            {
                if (thing == null || map == null || !thing.Spawned) return false;

                // Check if it's a blueprint
                Blueprint blueprint = thing as Blueprint;
                if (blueprint == null)
                    return false;

                int mapId = map.uniqueID;

                // Check if it's in our cache first
                if (_blueprintCache.ContainsKey(mapId) && _blueprintCache[mapId].Contains(blueprint))
                    return true;

                // If not in cache, check if blueprint needs construction
                return GenConstruct.CanConstruct(blueprint, null, false, false);
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in ShouldHaulItem for blueprint: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Check if the hauler can deliver resources to this blueprint
        /// </summary>
        public override bool ValidateHaulingJob(Thing thing, Pawn hauler)
        {
            try
            {
                Blueprint blueprint = thing as Blueprint;
                if (blueprint == null)
                    return false;

                // Specific validation for haulers
                if (!Utility_JobGiverManager.IsValidFactionInteraction(blueprint, hauler, requiresDesignator: false))
                    return false;

                // Delegate to common implementation
                return _commonImpl.ValidateDeliveryJob(thing, hauler);
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error validating deliver to blueprint job: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Create a job to deliver resources to this blueprint
        /// </summary>
        protected override Job CreateHaulingJob(Pawn hauler, Thing thing)
        {
            try
            {
                if (hauler == null || thing == null)
                    return null;

                Blueprint blueprint = thing as Blueprint;
                if (blueprint == null)
                    return null;

                // Delegate to common implementation
                Job job = _commonImpl.CreateDeliveryJob(hauler, thing);

                // If that didn't work, try the utility method
                if (job == null)
                {
                    job = Utility_JobGiverManager.ResourceDeliveryJobFor(hauler, blueprint);
                }

                if (job != null)
                {
                    Utility_DebugManager.LogNormal($"{hauler.LabelShort} created job to deliver resources to blueprint");
                }
                return job;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating deliver to blueprint job: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Adapter class for JobModule_Common_DeliverResources
        /// </summary>
        private class JobModule_Common_DeliverResources_Adapter : JobModule_Common_DeliverResources
        {
            private readonly JobModule_Hauling_DeliverResourcesToBlueprints _outer;

            public JobModule_Common_DeliverResources_Adapter(JobModule_Hauling_DeliverResourcesToBlueprints outer)
            {
                _outer = outer;
            }

            // Required implementations from JobModuleCore
            public override string UniqueID => _outer.UniqueID + "_Common";
            public override float Priority => _outer.Priority;
            public override string WorkTypeName => _outer.WorkTypeName;
            public override string Category => _outer.Category;

            // Configuration properties
            protected override bool RequiresConstructionSkill => false;
            protected override bool AllowHaulingWorkType => true;
            protected override bool OnlyFrames => false;
        }
    }
}