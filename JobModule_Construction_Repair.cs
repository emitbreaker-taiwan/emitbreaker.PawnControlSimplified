using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for repairing damaged buildings
    /// </summary>
    public class JobModule_Construction_Repair : JobModule_Construction
    {
        public override string UniqueID => "Repair";
        public override float Priority => 5.8f; // Same as original JobGiver priority
        public override string Category => "Construction";
        public override int CacheUpdateInterval => 180; // Update every 3 seconds

        // Cache for buildings that need repairs
        private static readonly Dictionary<int, List<Building>> _repairableBuildingsCache = new Dictionary<int, List<Building>>();
        private static readonly Dictionary<int, Dictionary<Building, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Building, bool>>();
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        // Static translation strings
        private static string NotInHomeAreaTrans;

        // This module deals with repairable buildings
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups =>
            new HashSet<ThingRequestGroup> { ThingRequestGroup.BuildingArtificial };

        public override void ResetStaticData()
        {
            Utility_CacheManager.ResetJobGiverCache<Building>(_repairableBuildingsCache, _reachabilityCache);
            NotInHomeAreaTrans = "NotInHomeArea".Translate();
        }

        public override void UpdateCache(Map map, List<Thing> targetCache)
        {
            if (map == null) return;
            int mapId = map.uniqueID;
            bool hasTargets = false;

            // Initialize caches if needed
            if (!_repairableBuildingsCache.ContainsKey(mapId))
                _repairableBuildingsCache[mapId] = new List<Building>();
            else
                _repairableBuildingsCache[mapId].Clear();

            if (!_reachabilityCache.ContainsKey(mapId))
                _reachabilityCache[mapId] = new Dictionary<Building, bool>();
            else
                _reachabilityCache[mapId].Clear();

            // Find all buildings that need repairs across all factions
            try
            {
                foreach (Faction faction in Find.FactionManager.AllFactions)
                {
                    // Process each faction's repairable buildings
                    foreach (Building building in map.listerBuildingsRepairable.RepairableBuildings(faction))
                    {
                        if (building != null && building.Spawned && building.def.building.repairable &&
                            building.HitPoints < building.MaxHitPoints)
                        {
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
                            targetCache.Add(building);
                            hasTargets = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error processing repairable buildings: {ex.Message}");
            }

            if (_repairableBuildingsCache[mapId].Count > 0)
            {
                Utility_DebugManager.LogNormal(
                    $"Found {_repairableBuildingsCache[mapId].Count} buildings needing repair on map {map.uniqueID}");
            }

            SetHasTargets(map, hasTargets);
        }

        public override bool ShouldProcessBuildable(Thing thing, Map map)
        {
            try
            {
                if (thing == null || map == null || !thing.Spawned) return false;

                // Must be a building
                Building building = thing as Building;
                if (building == null) return false;

                int mapId = map.uniqueID;

                // Check if it's in our cache first
                if (_repairableBuildingsCache.ContainsKey(mapId) && _repairableBuildingsCache[mapId].Contains(building))
                    return true;

                // Check if it needs repairs
                return building.def.building.repairable && building.HitPoints < building.MaxHitPoints;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in ShouldProcessBuildable for repair: {ex}");
                return false;
            }
        }

        public override bool ValidateConstructionJob(Thing thing, Pawn constructionWorker)
        {
            try
            {
                if (thing == null || constructionWorker == null || !thing.Spawned || !constructionWorker.Spawned)
                    return false;

                // Must be a building
                Building building = thing as Building;
                if (building == null) return false;

                // Building must need repairs
                if (building.HitPoints >= building.MaxHitPoints)
                    return false;

                // Check building faction - generally should be same as pawn's
                if (building.Faction != constructionWorker.Faction)
                    return false;

                // Check if in home area (for player faction)
                if (constructionWorker.Faction == Faction.OfPlayer &&
                    !constructionWorker.Map.areaManager.Home[building.Position])
                {
                    JobFailReason.Is(NotInHomeAreaTrans);
                    return false;
                }

                // Check existing designations
                if (constructionWorker.Map.designationManager.DesignationOn(building, DesignationDefOf.Deconstruct) != null)
                    return false;

                if (building.def.mineable &&
                    (constructionWorker.Map.designationManager.DesignationAt(building.Position, DesignationDefOf.Mine) != null ||
                     constructionWorker.Map.designationManager.DesignationAt(building.Position, DesignationDefOf.MineVein) != null))
                    return false;

                // Check if building is on fire
                if (building.IsBurning())
                    return false;

                // Check if pawn can reserve the building
                if (building.IsForbidden(constructionWorker) ||
                    !constructionWorker.CanReserve(building, 1, -1, null, false))
                    return false;

                // Check if reachable
                if (!constructionWorker.CanReach(building, PathEndMode.Touch, Danger.Deadly))
                    return false;

                // Use RepairUtility for the final check
                if (!RepairUtility.PawnCanRepairNow(constructionWorker, building))
                    return false;

                return true;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error validating repair job: {ex}");
                return false;
            }
        }

        protected override Job CreateConstructionJob(Pawn constructionWorker, Thing thing)
        {
            try
            {
                if (constructionWorker == null || thing == null)
                    return null;

                Building building = thing as Building;
                if (building == null) return null;

                Job job = JobMaker.MakeJob(JobDefOf.Repair, building);
                
                if (Prefs.DevMode)
                {
                    Utility_DebugManager.LogNormal($"{constructionWorker.LabelShort} created job to repair {building.LabelCap} ({building.HitPoints}/{building.MaxHitPoints})");
                }
                
                return job;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating repair job: {ex}");
                return null;
            }
        }
    }
}