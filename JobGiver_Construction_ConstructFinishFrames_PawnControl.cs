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
    public class JobGiver_Construction_ConstructFinishFrames_PawnControl : JobGiver_Construction_PawnControl
    {
        #region Configuration

        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.FinishFrame;

        /// <summary>
        /// Human-readable name for debug logging
        /// </summary>
        protected override string DebugName => "finish construction assignment";

        /// <summary>
        /// Whether this job giver requires a designator to operate
        /// Finishing frames doesn't require designations
        /// </summary>
        protected override bool RequiresDesignator => false;

        /// <summary>
        /// Whether this job giver requires map zone or area
        /// </summary>
        protected override bool RequiresMapZoneorArea => false;

        /// <summary>
        /// Whether this job requires player faction specifically
        /// Frames can be finished by any faction that owns them
        /// </summary>
        protected override bool RequiresPlayerFaction => false;

        /// <summary>
        /// Update cache every 3 seconds for construction frame jobs
        /// </summary>
        protected override int CacheUpdateInterval => 180;

        /// <summary>
        /// Standard distance thresholds for bucketing frames
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 225f, 625f, 2500f }; // 15, 25, 50 tiles

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_Construction_ConstructFinishFrames_PawnControl() : base()
        {
            // Base constructor already initializes the cache system
        }

        #endregion

        #region Core Flow

        /// <summary>
        /// Finishing construction is more important than starting new projects
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            return 6.0f;
        }

        /// <summary>
        /// Checks if the map meets requirements for this frame finishing job
        /// </summary>
        protected override bool AreMapRequirementsMet(Pawn pawn)
        {
            // Check basic requirements first
            if (pawn?.Map == null)
                return false;

            // Quick check - are there any building frames at all?
            return pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame).Any(f =>
                f is Frame frame && frame.IsCompleted());
        }

        #endregion

        #region Target Selection

        /// <summary>
        /// Job-specific cache update method that implements specialized target collection logic
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            if (map == null)
                yield break;

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
        /// Gets all frames that are ready to be finished
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // Use the specialized cache update method
            return UpdateJobSpecificCache(map);
        }

        #endregion

        #region Job Creation

        /// <summary>
        /// Implement to create the specific construction job for frame finishing
        /// </summary>
        protected override Job CreateConstructionJob(Pawn pawn, bool forced)
        {
            if (pawn?.Map == null)
                return null;

            int mapId = pawn.Map.uniqueID;

            // Get targets from the cache
            List<Thing> cachedTargets = GetCachedTargets(mapId);
            List<Frame> frames;

            // If cache is empty or not yet populated
            if (cachedTargets == null || cachedTargets.Count == 0)
            {
                // Try to update cache if needed
                if (ShouldUpdateCache(mapId))
                {
                    UpdateCache(mapId, pawn.Map);
                    cachedTargets = GetCachedTargets(mapId);
                }

                // If still empty, get targets directly
                if (cachedTargets == null || cachedTargets.Count == 0)
                {
                    frames = GetTargets(pawn.Map).OfType<Frame>().ToList();
                }
                else
                {
                    frames = cachedTargets.OfType<Frame>().ToList();
                }
            }
            else
            {
                frames = cachedTargets.OfType<Frame>().ToList();
            }

            if (frames.Count == 0)
                return null;

            // Filter for valid frames
            frames = frames.Where(f =>
                f.Spawned &&
                f.IsCompleted() &&
                !f.IsForbidden(pawn)).ToList();

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

            // Use distance bucketing for more efficient frame selection
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                frames,
                (frame) => (frame.Position - pawn.Position).LengthHorizontalSquared,
                DistanceThresholds
            );

            // Find the best frame to work on using bucketed approach
            Frame bestFrame = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (frame, actor) => ValidateConstructionTarget(frame, actor, forced),
                null
            ) as Frame;

            if (bestFrame != null)
            {
                Job job = JobMaker.MakeJob(WorkJobDef, bestFrame);
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to finish construction of {bestFrame.Label}");
                return job;
            }

            return null;
        }

        #endregion

        #region Thing-Based Helpers

        /// <summary>
        /// Basic validation for construction frame targets
        /// </summary>
        protected override bool ValidateConstructionTarget(Thing thing, Pawn pawn, bool forced = false)
        {
            // First perform base validation
            if (!base.ValidateConstructionTarget(thing, pawn, forced))
                return false;

            Frame frame = thing as Frame;
            if (frame == null)
                return false;

            // Skip frames from different factions
            if (frame.Faction != pawn.Faction)
                return false;

            // Verify frame is still valid and completed
            if (!frame.Spawned || !frame.IsCompleted())
                return false;

            // Check for blocking things
            Thing blocker = GenConstruct.FirstBlockingThing(frame, pawn);
            if (blocker != null)
                return false;

            // Check if pawn can construct this
            if (!GenConstruct.CanConstruct(frame, pawn, forced))
                return false;

            // Check if building is an attachment
            bool isAttachment = false;
            ThingDef builtDef = GenConstruct.BuiltDefOf(frame.def) as ThingDef;
            if (builtDef?.building != null && builtDef.building.isAttachment)
                isAttachment = true;

            // Check reachability with correct path end mode
            PathEndMode pathEndMode = isAttachment ? PathEndMode.OnCell : PathEndMode.Touch;
            if (!pawn.CanReach(frame, pathEndMode, Danger.Deadly))
                return false;

            return true;
        }

        #endregion

        #region Reset

        /// <summary>
        /// Reset the cache - implements IResettableCache
        /// </summary>
        public override void Reset()
        {
            // Use centralized cache reset from parent
            base.Reset();
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return "JobGiver_ConstructFinishFrames_PawnControl";
        }

        #endregion
    }
}