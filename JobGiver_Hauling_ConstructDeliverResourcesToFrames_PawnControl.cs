using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns hauling tasks specifically for delivering resources to construction frames.
    /// Uses the Hauling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Hauling_ConstructDeliverResourcesToFrames_PawnControl : JobGiver_Common_ConstructDeliverResources_PawnControl<Frame>
    {
        #region Configuration

        /// <summary>
        /// Use Hauling work tag
        /// </summary>
        public override string WorkTag => "Hauling";

        /// <summary>
        /// Unique name for debug messages
        /// </summary>
        protected override string JobDescription => "delivering resources to frames (hauling) assignment";

        /// <summary>
        /// Human-readable name for debug logging
        /// </summary>
        protected override string DebugName => JobDescription;

        /// <summary>
        /// Whether this construction job requires specific tag for non-humanlike pawns
        /// </summary>
        public override PawnEnumTags RequiredTag => PawnEnumTags.AllowWork_Hauling;

        /// <summary>
        /// Cache update interval - frames may change more frequently
        /// </summary>
        protected override int CacheUpdateInterval => base.CacheUpdateInterval;

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_Hauling_ConstructDeliverResourcesToFrames_PawnControl() : base()
        {
            // Base constructor already initializes the cache system
        }

        #endregion

        #region Core Flow

        /// <summary>
        /// Frame delivery is important hauling
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            return 5.6f;
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
                if (frame != null && frame.Spawned)
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
        /// Processes cached targets to create a job for the pawn.
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            if (pawn == null || targets == null || targets.Count == 0)
                return null;

            // First check faction validation
            if (!IsValidFactionForConstruction(pawn))
                return null;

            // Convert to frames and filter for valid faction match
            List<Frame> frames = targets
                .OfType<Frame>()
                .Where(f => f.Spawned && !f.IsForbidden(pawn) && IsValidTargetFaction(f, pawn))
                .ToList();

            if (frames.Count == 0)
                return null;

            // Try to create a job for each valid frame
            foreach (Frame frame in frames)
            {
                if (pawn.CanReserve(frame))
                {
                    Job job = ResourceDeliverJobFor(pawn, frame);
                    if (job != null)
                        return job;
                }
            }

            return null;
        }

        /// <summary>
        /// Creates a construction job for delivering resources to frames
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
                    frames = GetConstructionTargets(pawn.Map);
                }
                else
                {
                    // Convert cached targets to proper type
                    frames = cachedTargets.OfType<Frame>().ToList();
                }
            }
            else
            {
                // Convert cached targets to proper type
                frames = cachedTargets.OfType<Frame>().ToList();
            }

            if (frames.Count == 0)
                return null;

            // Filter for valid frames
            frames = frames
                .Where(f => f.Spawned &&
                          !f.IsForbidden(pawn) &&
                          IsValidTargetFaction(f, pawn))
                .ToList();

            if (frames.Count == 0)
                return null;

            // Try to create a job for each valid frame
            foreach (Frame frame in frames)
            {
                if (pawn.CanReserve(frame))
                {
                    Job job = ResourceDeliverJobFor(pawn, frame);
                    if (job != null)
                        return job;
                }
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

            // Add frame-specific validation
            Frame frame = thing as Frame;
            if (frame == null)
                return false;

            // Check if the frame still needs resources
            if (frame.TotalMaterialCost().Count == 0)
                return false;

            // Extra reservation check
            if (!pawn.CanReserve(frame))
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
            return "JobGiver_Hauling_ConstructDeliverResourcesToFrames_PawnControl";
        }

        #endregion
    }
}