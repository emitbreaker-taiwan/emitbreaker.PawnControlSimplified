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
    /// JobGiver that assigns hauling tasks specifically for delivering resources to construction blueprints.
    /// Uses the Hauling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_ConstructDeliverResourcesToBlueprints_PawnControl : ThinkNode_JobGiver
    {
        // Cache for blueprints needing materials
        private static readonly Dictionary<int, List<Blueprint>> _blueprintCache = new Dictionary<int, List<Blueprint>>();
        private static readonly Dictionary<int, Dictionary<Blueprint, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Blueprint, bool>>();
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
            // Blueprint delivery is slightly more important than frame delivery
            return 5.7f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<Plant>(
                pawn,
                "Hauling",
                (p, forced) => {
                    // Update fire cache
                    UpdateBlueprintCacheSafely(p.Map);

                    // Find and create a job for cutting plants with VALID DESIGNATORS ONLY
                    return TryCreateBlueprintDeliveryJob(pawn);
                },
                debugJobDesc: "delivering resources to blueprints assignment",
                skipEmergencyCheck: false);
        }

        /// <summary>
        /// Updates the cache of blueprints needing materials
        /// </summary>
        private void UpdateBlueprintCacheSafely(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_blueprintCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_blueprintCache.ContainsKey(mapId))
                    _blueprintCache[mapId].Clear();
                else
                    _blueprintCache[mapId] = new List<Blueprint>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Blueprint, bool>();

                // Find all blueprints needing materials - except plants and installations
                foreach (Blueprint blueprint in map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint))
                {
                    if (blueprint != null &&
                        blueprint.Spawned &&
                        !blueprint.IsForbidden(Faction.OfPlayer) &&
                        !(blueprint is Blueprint_Install) &&
                        !(blueprint.def.entityDefToBuild is ThingDef entityDefToBuild && entityDefToBuild.plant != null))
                    {
                        _blueprintCache[mapId].Add(blueprint);
                    }
                }

                // Limit cache size for performance
                int maxCacheSize = 200;
                if (_blueprintCache[mapId].Count > maxCacheSize)
                {
                    _blueprintCache[mapId] = _blueprintCache[mapId].Take(maxCacheSize).ToList();
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Creates a job for delivering resources to blueprints or creating no-cost frames
        /// </summary>
        private Job TryCreateBlueprintDeliveryJob(Pawn pawn)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_blueprintCache.ContainsKey(mapId) || _blueprintCache[mapId].Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                _blueprintCache[mapId],
                (blueprint) => (blueprint.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Find a valid blueprint
            for (int b = 0; b < buckets.Length; b++)
            {
                if (buckets[b].Count == 0)
                    continue;

                // Randomize within each bucket for even distribution
                buckets[b].Shuffle();

                foreach (Blueprint blueprint in buckets[b])
                {
                    // Check for blocking things first
                    Thing blocker = GenConstruct.FirstBlockingThing(blueprint, pawn);
                    if (blocker != null)
                    {
                        Job blockingJob = GenConstruct.HandleBlockingThingJob(blueprint, pawn, false);
                        if (blockingJob != null)
                            return blockingJob;

                        continue;
                    }

                    // Skip if pawn can't construct this
                    if (!GenConstruct.CanConstruct(blueprint, pawn, false, jobForReservation: JobDefOf.HaulToContainer))
                        continue;

                    // Check if we need to remove an existing floor first
                    if (ShouldRemoveExistingFloorFirst(pawn, blueprint))
                    {
                        Job removeFloorJob = RemoveExistingFloorJob(pawn, blueprint);
                        if (removeFloorJob != null)
                            return removeFloorJob;

                        continue;
                    }

                    // Try to deliver resources
                    Job resourceJob = ResourceDeliverJobFor(pawn, blueprint);
                    if (resourceJob != null)
                        return resourceJob;

                    // For blueprints with no material cost, create a frame directly
                    if (blueprint.TotalMaterialCost().Count == 0)
                    {
                        Job frameJob = JobMaker.MakeJob(JobDefOf.PlaceNoCostFrame, blueprint);

                        if (Prefs.DevMode)
                            Log.Message($"[PawnControl] {pawn.LabelShort} created job to place no-cost frame for {blueprint.Label}");

                        return frameJob;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Creates a resource delivery job for a blueprint
        /// </summary>
        private Job ResourceDeliverJobFor(Pawn pawn, Blueprint blueprint)
        {
            _missingResources.Clear();

            // Check each material needed
            foreach (ThingDefCountClass need in blueprint.TotalMaterialCost())
            {
                int neededCount = blueprint.ThingCountNeeded(need.thingDef);
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

                    // Find nearby blueprints that need the same material
                    int totalNeeded;
                    Job jobToMakeNeederAvailable;
                    HashSet<Thing> nearbyNeeders = FindNearbyNeeders(
                        pawn,
                        need.thingDef,
                        blueprint,
                        neededCount,
                        totalAvailable,
                        true,
                        out totalNeeded,
                        out jobToMakeNeederAvailable
                    );

                    // If we need to clear a floor first, do that
                    if (jobToMakeNeederAvailable != null)
                        return jobToMakeNeederAvailable;

                    // Add the current blueprint to the list
                    nearbyNeeders.Add(blueprint);

                    // Find the closest blueprint to haul to first
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
                        primaryTarget = blueprint;
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
                    job.targetC = blueprint;

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
                        Log.Message($"[PawnControl] {pawn.LabelShort} created job to deliver {resourcesNeeded}x {need.thingDef.label} to {blueprint.Label}");
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
        /// Finds nearby construction that needs the same resources
        /// </summary>
        private HashSet<Thing> FindNearbyNeeders(
            Pawn pawn,
            ThingDef stuff,
            Blueprint originalTarget,
            int resNeeded,
            int resTotalAvailable,
            bool canRemoveExistingFloorUnderNearbyNeeders,
            out int neededTotal,
            out Job jobToMakeNeederAvailable)
        {
            neededTotal = resNeeded;
            HashSet<Thing> nearbyNeeders = new HashSet<Thing>();

            // Look for other blueprints nearby
            foreach (Thing t in GenRadial.RadialDistinctThingsAround(originalTarget.Position, originalTarget.Map, NEARBY_CONSTRUCT_SCAN_RADIUS, true))
            {
                if (neededTotal < resTotalAvailable)
                {
                    // Check if it's a valid blueprint needing resources
                    if (IsNewValidNearbyNeeder(t, nearbyNeeders, originalTarget, pawn))
                    {
                        // Check if we need to remove existing floor first
                        Blueprint blue = t as Blueprint;
                        if (blue != null && ShouldRemoveExistingFloorFirst(pawn, blue))
                        {
                            continue;
                        }

                        // Get how much material is needed
                        int materialNeeded = 0;
                        if (t is IConstructible constructible)
                        {
                            materialNeeded = constructible.ThingCountNeeded(stuff);
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

            // Check if any blueprint needs floor removing first
            if (originalTarget.def.entityDefToBuild is TerrainDef &&
                canRemoveExistingFloorUnderNearbyNeeders &&
                neededTotal < resTotalAvailable)
            {
                foreach (Thing t in GenRadial.RadialDistinctThingsAround(originalTarget.Position, originalTarget.Map, 3f, false))
                {
                    if (IsNewValidNearbyNeeder(t, nearbyNeeders, originalTarget, pawn) && t is Blueprint blue)
                    {
                        Job job = RemoveExistingFloorJob(pawn, blue);
                        if (job != null)
                        {
                            jobToMakeNeederAvailable = job;
                            return nearbyNeeders;
                        }
                    }
                }
            }

            jobToMakeNeederAvailable = null;
            return nearbyNeeders;
        }

        /// <summary>
        /// Determines if a thing is a valid nearby construction needing resources
        /// </summary>
        private bool IsNewValidNearbyNeeder(Thing t, HashSet<Thing> nearbyNeeders, Blueprint originalTarget, Pawn pawn)
        {
            return t is Blueprint &&
                   t != originalTarget &&
                   t.Faction == pawn.Faction &&
                   !(t is Blueprint_Install) &&
                   !nearbyNeeders.Contains(t) &&
                   !t.IsForbidden(pawn) &&
                   GenConstruct.CanConstruct(t, pawn, false, jobForReservation: JobDefOf.HaulToContainer);
        }

        /// <summary>
        /// Determines if an existing floor needs to be removed before building
        /// </summary>
        private bool ShouldRemoveExistingFloorFirst(Pawn pawn, Blueprint blueprint)
        {
            return blueprint.def.entityDefToBuild is TerrainDef &&
                   pawn.Map.terrainGrid.CanRemoveTopLayerAt(blueprint.Position);
        }

        /// <summary>
        /// Creates a job to remove an existing floor
        /// </summary>
        private Job RemoveExistingFloorJob(Pawn pawn, Blueprint blueprint)
        {
            if (!ShouldRemoveExistingFloorFirst(pawn, blueprint))
                return null;

            if (!pawn.CanReserve(blueprint.Position))
                return null;

            if (pawn.WorkTypeIsDisabled(WorkTypeDefOf.Construction))
                return null;

            Job job = JobMaker.MakeJob(JobDefOf.RemoveFloor, blueprint.Position);
            job.ignoreDesignations = true;
            return job;
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            _blueprintCache.Clear();
            _reachabilityCache.Clear();
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_ConstructDeliverResourcesToBlueprints_PawnControl";
        }
    }
}