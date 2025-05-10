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
    /// Common abstract base class for modules that deliver resources to construction projects.
    /// Can be used by both Construction and Hauling job modules.
    /// </summary>
    public abstract class JobModule_Common_DeliverResources : JobModuleCore
    {
        // Cache for constructibles needing resources
        protected static readonly Dictionary<int, List<Thing>> _constructibleCache = new Dictionary<int, List<Thing>>();
        protected static readonly Dictionary<int, Dictionary<Thing, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Thing, bool>>();
        protected static int _lastCacheUpdateTick = -999;
        protected const int CACHE_UPDATE_INTERVAL = 180; // Update every 3 seconds

        // Distance thresholds for bucketing
        protected static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        // Common constants
        protected const float MULTI_PICKUP_RADIUS = 5f;
        protected const float NEARBY_CONSTRUCT_SCAN_RADIUS = 8f;

        // Translation strings
        protected static string ForbiddenLowerTranslated;
        protected static string NoPathTranslated;
        protected static string MissingMaterialsTranslated;

        // Configuration to be overridden by subclasses
        protected virtual bool RequiresConstructionSkill => true;
        protected virtual bool AllowHaulingWorkType => false;
        protected virtual bool OnlyFrames => false;

        /// <summary>
        /// Initialize or reset translation strings and caches
        /// </summary>
        public override void ResetStaticData()
        {
            // Reset translation strings
            ForbiddenLowerTranslated = "ForbiddenLower".Translate();
            NoPathTranslated = "NoPath".Translate();
            MissingMaterialsTranslated = "MissingMaterials".Translate();

            // Clear caches
            _constructibleCache.Clear();
            _reachabilityCache.Clear();
            _lastCacheUpdateTick = -999;
        }

        /// <summary>
        /// Check if this constructible needs resources delivered
        /// </summary>
        public bool ShouldProcessTarget(Thing constructible, Map map)
        {
            if (constructible == null || !constructible.Spawned || map == null) return false;

            // Skip if not a constructible target
            if (!(constructible is IConstructible c)) return false;

            // Filter by frames only if needed
            if (OnlyFrames && !(constructible is Frame)) return false;

            // Skip if not accessible
            if (!map.reachability.CanReachMapEdge(constructible.Position, TraverseParms.For(TraverseMode.PassDoors, Danger.Deadly)))
                return false;

            // Check if this specific target needs resources delivered
            if (constructible is Blueprint_Install)
            {
                // Special case for installations - check if the thing to install is available
                return true;
            }

            // For normal buildables, check if they need materials
            foreach (ThingDefCountClass need in c.TotalMaterialCost())
            {
                int neededCount = c.ThingCountNeeded(need.thingDef);
                if (neededCount > 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Check if the actor can deliver resources to this construction project
        /// </summary>
        public bool ValidateDeliveryJob(Thing target, Pawn actor)
        {
            if (target == null || actor == null || !target.Spawned || !actor.Spawned)
                return false;

            // Skip if wrong work type (construction skill check)
            if (RequiresConstructionSkill && !AllowHaulingWorkType)
            {
                if (actor.skills?.GetSkill(SkillDefOf.Construction)?.Level < 1)
                    return false;
            }

            // Check basic accessibility
            if (target.IsForbidden(actor) ||
                !actor.CanReserve(target) ||
                !actor.CanReach(target, PathEndMode.Touch, actor.NormalMaxDanger()))
                return false;

            // Special handling for installations
            if (target is Blueprint_Install blueprint_Install)
            {
                return ValidateInstallationJob(blueprint_Install, actor);
            }

            // For normal buildables, check if resources are available to deliver
            if (!(target is IConstructible c)) return false;

            // Check each required resource
            foreach (ThingDefCountClass need in c.TotalMaterialCost())
            {
                int neededCount = c.ThingCountNeeded(need.thingDef);
                if (neededCount <= 0) continue;

                // Check if resources are available on the map
                if (!actor.Map.itemAvailability.ThingsAvailableAnywhere(need.thingDef, neededCount, actor))
                    continue;

                // Check if there's a reachable resource of this type
                Thing foundRes = FindResource(actor, need);
                if (foundRes != null) return true;
            }

            return false;
        }

        /// <summary>
        /// Create a job to deliver resources to this construction project
        /// </summary>
        public Job CreateDeliveryJob(Pawn actor, Thing target)
        {
            // Handle Blueprint_Install as a special case
            if (target is Blueprint_Install install)
            {
                return CreateInstallJob(actor, install);
            }

            // Handle regular construction projects
            if (!(target is IConstructible c)) return null;

            Dictionary<ThingDef, int> missingResources = new Dictionary<ThingDef, int>();
            List<Thing> resourcesAvailable = new List<Thing>();

            // Check resource needs
            foreach (ThingDefCountClass need in c.TotalMaterialCost())
            {
                int neededCount = c.ThingCountNeeded(need.thingDef);
                if (neededCount <= 0) continue;

                // Check resource availability on the map
                if (!actor.Map.itemAvailability.ThingsAvailableAnywhere(need.thingDef, neededCount, actor))
                {
                    missingResources.Add(need.thingDef, neededCount);
                    continue;
                }

                // Find the resource
                Thing foundRes;
                if (CanUseCarriedResource(actor, c, need))
                {
                    foundRes = actor.carryTracker.CarriedThing;
                }
                else
                {
                    foundRes = GenClosest.ClosestThingReachable(
                        actor.Position,
                        actor.Map,
                        ThingRequest.ForDef(need.thingDef),
                        PathEndMode.ClosestTouch,
                        TraverseParms.For(actor),
                        9999f,
                        thing => ResourceValidator(actor, need, thing)
                    );
                }

                if (foundRes == null)
                {
                    missingResources.Add(need.thingDef, neededCount);
                    continue;
                }

                // Find nearby resources of the same type
                resourcesAvailable.Clear();
                resourcesAvailable.Add(foundRes);
                int resTotalAvailable = foundRes.stackCount;

                // If we haven't reached the max stack yet, look for more nearby
                int maxStack = actor.carryTracker.MaxStackSpaceEver(foundRes.def);
                if (resTotalAvailable < maxStack)
                {
                    resTotalAvailable = FindAvailableNearbyResources(
                        foundRes, actor, resourcesAvailable, maxStack, resTotalAvailable);
                }

                // Cap at max stack
                resTotalAvailable = Mathf.Min(resTotalAvailable, maxStack);

                // Find nearby construction projects that need the same resources
                HashSet<Thing> nearbyNeeders = FindNearbyNeeders(
                    actor, need.thingDef, c, neededCount, resTotalAvailable,
                    out int neededTotal, out Job jobToMakeNeederAvailable
                );

                // If there's a prep job needed (like removing floor), do that first
                if (jobToMakeNeederAvailable != null)
                {
                    return jobToMakeNeederAvailable;
                }

                // Add the original target to the list
                nearbyNeeders.Add((Thing)c);

                // Create the job for resource delivery
                return CreateResourceDeliveryJob(actor, foundRes, (Thing)c, nearbyNeeders,
                    resourcesAvailable, resTotalAvailable, neededTotal);
            }

            // No resources found or needed
            if (missingResources.Count > 0)
            {
                JobFailReason.Is(MissingMaterialsTranslated + missingResources
                    .Select(kvp => $"{kvp.Value}x {kvp.Key.label}")
                    .ToCommaList());
            }

            return null;
        }

        /// <summary>
        /// Updates the cache of constructible things that need resources
        /// </summary>
        protected void UpdateConstructibleCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_constructibleCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_constructibleCache.ContainsKey(mapId))
                    _constructibleCache[mapId].Clear();
                else
                    _constructibleCache[mapId] = new List<Thing>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Thing, bool>();

                HashSet<ThingRequestGroup> requestGroups = OnlyFrames ?
                    new HashSet<ThingRequestGroup> { ThingRequestGroup.BuildingFrame } :
                    new HashSet<ThingRequestGroup> { ThingRequestGroup.BuildingFrame, ThingRequestGroup.Blueprint };

                // Find all constructible things that need resources
                foreach (ThingRequestGroup group in requestGroups)
                {
                    foreach (Thing thing in map.listerThings.ThingsInGroup(group))
                    {
                        if (ShouldProcessTarget(thing, map))
                        {
                            _constructibleCache[mapId].Add(thing);
                        }
                    }
                }

                _lastCacheUpdateTick = currentTick;

                // Record whether we found any targets
                SetHasTargets(map, _constructibleCache[mapId].Count > 0);
            }
        }

        /// <summary>
        /// Gets the cached list of constructible things from the given map
        /// </summary>
        protected List<Thing> GetConstructibles(Map map)
        {
            if (map == null)
                return new List<Thing>();

            int mapId = map.uniqueID;

            if (_constructibleCache.TryGetValue(mapId, out var cachedTargets))
                return cachedTargets;

            return new List<Thing>();
        }

        /// <summary>
        /// Find a valid constructible to deliver resources to from the cache
        /// </summary>
        protected Thing FindValidConstructibleTarget(Pawn worker, Map map)
        {
            if (worker?.Map == null) return null;

            // Update the cache first
            UpdateConstructibleCache(map);

            int mapId = map.uniqueID;
            if (!_constructibleCache.ContainsKey(mapId) || _constructibleCache[mapId].Count == 0)
                return null;

            // Use distance bucketing for efficient selection
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                worker,
                _constructibleCache[mapId],
                (thing) => (thing.Position - worker.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Find the best constructible to deliver to
            return Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                worker,
                (thing, pawn) => ValidateDeliveryJob(thing, pawn),
                _reachabilityCache
            );
        }

        #region Private Helper Methods

        /// <summary>
        /// Validate an installation job
        /// </summary>
        private bool ValidateInstallationJob(Blueprint_Install blueprint, Pawn actor)
        {
            Thing miniToInstallOrBuildingToReinstall = blueprint.MiniToInstallOrBuildingToReinstall;

            // Check if the thing to install exists and is available
            if (miniToInstallOrBuildingToReinstall == null) return false;

            // Check if it's being carried by another pawn
            IThingHolder parentHolder = miniToInstallOrBuildingToReinstall.ParentHolder;
            if (parentHolder is Pawn_CarryTracker) return false;

            // Check if it's accessible
            if (miniToInstallOrBuildingToReinstall.IsForbidden(actor) ||
                !actor.CanReach(miniToInstallOrBuildingToReinstall, PathEndMode.ClosestTouch, actor.NormalMaxDanger()) ||
                !actor.CanReserve(miniToInstallOrBuildingToReinstall))
                return false;

            return true;
        }

        /// <summary>
        /// Create a job to deliver the thing to be installed
        /// </summary>
        private Job CreateInstallJob(Pawn pawn, Blueprint_Install install)
        {
            Thing miniToInstallOrBuildingToReinstall = install.MiniToInstallOrBuildingToReinstall;
            if (miniToInstallOrBuildingToReinstall == null) return null;

            // Create the job to haul the thing to the installation blueprint
            Job job = JobMaker.MakeJob(JobDefOf.HaulToContainer);
            job.targetA = miniToInstallOrBuildingToReinstall;
            job.targetB = install;
            job.count = 1;
            job.haulMode = HaulMode.ToContainer;

            Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to install {miniToInstallOrBuildingToReinstall.LabelCap}");
            return job;
        }

        /// <summary>
        /// Create the actual resource delivery job
        /// </summary>
        private Job CreateResourceDeliveryJob(Pawn actor, Thing primaryResource, Thing primaryTarget,
            HashSet<Thing> allTargets, List<Thing> allResources, int totalResources, int totalNeeded)
        {
            // Find the closest target as the primary target
            Thing primaryNeeder;
            if (allTargets.Count > 1) // It contains the original target too
            {
                primaryNeeder = allTargets.MinBy(t => IntVec3Utility.ManhattanDistanceFlat(primaryResource.Position, t.Position));
                allTargets.Remove(primaryNeeder);
            }
            else
            {
                primaryNeeder = primaryTarget;
                allTargets.Clear(); // Clear so we don't try to remove it again
            }

            // Remove the primary resource from additional resources list
            List<Thing> additionalResources = new List<Thing>(allResources);
            additionalResources.Remove(primaryResource);

            // Determine how much to haul
            int totalToHaul = Math.Min(totalResources, totalNeeded);

            // Create the haul job
            Job job = JobMaker.MakeJob(JobDefOf.HaulToContainer);
            job.targetA = primaryResource;
            job.targetC = primaryTarget;
            job.targetB = primaryNeeder;

            // Add additional resources to pick up
            if (additionalResources.Count > 0)
            {
                job.targetQueueA = new List<LocalTargetInfo>();
                foreach (Thing res in additionalResources)
                {
                    job.targetQueueA.Add(res);
                }
            }

            // Add additional delivery targets
            if (allTargets.Count > 0)
            {
                job.targetQueueB = new List<LocalTargetInfo>();
                foreach (Thing needer in allTargets)
                {
                    job.targetQueueB.Add(needer);
                }
            }

            job.count = totalToHaul;
            job.haulMode = HaulMode.ToContainer;

            Utility_DebugManager.LogNormal($"{actor.LabelShort} created job to deliver {totalToHaul}x {primaryResource.def.label} to {primaryNeeder.LabelCap}");
            return job;
        }

        /// <summary>
        /// Find a resource for a specific construction need
        /// </summary>
        private Thing FindResource(Pawn pawn, ThingDefCountClass need)
        {
            // First check if the pawn is carrying the needed resource
            if (pawn.carryTracker.CarriedThing?.def == need.thingDef)
                return pawn.carryTracker.CarriedThing;

            // Otherwise look for resources on the map
            return GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForDef(need.thingDef),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn),
                9999f,
                thing => ResourceValidator(pawn, need, thing)
            );
        }

        /// <summary>
        /// Check if a specific resource is valid for use
        /// </summary>
        private bool ResourceValidator(Pawn pawn, ThingDefCountClass need, Thing thing)
        {
            if (thing.def != need.thingDef) return false;
            if (thing.IsForbidden(pawn)) return false;
            if (!pawn.CanReserve(thing)) return false;
            return true;
        }

        /// <summary>
        /// Check if pawn's currently carried resource can be used for this job
        /// </summary>
        private bool CanUseCarriedResource(Pawn pawn, IConstructible c, ThingDefCountClass need)
        {
            // Must be carrying the right resource
            if (pawn.carryTracker.CarriedThing?.def != need.thingDef)
                return false;

            // Check if the pawn is already reserving the resource for another job
            if (!KeyBindingDefOf.QueueOrder.IsDownEvent)
                return true;

            // Check if current job is already a hauling job
            if (pawn.CurJob != null && !IsValidJob(pawn.CurJob))
                return false;

            // Check queued jobs
            foreach (QueuedJob item in pawn.jobs.jobQueue)
            {
                if (!IsValidJob(item.job))
                    return false;
            }

            return true;

            // Local function to check if a job is compatible
            bool IsValidJob(Job job)
            {
                if (job.def != JobDefOf.HaulToContainer)
                    return true;

                return job.targetA.Thing != pawn.carryTracker.CarriedThing;
            }
        }

        /// <summary>
        /// Find available resources of the same type near the first found resource
        /// </summary>
        private int FindAvailableNearbyResources(Thing firstFoundResource, Pawn pawn,
            List<Thing> resourcesList, int maxStack, int currentTotal)
        {
            int resTotalAvailable = currentTotal;

            foreach (Thing item in GenRadial.RadialDistinctThingsAround(
                firstFoundResource.PositionHeld,
                firstFoundResource.MapHeld,
                MULTI_PICKUP_RADIUS,
                useCenter: false))
            {
                // Stop if we've reached the max stack
                if (resTotalAvailable >= maxStack)
                {
                    return maxStack;
                }

                // Add if it's the same type and usable
                if (item.def == firstFoundResource.def && GenAI.CanUseItemForWork(pawn, item))
                {
                    resourcesList.Add(item);
                    resTotalAvailable += item.stackCount;
                }
            }

            // Cap at max stack
            return Math.Min(resTotalAvailable, maxStack);
        }

        /// <summary>
        /// Find nearby construction projects that need the same resources
        /// </summary>
        private HashSet<Thing> FindNearbyNeeders(Pawn pawn, ThingDef stuff, IConstructible c,
            int resNeeded, int resTotalAvailable, out int neededTotal, out Job jobToMakeNeederAvailable)
        {
            neededTotal = resNeeded;
            HashSet<Thing> result = new HashSet<Thing>();
            Thing thing = (Thing)c;

            // Look for nearby things that also need this resource
            foreach (Thing item in GenRadial.RadialDistinctThingsAround(
                thing.Position, thing.Map, NEARBY_CONSTRUCT_SCAN_RADIUS, useCenter: true))
            {
                // Stop if we've reached the resource limit
                if (neededTotal >= resTotalAvailable)
                    break;

                if (IsValidNearbyNeeder(item, result, c, pawn, stuff))
                {
                    // Calculate how much this needer needs
                    int amountNeeded = 0;
                    if (item is IHaulEnroute enroute)
                    {
                        amountNeeded = enroute.GetSpaceRemainingWithEnroute(stuff, pawn);
                    }
                    else if (item is IConstructible constructible)
                    {
                        amountNeeded = constructible.ThingCountNeeded(stuff);
                    }

                    if (amountNeeded > 0)
                    {
                        result.Add(item);
                        neededTotal += amountNeeded;
                    }
                }
            }

            // Check if a floor removal job is needed
            jobToMakeNeederAvailable = CheckForFloorRemovalJob(pawn, c, thing, result);

            return result;
        }

        /// <summary>
        /// Check if a nearby constructible needs floor removed first
        /// </summary>
        private Job CheckForFloorRemovalJob(Pawn pawn, IConstructible c, Thing thing, HashSet<Thing> currentNeeders)
        {
            // Only applicable for blueprints of terrain
            if (!(c is Blueprint blueprint) ||
                !(blueprint.def.entityDefToBuild is TerrainDef))
                return null;

            // Look for nearby things that need floor removal
            foreach (Thing item in GenRadial.RadialDistinctThingsAround(
                thing.Position, thing.Map, 3f, useCenter: false))
            {
                if (IsValidNearbyNeeder(item, currentNeeders, c, pawn, null) && item is Blueprint blue)
                {
                    if (ShouldRemoveExistingFloorFirst(pawn, blue))
                    {
                        return RemoveExistingFloorJob(pawn, blue);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Check if a thing is valid as a nearby resource needer
        /// </summary>
        private bool IsValidNearbyNeeder(Thing t, HashSet<Thing> nearbyNeeders, IConstructible constructible,
            Pawn pawn, ThingDef stuff = null)
        {
            // Must be a constructible thing that isn't the original target
            if (!(t is IConstructible) || t == constructible) return false;

            // Must belong to the pawn's faction
            if (t.Faction != pawn.Faction) return false;

            // Must not be an installation
            if (t is Blueprint_Install) return false;

            // Must not already be in the list
            if (nearbyNeeders.Contains(t)) return false;

            // Must not be forbidden
            if (t.IsForbidden(pawn)) return false;

            // If a specific stuff type is provided, verify the constructible needs it
            if (stuff != null)
            {
                bool needsStuff = false;
                IConstructible ic = t as IConstructible;

                foreach (ThingDefCountClass need in ic.TotalMaterialCost())
                {
                    if (need.thingDef == stuff && ic.ThingCountNeeded(stuff) > 0)
                    {
                        needsStuff = true;
                        break;
                    }
                }

                if (!needsStuff) return false;
            }

            // Must be constructible by this pawn
            bool checkSkills = RequiresConstructionSkill && !AllowHaulingWorkType;
            return GenConstruct.CanConstruct(t, pawn, checkSkills, forced: false, JobDefOf.HaulToContainer);
        }

        /// <summary>
        /// Check if floor needs to be removed before building
        /// </summary>
        private bool ShouldRemoveExistingFloorFirst(Pawn pawn, Blueprint blue)
        {
            if (blue.def.entityDefToBuild is TerrainDef)
            {
                return pawn.Map.terrainGrid.CanRemoveTopLayerAt(blue.Position);
            }
            return false;
        }

        /// <summary>
        /// Create a job to remove existing floor before building
        /// </summary>
        private Job RemoveExistingFloorJob(Pawn pawn, Blueprint blue)
        {
            if (!ShouldRemoveExistingFloorFirst(pawn, blue))
                return null;

            // Must be able to reserve the floor position
            if (!pawn.CanReserve(blue.Position, 1, -1, ReservationLayerDefOf.Floor))
                return null;

            // Check if pawn can do floor removal work
            if (pawn.WorkTypeIsDisabled(WorkGiverDefOf.ConstructRemoveFloors.workType))
                return null;

            // Create the job to remove floor
            Job job = JobMaker.MakeJob(JobDefOf.RemoveFloor, blue.Position);
            job.ignoreDesignations = true;
            return job;
        }

        #endregion
    }
}