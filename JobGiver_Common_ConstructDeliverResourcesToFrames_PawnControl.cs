using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Abstract base implementation for delivering resources to frames.
    /// </summary>
    public abstract class JobGiver_Common_ConstructDeliverResourcesToFrames_PawnControl : JobGiver_Common_ConstructDeliverResources_PawnControl<Frame>
    {
        protected override void UpdateTargetCacheSafely(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_targetCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_targetCache.ContainsKey(mapId))
                    _targetCache[mapId].Clear();
                else
                    _targetCache[mapId] = new List<Frame>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Frame, bool>();

                // Find all frames needing materials
                foreach (Frame frame in map.listerThings.ThingsInGroup(ThingRequestGroup.Construction))
                {
                    if (frame != null && frame.Spawned && !frame.IsForbidden(Faction.OfPlayer))
                    {
                        _targetCache[mapId].Add(frame);
                    }
                }

                // Limit cache size for performance
                int maxCacheSize = 200;
                if (_targetCache[mapId].Count > maxCacheSize)
                {
                    _targetCache[mapId] = _targetCache[mapId].Take(maxCacheSize).ToList();
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        protected override Job TryCreateDeliveryJob(Pawn pawn)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_targetCache.ContainsKey(mapId) || _targetCache[mapId].Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                _targetCache[mapId],
                (frame) => (frame.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Find a valid frame
            for (int b = 0; b < buckets.Length; b++)
            {
                if (buckets[b].Count == 0)
                    continue;

                // Randomize within each bucket for even distribution
                buckets[b].Shuffle();

                foreach (Frame frame in buckets[b])
                {
                    // Skip frames from different factions
                    if (frame.Faction != pawn.Faction)
                        continue;

                    // Skip if thing is forbidden or unreachable
                    if (frame.IsForbidden(pawn) || !pawn.CanReach(frame, PathEndMode.Touch, pawn.NormalMaxDanger()))
                        continue;

                    // Skip if we can't reserve
                    if (!pawn.CanReserve(frame))
                        continue;

                    // Check if there are resources to deliver
                    Job resourceJob = ResourceDeliverJobFor(pawn, frame);
                    if (resourceJob != null)
                        return resourceJob;
                }
            }

            return null;
        }

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
                        Frame frame = t as Frame;
                        if (frame != null)
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

        protected override bool IsNewValidNearbyNeeder(Thing t, HashSet<Thing> nearbyNeeders, Frame originalTarget, Pawn pawn)
        {
            return t is Frame &&
                   t != originalTarget &&
                   t.Faction == pawn.Faction &&
                   !nearbyNeeders.Contains(t) &&
                   !t.IsForbidden(pawn) &&
                   pawn.CanReserve(t);
        }
    }
}