using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that allows non-humanlike pawns to fix broken down buildings.
    /// Uses the Construction work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Construction_FixBrokenDownBuilding_PawnControl : JobGiver_Scan_PawnControl
    {
        #region Overrides

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        protected override bool RequiresMapZoneorArea => false;

        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.FixBrokenDownBuilding;
        protected override string WorkTag => "Construction";
        protected override int CacheUpdateInterval => 300; // Update every 5 seconds (broken buildings don't change often)
        protected override string DebugName => "broken building repair assignment";

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 100f, 400f, 900f }; // 10, 20, 30 tiles

        // Static translation strings
        private static string NotInHomeAreaTrans;
        private static string NoComponentsToRepairTrans;

        protected override bool ShouldExecuteNow(int mapId)
        {
            // Quick check if there are broken down buildings on the map
            var map = Find.Maps.FirstOrDefault(m => m.uniqueID == mapId);
            return map != null && map.GetComponent<BreakdownManager>()?.brokenDownThings?.Count > 0;
        }

        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null) return Enumerable.Empty<Thing>();

            // Use the BreakdownManager to get all broken buildings
            var brokenDownThings = map.GetComponent<BreakdownManager>()?.brokenDownThings;
            if (brokenDownThings == null) return Enumerable.Empty<Thing>();

            // Return only valid, spawned repairable buildings
            return brokenDownThings.Where(building =>
                building != null &&
                building.Spawned &&
                building.def.building.repairable);
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Use the StandardTryGiveJob pattern directly
            return Utility_JobGiverManager.StandardTryGiveJob<Thing>(
                pawn,
                WorkTag,  // Use the WorkTag property from the base class
                (p, forced) => {
                    // Get targets from the cache
                    List<Thing> targets = GetTargets(p.Map).ToList();
                    if (targets.Count == 0) return null;

                    // Find the best building to repair
                    Building bestTarget = null;
                    float bestDistSq = float.MaxValue;
                    Thing bestComponent = null;

                    foreach (Thing thing in targets)
                    {
                        Building building = thing as Building;
                        if (building == null) continue;

                        if (!ValidateBuildingRepair(building, p, forced))
                            continue;

                        // Find component for repair
                        Thing component = FindClosestComponent(p);
                        if (component == null)
                        {
                            JobFailReason.Is(NoComponentsToRepairTrans);
                            continue;
                        }

                        float distSq = (building.Position - p.Position).LengthHorizontalSquared;
                        if (distSq < bestDistSq)
                        {
                            bestDistSq = distSq;
                            bestTarget = building;
                            bestComponent = component;
                        }
                    }

                    if (bestTarget != null && bestComponent != null)
                    {
                        // Create the job
                        Job job = JobMaker.MakeJob(WorkJobDef, bestTarget, bestComponent);
                        job.count = 1;
                        Utility_DebugManager.LogNormal($"{p.LabelShort} created job to repair broken {bestTarget.LabelCap}");
                        return job;
                    }

                    return null;
                },
                debugJobDesc: DebugName,
                skipEmergencyCheck: true);
        }

        /// <summary>
        /// Implements the abstract method from JobGiver_Scan_PawnControl to process cached targets
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            if (pawn == null || targets == null || targets.Count == 0)
                return null;

            // Use distance bucketing for efficiency
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                targets,
                (thing) => (thing.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Find the best building to repair
            // Convert the method group to the expected Func<Thing, Pawn, bool> delegate type
            Building bestTarget = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (thing, validator) => thing is Building building && ValidateBuildingRepair(building, validator, forced),
                null
            ) as Building;

            if (bestTarget != null)
            {
                // Find component for repair
                Thing component = FindClosestComponent(pawn);
                if (component != null)
                {
                    // Create the job
                    Job job = JobMaker.MakeJob(WorkJobDef, bestTarget, component);
                    job.count = 1;
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to repair broken {bestTarget.LabelCap}");
                    return job;
                }
            }

            return null;
        }

        #endregion

        #region Target validation helpers

        /// <summary>
        /// Validates if a building is appropriate for repair
        /// </summary>
        private bool ValidateBuildingRepair(Building building, Pawn pawn, bool forced)
        {
            if (building == null) return false;

            // Basic validation checks
            if (!building.Spawned || !building.IsBrokenDown() || building.IsForbidden(pawn))
                return false;

            // Check building faction - must be same as pawn's
            if (building.Faction != pawn.Faction)
                return false;

            // Check if in home area (for player faction)
            if (pawn.Faction == Faction.OfPlayer && !pawn.Map.areaManager.Home[building.Position])
            {
                JobFailReason.Is(NotInHomeAreaTrans);
                return false;
            }

            // Check existing designations
            if (pawn.Map.designationManager.DesignationOn(building, DesignationDefOf.Deconstruct) != null)
                return false;

            // Check if building is on fire
            if (building.IsBurning())
                return false;

            // Check if pawn can reserve the building
            if (!pawn.CanReserve(building, 1, -1, null, forced))
                return false;

            // Check if reachable
            if (!pawn.CanReach(building, PathEndMode.Touch, Danger.Deadly))
                return false;

            return true;
        }

        /// <summary>
        /// Find the closest component available for repairs
        /// </summary>
        private Thing FindClosestComponent(Pawn pawn)
        {
            return GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForDef(ThingDefOf.ComponentIndustrial),
                PathEndMode.InteractionCell,
                TraverseParms.For(pawn, pawn.NormalMaxDanger()),
                validator: x => !x.IsForbidden(pawn) && pawn.CanReserve(x)
            );
        }

        #endregion

        #region Utility

        public static void ResetStaticData()
        {
            NotInHomeAreaTrans = "NotInHomeArea".Translate();
            NoComponentsToRepairTrans = "NoComponentsToRepair".Translate();
        }

        public override string ToString()
        {
            return "JobGiver_FixBrokenDownBuilding_PawnControl";
        }

        #endregion
    }
}