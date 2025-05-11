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
    public class JobGiver_Construction_ConstructFinishFrames_PawnControl : JobGiver_Scan_PawnControl
    {
        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 2500f }; // 15, 25, 50 tiles

        #region Overrides

        /// <summary>
        /// Use Construction work tag
        /// </summary>
        protected override string WorkTag => "Construction";

        /// <summary>
        /// Override cache update interval for construction jobs
        /// </summary>
        protected override int CacheUpdateInterval => 180; // Update every 3 seconds

        /// <summary>
        /// Finishing construction is more important than starting new projects
        /// </summary>
        public override float GetPriority(Pawn pawn)
        {
            return 6.0f;
        }

        /// <summary>
        /// Gets all frames that are ready to be finished
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null) yield break;

            // Find all frames ready to be finished
            foreach (Frame frame in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame))
            {
                // Skip if not completed or forbidden
                if (frame == null || !frame.Spawned || !frame.IsCompleted() || frame.IsForbidden(Faction.OfPlayer))
                    continue;

                yield return frame;
            }
        }

        /// <summary>
        /// Override TryGiveJob to use StandardTryGiveJob pattern
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            // Use the StandardTryGiveJob pattern
            return Utility_JobGiverManager.StandardTryGiveJob<Frame>(
                pawn,
                "Construction",
                (p, forced) => {
                    if (p?.Map == null)
                        return null;

                    // Quick early exit if there are no valid frames on the map
                    if (!p.Map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame).Any())
                        return null;

                    // Get targets from cache and process them
                    List<Thing> allTargets = GetTargets(p.Map).ToList();
                    if (allTargets.Count == 0)
                        return null;

                    // Convert targets to frames for proper typing
                    List<Frame> frames = allTargets.OfType<Frame>().ToList();
                    if (frames.Count == 0)
                        return null;

                    // Use JobGiverManager for distance bucketing and target selection
                    var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                        p,
                        frames,
                        (frame) => (frame.Position - p.Position).LengthHorizontalSquared,
                        DISTANCE_THRESHOLDS
                    );

                    // Process each bucket to first check for blocking jobs
                    for (int i = 0; i < buckets.Length; i++)
                    {
                        foreach (Frame frame in buckets[i])
                        {
                            // Filter out invalid frames immediately
                            if (frame.Faction != p.Faction || !frame.Spawned ||
                                !frame.IsCompleted() || frame.IsForbidden(p))
                                continue;

                            // Check for blocking things first - this replaces the exception pattern
                            Thing blocker = GenConstruct.FirstBlockingThing(frame, p);
                            if (blocker != null)
                            {
                                Job blockingJob = GenConstruct.HandleBlockingThingJob(frame, p, forced);
                                if (blockingJob != null)
                                {
                                    Utility_DebugManager.LogNormal($"{p.LabelShort} created job to handle {blocker.LabelCap} blocking construction");
                                    return blockingJob;
                                }
                            }
                        }
                    }

                    // Create dictionary for reachability cache with proper nesting
                    int mapId = p.Map.uniqueID;
                    Dictionary<int, Dictionary<Frame, bool>> reachabilityCache = new Dictionary<int, Dictionary<Frame, bool>>();
                    reachabilityCache[mapId] = new Dictionary<Frame, bool>();

                    // With no blocking jobs found, proceed with normal target selection
                    Frame bestFrame = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                        buckets,
                        p,
                        (frame, actor) => { // Changed parameter name from 'pawn' to 'actor' to avoid naming conflict
                            // IMPORTANT: Check faction interaction validity first
                            if (!Utility_JobGiverManager.IsValidFactionInteraction(frame, actor, requiresDesignator: false))
                                return false;

                            // Skip frames from different factions
                            if (frame.Faction != actor.Faction)
                                return false;

                            // Verify frame is still valid
                            if (!frame.Spawned || !frame.IsCompleted() || frame.IsForbidden(actor))
                                return false;

                            // Check for blocking things - we already handled these
                            Thing blocker = GenConstruct.FirstBlockingThing(frame, actor);
                            if (blocker != null)
                                return false;

                            // Check if pawn can construct this
                            if (!GenConstruct.CanConstruct(frame, actor, forced: forced))
                                return false;

                            // Check if building is an attachment
                            bool isAttachment = false;
                            ThingDef builtDef = GenConstruct.BuiltDefOf(frame.def) as ThingDef;
                            if (builtDef?.building != null && builtDef.building.isAttachment)
                                isAttachment = true;

                            // Check reachability with correct path end mode
                            PathEndMode pathEndMode = isAttachment ? PathEndMode.OnCell : PathEndMode.Touch;
                            if (!actor.CanReach(frame, pathEndMode, Danger.Deadly))
                                return false;

                            // Skip if pawn can't reserve
                            if (!actor.CanReserve(frame))
                                return false;

                            return true;
                        },
                        reachabilityCache
                    ) as Frame;

                    // Create job if valid frame found
                    if (bestFrame != null)
                    {
                        Job job = JobMaker.MakeJob(JobDefOf.FinishFrame, bestFrame);
                        Utility_DebugManager.LogNormal($"{p.LabelShort} created job to finish construction of {bestFrame.Label}");
                        return job;
                    }

                    return null;
                },
                debugJobDesc: "finish construction assignment");
        }

        /// <summary>
        /// Creates a job for the pawn using the cached targets
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            if (pawn == null || targets == null || targets.Count == 0)
                return null;

            // Convert targets to frames for proper typing
            List<Frame> frames = targets
                .OfType<Frame>()
                .Where(f => f.Spawned && f.IsCompleted() && !f.IsForbidden(pawn))
                .ToList();

            if (frames.Count == 0)
                return null;

            // Process each frame to check for blocking jobs first
            foreach (Frame frame in frames)
            {
                if (frame.Faction != pawn.Faction)
                    continue;

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

            // Create dictionary for reachability cache
            Dictionary<Frame, bool> reachabilityCache = new Dictionary<Frame, bool>();

            // Find the best frame to work on
            foreach (Frame frame in frames)
            {
                // Skip frames from different factions
                if (frame.Faction != pawn.Faction)
                    continue;

                // Check if pawn can construct this
                if (!GenConstruct.CanConstruct(frame, pawn, forced: forced))
                    continue;

                // Check if building is an attachment
                bool isAttachment = false;
                ThingDef builtDef = GenConstruct.BuiltDefOf(frame.def) as ThingDef;
                if (builtDef?.building != null && builtDef.building.isAttachment)
                    isAttachment = true;

                // Check reachability with correct path end mode
                PathEndMode pathEndMode = isAttachment ? PathEndMode.OnCell : PathEndMode.Touch;
                if (!pawn.CanReach(frame, pathEndMode, Danger.Deadly))
                    continue;

                // Skip if pawn can't reserve
                if (!pawn.CanReserve(frame))
                    continue;

                // Create job if valid frame found
                Job job = JobMaker.MakeJob(JobDefOf.FinishFrame, frame);
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to finish construction of {frame.Label}");
                return job;
            }

            return null;
        }

        #endregion

        /// <summary>
        /// Custom ToString implementation for debugging
        /// </summary>
        public override string ToString()
        {
            return "JobGiver_ConstructFinishFrames_PawnControl";
        }
    }
}