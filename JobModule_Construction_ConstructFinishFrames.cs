using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for building/finishing frames
    /// </summary>
    public class JobModule_Construction_ConstructFinishFrames : JobModule_Construction
    {
        public override string UniqueID => "FinishFrames";
        public override float Priority => 6.0f; // Same priority as original JobGiver
        public override string Category => "Construction";
        public override int CacheUpdateInterval => 150; // Update slightly more frequently than delivery jobs

        // Static caches for map-specific data persistence
        private static readonly Dictionary<int, List<Frame>> _frameCache = new Dictionary<int, List<Frame>>();
        private static readonly Dictionary<int, Dictionary<Frame, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Frame, bool>>();
        private static readonly Dictionary<int, Dictionary<Frame, bool>> _resourceAvailabilityCache = new Dictionary<int, Dictionary<Frame, bool>>();
        private static int _lastCacheUpdateTick = -999;

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        // These ThingRequestGroups are what this module cares about
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups =>
            new HashSet<ThingRequestGroup> { ThingRequestGroup.BuildingFrame };

        public override void ResetStaticData()
        {
            Utility_CacheManager.ResetJobGiverCache(_frameCache, _reachabilityCache);

            foreach (var resourceMap in _resourceAvailabilityCache.Values)
            {
                resourceMap.Clear();
            }
            _resourceAvailabilityCache.Clear();

            _lastCacheUpdateTick = -999;
        }

        public override void UpdateCache(Map map, List<Thing> targetCache)
        {
            base.UpdateCache(map, targetCache);

            if (map == null) return;
            int mapId = map.uniqueID;
            bool hasTargets = false;

            int currentTick = Find.TickManager.TicksGame;

            // Initialize caches if needed
            if (!_frameCache.ContainsKey(mapId))
                _frameCache[mapId] = new List<Frame>();

            if (!_reachabilityCache.ContainsKey(mapId))
                _reachabilityCache[mapId] = new Dictionary<Frame, bool>();

            if (!_resourceAvailabilityCache.ContainsKey(mapId))
                _resourceAvailabilityCache[mapId] = new Dictionary<Frame, bool>();

            // Only do a full update if needed
            if (currentTick > _lastCacheUpdateTick + CacheUpdateInterval ||
                !_frameCache.ContainsKey(mapId))
            {
                try
                {
                    // Clear existing caches for this map
                    _frameCache[mapId].Clear();
                    _reachabilityCache[mapId].Clear();
                    _resourceAvailabilityCache[mapId].Clear();

                    // Find all completed frames ready for construction
                    foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame))
                    {
                        Frame frame = thing as Frame;
                        if (frame != null && frame.Spawned && frame.IsCompleted() &&
                            frame.Faction == Faction.OfPlayer &&
                            !frame.IsForbidden(Faction.OfPlayer) &&
                            GenConstruct.FirstBlockingThing(frame, null) == null)
                        {
                            _frameCache[mapId].Add(frame);

                            // Also add to the target cache provided by the job giver
                            targetCache.Add(frame);
                        }
                    }

                    if (_frameCache[mapId].Count > 0)
                    {
                        Utility_DebugManager.LogNormal(
                            $"Found {_frameCache[mapId].Count} frames ready for building on map {map.uniqueID}");
                    }

                    _lastCacheUpdateTick = currentTick;
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"Error updating construction frames cache: {ex}");
                }
            }
            else
            {
                // Just add the cached targets to the target cache
                foreach (Frame frame in _frameCache[mapId])
                {
                    // Skip if no longer valid
                    if (!frame.Spawned || frame.Destroyed || !frame.IsCompleted() ||
                        frame.IsForbidden(Faction.OfPlayer) ||
                        GenConstruct.FirstBlockingThing(frame, null) != null)
                        continue;

                    targetCache.Add(frame);
                }
            }

            SetHasTargets(map, hasTargets || (targetCache != null && targetCache.Count > 0));
        }

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

                // If not in cache, check if frame is completed and ready to build
                return frame.IsCompleted() &&
                       frame.Faction == Faction.OfPlayer &&
                       !frame.IsForbidden(Faction.OfPlayer) &&
                       GenConstruct.FirstBlockingThing(frame, null) == null;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in ShouldProcessBuildable for frame: {ex}");
                return false;
            }
        }

        public override bool ValidateConstructionJob(Thing thing, Pawn constructionWorker)
        {
            try
            {
                if (thing == null || constructionWorker == null || !thing.Spawned || !constructionWorker.Spawned)
                    return false;

                Frame frame = thing as Frame;
                if (frame == null)
                    return false;

                // IMPORTANT: Check faction interaction validity first
                if (!Utility_JobGiverManager.IsValidFactionInteraction(frame, constructionWorker, requiresDesignator: false))
                    return false;

                // Skip frames from different factions
                if (frame.Faction != constructionWorker.Faction)
                    return false;

                // Verify frame is still valid
                if (!frame.Spawned || !frame.IsCompleted() || frame.IsForbidden(constructionWorker))
                    return false;

                // Check for blocking things - we don't handle these here
                Thing blocker = GenConstruct.FirstBlockingThing(frame, constructionWorker);
                if (blocker != null)
                    return false;

                // Check if pawn can construct this
                if (!GenConstruct.CanConstruct(frame, constructionWorker, false))
                    return false;

                // Check construction skill requirements
                int skillRequired = 0;
                if (frame.def.entityDefToBuild is ThingDef thingDef && thingDef.constructionSkillPrerequisite > 0)
                {
                    skillRequired = thingDef.constructionSkillPrerequisite;
                    int constructorSkill = constructionWorker.skills?.GetSkill(SkillDefOf.Construction)?.Level ?? 0;
                    if (constructorSkill < skillRequired)
                        return false;
                }

                // Check if building is an attachment
                bool isAttachment = false;
                ThingDef builtDef = GenConstruct.BuiltDefOf(frame.def) as ThingDef;
                if (builtDef?.building != null && builtDef.building.isAttachment)
                    isAttachment = true;

                // Check reachability with correct path end mode
                PathEndMode pathEndMode = isAttachment ? PathEndMode.OnCell : PathEndMode.Touch;
                if (!constructionWorker.CanReach(frame, pathEndMode, Danger.Deadly))
                    return false;

                // Skip if pawn can't reserve
                if (!constructionWorker.CanReserve(frame))
                    return false;

                return true;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error validating frame building job: {ex}");
                return false;
            }
        }

        protected override Job CreateConstructionJob(Pawn constructionWorker, Thing thing)
        {
            try
            {
                if (constructionWorker == null || thing == null)
                    return null;

                Frame frame = thing as Frame;
                if (frame == null)
                    return null;

                Job job = JobMaker.MakeJob(JobDefOf.FinishFrame, frame);
                if (job != null)
                {
                    Utility_DebugManager.LogNormal($"{constructionWorker.LabelShort} created job to finish construction of {frame.Label}");
                }
                return job;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating frame building job: {ex}");
                return null;
            }
        }
    }
}