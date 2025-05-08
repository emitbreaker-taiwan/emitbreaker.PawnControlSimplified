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
    public class JobGiver_Repair_PawnControl : ThinkNode_JobGiver
    {
        // Cache for buildings that need repairs
        private static readonly Dictionary<int, List<Thing>> _repairableBuildingsCache = new Dictionary<int, List<Thing>>();
        private static readonly Dictionary<int, Dictionary<Thing, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Thing, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 180; // Update every 3 seconds

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        // Translation strings
        private static string NotInHomeAreaTrans;

        public static void ResetStaticData()
        {
            NotInHomeAreaTrans = "NotInHomeArea".Translate();
        }

        public override float GetPriority(Pawn pawn)
        {
            // Repairing is fairly important
            return 5.8f;
        }

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
                    // Update building cache
                    UpdateRepairableBuildingsCache(p);

                    // Find and create a job for repairing buildings
                    return TryCreateRepairJob(p, forced);
                },
                debugJobDesc: "building repair assignment");
        }

        /// <summary>
        /// Updates the cache of buildings that need repairs
        /// </summary>
        private void UpdateRepairableBuildingsCache(Pawn pawn)
        {
            if (pawn?.Map == null || pawn.Faction == null) return;

            Map map = pawn.Map;
            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_repairableBuildingsCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_repairableBuildingsCache.ContainsKey(mapId))
                    _repairableBuildingsCache[mapId].Clear();
                else
                    _repairableBuildingsCache[mapId] = new List<Thing>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Thing, bool>();

                // Find all buildings that need repairs for this pawn's faction
                try
                {
                    foreach (Building building in pawn.Map.listerBuildingsRepairable.RepairableBuildings(pawn.Faction))
                    {
                        if (building != null && building.Spawned && RepairUtility.PawnCanRepairNow(pawn, building))
                        {
                            // Skip buildings outside home area for player pawns
                            if (pawn.Faction == Faction.OfPlayer && !pawn.Map.areaManager.Home[building.Position])
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

                            _repairableBuildingsCache[mapId].Add(building);
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Utility_DebugManager.LogError($"Error processing repairable buildings for {pawn.LabelShort}: {ex.Message}");
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Creates a job for repairing buildings
        /// </summary>
        private Job TryCreateRepairJob(Pawn pawn, bool forced)
        {
            if (pawn?.Map == null || pawn.Faction == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_repairableBuildingsCache.ContainsKey(mapId) || _repairableBuildingsCache[mapId].Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing and target selection
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                _repairableBuildingsCache[mapId],
                (building) => (building.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Find the best building to repair
            Thing bestBuilding = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (building, p) => {
                    // IMPORTANT: Check faction interaction validity first
                    if (!Utility_JobGiverManager.IsValidFactionInteraction(building, p, requiresDesignator: false))
                        return false;

                    // Skip if no longer valid
                    if (building == null || building.Destroyed || !building.Spawned)
                        return false;

                    // Skip if already at max health
                    if (building.HitPoints >= building.MaxHitPoints)
                        return false;

                    // Skip if outside home area for player pawns
                    if (p.Faction == Faction.OfPlayer && !p.Map.areaManager.Home[building.Position])
                    {
                        JobFailReason.Is(NotInHomeAreaTrans);
                        return false;
                    }

                    // Skip if forbidden or burning or unreachable
                    if (building.IsForbidden(p) ||
                        building.IsBurning() ||
                        !p.CanReserve(building, 1, -1, null, forced) ||
                        !p.CanReach(building, PathEndMode.Touch, Danger.Deadly))
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
                _reachabilityCache
            );

            // Create job if target found
            if (bestBuilding != null)
            {
                Job job = JobMaker.MakeJob(JobDefOf.Repair, bestBuilding);

                if (Prefs.DevMode)
                {
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to repair {bestBuilding.LabelCap} ({bestBuilding.HitPoints}/{bestBuilding.MaxHitPoints})");
                }

                return job;
            }

            return null;
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_repairableBuildingsCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
            ResetStaticData();
        }

        public override string ToString()
        {
            return "JobGiver_Repair_PawnControl";
        }
    }
}