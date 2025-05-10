using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Abstract base module for all floor-affecting construction jobs (smooth, remove, etc.)
    /// Inherits from JobModule_Construction to integrate with the unified job system
    /// </summary>
    public abstract class JobModule_Construction_AffectFloor : JobModule_Construction
    {
        // Cache for cells with floor-related designations
        protected static readonly Dictionary<int, Dictionary<DesignationDef, List<IntVec3>>> _designatedCellsCache = 
            new Dictionary<int, Dictionary<DesignationDef, List<IntVec3>>>();
        protected static readonly Dictionary<int, Dictionary<IntVec3, bool>> _cellReachabilityCache = 
            new Dictionary<int, Dictionary<IntVec3, bool>>();
        
        // This module requires special handling for cells mapped to Things, similar to BuildRoof module
        protected static readonly Dictionary<int, Dictionary<IntVec3, Thing>> _cellThingMapping = 
            new Dictionary<int, Dictionary<IntVec3, Thing>>();
        protected static readonly Dictionary<int, List<Thing>> _dummyTargetCache = 
            new Dictionary<int, List<Thing>>();
            
        // Distance thresholds for bucketing - match original JobGiver distances
        protected static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles
        
        /// <summary>
        /// Default cache update interval - 2 seconds for floor affecting jobs
        /// </summary>
        public override int CacheUpdateInterval => 120;
        
        /// <summary>
        /// Default category for all floor-affecting jobs
        /// </summary>
        public override string Category => "Construction";
        
        /// <summary>
        /// Default priority for floor jobs is slightly lower than regular construction
        /// </summary>
        public override float Priority => 5.3f;
        
        /// <summary>
        /// Must be implemented by subclasses to specify which designation to target
        /// </summary>
        protected abstract DesignationDef TargetDesignation { get; }
        
        /// <summary>
        /// Must be implemented by subclasses to specify which job to use
        /// </summary>
        protected abstract JobDef FloorJobDef { get; }
        
        /// <summary>
        /// To satisfy the ThingRequestGroup pattern, even though we primarily work with cells
        /// </summary>
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups =>
            new HashSet<ThingRequestGroup> { ThingRequestGroup.BuildingArtificial };
            
        public override void ResetStaticData()
        {
            if (_designatedCellsCache != null)
            {
                foreach (var mapCache in _designatedCellsCache.Values)
                {
                    foreach (var list in mapCache.Values)
                    {
                        list.Clear();
                    }
                    mapCache.Clear();
                }
                _designatedCellsCache.Clear();
            }

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
            
            // Quick early exit if there are no relevant designations
            if (!map.designationManager.AnySpawnedDesignationOfDef(TargetDesignation))
            {
                SetHasTargets(map, false);
                return;
            }
            
            // Initialize caches if needed
            if (!_designatedCellsCache.ContainsKey(mapId))
                _designatedCellsCache[mapId] = new Dictionary<DesignationDef, List<IntVec3>>();
                
            if (!_designatedCellsCache[mapId].ContainsKey(TargetDesignation))
                _designatedCellsCache[mapId][TargetDesignation] = new List<IntVec3>();
            else
                _designatedCellsCache[mapId][TargetDesignation].Clear();
            
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
                // Find all cells with designations
                foreach (Designation designation in map.designationManager.SpawnedDesignationsOfDef(TargetDesignation))
                {
                    IntVec3 cell = designation.target.Cell;
                    
                    // Skip if the cell doesn't pass the validation
                    if (!ShouldProcessFloorCell(cell, map))
                        continue;
                        
                    _designatedCellsCache[mapId][TargetDesignation].Add(cell);
                    
                    // Create a reference Thing for this cell to use with the Thing-based JobModule system
                    Thing referenceThing = FindReferenceThingForCell(cell, map);
                    _cellThingMapping[mapId][cell] = referenceThing;
                    
                    // Only add to target list if it's not already included
                    if (referenceThing != null && !_dummyTargetCache[mapId].Contains(referenceThing))
                    {
                        _dummyTargetCache[mapId].Add(referenceThing);
                        targetCache.Add(referenceThing);
                    }
                    
                    hasTargets = true;
                }
                
                if (_designatedCellsCache[mapId][TargetDesignation].Count > 0)
                {
                    Utility_DebugManager.LogNormal(
                        $"Found {_designatedCellsCache[mapId][TargetDesignation].Count} cells designated for {TargetDesignation.defName} on map {map.uniqueID}");
                }
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error updating {TargetDesignation.defName} cell cache: {ex}");
            }
            
            SetHasTargets(map, hasTargets);
        }
        
        /// <summary>
        /// Find a reference thing to use as a proxy for this cell in the Thing-based JobModule system
        /// </summary>
        protected virtual Thing FindReferenceThingForCell(IntVec3 cell, Map map)
        {
            // First check for things directly in the cell
            List<Thing> things = map.thingGrid.ThingsListAt(cell);
            if (things.Any())
            {
                return things[0];
            }
            
            // Then check nearby cells for a reference object
            int radius = 2;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(cell, radius, true))
            {
                if (c.InBounds(map))
                {
                    List<Thing> nearbyThings = map.thingGrid.ThingsListAt(c);
                    foreach (Thing t in nearbyThings)
                    {
                        if (t.def.category == ThingCategory.Building || t.def.category == ThingCategory.Item)
                            return t;
                    }
                }
            }
            
            // Last resort: Use any building in a wider radius
            foreach (Building b in map.listerBuildings.allBuildingsColonist)
            {
                if (b.Position.DistanceTo(cell) < 10)
                    return b;
            }
            
            // No suitable reference found, we'll have to handle this case specially in validation
            return null;
        }
        
        /// <summary>
        /// Override this to implement floor cell specific validation logic
        /// </summary>
        protected virtual bool ShouldProcessFloorCell(IntVec3 cell, Map map)
        {
            // Default implementation validates that the cell is in bounds
            return cell.InBounds(map);
        }
        
        public override bool ShouldProcessBuildable(Thing thing, Map map)
        {
            if (thing == null || map == null) return false;
            int mapId = map.uniqueID;
            
            // Check if this thing is used as a reference for any floor cells
            if (_cellThingMapping.TryGetValue(mapId, out Dictionary<IntVec3, Thing> mapping))
            {
                return mapping.Values.Contains(thing);
            }
            
            return false;
        }
        
        public override bool ValidateConstructionJob(Thing thing, Pawn constructionWorker)
        {
            if (constructionWorker?.Map == null) return false;
            
            int mapId = constructionWorker.Map.uniqueID;
            
            // Find a floor cell that's designated for this job, using this thing as a reference
            if (_cellThingMapping.TryGetValue(mapId, out Dictionary<IntVec3, Thing> mapping))
            {
                // First try to find a cell that uses this thing as its reference
                foreach (var pair in mapping.Where(p => p.Value == thing))
                {
                    IntVec3 cell = pair.Key;
                    
                    // Skip if cell no longer has the appropriate designation
                    if (constructionWorker.Map.designationManager.DesignationAt(cell, TargetDesignation) == null)
                        continue;
                        
                    // Skip if cell doesn't pass validation
                    if (!ValidateFloorCell(cell, constructionWorker))
                        continue;
                        
                    // Found a valid cell for floor operation
                    return true;
                }
                
                // If no direct match, scan all cells for one that's valid
                if (thing == null || mapping.Values.All(v => v != thing))
                {
                    if (_designatedCellsCache.TryGetValue(mapId, out Dictionary<DesignationDef, List<IntVec3>> defCells) && 
                        defCells.TryGetValue(TargetDesignation, out List<IntVec3> cells))
                    {
                        foreach (IntVec3 cell in cells)
                        {
                            // Skip if cell no longer has the appropriate designation
                            if (constructionWorker.Map.designationManager.DesignationAt(cell, TargetDesignation) == null)
                                continue;
                                
                            // Skip if cell doesn't pass validation
                            if (!ValidateFloorCell(cell, constructionWorker))
                                continue;
                                
                            // Found a valid cell for floor operation
                            return true;
                        }
                    }
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Override to implement cell-specific validation logic
        /// </summary>
        protected virtual bool ValidateFloorCell(IntVec3 cell, Pawn constructionWorker)
        {
            // Basic validation common to all floor jobs
            if (cell.IsForbidden(constructionWorker) ||
                !constructionWorker.CanReserve(cell) ||
                !constructionWorker.CanReach(cell, PathEndMode.Touch, constructionWorker.NormalMaxDanger()))
                return false;
                
            return true;
        }
        
        protected override Job CreateConstructionJob(Pawn constructionWorker, Thing thing)
        {
            if (constructionWorker?.Map == null) return null;
            
            int mapId = constructionWorker.Map.uniqueID;
            
            // Find a floor cell that needs processing, using this thing as a reference
            if (_cellThingMapping.TryGetValue(mapId, out Dictionary<IntVec3, Thing> mapping))
            {
                // First try to find a cell that uses this thing as its reference
                foreach (var pair in mapping.Where(p => p.Value == thing))
                {
                    IntVec3 cell = pair.Key;
                    
                    // Skip if cell no longer has the appropriate designation
                    if (constructionWorker.Map.designationManager.DesignationAt(cell, TargetDesignation) == null)
                        continue;
                        
                    // Skip if cell doesn't pass validation
                    if (!ValidateFloorCell(cell, constructionWorker))
                        continue;
                        
                    // Create the job for this cell
                    Job job = JobMaker.MakeJob(FloorJobDef, cell);
                    Utility_DebugManager.LogNormal(
                        $"{constructionWorker.LabelShort} created job to {FloorJobDef.defName} floor at {cell}");
                    return job;
                }
                
                // If no direct match, scan all cells for one that's valid
                if (thing == null || mapping.Values.All(v => v != thing))
                {
                    if (_designatedCellsCache.TryGetValue(mapId, out Dictionary<DesignationDef, List<IntVec3>> defCells) && 
                        defCells.TryGetValue(TargetDesignation, out List<IntVec3> cells))
                    {
                        // Use distance bucketing to prefer closer cells
                        List<IntVec3>[] buckets = new List<IntVec3>[DISTANCE_THRESHOLDS.Length + 1];
                        for (int i = 0; i < buckets.Length; i++)
                            buckets[i] = new List<IntVec3>();
                            
                        // Sort cells into buckets by distance
                        foreach (IntVec3 cell in cells)
                        {
                            float distSq = (cell - constructionWorker.Position).LengthHorizontalSquared;
                            int bucketIndex = buckets.Length - 1;
                            
                            for (int i = 0; i < DISTANCE_THRESHOLDS.Length; i++)
                            {
                                if (distSq <= DISTANCE_THRESHOLDS[i])
                                {
                                    bucketIndex = i;
                                    break;
                                }
                            }
                            
                            buckets[bucketIndex].Add(cell);
                        }
                        
                        // Process each bucket in order
                        for (int i = 0; i < buckets.Length; i++)
                        {
                            if (buckets[i].Count == 0)
                                continue;
                                
                            // Randomize within bucket for even distribution
                            buckets[i].Shuffle();
                            
                            foreach (IntVec3 cell in buckets[i])
                            {
                                // Skip if cell no longer has the appropriate designation
                                if (constructionWorker.Map.designationManager.DesignationAt(cell, TargetDesignation) == null)
                                    continue;
                                    
                                // Skip if cell doesn't pass validation
                                if (!ValidateFloorCell(cell, constructionWorker))
                                    continue;
                                    
                                // Create the job for this cell
                                Job job = JobMaker.MakeJob(FloorJobDef, cell);
                                Utility_DebugManager.LogNormal(
                                    $"{constructionWorker.LabelShort} created job to {FloorJobDef.defName} floor at {cell}");
                                return job;
                            }
                        }
                    }
                }
            }
            
            return null;
        }
    }
}