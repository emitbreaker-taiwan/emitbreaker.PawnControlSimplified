using RimWorld;
using System.Collections.Generic;
using System.Linq;
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
        #region Configuration

        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.HaulToContainer;

        /// <summary>
        /// Use Construction work tag
        /// </summary>
        public override string WorkTag => "Construction";

        /// <summary>
        /// Unique name for debug messages
        /// </summary>
        protected override string JobDescription => "delivering resources to blueprints (construction) assignment";

        /// <summary>
        /// Human-readable name for debug logging
        /// </summary>
        protected override string DebugName => JobDescription;

        /// <summary>
        /// Cache update interval - blueprints change more frequently
        /// </summary>
        protected override int CacheUpdateInterval => 200; // ~3.3 seconds

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_Construction_ConstructDeliverResourcesToBlueprints_PawnControl() : base()
        {
            // Base constructor already initializes the cache system
        }

        #endregion

        #region Core Flow

        /// <summary>
        /// Construction workers should prioritize this even higher than haulers
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            return 5.9f;
        }

        /// <summary>
        /// Check if map meets requirements for blueprint resource delivery
        /// </summary>
        protected override bool AreMapRequirementsMet(Pawn pawn)
        {
            // Check if map has any blueprints that need resources
            if (pawn?.Map == null)
                return false;

            // Quick check for blueprint count
            return pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).Any(b =>
                b is Blueprint blueprint &&
                !(blueprint is Blueprint_Install) &&
                blueprint.Spawned &&
                blueprint.TotalMaterialCost().Count > 0);
        }

        #endregion

        #region Target Selection

        /// <summary>
        /// Job-specific cache update method for blueprint resource delivery
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            // Get all blueprints that need resources
            return GetConstructionTargets(map).Cast<Thing>();
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
            return LimitListSize(result, 200);
        }

        #endregion

        #region Job Creation

        /// <summary>
        /// Process the cached targets to create a job for the pawn
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            if (pawn == null || targets == null || targets.Count == 0)
                return null;

            // First check faction validation
            if (!IsValidFactionForConstruction(pawn))
                return null;

            // Convert generic Things to Blueprints and filter for valid faction match
            List<Blueprint> blueprints = targets
                .OfType<Blueprint>()
                .Where(b => b.Spawned && !b.IsForbidden(pawn) && IsValidTargetFaction(b, pawn))
                .ToList();

            if (blueprints.Count == 0)
                return null;

            // Try to create a resource delivery job for each blueprint
            foreach (Blueprint blueprint in blueprints)
            {
                Job job = ResourceDeliverJobFor(pawn, blueprint);
                if (job != null)
                    return job;
            }

            return null;
        }

        #endregion

        #region Resource Delivery Helpers

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

        #region Reset

        /// <summary>
        /// Reset the cache - implements IResettableCache
        /// </summary>
        public override void Reset()
        {
            // Use centralized cache reset from parent
            base.Reset();
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return "JobGiver_Construction_ConstructDeliverResourcesToBlueprints_PawnControl";
        }

        #endregion
    }
}