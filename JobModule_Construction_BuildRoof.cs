using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for building roofs in designated areas
    /// </summary>
    public class JobModule_Construction_BuildRoof : JobModule_Construction
    {
        public override string UniqueID => "BuildRoof";
        public override float Priority => 5.5f; // Same as original JobGiver priority
        public override string Category => "Construction";
        public override int CacheUpdateInterval => 250; // Update every 4 seconds, matching original

        // Cache for cells marked for roof building
        private static readonly Dictionary<int, List<IntVec3>> _roofBuildCellsCache = new Dictionary<int, List<IntVec3>>();
        private static readonly Dictionary<int, Dictionary<IntVec3, bool>> _cellReachabilityCache = new Dictionary<int, Dictionary<IntVec3, bool>>();
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 2500f }; // 15, 25, 50 tiles

        // This module requires special handling for cells mapped to Things
        private static readonly Dictionary<int, Dictionary<IntVec3, Thing>> _cellThingMapping = new Dictionary<int, Dictionary<IntVec3, Thing>>();
        private static readonly Dictionary<int, List<Thing>> _dummyTargetCache = new Dictionary<int, List<Thing>>();

        // We need to use this to satisfy the base class contract, even though we primarily work with cells
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups =>
            new HashSet<ThingRequestGroup> { ThingRequestGroup.BuildingArtificial };

        public override void ResetStaticData()
        {
            _roofBuildCellsCache.Clear();

            if (_cellReachabilityCache != null)
            {
                foreach (var mapCache in _cellReachabilityCache.Values)
                {
                    mapCache.Clear();
                }
                _cellReachabilityCache.Clear();
            }

            if (_cellThingMapping != null)
            {
                foreach (var mapCache in _cellThingMapping.Values)
                {
                    mapCache.Clear();
                }
                _cellThingMapping.Clear();
            }

            if (_dummyTargetCache != null)
            {
                foreach (var mapCache in _dummyTargetCache.Values)
                {
                    mapCache.Clear();
                }
                _dummyTargetCache.Clear();
            }
        }

        public override void UpdateCache(Map map, List<Thing> targetCache)
        {
            if (map == null) return;
            int mapId = map.uniqueID;
            bool hasTargets = false;

            // Skip if no build roof area exists
            if (map.areaManager.BuildRoof == null || map.areaManager.BuildRoof.TrueCount == 0)
            {
                SetHasTargets(map, false);
                return;
            }

            // Initialize caches if needed
            if (!_roofBuildCellsCache.ContainsKey(mapId))
                _roofBuildCellsCache[mapId] = new List<IntVec3>();
            else
                _roofBuildCellsCache[mapId].Clear();

            if (!_cellReachabilityCache.ContainsKey(mapId))
                _cellReachabilityCache[mapId] = new Dictionary<IntVec3, bool>();
            else
                _cellReachabilityCache[mapId].Clear();

            if (!_cellThingMapping.ContainsKey(mapId))
                _cellThingMapping[mapId] = new Dictionary<IntVec3, Thing>();
            else
                _cellThingMapping[mapId].Clear();

            if (!_dummyTargetCache.ContainsKey(mapId))
                _dummyTargetCache[mapId] = new List<Thing>();
            else
                _dummyTargetCache[mapId].Clear();

            try
            {
                // Find all cells marked for roof building
                foreach (IntVec3 cell in map.areaManager.BuildRoof.ActiveCells)
                {
                    // Skip if already roofed
                    if (cell.Roofed(map))
                        continue;

                    // Skip if this would create an unsupported roof
                    if (!RoofCollapseUtility.WithinRangeOfRoofHolder(cell, map))
                        continue;

                    // Skip if not connected to existing roof/support
                    if (!RoofCollapseUtility.ConnectedToRoofHolder(cell, map, assumeRoofAtRoot: true))
                        continue;

                    _roofBuildCellsCache[mapId].Add(cell);

                    // For each valid cell, create a dummy target that can be used by the base class methods
                    // Use the building that supports the roof if possible; otherwise use a dummy marker
                    Thing supportingThing = FindNearestSupportingThing(cell, map);
                    _cellThingMapping[mapId][cell] = supportingThing;

                    // Only add to target list if it's not already included
                    if (supportingThing != null && !_dummyTargetCache[mapId].Contains(supportingThing))
                    {
                        _dummyTargetCache[mapId].Add(supportingThing);
                        targetCache.Add(supportingThing);
                    }

                    hasTargets = true;
                }

                // Limit cache size for performance
                int maxCacheSize = 500;
                if (_roofBuildCellsCache[mapId].Count > maxCacheSize)
                {
                    _roofBuildCellsCache[mapId] = _roofBuildCellsCache[mapId].Take(maxCacheSize).ToList();
                }
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error updating roof build cells cache: {ex}");
            }

            if (_roofBuildCellsCache[mapId].Count > 0)
            {
                Utility_DebugManager.LogNormal($"Found {_roofBuildCellsCache[mapId].Count} cells needing roofs on map {map.uniqueID}");
            }

            SetHasTargets(map, hasTargets);
        }

        /// <summary>
        /// Find the nearest building that could support a roof at this cell
        /// </summary>
        private Thing FindNearestSupportingThing(IntVec3 cell, Map map)
        {
            // First check if there's something in the cell itself
            List<Thing> things = map.thingGrid.ThingsListAt(cell);
            foreach (Thing t in things)
            {
                if (t.def.building != null && t.def.building.isNaturalRock)
                    return t;
            }

            // Then look in a small radius for walls or other buildings
            int radius = 1;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(cell, radius, true))
            {
                if (c.InBounds(map))
                {
                    foreach (Thing t in map.thingGrid.ThingsListAt(c))
                    {
                        if (t.def.building != null &&
                            (t.def.building.isNaturalRock || t.def.fillPercent >= 0.2f))
                            return t;
                    }
                }
            }

            // If no supporting structure found, return null
            return null;
        }

        public override bool ShouldProcessBuildable(Thing thing, Map map)
        {
            if (thing == null || map == null) return false;
            int mapId = map.uniqueID;

            // Check if any cells using this thing as their support need roofing
            if (_cellThingMapping.TryGetValue(mapId, out Dictionary<IntVec3, Thing> mapping))
            {
                return mapping.Values.Contains(thing);
            }

            return false;
        }

        public override bool ValidateConstructionJob(Thing thing, Pawn constructionWorker)
        {
            if (thing == null || constructionWorker == null) return false;

            int mapId = constructionWorker.Map.uniqueID;

            // Find a roof cell that needs building, using this thing as a reference
            if (_cellThingMapping.TryGetValue(mapId, out Dictionary<IntVec3, Thing> mapping))
            {
                foreach (var pair in mapping)
                {
                    if (pair.Value == thing)
                    {
                        // Found a cell that uses this thing as reference
                        IntVec3 cell = pair.Key;

                        // Skip if cell no longer valid
                        if (!constructionWorker.Map.areaManager.BuildRoof[cell] || cell.Roofed(constructionWorker.Map))
                            continue;

                        // Check for blocking things
                        Thing blocker = RoofUtility.FirstBlockingThing(cell, constructionWorker.Map);
                        if (blocker != null)
                        {
                            // Can't build roof yet because of blocker
                            return false;
                        }

                        // Check if cell can be reserved and reached
                        if (cell.IsForbidden(constructionWorker) ||
                            !constructionWorker.CanReserve(cell) ||
                            !constructionWorker.CanReach(cell, PathEndMode.Touch, constructionWorker.NormalMaxDanger()))
                            continue;

                        // Re-check for roof stability
                        if (!RoofCollapseUtility.WithinRangeOfRoofHolder(cell, constructionWorker.Map) ||
                            !RoofCollapseUtility.ConnectedToRoofHolder(cell, constructionWorker.Map, assumeRoofAtRoot: true))
                            continue;

                        // Found a valid cell to build roof
                        return true;
                    }
                }
            }

            return false;
        }

        protected override Job CreateConstructionJob(Pawn constructionWorker, Thing thing)
        {
            if (thing == null || constructionWorker == null) return null;

            int mapId = constructionWorker.Map.uniqueID;

            // Find a roof cell that needs building, using this thing as a reference
            if (_cellThingMapping.TryGetValue(mapId, out Dictionary<IntVec3, Thing> mapping))
            {
                foreach (var pair in mapping)
                {
                    if (pair.Value == thing)
                    {
                        // Found a cell that uses this thing as reference
                        IntVec3 cell = pair.Key;

                        // Skip if cell no longer valid
                        if (!constructionWorker.Map.areaManager.BuildRoof[cell] || cell.Roofed(constructionWorker.Map))
                            continue;

                        // Check if there's a blocker that needs handling first
                        Thing blocker = RoofUtility.FirstBlockingThing(cell, constructionWorker.Map);
                        if (blocker != null)
                        {
                            // Try to create a job to handle the blocker
                            Job blockingJob = RoofUtility.HandleBlockingThingJob(blocker, constructionWorker, false);
                            if (blockingJob != null)
                            {
                                Utility_DebugManager.LogNormal($"{constructionWorker.LabelShort} created job to handle {blocker.LabelCap} blocking roof construction");
                                return blockingJob;
                            }
                            continue;
                        }

                        // Final checks before creating job
                        if (cell.IsForbidden(constructionWorker) ||
                            !constructionWorker.CanReserve(cell) ||
                            !constructionWorker.CanReach(cell, PathEndMode.Touch, constructionWorker.NormalMaxDanger()))
                            continue;

                        // Re-check for roof stability
                        if (!RoofCollapseUtility.WithinRangeOfRoofHolder(cell, constructionWorker.Map) ||
                            !RoofCollapseUtility.ConnectedToRoofHolder(cell, constructionWorker.Map, assumeRoofAtRoot: true))
                            continue;

                        // Create roof building job targeting the cell
                        Job job = JobMaker.MakeJob(JobDefOf.BuildRoof);
                        job.targetA = cell;
                        job.targetB = cell; // Some jobs need a backup target
                        Utility_DebugManager.LogNormal($"{constructionWorker.LabelShort} created job to build roof at {cell}");
                        return job;
                    }
                }
            }

            return null;
        }
    }
}