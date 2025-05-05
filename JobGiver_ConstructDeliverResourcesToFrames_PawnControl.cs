using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns hauling tasks specifically for delivering resources to construction frames.
    /// Uses the Hauling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_ConstructDeliverResourcesToFrames_PawnControl : ThinkNode_JobGiver
    {
        // Cache for frames needing materials
        private static readonly Dictionary<int, List<Frame>> _frameCache = new Dictionary<int, List<Frame>>();
        private static readonly Dictionary<int, Dictionary<Frame, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Frame, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 250; // Update every ~4 seconds

        // Cache for resource scanning
        private static readonly List<Thing> _resourcesAvailable = new List<Thing>();
        private static readonly Dictionary<ThingDef, int> _missingResources = new Dictionary<ThingDef, int>();

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 2500f }; // 15, 25, 50 tiles

        // Constants
        private const float MULTI_PICKUP_RADIUS = 5f;
        private const float NEARBY_CONSTRUCT_SCAN_RADIUS = 8f;

        // Translation strings
        private static string ForbiddenLowerTranslated;
        private static string NoPathTranslated;

        public static void ResetStaticData()
        {
            ForbiddenLowerTranslated = "ForbiddenLower".Translate();
            NoPathTranslated = "NoPath".Translate();
        }

        public override float GetPriority(Pawn pawn)
        {
            // Frame delivery is important hauling
            return 5.6f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<Plant>(
                pawn,
                "Hauling",
                (p, forced) => {
                    // Update fire cache
                    UpdateFrameCacheSafely(p.Map);

                    // Find and create a job for cutting plants with VALID DESIGNATORS ONLY
                    return TryCreateFrameDeliveryJob(pawn);
                },
                debugJobDesc: "delivering resources to frames assignment",
                skipEmergencyCheck: false);
        }

        /// <summary>
        /// Updates the cache of frames needing materials
        /// </summary>
        private void UpdateFrameCacheSafely(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_frameCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_frameCache.ContainsKey(mapId))
                    _frameCache[mapId].Clear();
                else
                    _frameCache[mapId] = new List<Frame>();

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
                        _frameCache[mapId].Add(frame);
                    }
                }

                // Limit cache size for performance
                int maxCacheSize = 200;
                if (_frameCache[mapId].Count > maxCacheSize)
                {
                    _frameCache[mapId] = _frameCache[mapId].Take(maxCacheSize).ToList();
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Creates a job for delivering resources to frames
        /// </summary>
        private Job TryCreateFrameDeliveryJob(Pawn pawn)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_frameCache.ContainsKey(mapId) || _frameCache[mapId].Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                _frameCache[mapId],
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

        /// <summary>
        /// Creates a resource delivery job for a frame
        /// </summary>
        private Job ResourceDeliverJobFor(Pawn pawn, Frame frame)
        {
            _missingResources.Clear();

            // Check each material needed - use TotalMaterialCost() from Frame class
            foreach (ThingDefCountClass need in frame.TotalMaterialCost())
            {
                // Use ThingCountNeeded from Frame class
                int neededCount = frame.ThingCountNeeded(need.thingDef);
                if (neededCount > 0)
                {
                    // Check if materials are available
                    if (!pawn.Map.itemAvailability.ThingsAvailableAnywhere(need.thingDef, neededCount, pawn))
                    {
                        _missingResources.Add(need.thingDef, neededCount);
                        continue;
                    }

                    // Find closest resource
                    Thing foundResource = GenClosest.ClosestThingReachable(
                        pawn.Position,
                        pawn.Map,
                        ThingRequest.ForDef(need.thingDef),
                        PathEndMode.ClosestTouch,
                        TraverseParms.For(pawn),
                        validator: r => ResourceValidator(pawn, need, r)
                    );

                    // If no resource found, mark as missing
                    if (foundResource == null)
                    {
                        _missingResources.Add(need.thingDef, neededCount);
                        continue;
                    }

                    // Find all available nearby resources of the same type
                    int totalAvailable;
                    FindAvailableNearbyResources(foundResource, pawn, out totalAvailable);

                    // Find nearby frames that need the same material
                    int totalNeeded;
                    HashSet<Thing> nearbyNeeders = FindNearbyNeeders(
                        pawn,
                        need.thingDef,
                        frame,
                        neededCount,
                        totalAvailable,
                        out totalNeeded
                    );

                    // Add the current frame to the list
                    nearbyNeeders.Add(frame);

                    // Find the closest frame to haul to first
                    Thing primaryTarget;
                    if (nearbyNeeders.Count > 0)
                    {
                        // Pick the closest needer to the resource
                        primaryTarget = nearbyNeeders.MinBy(needer =>
                            IntVec3Utility.ManhattanDistanceFlat(foundResource.Position, needer.Position));
                        nearbyNeeders.Remove(primaryTarget);
                    }
                    else
                    {
                        primaryTarget = frame;
                    }

                    // Calculate how many resources to pick up
                    int resourcesNeeded = Math.Min(totalAvailable, totalNeeded);
                    int resourcesAvailableCount = 0;
                    int index = 0;

                    do
                    {
                        resourcesAvailableCount += _resourcesAvailable[index].stackCount;
                        index++;
                    }
                    while (resourcesAvailableCount < resourcesNeeded &&
                           resourcesAvailableCount < totalAvailable &&
                           index < _resourcesAvailable.Count);

                    // Clean up resource list - remove ones we won't use
                    _resourcesAvailable.RemoveRange(index, _resourcesAvailable.Count - index);
                    _resourcesAvailable.Remove(foundResource);

                    // Create the hauling job
                    Job job = JobMaker.MakeJob(JobDefOf.HaulToContainer);
                    job.targetA = foundResource;
                    job.targetQueueA = new List<LocalTargetInfo>();

                    // Add all resources to pick up
                    foreach (Thing resource in _resourcesAvailable)
                    {
                        job.targetQueueA.Add(resource);
                    }

                    // Set primary target
                    job.targetB = primaryTarget;
                    job.targetC = frame;

                    // Add secondary targets if available
                    if (nearbyNeeders.Count > 0)
                    {
                        job.targetQueueB = new List<LocalTargetInfo>();
                        foreach (Thing needer in nearbyNeeders)
                        {
                            job.targetQueueB.Add(needer);
                        }
                    }

                    // Set count and mode
                    job.count = resourcesNeeded;
                    job.haulMode = HaulMode.ToContainer;

                    if (Prefs.DevMode)
                    {
                        Log.Message($"[PawnControl] {pawn.LabelShort} created job to deliver {resourcesNeeded}x {need.thingDef.label} to {frame.Label}");
                    }

                    return job;
                }
            }

            return null;
        }

        /// <summary>
        /// Determines if a resource is valid for hauling
        /// </summary>
        private bool ResourceValidator(Pawn pawn, ThingDefCountClass need, Thing t)
        {
            return t.def == need.thingDef && !t.IsForbidden(pawn) && pawn.CanReserve(t);
        }

        /// <summary>
        /// Finds all available resources of the same type near a resource
        /// </summary>
        private void FindAvailableNearbyResources(Thing firstFoundResource, Pawn pawn, out int resTotalAvailable)
        {
            int maxStackSpace = pawn.carryTracker.MaxStackSpaceEver(firstFoundResource.def);
            resTotalAvailable = 0;
            _resourcesAvailable.Clear();
            _resourcesAvailable.Add(firstFoundResource);
            resTotalAvailable += firstFoundResource.stackCount;

            if (resTotalAvailable < maxStackSpace)
            {
                foreach (Thing thing in GenRadial.RadialDistinctThingsAround(firstFoundResource.PositionHeld, firstFoundResource.MapHeld, MULTI_PICKUP_RADIUS, false))
                {
                    if (resTotalAvailable >= maxStackSpace)
                    {
                        resTotalAvailable = maxStackSpace;
                        break;
                    }

                    if (thing.def == firstFoundResource.def && !thing.IsForbidden(pawn) && pawn.CanReserve(thing))
                    {
                        _resourcesAvailable.Add(thing);
                        resTotalAvailable += thing.stackCount;
                    }
                }
            }

            resTotalAvailable = Mathf.Min(resTotalAvailable, maxStackSpace);
        }

        /// <summary>
        /// Finds nearby frames that need the same resources
        /// </summary>
        private HashSet<Thing> FindNearbyNeeders(
            Pawn pawn,
            ThingDef stuff,
            Frame originalTarget,
            int resNeeded,
            int resTotalAvailable,
            out int neededTotal)
        {
            neededTotal = resNeeded;
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
                            // Use ThingCountNeeded from Frame class
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
        /// Determines if a thing is a valid nearby frame needing resources
        /// </summary>
        private bool IsNewValidNearbyNeeder(Thing t, HashSet<Thing> nearbyNeeders, Frame originalTarget, Pawn pawn)
        {
            return t is Frame &&
                   t != originalTarget &&
                   t.Faction == pawn.Faction &&
                   !nearbyNeeders.Contains(t) &&
                   !t.IsForbidden(pawn) &&
                   pawn.CanReserve(t);
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            _frameCache.Clear();
            _reachabilityCache.Clear();
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_ConstructDeliverResourcesToFrames_PawnControl";
        }
    }
}