using RimWorld;
using System;
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
        #region Overrides

        /// <summary>
        /// Use Hauling work tag
        /// </summary>
        protected override string WorkTag => "Hauling";

        /// <summary>
        /// Unique name for debug messages
        /// </summary>
        protected override string JobDescription => "delivering resources to frames (hauling) assignment";

        /// <summary>
        /// Frame delivery is important hauling
        /// </summary>
        public override float GetPriority(Pawn pawn)
        {
            return 5.6f;
        }

        /// <summary>
        /// Gets construction frames that need resources
        /// </summary>
        protected override List<Frame> GetConstructionTargets(Map map)
        {
            if (map == null) return new List<Frame>();

            var result = new List<Frame>();

            // Find all frames needing materials
            foreach (Frame frame in map.listerThings.ThingsInGroup(ThingRequestGroup.Construction))
            {
                if (frame != null && frame.Spawned && !frame.IsForbidden(Faction.OfPlayer))
                {
                    result.Add(frame);
                }
            }

            // Limit cache size for performance
            int maxCacheSize = 200;
            if (result.Count > maxCacheSize)
            {
                result = result.Take(maxCacheSize).ToList();
            }

            return result;
        }

        /// <summary>
        /// Override TryGiveJob to use StandardTryGiveJob pattern
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            return CreateDeliveryJob<JobGiver_Hauling_ConstructDeliverResourcesToFrames_PawnControl>(pawn);
        }

        /// <summary>
        /// Processes cached targets to create a job for the pawn.
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            foreach (var target in targets)
            {
                if (target is Frame frame && !frame.IsForbidden(pawn) && pawn.CanReserve(frame))
                {
                    return ResourceDeliverJobFor(pawn, frame);
                }
            }
            return null;
        }

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
        /// </summary>
        protected override bool IsNewValidNearbyNeeder(Thing t, HashSet<Thing> nearbyNeeders, Frame originalTarget, Pawn pawn)
        {
            return t is Frame &&
                   t != originalTarget &&
                   t.Faction == pawn.Faction &&
                   !nearbyNeeders.Contains(t) &&
                   !t.IsForbidden(pawn) &&
                   pawn.CanReserve(t);
        }

        #endregion
    }
}