using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Abstract base implementation for delivering resources to blueprints.
    /// </summary>
    public abstract class JobGiver_Common_ConstructDeliverResourcesToBlueprints_PawnControl : JobGiver_Common_ConstructDeliverResources_PawnControl<Blueprint>
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
                    _targetCache[mapId] = new List<Blueprint>();

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
                        _targetCache[mapId].Add(blueprint);
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
                    // Skip frames from different factions
                    if (blueprint.Faction != pawn.Faction)
                        continue;

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
                        Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to place no-cost frame for {blueprint.Label}");
                        return frameJob;
                    }
                }
            }

            return null;
        }

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

        protected override HashSet<Thing> FindNearbyNeeders(
            Pawn pawn,
            ThingDef stuff,
            Blueprint originalTarget,
            int resNeeded,
            int resTotalAvailable,
            out int neededTotal,
            out Job jobToMakeNeederAvailable)
        {
            neededTotal = resNeeded;
            jobToMakeNeederAvailable = null;
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
            if (originalTarget.def.entityDefToBuild is TerrainDef && neededTotal < resTotalAvailable)
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

            return nearbyNeeders;
        }

        protected override bool IsNewValidNearbyNeeder(Thing t, HashSet<Thing> nearbyNeeders, Blueprint originalTarget, Pawn pawn)
        {
            return t is Blueprint &&
                   t != originalTarget &&
                   t.Faction == pawn.Faction &&
                   !(t is Blueprint_Install) &&
                   !nearbyNeeders.Contains(t) &&
                   !t.IsForbidden(pawn) &&
                   GenConstruct.CanConstruct(t, pawn, false, jobForReservation: JobDefOf.HaulToContainer);
        }
    }
}