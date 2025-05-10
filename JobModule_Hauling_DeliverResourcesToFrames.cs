using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for hauling tasks specifically for delivering resources to construction frames
    /// </summary>
    public class JobModule_Hauling_DeliverResourcesToFrames : JobModule_Hauling
    {
        // Reference to the common resources implementation
        private readonly JobModule_Common_DeliverResources _commonImpl;

        // Static caches for map-specific data persistence
        private static readonly Dictionary<int, List<Frame>> _frameCache = new Dictionary<int, List<Frame>>();
        private static readonly Dictionary<int, Dictionary<Frame, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Frame, bool>>();
        private static int _lastFrameCacheUpdateTick = -999;

        // Module metadata
        public override string UniqueID => "Hauling_DeliverToFrames";
        public override float Priority => 5.6f; // Lower priority than construction version
        public override string Category => "Construction";
        public override int CacheUpdateInterval => 180; // Update every 3 seconds

        // Constructor initializes the common implementation directly
        public JobModule_Hauling_DeliverResourcesToFrames()
        {
            _commonImpl = new JobModule_Common_DeliverResources_Adapter(this);
        }

        /// <summary>
        /// These ThingRequestGroups are what this module cares about
        /// </summary>
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups =>
            new HashSet<ThingRequestGroup> { ThingRequestGroup.BuildingFrame };

        /// <summary>
        /// Reset any static data when language changes or game reloads
        /// </summary>
        public override void ResetStaticData()
        {
            base.ResetStaticData();
            _commonImpl.ResetStaticData();
            Utility_CacheManager.ResetJobGiverCache(_frameCache, _reachabilityCache);
            _lastFrameCacheUpdateTick = -999;
        }

        /// <summary>
        /// Update the cache of frames that need resources
        /// </summary>
        public override void UpdateCache(Map map, List<Thing> targetCache)
        {
            if (map == null) return;
            int mapId = map.uniqueID;
            bool hasTargets = false;

            int currentTick = Find.TickManager.TicksGame;

            // Initialize caches if needed
            if (!_frameCache.ContainsKey(mapId))
                _frameCache[mapId] = new List<Frame>();

            if (!_reachabilityCache.ContainsKey(mapId))
                _reachabilityCache[mapId] = new Dictionary<Frame, bool>();

            // Only do a full update if needed
            if (currentTick > _lastFrameCacheUpdateTick + CacheUpdateInterval ||
                !_frameCache.ContainsKey(mapId))
            {
                try
                {
                    // Clear existing caches for this map
                    _frameCache[mapId].Clear();
                    _reachabilityCache[mapId].Clear();

                    // Find all frames on the map
                    foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame))
                    {
                        Frame frame = thing as Frame;
                        if (frame != null && frame.Spawned && frame.TotalMaterialCost().Count > 0)
                        {
                            _frameCache[mapId].Add(frame);

                            // Also add to the target cache provided by the job giver
                            targetCache.Add(frame);
                        }
                    }

                    if (_frameCache[mapId].Count > 0)
                    {
                        Utility_DebugManager.LogNormal(
                            $"Found {_frameCache[mapId].Count} frames needing resources on map {map.uniqueID}");
                    }

                    _lastFrameCacheUpdateTick = currentTick;
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"Error updating frame cache: {ex}");
                }
            }
            else
            {
                // Just add the cached targets to the target cache
                foreach (Frame frame in _frameCache[mapId])
                {
                    // Skip if no longer valid
                    if (!frame.Spawned || frame.Destroyed)
                        continue;

                    targetCache.Add(frame);
                }
            }

            SetHasTargets(map, hasTargets || (targetCache != null && targetCache.Count > 0));
        }

        /// <summary>
        /// Check if this item should be hauled (resources to frame)
        /// </summary>
        public override bool ShouldHaulItem(Thing thing, Map map)
        {
            try
            {
                if (thing == null || map == null || !thing.Spawned) return false;

                // Check if it's a frame
                Frame frame = thing as Frame;
                if (frame == null)
                    return false;

                int mapId = map.uniqueID;

                // Check if it's in our cache first
                if (_frameCache.ContainsKey(mapId) && _frameCache[mapId].Contains(frame))
                    return true;

                // If not in cache, check if frame needs materials
                return frame.TotalMaterialCost().Count > 0;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in ShouldHaulItem for frame: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Check if the hauler can deliver resources to this frame
        /// </summary>
        public override bool ValidateHaulingJob(Thing thing, Pawn hauler)
        {
            try
            {
                Frame frame = thing as Frame;
                if (frame == null)
                    return false;

                // Delegate to common implementation
                return _commonImpl.ValidateDeliveryJob(thing, hauler);
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error validating deliver to frame job: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Create a job to deliver resources to this frame
        /// </summary>
        protected override Job CreateHaulingJob(Pawn hauler, Thing thing)
        {
            try
            {
                if (hauler == null || thing == null)
                    return null;

                Frame frame = thing as Frame;
                if (frame == null)
                    return null;

                // Delegate to common implementation
                Job job = _commonImpl.CreateDeliveryJob(hauler, thing);

                // If that didn't work, try the utility method
                if (job == null)
                {
                    job = Utility_JobGiverManager.ResourceDeliveryJobFor(hauler, frame);
                }

                if (job != null)
                {
                    Utility_DebugManager.LogNormal($"{hauler.LabelShort} created job to deliver resources to frame");
                }
                return job;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating deliver to frame job: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Adapter class for JobModule_Common_DeliverResources
        /// </summary>
        private class JobModule_Common_DeliverResources_Adapter : JobModule_Common_DeliverResources
        {
            private readonly JobModule_Hauling_DeliverResourcesToFrames _outer;

            public JobModule_Common_DeliverResources_Adapter(JobModule_Hauling_DeliverResourcesToFrames outer)
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
            protected override bool OnlyFrames => true;
        }
    }
}