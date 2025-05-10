using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that allows pawns to finish construction frames belonging to their faction.
    /// Uses the Construction work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Construction_ConstructFinishFrames_PawnControl : ThinkNode_JobGiver
    {
        // Cache for frames that are ready to be finished
        private static readonly Dictionary<int, List<Frame>> _completedFrameCache = new Dictionary<int, List<Frame>>();
        private static readonly Dictionary<int, Dictionary<Frame, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Frame, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 180; // Update every 3 seconds

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 2500f }; // 15, 25, 50 tiles

        public override float GetPriority(Pawn pawn)
        {
            // Finishing construction is more important than starting new projects
            return 6.0f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Quick early exit if there are no valid frames on the map
            if (pawn?.Map == null || !pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame).Any())
                return null;

            return Utility_JobGiverManager.StandardTryGiveJob<Frame>(
                pawn,
                "Construction",
                (p, forced) => {
                    // Update cache
                    UpdateCompletedFrameCache(p.Map);

                    // Find and create a job for finishing frames
                    return TryCreateFinishFrameJob(p, forced);
                },
                debugJobDesc: "finish construction assignment");
        }

        /// <summary>
        /// Updates the cache of frames that are ready to be finished
        /// </summary>
        private void UpdateCompletedFrameCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_completedFrameCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_completedFrameCache.ContainsKey(mapId))
                    _completedFrameCache[mapId].Clear();
                else
                    _completedFrameCache[mapId] = new List<Frame>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Frame, bool>();

                // Find all frames ready to be finished
                foreach (Frame frame in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame))
                {
                    // Skip if not completed or forbidden
                    if (frame == null || !frame.Spawned || !frame.IsCompleted() || frame.IsForbidden(Faction.OfPlayer))
                        continue;

                    _completedFrameCache[mapId].Add(frame);
                }

                // Limit cache size for performance
                int maxCacheSize = 200;
                if (_completedFrameCache[mapId].Count > maxCacheSize)
                {
                    _completedFrameCache[mapId] = _completedFrameCache[mapId].Take(maxCacheSize).ToList();
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Creates a job for finishing frames in the nearest valid location
        /// </summary>
        private Job TryCreateFinishFrameJob(Pawn pawn, bool forced)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_completedFrameCache.ContainsKey(mapId) || _completedFrameCache[mapId].Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing and target selection
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                _completedFrameCache[mapId],
                (frame) => (frame.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Process each bucket to first check for blocking jobs
            for (int i = 0; i < buckets.Length; i++)
            {
                foreach (Frame frame in buckets[i])
                {
                    // Filter out invalid frames immediately
                    if (frame.Faction != pawn.Faction || !frame.Spawned ||
                        !frame.IsCompleted() || frame.IsForbidden(pawn))
                        continue;

                    // Check for blocking things first - this replaces the exception pattern
                    Thing blocker = GenConstruct.FirstBlockingThing(frame, pawn);
                    if (blocker != null)
                    {
                        Job blockingJob = GenConstruct.HandleBlockingThingJob(frame, pawn, forced);
                        if (blockingJob != null)
                        {
                            Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to handle {blocker.LabelCap} blocking construction");
                            return blockingJob;
                        }
                    }
                }
            }

            // With no blocking jobs found, proceed with normal target selection
            Frame bestFrame = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (frame, p) => {
                    // IMPORTANT: Check faction interaction validity first
                    if (!Utility_JobGiverManager.IsValidFactionInteraction(frame, p, requiresDesignator: false))
                        return false;

                    // Skip frames from different factions
                    if (frame.Faction != p.Faction)
                        return false;

                    // Verify frame is still valid
                    if (!frame.Spawned || !frame.IsCompleted() || frame.IsForbidden(p))
                        return false;

                    // Check for blocking things - we already handled these
                    Thing blocker = GenConstruct.FirstBlockingThing(frame, p);
                    if (blocker != null)
                        return false;

                    // Check if pawn can construct this
                    if (!GenConstruct.CanConstruct(frame, p, forced: forced))
                        return false;

                    // Check if building is an attachment
                    bool isAttachment = false;
                    ThingDef builtDef = GenConstruct.BuiltDefOf(frame.def) as ThingDef;
                    if (builtDef?.building != null && builtDef.building.isAttachment)
                        isAttachment = true;

                    // Check reachability with correct path end mode
                    PathEndMode pathEndMode = isAttachment ? PathEndMode.OnCell : PathEndMode.Touch;
                    if (!p.CanReach(frame, pathEndMode, Danger.Deadly))
                        return false;

                    // Skip if pawn can't reserve
                    if (!p.CanReserve(frame))
                        return false;

                    return true;
                },
                _reachabilityCache
            ) as Frame;

            // Create job if valid frame found
            if (bestFrame != null)
            {
                Job job = JobMaker.MakeJob(JobDefOf.FinishFrame, bestFrame);
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to finish construction of {bestFrame.Label}");
                return job;
            }

            return null;
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_completedFrameCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_ConstructFinishFrames_PawnControl";
        }
    }
}