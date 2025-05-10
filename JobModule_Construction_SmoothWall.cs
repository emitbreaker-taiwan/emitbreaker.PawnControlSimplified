using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for smoothing walls with designated for smoothing
    /// </summary>
    public class JobModule_Construction_SmoothWall : JobModule_Construction
    {
        public override string UniqueID => "SmoothWall";
        public override float Priority => 5.4f; // Same as original JobGiver priority
        public override string Category => "Construction";
        public override int CacheUpdateInterval => 180; // Update every 3 seconds, matching original

        // Cache for buildings designated for smoothing
        private static readonly Dictionary<int, List<Building>> _smoothBuildingsCache = new Dictionary<int, List<Building>>();
        private static readonly Dictionary<int, Dictionary<Building, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Building, bool>>();
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        // This module works with buildings, specifically walls that can be smoothed
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups =>
            new HashSet<ThingRequestGroup> { ThingRequestGroup.BuildingArtificial };

        public override void ResetStaticData()
        {
            Utility_CacheManager.ResetJobGiverCache<Building>(_smoothBuildingsCache, _reachabilityCache);
        }

        public override void UpdateCache(Map map, List<Thing> targetCache)
        {
            if (map == null) return;
            int mapId = map.uniqueID;
            bool hasTargets = false;

            // Quick early exit if there are no wall smoothing designations
            if (!map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.SmoothWall))
            {
                SetHasTargets(map, false);
                return;
            }

            // Initialize caches if needed
            if (!_smoothBuildingsCache.ContainsKey(mapId))
                _smoothBuildingsCache[mapId] = new List<Building>();
            else
                _smoothBuildingsCache[mapId].Clear();

            if (!_reachabilityCache.ContainsKey(mapId))
                _reachabilityCache[mapId] = new Dictionary<Building, bool>();
            else
                _reachabilityCache[mapId].Clear();

            try
            {
                // Find all designated walls for smoothing
                foreach (Designation designation in map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.SmoothWall))
                {
                    // Get the building at this cell
                    Building edifice = designation.target.Cell.GetEdifice(map);

                    // Skip if there's no building or it's not smoothable
                    if (edifice == null || !edifice.def.IsSmoothable)
                        continue;

                    _smoothBuildingsCache[mapId].Add(edifice);
                    targetCache.Add(edifice);
                    hasTargets = true;
                }

                if (_smoothBuildingsCache[mapId].Count > 0)
                {
                    Utility_DebugManager.LogNormal($"Found {_smoothBuildingsCache[mapId].Count} walls designated for smoothing on map {map.uniqueID}");
                }
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error updating wall smoothing cache: {ex}");
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

                // Must be smoothable
                if (!building.def.IsSmoothable)
                    return false;

                int mapId = map.uniqueID;

                // Check if it's in our cache first
                if (_smoothBuildingsCache.ContainsKey(mapId) && _smoothBuildingsCache[mapId].Contains(building))
                    return true;

                // Otherwise check if it has a smooth wall designation
                return map.designationManager.DesignationAt(building.Position, DesignationDefOf.SmoothWall) != null;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in ShouldProcessBuildable for wall smoothing: {ex}");
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

                // Skip if no longer designated
                if (constructionWorker.Map.designationManager.DesignationAt(building.Position, DesignationDefOf.SmoothWall) == null)
                    return false;

                // Skip if no longer smoothable
                if (!building.def.IsSmoothable)
                    return false;

                // Skip if forbidden or unreachable
                if (building.IsForbidden(constructionWorker) ||
                    !constructionWorker.CanReserve(building, 1, -1, null, false) ||
                    !constructionWorker.CanReserve(building.Position, 1, -1, null, false) ||
                    !constructionWorker.CanReach(building, PathEndMode.Touch, Danger.Some))
                    return false;

                return true;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error validating wall smoothing job: {ex}");
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

                // Perform final validation
                if (!ValidateConstructionJob(building, constructionWorker))
                    return null;

                // Create the wall smoothing job
                Job job = JobMaker.MakeJob(JobDefOf.SmoothWall, building);
                Utility_DebugManager.LogNormal($"{constructionWorker.LabelShort} created job to smooth wall: {building}");
                return job;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating wall smoothing job: {ex}");
                return null;
            }
        }
    }
}