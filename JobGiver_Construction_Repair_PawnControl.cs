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
    public class JobGiver_Construction_Repair_PawnControl : JobGiver_Scan_PawnControl
    {
        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        // Translation strings
        private static string NotInHomeAreaTrans;

        #region Overrides

        /// <summary>
        /// Use Construction work tag
        /// </summary>
        protected override string WorkTag => "Construction";

        /// <summary>
        /// Override cache update interval for repair jobs
        /// </summary>
        protected override int CacheUpdateInterval => 180; // Update every 3 seconds

        /// <summary>
        /// Repairing is fairly important
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            return 5.8f;
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

        /// <summary>
        /// Override TryGiveJob to use StandardTryGiveJob pattern
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            // Quick check if there are any buildings to repair
            if (pawn?.Map == null || pawn.Faction == null)
                return null;

            try
            {
                if (pawn.Map.listerBuildingsRepairable.RepairableBuildings(pawn.Faction).Count == 0)
                    return null;
            }
            catch (System.Exception ex)
            {
                Utility_DebugManager.LogError($"Error checking repairable buildings: {ex.Message}");
                return null;
            }

            return Utility_JobGiverManager.StandardTryGiveJob<Building>(
                pawn,
                "Construction",
                (p, forced) => {
                    if (p?.Map == null || p.Faction == null) return null;

                    // Get buildings that need repairs
                    List<Thing> repairableBuildings = GetTargets(p.Map).ToList();
                    if (repairableBuildings.Count == 0) return null;

                    // Use JobGiverManager for distance bucketing and target selection
                    var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                        p,
                        repairableBuildings,
                        (building) => (building.Position - p.Position).LengthHorizontalSquared,
                        DISTANCE_THRESHOLDS
                    );

                    // Dictionary for tracking reachability checks - create a properly typed dictionary
                    int mapId = p.Map.uniqueID;
                    Dictionary<int, Dictionary<Thing, bool>> reachabilityCache = new Dictionary<int, Dictionary<Thing, bool>>();
                    reachabilityCache[mapId] = new Dictionary<Thing, bool>();

                    // Find the best building to repair
                    Thing bestBuilding = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                        buckets,
                        p,
                        (building, actor) => { // Changed parameter name from 'pawn' to 'actor' to avoid naming conflict
                            // IMPORTANT: Check faction interaction validity first
                            if (!Utility_JobGiverManager.IsValidFactionInteraction(building, actor, requiresDesignator: false))
                                return false;

                            // Skip if no longer valid
                            if (building == null || building.Destroyed || !building.Spawned)
                                return false;

                            // Skip if already at max health
                            if (building.HitPoints >= building.MaxHitPoints)
                                return false;

                            // Skip if outside home area for player pawns
                            if (actor.Faction == Faction.OfPlayer && !actor.Map.areaManager.Home[building.Position])
                            {
                                JobFailReason.Is(NotInHomeAreaTrans);
                                return false;
                            }

                            // Skip if forbidden or burning or unreachable
                            if (building.IsForbidden(actor) ||
                                building.IsBurning() ||
                                !actor.CanReserve(building, 1, -1, null, forced) ||
                                !actor.CanReach(building, PathEndMode.Touch, Danger.Deadly))
                                return false;

                            // Skip if has a deconstruct or mine designation
                            if (building.Map.designationManager.DesignationOn(building, DesignationDefOf.Deconstruct) != null)
                                return false;

                            if (building.def.mineable &&
                                (building.Map.designationManager.DesignationAt(building.Position, DesignationDefOf.Mine) != null ||
                                 building.Map.designationManager.DesignationAt(building.Position, DesignationDefOf.MineVein) != null))
                                return false;

                            return true;
                        },
                        reachabilityCache
                    );

                    // Create job if target found
                    if (bestBuilding != null)
                    {
                        Job job = JobMaker.MakeJob(JobDefOf.Repair, bestBuilding);

                        if (Prefs.DevMode)
                        {
                            Utility_DebugManager.LogNormal($"{p.LabelShort} created job to repair {bestBuilding.LabelCap} ({bestBuilding.HitPoints}/{bestBuilding.MaxHitPoints})");
                        }

                        return job;
                    }

                    return null;
                },
                debugJobDesc: "building repair assignment");
        }

        /// <summary>
        /// Process cached targets to create a repair job for the pawn.
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            if (targets == null || targets.Count == 0)
                return null;

            // Find the best target to repair
            foreach (var target in targets)
            {
                if (target is Building building && building.HitPoints < building.MaxHitPoints)
                {
                    if (pawn.CanReserveAndReach(building, PathEndMode.Touch, Danger.Deadly, 1, -1, null, forced))
                    {
                        return JobMaker.MakeJob(JobDefOf.Repair, building);
                    }
                }
            }

            return null;
        }

        #endregion

        /// <summary>
        /// Initialize static data
        /// </summary>
        public static void ResetStaticData()
        {
            NotInHomeAreaTrans = "NotInHomeArea".Translate();
        }

        /// <summary>
        /// Custom ToString implementation for debugging
        /// </summary>
        public override string ToString()
        {
            return "JobGiver_Repair_PawnControl";
        }
    }
}