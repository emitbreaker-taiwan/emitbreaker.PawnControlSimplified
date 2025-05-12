using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns hauling tasks specifically for delivering resources to construction blueprints.
    /// Uses the Hauling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Hauling_ConstructDeliverResourcesToBlueprints_PawnControl : JobGiver_Common_ConstructDeliverResources_PawnControl<Blueprint>
    {
        #region Overrides

        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.HaulToContainer;

        /// <summary>
        /// Use Hauling work tag
        /// </summary>
        protected override string WorkTag => "Hauling";

        /// <summary>
        /// Unique name for debug messages
        /// </summary>
        protected override string JobDescription => "delivering resources to blueprints (hauling) assignment";

        /// <summary>
        /// Whether this construction job requires specific tag for non-humanlike pawns
        /// </summary>
        protected override PawnEnumTags RequiredTag => PawnEnumTags.AllowWork_Hauling;

        /// <summary>
        /// Blueprint delivery is slightly more important than frame delivery
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            return 5.7f;
        }

        /// <summary>
        /// Gets blueprints that need resources
        /// </summary>
        protected override List<Blueprint> GetConstructionTargets(Map map)
        {
            if (map == null) return new List<Blueprint>();

            var result = new List<Blueprint>();

            // Find all blueprints needing materials - except plants and installations
            // Don't filter by faction here - let the job creation process handle that
            foreach (Blueprint blueprint in map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint))
            {
                if (blueprint != null &&
                    blueprint.Spawned &&
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
        /// Processes cached targets to create a job for the pawn.
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            if (pawn == null || targets == null || targets.Count == 0)
                return null;

            // First check faction validation
            if (!IsValidFactionForConstruction(pawn))
                return null;

            // Convert to blueprints and filter for valid faction match
            List<Blueprint> blueprints = targets
                .OfType<Blueprint>()
                .Where(b => b.Spawned && !b.IsForbidden(pawn) && IsValidTargetFaction(b, pawn))
                .ToList();

            if (blueprints.Count == 0)
                return null;

            // Try to create a job for each valid blueprint
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
        /// Ensures proper faction matching
        /// </summary>
        protected override bool IsNewValidNearbyNeeder(Thing t, HashSet<Thing> nearbyNeeders, Blueprint originalTarget, Pawn pawn)
        {
            return t is Blueprint blueprint &&
                   blueprint != originalTarget &&
                   blueprint.Faction == pawn.Faction &&  // Must match pawn's faction
                   !(blueprint is Blueprint_Install) &&
                   !nearbyNeeders.Contains(blueprint) &&
                   !blueprint.IsForbidden(pawn) &&
                   GenConstruct.CanConstruct(blueprint, pawn, false, jobForReservation: WorkJobDef);
        }

        #endregion
    }
}