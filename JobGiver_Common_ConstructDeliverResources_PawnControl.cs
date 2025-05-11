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
    /// Abstract base class for JobGivers that handle delivering resources to construction sites.
    /// </summary>
    public abstract class JobGiver_Common_ConstructDeliverResources_PawnControl<T> : JobGiver_Scan_PawnControl where T : Thing, IConstructible
    {
        #region Configuration

        // Cache for resource scanning
        private static readonly List<Thing> _resourcesAvailable = new List<Thing>();
        private static readonly Dictionary<ThingDef, int> _missingResources = new Dictionary<ThingDef, int>();

        // Distance thresholds for bucketing
        protected static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 2500f }; // 15, 25, 50 tiles

        // Constants
        private const float MULTI_PICKUP_RADIUS = 5f;
        protected const float NEARBY_CONSTRUCT_SCAN_RADIUS = 8f;

        // Translation strings
        protected static string ForbiddenLowerTranslated;
        protected static string NoPathTranslated;

        #endregion

        #region Overrides

        /// <summary>
        /// Whether to use Hauling or Construction work tag
        /// </summary>
        protected override string WorkTag => "Construction";

        /// <summary>
        /// Unique name for debug messages
        /// </summary>
        protected abstract string JobDescription { get; }

        /// <summary>
        /// Override debug name to use JobDescription
        /// </summary>
        protected override string DebugName => JobDescription;

        /// <summary>
        /// Override cache interval - construction targets don't change as often
        /// </summary>
        protected override int CacheUpdateInterval => 250; // Update every ~4 seconds

        /// <summary>
        /// Get construction targets that need resources
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // Subclasses must implement the specific target collection logic
            List<T> targets = GetConstructionTargets(map);
            return targets.Cast<Thing>();
        }

        /// <summary>
        /// Common implementation for TryGiveJob using StandardTryGiveJob pattern
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            return CreateDeliveryJob<JobGiver_Common_ConstructDeliverResources_PawnControl<T>>(pawn);
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Generic helper method to create a resource delivery job that can be used by all subclasses
        /// </summary>
        /// <typeparam name="TJobGiver">The specific JobGiver subclass type</typeparam>
        /// <param name="pawn">The pawn that will perform the delivery job</param>
        /// <returns>A job to deliver resources, or null if no valid job could be created</returns>
        protected Job CreateDeliveryJob<TJobGiver>(Pawn pawn) where TJobGiver : JobGiver_Common_ConstructDeliverResources_PawnControl<T>
        {
            // Use the StandardTryGiveJob pattern that works with all resource delivery jobs
            return Utility_JobGiverManager.StandardTryGiveJob<TJobGiver>(
                pawn,
                WorkTag,
                (p, forced) => {
                    if (p?.Map == null) return null;

                    // Get all valid targets from cache
                    List<T> targets = GetConstructionTargets(p.Map);
                    if (targets == null || targets.Count == 0) return null;

                    // Use JobGiverManager for distance bucketing
                    var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                        p,
                        targets,
                        (target) => (target.Position - p.Position).LengthHorizontalSquared,
                        DISTANCE_THRESHOLDS
                    );

                    // Process bucket by bucket
                    for (int b = 0; b < buckets.Length; b++)
                    {
                        if (buckets[b].Count == 0)
                            continue;

                        // Randomize within each bucket for even distribution
                        buckets[b].Shuffle();

                        // Check each target in this bucket
                        foreach (T target in buckets[b])
                        {
                            // Process target based on its type
                            if (target is Blueprint blueprint)
                            {
                                // Skip blueprints from different factions
                                if (blueprint.Faction != p.Faction)
                                    continue;

                                // Check for blocking things first
                                Thing blocker = GenConstruct.FirstBlockingThing(blueprint, p);
                                if (blocker != null)
                                {
                                    Job blockingJob = GenConstruct.HandleBlockingThingJob(blueprint, p, false);
                                    if (blockingJob != null)
                                        return blockingJob;

                                    continue;
                                }

                                // Skip if pawn can't construct this
                                if (!GenConstruct.CanConstruct(blueprint, p, false, jobForReservation: JobDefOf.HaulToContainer))
                                    continue;

                                // Check if we need to remove an existing floor first
                                if (ShouldRemoveExistingFloorFirst(p, blueprint))
                                {
                                    Job removeFloorJob = RemoveExistingFloorJob(p, blueprint);
                                    if (removeFloorJob != null)
                                        return removeFloorJob;

                                    continue;
                                }

                                // Try to deliver resources
                                Job resourceJob = ResourceDeliverJobFor(p, target);
                                if (resourceJob != null)
                                    return resourceJob;

                                // For blueprints with no material cost, create a frame directly
                                if (blueprint.TotalMaterialCost().Count == 0)
                                {
                                    Job frameJob = JobMaker.MakeJob(JobDefOf.PlaceNoCostFrame, blueprint);
                                    Utility_DebugManager.LogNormal($"{p.LabelShort} created job to place no-cost frame for {blueprint.Label}");
                                    return frameJob;
                                }
                            }
                            else if (target is Frame frame)
                            {
                                // Skip frames from different factions
                                if (frame.Faction != p.Faction)
                                    continue;

                                // Skip if thing is forbidden or unreachable
                                if (frame.IsForbidden(p) || !p.CanReach(frame, PathEndMode.Touch, p.NormalMaxDanger()))
                                    continue;

                                // Skip if we can't reserve
                                if (!p.CanReserve(frame))
                                    continue;

                                // Check if there are resources to deliver
                                Job resourceJob = ResourceDeliverJobFor(p, target);
                                if (resourceJob != null)
                                    return resourceJob;
                            }
                            else if (target is Blueprint_Install blueprintInstall)
                            {
                                // Handle Blueprint_Install specific logic (used in replanting)
                                if (typeof(T) == typeof(Blueprint_Install))
                                {
                                    // Special case for replanting - must be handled by subclass
                                    continue;
                                }
                            }
                        }
                    }

                    return null;
                },
                debugJobDesc: JobDescription);
        }

        public static void ResetStaticData()
        {
            ForbiddenLowerTranslated = "ForbiddenLower".Translate();
            NoPathTranslated = "NoPath".Translate();
        }

        /// <summary>
        /// Gets construction targets that need resources
        /// </summary>
        protected abstract List<T> GetConstructionTargets(Map map);

        /// <summary>
        /// Creates a resource delivery job for a construction site
        /// </summary>
        protected Job ResourceDeliverJobFor(Pawn pawn, T target)
        {
            _missingResources.Clear();

            // Check each material needed
            foreach (ThingDefCountClass need in target.TotalMaterialCost())
            {
                // Use ThingCountNeeded from target
                int neededCount = target.ThingCountNeeded(need.thingDef);
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

                    // Find nearby construction sites that need the same material
                    int totalNeeded;
                    Job jobToMakeNeederAvailable = null;
                    HashSet<Thing> nearbyNeeders = FindNearbyNeeders(
                        pawn,
                        need.thingDef,
                        target,
                        neededCount,
                        totalAvailable,
                        out totalNeeded,
                        out jobToMakeNeederAvailable
                    );

                    // If we need to prepare another site first, do that
                    if (jobToMakeNeederAvailable != null)
                        return jobToMakeNeederAvailable;

                    // Add the current target to the list
                    nearbyNeeders.Add(target);

                    // Find the closest target to haul to first
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
                        primaryTarget = target;
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
                    job.targetC = target;

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
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to deliver {resourcesNeeded}x {need.thingDef.label} to {target.Label}");
                    return job;
                }
            }

            return null;
        }

        /// <summary>
        /// Determines if a resource is valid for hauling
        /// </summary>
        protected bool ResourceValidator(Pawn pawn, ThingDefCountClass need, Thing t)
        {
            return t.def == need.thingDef && !t.IsForbidden(pawn) && pawn.CanReserve(t);
        }

        /// <summary>
        /// Finds all available resources of the same type near a resource
        /// </summary>
        protected void FindAvailableNearbyResources(Thing firstFoundResource, Pawn pawn, out int resTotalAvailable)
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
        /// Finds nearby construction sites that need the same resources
        /// </summary>
        protected abstract HashSet<Thing> FindNearbyNeeders(
            Pawn pawn,
            ThingDef stuff,
            T originalTarget,
            int resNeeded,
            int resTotalAvailable,
            out int neededTotal,
            out Job jobToMakeNeederAvailable);

        /// <summary>
        /// Determines if a thing is a valid nearby construction site needing resources
        /// </summary>
        protected abstract bool IsNewValidNearbyNeeder(Thing t, HashSet<Thing> nearbyNeeders, T originalTarget, Pawn pawn);

        #endregion

        #region Utility

        public override string ToString()
        {
            return $"JobGiver_ConstructDeliverResources_PawnControl<{typeof(T).Name}>";
        }

        #endregion

        #region Blueprint-specific helpers

        /// <summary>
        /// Determines if an existing floor needs to be removed before building
        /// </summary>
        protected bool ShouldRemoveExistingFloorFirst(Pawn pawn, Blueprint blueprint)
        {
            return blueprint.def.entityDefToBuild is TerrainDef &&
                   pawn.Map.terrainGrid.CanRemoveTopLayerAt(blueprint.Position);
        }

        /// <summary>
        /// Creates a job to remove an existing floor
        /// </summary>
        protected Job RemoveExistingFloorJob(Pawn pawn, Blueprint blueprint)
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

        #endregion
    }
}