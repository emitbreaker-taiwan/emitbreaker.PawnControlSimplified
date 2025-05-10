using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for construction tasks specifically for delivering resources to construction frames
    /// </summary>
    public class JobModule_Construction_DeliverResourcesToFrames : JobModule_Construction
    {
        // Reference to the common resources implementation
        private readonly JobModule_Common_DeliverResources _commonImpl;

        // Static caches for map-specific data persistence
        private static readonly Dictionary<int, List<Frame>> _frameCache = new Dictionary<int, List<Frame>>();
        private static readonly Dictionary<int, Dictionary<Frame, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Frame, bool>>();
        private static int _lastFrameCacheUpdateTick = -999;

        // Module metadata
        public override string UniqueID => "Const_DeliverResourcesToFrames";
        public override float Priority => 5.8f; // Same priority as original JobGiver - higher than hauling version
        public override string Category => "Construction";
        public override int CacheUpdateInterval => 150; // Update slightly more frequently than hauling version

        // Constructor initializes the common implementation directly
        public JobModule_Construction_DeliverResourcesToFrames()
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
                            $"Found {_frameCache[mapId].Count} frames needing resources for constructors on map {map.uniqueID}");
                    }

                    _lastFrameCacheUpdateTick = currentTick;
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"Error updating constructor frame cache: {ex}");
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
        /// Check if resources can be delivered to this construction project
        /// </summary>
        public override bool ShouldProcessBuildable(Thing thing, Map map)
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
                Utility_DebugManager.LogWarning($"Error in ShouldProcessBuildable for frame: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Check if the constructor can deliver resources to this frame
        /// </summary>
        public override bool ValidateConstructionJob(Thing thing, Pawn constructionWorker)
        {
            try
            {
                Frame frame = thing as Frame;
                if (frame == null)
                    return false;

                // Delegate to common implementation
                return _commonImpl.ValidateDeliveryJob(thing, constructionWorker);
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error validating construction resource delivery job to frame: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Create a job to deliver resources to this frame
        /// </summary>
        protected override Job CreateConstructionJob(Pawn constructionWorker, Thing thing)
        {
            try
            {
                if (constructionWorker == null || thing == null)
                    return null;

                Frame frame = thing as Frame;
                if (frame == null)
                    return null;

                // Delegate to common implementation
                Job job = _commonImpl.CreateDeliveryJob(constructionWorker, thing);

                // If that didn't work, try the utility method
                if (job == null)
                {
                    job = Utility_JobGiverManager.ResourceDeliveryJobFor(constructionWorker, frame);
                }

                if (job != null)
                {
                    Utility_DebugManager.LogNormal($"{constructionWorker.LabelShort} created construction job to deliver resources to frame");
                }
                return job;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating construction resource delivery job to frame: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Adapter class for JobModule_Common_DeliverResources
        /// </summary>
        private class JobModule_Common_DeliverResources_Adapter : JobModule_Common_DeliverResources
        {
            private readonly JobModule_Construction_DeliverResourcesToFrames _outer;

            public JobModule_Common_DeliverResources_Adapter(JobModule_Construction_DeliverResourcesToFrames outer)
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
            protected override bool OnlyFrames => true;
        }
    }
}