using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to haul items to map portals.
    /// Uses the Hauling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Hauling_HaulToPortal_PawnControl : JobGiver_Hauling_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        public override bool RequiresMapZoneorArea => false;

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "HaulToPortal";

        /// <summary>
        /// Update cache every 2 seconds
        /// </summary>
        protected override int CacheUpdateInterval => base.CacheUpdateInterval;

        /// <summary>
        /// Distance thresholds for bucketing (10, 20, 30 tiles)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 100f, 400f, 900f };

        /// <summary>
        /// The job to create when a valid target is found
        /// For portals, this is null as we use a special job creation method
        /// </summary>
        protected override JobDef WorkJobDef => null;

        #endregion

        #region Core flow

        protected override float GetBasePriority(string workTag)
        {
            // Loading portals is important
            return 5.9f;
        }

        /// <summary>
        /// Gets all portals on the map as targets for hauling jobs
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null)
                yield break;

            // Find all portals on the map
            foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.MapPortal))
            {
                if (thing is MapPortal portal && portal.Spawned && !portal.Destroyed)
                {
                    yield return portal;
                }
            }
        }

        /// <summary>
        /// Process cached targets (portals) to find a valid job
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            if (pawn == null || targets == null || targets.Count == 0)
                return null;

            // Filter the list to only include MapPortal objects
            List<MapPortal> portals = new List<MapPortal>();
            foreach (Thing thing in targets)
            {
                if (thing is MapPortal portal)
                {
                    portals.Add(portal);
                }
            }

            if (portals.Count == 0)
                return null;

            // Create distance buckets for portals
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                portals.Cast<Thing>().ToList(),
                (thing) => (thing.Position - pawn.Position).LengthHorizontalSquared,
                DistanceThresholds);

            // Find the best portal to load
            Thing targetThing = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (thing, worker) => IsValidPortalTarget(thing as MapPortal, worker),
                null); // Let the parent class handle reachability caching

            // Create job if target found
            if (targetThing != null && targetThing is MapPortal targetPortal)
            {
                // Use the utility method if available
                Job job = EnterPortalUtility.JobOnPortal(pawn, targetPortal);

                if (job != null)
                {
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to haul to portal {targetPortal.LabelCap}");
                }

                return job;
            }

            return null;
        }

        #endregion

        #region Target selection

        /// <summary>
        /// Determines if a portal is valid for a pawn
        /// </summary>
        private bool IsValidPortalTarget(MapPortal portal, Pawn pawn)
        {
            // Skip if portal is null or destroyed
            if (portal == null || portal.Destroyed || !portal.Spawned)
                return false;

            // Use the portal utility to check if the pawn can use this portal
            return EnterPortalUtility.HasJobOnPortal(pawn, portal);
        }

        #endregion
    }
}