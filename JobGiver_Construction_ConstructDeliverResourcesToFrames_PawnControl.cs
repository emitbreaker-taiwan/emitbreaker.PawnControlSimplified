using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns construction tasks specifically for delivering resources to construction frames.
    /// Uses the Construction work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Construction_ConstructDeliverResourcesToFrames_PawnControl : JobGiver_Common_ConstructDeliverResources_PawnControl<Frame>
    {
        #region Configuration

        /// <summary>
        /// Use Construction work tag
        /// </summary>
        public override string WorkTag => "Construction";

        /// <summary>
        /// Unique name for debug messages
        /// </summary>
        protected override string JobDescription => "delivering resources to frames (construction) assignment";

        /// <summary>
        /// Human-readable name for debug logging
        /// </summary>
        protected override string DebugName => JobDescription;

        /// <summary>
        /// Cache update interval - frames need more frequent updates than blueprints
        /// </summary>
        protected override int CacheUpdateInterval => base.CacheUpdateInterval;

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_Construction_ConstructDeliverResourcesToFrames_PawnControl() : base()
        {
            // Base constructor already initializes the cache system
        }

        #endregion

        #region Core Flow

        /// <summary>
        /// Construction workers should prioritize this even higher than haulers
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            return 5.8f;
        }

        /// <summary>
        /// Check if map meets requirements for frame resource delivery
        /// </summary>
        protected override bool AreMapRequirementsMet(Pawn pawn)
        {
            // Check if map has any frames that need resources
            if (pawn?.Map == null)
                return false;

            // Quick check for construction frames
            return pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Construction).Any(f =>
                f is Frame frame &&
                frame.Spawned &&
                frame.TotalMaterialCost().Count > 0);
        }

        #endregion

        #region Target Selection

        /// <summary>
        /// Job-specific cache update method for frame resource delivery
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            // Get all frames that need resources
            return GetConstructionTargets(map).Cast<Thing>();
        }

        /// <summary>
        /// Gets construction frames that need resources
        /// </summary>
        protected override List<Frame> GetConstructionTargets(Map map)
        {
            if (map == null) return new List<Frame>();

            var result = new List<Frame>();

            // Find all frames needing materials, regardless of faction
            foreach (Frame frame in map.listerThings.ThingsInGroup(ThingRequestGroup.Construction))
            {
                if (frame != null && frame.Spawned && !frame.IsForbidden(Faction.OfPlayer))
                {
                    result.Add(frame);
                }
            }

            // Limit cache size for performance
            return LimitListSize(result, 200);
        }

        #endregion

        #region Job Creation

        /// <summary>
        /// Processes the cached targets to find valid frames for resource delivery jobs
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            if (pawn == null || targets == null || targets.Count == 0)
                return null;

            // First check faction validation
            if (!IsValidFactionForConstruction(pawn))
                return null;

            // Convert generic Things to Frames and filter for valid faction match
            List<Frame> frames = targets
                .OfType<Frame>()
                .Where(f => f.Spawned && !f.IsForbidden(pawn) && IsValidTargetFaction(f, pawn))
                .ToList();

            if (frames.Count == 0)
                return null;

            // Try to create a resource delivery job for each frame
            foreach (Frame frame in frames)
            {
                Job job = ResourceDeliverJobFor(pawn, frame);
                if (job != null)
                    return job;
            }

            return null;
        }

        #endregion

        #region Resource Delivery Helpers

        /// <summary>
        /// Finds nearby construction sites that need the same resources
        /// </summary>
        protected override HashSet<Thing> FindNearbyNeeders(
            Pawn pawn,
            ThingDef stuff,
            Frame originalTarget,
            int resNeeded,
            int resTotalAvailable,
            out int neededTotal,
            out Job jobToMakeNeederAvailable)
        {
            neededTotal = resNeeded;
            jobToMakeNeederAvailable = null;
            HashSet<Thing> nearbyNeeders = new HashSet<Thing>();

            // Look for other frames nearby
            foreach (Thing t in GenRadial.RadialDistinctThingsAround(originalTarget.Position, originalTarget.Map, NEARBY_CONSTRUCT_SCAN_RADIUS, true))
            {
                if (neededTotal < resTotalAvailable)
                {
                    // Check if it's a valid frame needing resources
                    if (IsNewValidNearbyNeeder(t, nearbyNeeders, originalTarget, pawn))
                    {
                        // Get how much material is needed
                        int materialNeeded = 0;
                        if (t is Frame frame)
                        {
                            materialNeeded = frame.ThingCountNeeded(stuff);
                        }

                        if (materialNeeded > 0)
                        {
                            nearbyNeeders.Add(t);
                            neededTotal += materialNeeded;
                        }
                    }
                }
                else
                {
                    break; // We have enough needers
                }
            }

            return nearbyNeeders;
        }

        /// <summary>
        /// Determines if a thing is a valid nearby construction site needing resources
        /// Ensures proper faction matching
        /// </summary>
        protected override bool IsNewValidNearbyNeeder(Thing t, HashSet<Thing> nearbyNeeders, Frame originalTarget, Pawn pawn)
        {
            return t is Frame frame &&
                   frame != originalTarget &&
                   frame.Faction == pawn.Faction &&  // Must match pawn's faction
                   !nearbyNeeders.Contains(frame) &&
                   !frame.IsForbidden(pawn) &&
                   pawn.CanReserve(frame);
        }

        #endregion

        #region Thing-Based Helpers

        /// <summary>
        /// Basic validation for frame targets
        /// </summary>
        protected override bool ValidateConstructionTarget(Thing thing, Pawn pawn, bool forced = false)
        {
            // First perform base validation
            if (!base.ValidateConstructionTarget(thing, pawn, forced))
                return false;

            // Add frame-specific validation if needed
            Frame frame = thing as Frame;
            if (frame == null)
                return false;

            // Check if frame still needs resources
            if (frame.TotalMaterialCost().Count == 0)
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
            return "JobGiver_Construction_ConstructDeliverResourcesToFrames_PawnControl";
        }

        #endregion
    }
}