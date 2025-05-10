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
    public abstract class JobGiver_Common_ConstructDeliverResources_PawnControl<T> : ThinkNode_JobGiver where T : Thing, IConstructible
    {
        // Cache for construction objects needing materials
        protected static readonly Dictionary<int, List<T>> _targetCache = new Dictionary<int, List<T>>();
        protected static readonly Dictionary<int, Dictionary<T, bool>> _reachabilityCache = new Dictionary<int, Dictionary<T, bool>>();
        protected static int _lastCacheUpdateTick = -999;
        protected const int CACHE_UPDATE_INTERVAL = 250; // Update every ~4 seconds

        // Cache for resource scanning
        protected static readonly List<Thing> _resourcesAvailable = new List<Thing>();
        protected static readonly Dictionary<ThingDef, int> _missingResources = new Dictionary<ThingDef, int>();

        // Distance thresholds for bucketing
        protected static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 2500f }; // 15, 25, 50 tiles

        // Constants
        protected const float MULTI_PICKUP_RADIUS = 5f;
        protected const float NEARBY_CONSTRUCT_SCAN_RADIUS = 8f;

        // Translation strings
        protected static string ForbiddenLowerTranslated;
        protected static string NoPathTranslated;

        /// <summary>
        /// Whether to use Hauling or Construction work tag
        /// </summary>
        protected abstract string WorkTypeDef { get; }

        /// <summary>
        /// Unique name for debug messages
        /// </summary>
        protected abstract string JobDescription { get; }

        public static void ResetStaticData()
        {
            ForbiddenLowerTranslated = "ForbiddenLower".Translate();
            NoPathTranslated = "NoPath".Translate();
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManagerOld.StandardTryGiveJob<Plant>(
                pawn,
                WorkTypeDef,
                (p, forced) => {
                    // Update cache
                    UpdateTargetCacheSafely(p.Map);

                    // Find and create a job for delivering resources
                    return TryCreateDeliveryJob(p);
                },
                debugJobDesc: JobDescription,
                skipEmergencyCheck: false);
        }

        /// <summary>
        /// Updates the cache of objects needing materials
        /// </summary>
        protected abstract void UpdateTargetCacheSafely(Map map);

        /// <summary>
        /// Creates a job for delivering resources to a construction site
        /// </summary>
        protected abstract Job TryCreateDeliveryJob(Pawn pawn);

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

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            _targetCache.Clear();
            _reachabilityCache.Clear();
            _lastCacheUpdateTick = -999;
            ResetStaticData();
        }

        public override string ToString()
        {
            return $"JobGiver_ConstructDeliverResources_PawnControl<{typeof(T).Name}>";
        }
    }
}