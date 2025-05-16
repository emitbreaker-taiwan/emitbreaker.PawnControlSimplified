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
    public class JobGiver_Construction_FixBrokenDownBuilding_PawnControl : JobGiver_Construction_PawnControl
    {
        #region Configuration

        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.FixBrokenDownBuilding;

        /// <summary>
        /// Human-readable name for debug logging
        /// </summary>
        protected override string DebugName => "broken building repair assignment";

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// </summary>
        public override bool RequiresMapZoneorArea => false;

        /// <summary>
        /// Whether this job requires a designator (false for repair jobs)
        /// </summary>
        public override bool RequiresDesignator => false;

        /// <summary>
        /// The designation this job giver targets - null for repair tasks
        /// </summary>
        protected override DesignationDef TargetDesignation => null;

        /// <summary>
        /// Update cache every 5 seconds (broken buildings don't change often)
        /// </summary>
        protected override int CacheUpdateInterval => base.CacheUpdateInterval;

        /// <summary>
        /// Standard distance thresholds for repair tasks
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 100f, 400f, 900f }; // 10, 20, 30 tiles

        // Static translation strings
        private static string NotInHomeAreaTrans;
        private static string NoComponentsToRepairTrans;

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_Construction_FixBrokenDownBuilding_PawnControl() : base()
        {
            // Base constructor already initializes the cache system
        }

        #endregion

        #region Core Flow

        /// <summary>
        /// Quick check if there are broken down buildings on the map
        /// </summary>
        protected override bool ShouldExecuteNow(int mapId)
        {
            // First check base conditions
            if (!base.ShouldExecuteNow(mapId))
                return false;

            // Quick check if there are broken down buildings on the map
            var map = Find.Maps.FirstOrDefault(m => m.uniqueID == mapId);
            return map != null && map.GetComponent<BreakdownManager>()?.brokenDownThings?.Count > 0;
        }

        /// <summary>
        /// Checks if the map meets requirements for repair jobs
        /// </summary>
        protected override bool AreMapRequirementsMet(Pawn pawn)
        {
            // Check if map has broken down buildings
            return pawn?.Map != null &&
                   pawn.Map.GetComponent<BreakdownManager>()?.brokenDownThings?.Count > 0;
        }

        #endregion

        #region Target Selection

        /// <summary>
        /// Job-specific cache update method that implements specialized collection of broken buildings
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            // Use the BreakdownManager to get all broken buildings
            var brokenDownThings = map?.GetComponent<BreakdownManager>()?.brokenDownThings;
            if (brokenDownThings == null)
                return Enumerable.Empty<Thing>();

            // Return only valid, spawned repairable buildings
            return brokenDownThings.Where(building =>
                building != null &&
                building.Spawned &&
                building.def.building.repairable);
        }

        /// <summary>
        /// Get broken buildings as targets
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            return UpdateJobSpecificCache(map);
        }

        #endregion

        #region Job Creation

        /// <summary>
        /// Creates a construction job for repairing broken buildings
        /// </summary>
        protected override Job CreateConstructionJob(Pawn pawn, bool forced)
        {
            if (pawn?.Map == null)
                return null;

            int mapId = pawn.Map.uniqueID;

            // Get targets from the cache
            List<Thing> brokenBuildings = GetCachedTargets(mapId);

            // If cache is empty or not yet populated
            if (brokenBuildings == null || brokenBuildings.Count == 0)
            {
                // Try to update cache if needed
                if (ShouldUpdateCache(mapId))
                {
                    UpdateCache(mapId, pawn.Map);
                    brokenBuildings = GetCachedTargets(mapId);
                }

                // If still empty, get targets directly
                if (brokenBuildings == null || brokenBuildings.Count == 0)
                {
                    brokenBuildings = GetTargets(pawn.Map).ToList();
                }
            }

            if (brokenBuildings.Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                brokenBuildings,
                (thing) => (thing.Position - pawn.Position).LengthHorizontalSquared,
                DistanceThresholds
            );

            // Find the best building to repair
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
                else
                {
                    JobFailReason.Is(NoComponentsToRepairTrans);
                }
            }

            return null;
        }

        #endregion

        #region Thing-Based Helpers

        /// <summary>
        /// Basic validation for repair targets
        /// </summary>
        protected override bool ValidateConstructionTarget(Thing thing, Pawn pawn, bool forced = false)
        {
            // Skip if not a building
            Building building = thing as Building;
            if (building == null)
                return false;

            // Skip if not valid
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

            // Skip if forbidden or unreachable
            if (!pawn.CanReserve(building, 1, -1, null, forced) ||
                !pawn.CanReach(building, PathEndMode.Touch, Danger.Deadly))
                return false;

            return true;
        }

        /// <summary>
        /// Validates if a building is appropriate for repair
        /// </summary>
        private bool ValidateBuildingRepair(Building building, Pawn pawn, bool forced)
        {
            return ValidateConstructionTarget(building, pawn, forced);
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

        #region Reset

        /// <summary>
        /// Reset the cache - implements IResettableCache
        /// </summary>
        public override void Reset()
        {
            // Use centralized cache reset
            base.Reset();
        }

        #endregion

        #region Utility

        /// <summary>
        /// Initialize static translation data
        /// </summary>
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