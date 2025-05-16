using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that allows pawns to repair damaged buildings belonging to their faction.
    /// Uses the Construction work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Construction_Repair_PawnControl : JobGiver_Construction_PawnControl
    {
        #region Configuration

        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.Repair;

        /// <summary>
        /// Human-readable name for debug logging
        /// </summary>
        protected override string DebugName => "building repair assignment";

        /// <summary>
        /// Whether this job giver requires a designator to operate
        /// Repairs don't require specific designations
        /// </summary>
        public override bool RequiresDesignator => false;

        /// <summary>
        /// Whether this job giver requires map zone or area
        /// </summary>
        public override bool RequiresMapZoneorArea => false;

        /// <summary>
        /// Repair requires player faction for home area restrictions
        /// </summary>
        public override bool RequiresPlayerFaction => true;

        /// <summary>
        /// Update cache every 3 seconds for repair jobs
        /// </summary>
        protected override int CacheUpdateInterval => base.CacheUpdateInterval;

        // Translation strings
        private static string NotInHomeAreaTrans;

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_Construction_Repair_PawnControl() : base()
        {
            // Base constructor already initializes the cache system
        }

        #endregion

        #region Core Flow

        /// <summary>
        /// Repairing is fairly important
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            return 5.8f;
        }

        /// <summary>
        /// Checks if the map meets requirements for repair jobs
        /// </summary>
        protected override bool AreMapRequirementsMet(Pawn pawn)
        {
            // Quick check if there are any buildings to repair
            if (pawn?.Map == null || pawn.Faction == null)
                return false;

            try
            {
                return pawn.Map.listerBuildingsRepairable.RepairableBuildings(pawn.Faction).Count > 0;
            }
            catch (System.Exception ex)
            {
                Utility_DebugManager.LogError($"Error checking repairable buildings: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Target Selection

        /// <summary>
        /// Job-specific cache update method that implements specialized target collection logic
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            return GetTargets(map);
        }

        /// <summary>
        /// Get all buildings that need repairs for this faction
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // No map, no buildings
            if (map == null) yield break;

            // Find all buildings that need repairs for player faction
            foreach (Building building in map.listerBuildingsRepairable.RepairableBuildings(Faction.OfPlayer))
            {
                if (building != null && building.Spawned && building.HitPoints < building.MaxHitPoints)
                {
                    // Skip buildings outside home area for player pawns
                    if (!map.areaManager.Home[building.Position])
                        continue;

                    // Skip buildings with deconstruct or mine designations
                    if (building.Map.designationManager.DesignationOn(building, DesignationDefOf.Deconstruct) != null)
                        continue;

                    if (building.def.mineable &&
                        (building.Map.designationManager.DesignationAt(building.Position, DesignationDefOf.Mine) != null ||
                         building.Map.designationManager.DesignationAt(building.Position, DesignationDefOf.MineVein) != null))
                        continue;

                    // Skip burning buildings
                    if (building.IsBurning())
                        continue;

                    yield return building;
                }
            }
        }

        #endregion

        #region Job Creation

        /// <summary>
        /// Implement to create the specific construction job for repairs
        /// </summary>
        protected override Job CreateConstructionJob(Pawn pawn, bool forced)
        {
            if (pawn?.Map == null || pawn.Faction == null)
                return null;

            int mapId = pawn.Map.uniqueID;

            // Get targets from the cache
            List<Thing> cachedTargets = GetCachedTargets(mapId);
            List<Thing> repairableBuildings;

            // If cache is empty or not yet populated
            if (cachedTargets == null || cachedTargets.Count == 0)
            {
                // Try to update cache if needed
                if (ShouldUpdateCache(mapId))
                {
                    UpdateCache(mapId, pawn.Map);
                    cachedTargets = GetCachedTargets(mapId);
                }

                // If still empty, get targets directly
                if (cachedTargets == null || cachedTargets.Count == 0)
                {
                    repairableBuildings = GetTargets(pawn.Map).ToList();
                }
                else
                {
                    repairableBuildings = cachedTargets;
                }
            }
            else
            {
                repairableBuildings = cachedTargets;
            }

            if (repairableBuildings.Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                repairableBuildings,
                (building) => (building.Position - pawn.Position).LengthHorizontalSquared,
                DistanceThresholds
            );

            // Find the best building to repair - let JobGiverManager handle reachability caching internally
            Thing bestBuilding = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (building, actor) => ValidateConstructionTarget(building, actor, forced),
                null  // Pass null to use the centralized caching system
            );

            // Create job if target found
            if (bestBuilding != null)
            {
                Job job = JobMaker.MakeJob(WorkJobDef, bestBuilding);

                if (Prefs.DevMode)
                {
                    Building bldg = bestBuilding as Building;
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to repair {bestBuilding.LabelCap} ({bldg?.HitPoints}/{bldg?.MaxHitPoints})");
                }

                return job;
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
            // First perform base validation
            if (!base.ValidateConstructionTarget(thing, pawn, forced))
                return false;

            Building building = thing as Building;
            if (building == null)
                return false;

            // Skip if already at max health
            if (building.HitPoints >= building.MaxHitPoints)
                return false;

            // Skip if outside home area for player pawns
            if (pawn.Faction == Faction.OfPlayer && !pawn.Map.areaManager.Home[building.Position])
            {
                JobFailReason.Is(NotInHomeAreaTrans);
                return false;
            }

            // Skip if has a deconstruct or mine designation
            if (building.Map.designationManager.DesignationOn(building, DesignationDefOf.Deconstruct) != null)
                return false;

            if (building.def.mineable &&
                (building.Map.designationManager.DesignationAt(building.Position, DesignationDefOf.Mine) != null ||
                 building.Map.designationManager.DesignationAt(building.Position, DesignationDefOf.MineVein) != null))
                return false;

            // Skip if burning
            if (building.IsBurning())
                return false;

            return true;
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

        /// <summary>
        /// Initialize static data
        /// </summary>
        public static void ResetStaticData()
        {
            NotInHomeAreaTrans = "NotInHomeArea".Translate();
        }

        public override string ToString()
        {
            return "JobGiver_Repair_PawnControl";
        }

        #endregion
    }
}