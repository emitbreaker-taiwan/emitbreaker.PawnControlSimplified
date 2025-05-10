using UnityEngine;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for removing roofs in designated areas
    /// </summary>
    public class JobModule_Construction_RemoveRoof : JobModule_Construction
    {
        public override string UniqueID => "RemoveRoof";
        public override float Priority => 5.6f; // Same as original JobGiver priority - slightly more important than building roofs
        public override string Category => "Construction";
        public override int CacheUpdateInterval => 250; // Update every 4 seconds, matching original

        // Cache for cells marked for roof removal
        private static readonly Dictionary<int, List<IntVec3>> _roofRemovalCellsCache = new Dictionary<int, List<IntVec3>>();
        private static readonly Dictionary<int, Dictionary<IntVec3, bool>> _cellReachabilityCache = new Dictionary<int, Dictionary<IntVec3, bool>>();
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 2500f }; // 15, 25, 50 tiles

        // This module requires special handling for cells mapped to Things, similar to BuildRoof module
        private static readonly Dictionary<int, Dictionary<IntVec3, Thing>> _cellThingMapping = new Dictionary<int, Dictionary<IntVec3, Thing>>();
        private static readonly Dictionary<int, List<Thing>> _dummyTargetCache = new Dictionary<int, List<Thing>>();

        // We need to use this to satisfy the base class contract, even though we primarily work with cells
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups =>
            new HashSet<ThingRequestGroup> { ThingRequestGroup.BuildingArtificial };

        public override void ResetStaticData()
        {
            _roofRemovalCellsCache.Clear();

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

            // Skip if no remove roof area exists
            if (map.areaManager.NoRoof == null || map.areaManager.NoRoof.TrueCount == 0)
            {
                SetHasTargets(map, false);
                return;
            }

            // Initialize caches if needed
            if (!_roofRemovalCellsCache.ContainsKey(mapId))
                _roofRemovalCellsCache[mapId] = new List<IntVec3>();
            else
                _roofRemovalCellsCache[mapId].Clear();

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
                // Find all cells marked for roof removal
                foreach (IntVec3 cell in map.areaManager.NoRoof.ActiveCells)
                {
                    // Skip if not roofed - only remove existing roofs
                    if (!cell.Roofed(map))
                        continue;

                    _roofRemovalCellsCache[mapId].Add(cell);

                    // For each valid cell, find a nearby structure to use as a reference
                    // This allows us to work with the Thing-based JobModule framework
                    Thing referenceThing = FindNearestStructure(cell, map);
                    _cellThingMapping[mapId][cell] = referenceThing;

                    // Only add to target list if it's not already included
                    if (referenceThing != null && !_dummyTargetCache[mapId].Contains(referenceThing))
                    {
                        _dummyTargetCache[mapId].Add(referenceThing);
                        targetCache.Add(referenceThing);
                    }

                    hasTargets = true;
                }

                // Sort cells by roof removal priority if there are multiple
                if (_roofRemovalCellsCache[mapId].Count > 1)
                {
                    _roofRemovalCellsCache[mapId].Sort((a, b) => 
                        GetCellPriority(a, map).CompareTo(GetCellPriority(b, map)));
                }

                // Limit cache size for performance
                int maxCacheSize = 500;
                if (_roofRemovalCellsCache[mapId].Count > maxCacheSize)
                {
                    _roofRemovalCellsCache[mapId] = _roofRemovalCellsCache[mapId].Take(maxCacheSize).ToList();
                }
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error updating roof removal cells cache: {ex}");
            }

            if (_roofRemovalCellsCache[mapId].Count > 0)
            {
                Utility_DebugManager.LogNormal($"Found {_roofRemovalCellsCache[mapId].Count} cells needing roof removal on map {map.uniqueID}");
            }

            SetHasTargets(map, hasTargets);
        }

        /// <summary>
        /// Find the nearest structure to use as a reference point for this cell
        /// </summary>
        private Thing FindNearestStructure(IntVec3 cell, Map map)
        {
            // First check if there's something in the cell itself
            List<Thing> things = map.thingGrid.ThingsListAt(cell);
            foreach (Thing t in things)
            {
                if (t.def.category == ThingCategory.Building || t.def.category == ThingCategory.Item)
                    return t;
            }
            
            // Then look in a small radius for any buildings or items
            int radius = 3;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(cell, radius, true))
            {
                if (c.InBounds(map))
                {
                    foreach (Thing t in map.thingGrid.ThingsListAt(c))
                    {
                        if (t.def.category == ThingCategory.Building || t.def.category == ThingCategory.Item)
                            return t;
                    }
                }
            }
            
            // If no structure found nearby, create a dummy marker at this position
            // since we don't have an equivalent to RoofUtility.RoofDefOf like in BuildRoof
            Building edifice = cell.GetEdifice(map);
            if (edifice != null)
                return edifice;
                
            // Last resort: Use a terrain based approach
            TerrainDef terrain = map.terrainGrid.TerrainAt(cell);
            if (terrain != null)
            {
                foreach (Thing t in map.listerThings.AllThings)
                {
                    if ((t.def.category == ThingCategory.Building || t.def.category == ThingCategory.Item) &&
                        t.Position.DistanceTo(cell) < radius * 2)
                        return t;
                }
            }
            
            // No suitable reference found, will require a fallback in validateJob
            return null;
        }

        /// <summary>
        /// Calculates roof removal priority based on proximity to other roofed cells
        /// </summary>
        private float GetCellPriority(IntVec3 cell, Map map)
        {
            // Logic from vanilla WorkGiver_RemoveRoof:
            // - Prioritize cells NOT adjacent to support structures
            // - Prioritize cells with fewer adjacent roofed cells

            int adjacentRoofedCellsCount = 0;
            for (int i = 0; i < 8; ++i)
            {
                IntVec3 c = cell + GenAdj.AdjacentCells[i];
                if (c.InBounds(map))
                {
                    Building edifice = c.GetEdifice(map);
                    if (edifice != null && edifice.def.holdsRoof)
                        return -60f; // De-prioritize cells next to roof supports

                    if (c.Roofed(map))
                        ++adjacentRoofedCellsCount;
                }
            }

            // Prioritize cells with fewer adjacent roofed cells
            return -Mathf.Min(adjacentRoofedCellsCount, 3);
        }

        public override bool ShouldProcessBuildable(Thing thing, Map map)
        {
            if (thing == null || map == null) return false;
            int mapId = map.uniqueID;
            
            // Check if this thing is used as a reference for any roof removal cells
            if (_cellThingMapping.TryGetValue(mapId, out Dictionary<IntVec3, Thing> mapping))
            {
                return mapping.Values.Contains(thing);
            }
            
            return false;
        }

        public override bool ValidateConstructionJob(Thing thing, Pawn constructionWorker)
        {
            if (thing == null || constructionWorker == null || constructionWorker.Map == null) return false;
            
            int mapId = constructionWorker.Map.uniqueID;
            
            // Find a roof cell that needs removal, using this thing as a reference
            if (_cellThingMapping.TryGetValue(mapId, out Dictionary<IntVec3, Thing> mapping))
            {
                // First try to find a cell that uses this thing as its reference
                foreach (var pair in mapping.Where(p => p.Value == thing))
                {
                    IntVec3 cell = pair.Key;
                    
                    // Skip if cell no longer valid
                    if (!constructionWorker.Map.areaManager.NoRoof[cell] || !cell.Roofed(constructionWorker.Map))
                        continue;
                        
                    // Check if cell can be reserved and reached
                    if (cell.IsForbidden(constructionWorker) ||
                        !constructionWorker.CanReserve(cell) ||
                        !constructionWorker.CanReach(cell, PathEndMode.Touch, constructionWorker.NormalMaxDanger()))
                        continue;
                        
                    // Found a valid cell for roof removal
                    return true;
                }
                
                // If no direct match, find any removable roof cell
                // This is a fallback for when we don't have a specific thing reference
                if (thing == null || mapping.Values.All(v => v != thing))
                {
                    foreach (IntVec3 cell in _roofRemovalCellsCache[mapId])
                    {
                        // Skip if cell no longer valid
                        if (!constructionWorker.Map.areaManager.NoRoof[cell] || !cell.Roofed(constructionWorker.Map))
                            continue;
                            
                        // Check if cell can be reserved and reached
                        if (cell.IsForbidden(constructionWorker) ||
                            !constructionWorker.CanReserve(cell) ||
                            !constructionWorker.CanReach(cell, PathEndMode.Touch, constructionWorker.NormalMaxDanger()))
                            continue;
                            
                        // Found a valid cell for roof removal
                        return true;
                    }
                }
            }
            
            return false;
        }

        protected override Job CreateConstructionJob(Pawn constructionWorker, Thing thing)
        {
            if (constructionWorker?.Map == null) return null;
            
            int mapId = constructionWorker.Map.uniqueID;
            
            // Find a roof cell that needs removal, using this thing as a reference
            if (_cellThingMapping.TryGetValue(mapId, out Dictionary<IntVec3, Thing> mapping))
            {
                // First try to find a cell that uses this thing as its reference
                foreach (var pair in mapping.Where(p => p.Value == thing))
                {
                    IntVec3 cell = pair.Key;
                    
                    // Skip if cell no longer valid
                    if (!constructionWorker.Map.areaManager.NoRoof[cell] || !cell.Roofed(constructionWorker.Map))
                        continue;
                        
                    // Final checks before creating job
                    if (cell.IsForbidden(constructionWorker) ||
                        !constructionWorker.CanReserve(cell) ||
                        !constructionWorker.CanReach(cell, PathEndMode.Touch, constructionWorker.NormalMaxDanger()))
                        continue;
                        
                    // Create roof removal job targeting the cell
                    Job job = JobMaker.MakeJob(JobDefOf.RemoveRoof);
                    job.targetA = cell;
                    job.targetB = cell; // Some jobs need a backup target
                    Utility_DebugManager.LogNormal($"{constructionWorker.LabelShort} created job to remove roof at {cell}");
                    return job;
                }
                
                // If no direct match, find any removable roof cell
                // This is a fallback for when we don't have a specific thing reference
                if (thing == null || mapping.Values.All(v => v != thing))
                {
                    foreach (IntVec3 cell in _roofRemovalCellsCache[mapId])
                    {
                        // Skip if cell no longer valid
                        if (!constructionWorker.Map.areaManager.NoRoof[cell] || !cell.Roofed(constructionWorker.Map))
                            continue;
                            
                        // Final checks before creating job
                        if (cell.IsForbidden(constructionWorker) ||
                            !constructionWorker.CanReserve(cell) ||
                            !constructionWorker.CanReach(cell, PathEndMode.Touch, constructionWorker.NormalMaxDanger()))
                            continue;
                            
                        // Create roof removal job targeting the cell
                        Job job = JobMaker.MakeJob(JobDefOf.RemoveRoof);
                        job.targetA = cell;
                        job.targetB = cell; // Some jobs need a backup target
                        Utility_DebugManager.LogNormal($"{constructionWorker.LabelShort} created job to remove roof at {cell}");
                        return job;
                    }
                }
            }
            
            return null;
        }
    }
}