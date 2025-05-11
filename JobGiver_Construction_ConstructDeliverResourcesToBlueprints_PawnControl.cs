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
    /// JobGiver that assigns construction tasks specifically for delivering resources to construction blueprints.
    /// Uses the Construction work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Construction_ConstructDeliverResourcesToBlueprints_PawnControl : JobGiver_Common_ConstructDeliverResources_PawnControl<Blueprint>
    {
        #region Overrides

        /// <summary>
        /// Use Construction work tag
        /// </summary>
        protected override string WorkTag => "Construction";

        /// <summary>
        /// Unique name for debug messages
        /// </summary>
        protected override string JobDescription => "delivering resources to blueprints (construction) assignment";

        /// <summary>
        /// Construction workers should prioritize this even higher than haulers
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            return 5.9f;
        }

        /// <summary>
        /// Gets blueprints that need resources
        /// </summary>
        protected override List<Blueprint> GetConstructionTargets(Map map)
        {
            if (map == null) return new List<Blueprint>();

            var result = new List<Blueprint>();

            // Find all blueprints needing materials - except plants and installations
            foreach (Blueprint blueprint in map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint))
            {
                if (blueprint != null &&
                    blueprint.Spawned &&
                    !blueprint.IsForbidden(Faction.OfPlayer) &&
                    !(blueprint is Blueprint_Install) &&
                    !(blueprint.def.entityDefToBuild is ThingDef entityDefToBuild && entityDefToBuild.plant != null))
                {
                    result.Add(blueprint);
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
        /// <summary>
        /// Use the common helper method for creating delivery jobs
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            return CreateDeliveryJob<JobGiver_Construction_ConstructDeliverResourcesToBlueprints_PawnControl>(pawn);
        }

        /// <summary>
        /// Process the cached targets to create a job for the pawn
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            if (pawn == null || targets == null || targets.Count == 0)
                return null;

            // Convert generic Things to Blueprints and filter out non-Blueprint items
            List<Blueprint> blueprints = targets
                .OfType<Blueprint>()
                .Where(b => b.Spawned && !b.IsForbidden(pawn.Faction))
                .ToList();

            if (blueprints.Count == 0)
                return null;

            // Try to find a blueprint that needs resources and create a delivery job
            foreach (Blueprint blueprint in blueprints)
            {
                Job job = ResourceDeliverJobFor(pawn, blueprint);
                if (job != null)
                    return job;
            }

            return null;
        }

        /// <summary>
        /// Finds nearby blueprints that need the same resources
        /// </summary>
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

        /// <summary>
        /// Determines if a thing is a valid nearby blueprint needing resources
        /// </summary>
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

        #endregion
    }
}