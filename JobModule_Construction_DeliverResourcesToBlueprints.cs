using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for construction tasks specifically for delivering resources to construction blueprints
    /// </summary>
    public class JobModule_Construction_DeliverResourcesToBlueprints : JobModule_Construction
    {
        // Reference to the common resources implementation
        private readonly JobModule_Common_DeliverResources _commonImpl;

        // Static caches for map-specific data persistence
        private static readonly Dictionary<int, List<Blueprint>> _blueprintCache = new Dictionary<int, List<Blueprint>>();
        private static readonly Dictionary<int, Dictionary<Blueprint, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Blueprint, bool>>();
        private static int _lastBlueprintCacheUpdateTick = -999;

        // Module metadata
        public override string UniqueID => "Const_DeliverResourcesToBP";
        public override float Priority => 5.9f; // Same priority as original JobGiver - higher than hauling version
        public override string Category => "Construction";
        public override int CacheUpdateInterval => 150; // Update slightly more frequently than hauling version

        // Constructor initializes the common implementation directly
        public JobModule_Construction_DeliverResourcesToBlueprints()
        {
            _commonImpl = new JobModule_Common_DeliverResources_Adapter(this);
        }

        /// <summary>
        /// Mixed work type handling - both construction and hauling related
        /// </summary>
        public override bool QuickFilterCheck(Pawn pawn)
        {
            // Need to check both construction and hauling work tags
            return !pawn.WorkTagIsDisabled(WorkTags.Constructing) && !pawn.WorkTagIsDisabled(WorkTags.Hauling);
        }

        /// <summary>
        /// Check if both work types are active for this pawn
        /// </summary>
        public override bool WorkTypeApplies(Pawn pawn)
        {
            // Must be able to do both construction and hauling
            return pawn?.workSettings?.WorkIsActive(WorkTypeDefOf.Construction) == true &&
                   pawn?.workSettings?.WorkIsActive(WorkTypeDefOf.Hauling) == true;
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
                            $"Found {_blueprintCache[mapId].Count} blueprints needing resources for constructors on map {map.uniqueID}");
                    }

                    _lastBlueprintCacheUpdateTick = currentTick;
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"Error updating constructor blueprint cache: {ex}");
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
        /// Check if resources can be delivered to this blueprint
        /// </summary>
        public override bool ShouldProcessBuildable(Thing thing, Map map)
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
                Utility_DebugManager.LogWarning($"Error in ShouldProcessBuildable for blueprint: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Check if the constructor can deliver resources to this blueprint
        /// </summary>
        public override bool ValidateConstructionJob(Thing thing, Pawn constructionWorker)
        {
            try
            {
                Blueprint blueprint = thing as Blueprint;
                if (blueprint == null)
                    return false;

                // Specific validation for constructors first
                if (!Utility_JobGiverManager.IsValidFactionInteraction(blueprint, constructionWorker, requiresDesignator: false))
                    return false;

                // Check if the blueprint can be constructed
                if (!GenConstruct.CanConstruct(blueprint, constructionWorker, false))
                    return false;

                // Check for construction skill level
                if (constructionWorker.skills?.GetSkill(SkillDefOf.Construction)?.Level < 1)
                    return false;

                // Delegate to common implementation
                return _commonImpl.ValidateDeliveryJob(thing, constructionWorker);
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error validating construction resource delivery job to blueprint: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Create a job to deliver resources to this blueprint
        /// </summary>
        protected override Job CreateConstructionJob(Pawn constructionWorker, Thing thing)
        {
            try
            {
                if (constructionWorker == null || thing == null)
                    return null;

                Blueprint blueprint = thing as Blueprint;
                if (blueprint == null)
                    return null;

                // Delegate to common implementation
                Job job = _commonImpl.CreateDeliveryJob(constructionWorker, thing);

                // If that didn't work, try the utility method
                if (job == null)
                {
                    job = Utility_JobGiverManager.ResourceDeliveryJobFor(constructionWorker, blueprint);
                }

                if (job != null)
                {
                    Utility_DebugManager.LogNormal($"{constructionWorker.LabelShort} created construction job to deliver resources to blueprint");
                }
                return job;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating construction resource delivery job to blueprint: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Adapter class for JobModule_Common_DeliverResources
        /// </summary>
        private class JobModule_Common_DeliverResources_Adapter : JobModule_Common_DeliverResources
        {
            private readonly JobModule_Construction_DeliverResourcesToBlueprints _outer;

            public JobModule_Common_DeliverResources_Adapter(JobModule_Construction_DeliverResourcesToBlueprints outer)
            {
                _outer = outer;
            }

            // Required implementations from JobModuleCore
            public override string UniqueID => _outer.UniqueID + "_Common";
            public override float Priority => _outer.Priority;
            public override string WorkTypeName => _outer.WorkTypeName;
            public override string Category => _outer.Category;

            // Configuration properties
            protected override bool RequiresConstructionSkill => true;
            protected override bool AllowHaulingWorkType => false;
            protected override bool OnlyFrames => false;
        }
    }
}