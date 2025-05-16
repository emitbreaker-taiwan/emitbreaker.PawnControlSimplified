using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Provides advanced target pre-filtering capabilities to optimize JobGiver target selection
    /// </summary>
    public static class Utility_TargetPrefilteringManager
    {
        #region Spatial Indexing
        
        // Dictionary of spatial indices for each map
        private static readonly Dictionary<int, Dictionary<Type, SpatialIndex>> _spatialIndices = new Dictionary<int, Dictionary<Type, SpatialIndex>>();
            
        // Tracks when spatial indices were last updated for each map
        private static readonly Dictionary<int, Dictionary<Type, int>> _spatialIndexUpdateTicks = new Dictionary<int, Dictionary<Type, int>>();

        private static readonly Dictionary<int, Dictionary<Type, List<Thing>>> _thingsByTypeCaches = new Dictionary<int, Dictionary<Type, List<Thing>>>();
        private static readonly Dictionary<int, int> _thingsByTypeUpdateTicks = new Dictionary<int, int>();
        private const int TYPE_CACHE_UPDATE_INTERVAL = 180; // 3 seconds

        public static List<T> GetThingsByType<T>(Map map) where T : Thing
        {
            if (map == null)
                return new List<T>();

            int mapId = map.uniqueID;
            Type type = typeof(T);
            int currentTick = Find.TickManager.TicksGame;

            // Initialize map cache if needed
            if (!_thingsByTypeCaches.TryGetValue(mapId, out var typeCache))
            {
                typeCache = new Dictionary<Type, List<Thing>>();
                _thingsByTypeCaches[mapId] = typeCache;
                _thingsByTypeUpdateTicks[mapId] = currentTick - TYPE_CACHE_UPDATE_INTERVAL - 1; // Force update on first access
            }

            // Check if update is needed
            int lastUpdate = _thingsByTypeUpdateTicks.GetValueSafe(mapId, 0);
            bool needsUpdate = (currentTick - lastUpdate >= TYPE_CACHE_UPDATE_INTERVAL);

            // Update if needed
            if (needsUpdate)
            {
                // Clear all type caches for this map
                typeCache.Clear();
                _thingsByTypeUpdateTicks[mapId] = currentTick;
            }

            // Get or create cache for this type
            if (!typeCache.TryGetValue(type, out var things))
            {
                // Build the cache for this type
                things = map.listerThings.AllThings.Where(t => t is T).ToList();
                typeCache[type] = things;
            }

            // Return the typed list
            return things.OfType<T>().ToList();
        }

        /// <summary>
        /// Spatial index for quickly finding nearby objects
        /// </summary>
        public class SpatialIndex
        {
            // Grid of cells to objects, using a sparse representation for memory efficiency
            private readonly Dictionary<int, List<Thing>> _grid = new Dictionary<int, List<Thing>>();
            
            // Size of each cell in the grid (in game units)
            private readonly int _cellSize;
            
            // Maximum number of items to store in the spatial index
            private readonly int _maxItems;

            private readonly QuadTree _quadTree;
            private readonly bool _useQuadTree = true;

            /// <summary>
            /// Creates a new spatial index
            /// </summary>
            public SpatialIndex(Map map, int cellSize = 12, int maxItems = 5000)
            {
                _cellSize = cellSize;
                _maxItems = maxItems;

                // Initialize quad tree with the map
                if (map != null)
                {
                    _quadTree = new QuadTree(map, maxItems);
                    _useQuadTree = true;
                }
                else
                {
                    _useQuadTree = false;
                }
            }

            /// <summary>
            /// Clear the spatial index
            /// </summary>
            public void Clear()
            {
                _grid.Clear();

                if (_useQuadTree)
                {
                    _quadTree.Clear();
                }
            }

            /// <summary>
            /// Add a thing to the spatial index
            /// </summary>
            public void Add(Thing thing)
            {
                if (thing == null || !thing.Spawned)
                    return;

                if (_useQuadTree)
                {
                    _quadTree.Add(thing);
                }
                else
                {
                    // Calculate the cell index for this thing
                    int cellIndex = GetCellIndex(thing.Position);

                    // Get or create the list for this cell
                    if (!_grid.TryGetValue(cellIndex, out var cellItems))
                    {
                        cellItems = new List<Thing>();
                        _grid[cellIndex] = cellItems;
                    }

                    // Add the thing to this cell
                    if (!cellItems.Contains(thing))
                    {
                        cellItems.Add(thing);
                    }
                }
            }

            /// <summary>
            /// Add multiple things to the spatial index
            /// </summary>
            public void AddRange<T>(IEnumerable<T> things) where T : Thing
            {
                if (things == null)
                    return;

                // Skip bulk add if we'd exceed max items
                int count = _useQuadTree ? 0 : _grid.Values.Sum(list => list.Count);
                if (count >= _maxItems && !_useQuadTree)
                    return;

                if (_useQuadTree)
                {
                    _quadTree.AddRange(things);
                }
                else
                {
                    // Add each thing
                    foreach (var thing in things)
                    {
                        Add(thing);

                        // Stop if we've reached the max items
                        if (++count >= _maxItems)
                            break;
                    }
                }
            }

            /// <summary>
            /// Get things within the specified radius of a position
            /// </summary>
            public List<Thing> GetThingsInRadius(IntVec3 center, float radius)
            {
                if (_useQuadTree)
                {
                    // Use quad tree for efficient queries
                    return _quadTree.QueryRange(center, radius);
                }

                // Original grid-based implementation
                // Calculate the range of cells to check
                int radiusCells = Mathf.CeilToInt(radius / _cellSize);
                int centerIndex = GetCellIndex(center);
                int xCenter = centerIndex / 1000000;
                int zCenter = centerIndex % 1000000;

                List<Thing> results = new List<Thing>();
                float radiusSq = radius * radius;

                // Check every cell in the radius
                for (int xOffset = -radiusCells; xOffset <= radiusCells; xOffset++)
                {
                    for (int zOffset = -radiusCells; zOffset <= radiusCells; zOffset++)
                    {
                        int cellX = xCenter + xOffset;
                        int cellZ = zCenter + zOffset;
                        int cellIndex = cellX * 1000000 + cellZ;

                        // Skip if this cell has no items
                        if (!_grid.TryGetValue(cellIndex, out var cellItems))
                            continue;

                        // Check each item in this cell
                        foreach (var thing in cellItems)
                        {
                            // Skip if the thing no longer exists
                            if (thing == null || thing.Destroyed || !thing.Spawned)
                                continue;

                            // Check if the thing is within the radius
                            if ((thing.Position - center).LengthHorizontalSquared <= radiusSq)
                            {
                                results.Add(thing);
                            }
                        }
                    }
                }

                return results;
            }

            /// <summary>
            /// Calculate the cell index for a position
            /// </summary>
            private int GetCellIndex(IntVec3 pos)
            {
                int cellX = Mathf.FloorToInt(pos.x / _cellSize);
                int cellZ = Mathf.FloorToInt(pos.z / _cellSize);
                return cellX * 1000000 + cellZ; // Large multiplier to avoid collisions
            }
        }

        /// <summary>
        /// Gets or creates a spatial index for a specific thing type on a map
        /// </summary>
        public static SpatialIndex GetSpatialIndex<T>(Map map, int updateInterval = TYPE_CACHE_UPDATE_INTERVAL) where T : Thing
        {
            if (map == null)
                return null;

            int mapId = map.uniqueID;
            Type thingType = typeof(T);
            int currentTick = Find.TickManager.TicksGame;

            // Initialize map dictionaries if needed
            if (!_spatialIndices.TryGetValue(mapId, out var mapIndices))
            {
                mapIndices = new Dictionary<Type, SpatialIndex>();
                _spatialIndices[mapId] = mapIndices;

                var mapUpdateTicks = new Dictionary<Type, int>();
                _spatialIndexUpdateTicks[mapId] = mapUpdateTicks;
            }

            // Get map update ticks dictionary
            var updateTicks = _spatialIndexUpdateTicks[mapId];

            // Get or create the spatial index
            if (!mapIndices.TryGetValue(thingType, out var spatialIndex))
            {
                spatialIndex = new SpatialIndex(map); // Pass the map to the constructor
                mapIndices[thingType] = spatialIndex;
                updateTicks[thingType] = -updateInterval; // Force update on first access
            }

            // Check if we need to update the index
            int lastUpdateTick = updateTicks.GetValueSafe(thingType, -updateInterval);
            if (currentTick - lastUpdateTick >= updateInterval)
            {
                // Record update time first
                updateTicks[thingType] = currentTick;

                // Clear the existing index
                spatialIndex.Clear();

                // Populate with new data
                var things = map.listerThings.AllThings.Where(t => t is T).Cast<T>().ToList();
                spatialIndex.AddRange(things);

                if (Prefs.DevMode)
                {
                    Utility_DebugManager.LogNormal($"Updated spatial index for {typeof(T).Name} on map {mapId} ({things.Count} items)");
                }
            }

            return spatialIndex;
        }

        /// <summary>
        /// Gets things of type T within the specified radius around a position
        /// </summary>
        public static List<T> GetNearbyThings<T>(Map map, IntVec3 center, float radius, int updateInterval = TYPE_CACHE_UPDATE_INTERVAL) where T : Thing
        {
            // Record start time
            long startTick = DateTime.Now.Ticks;

            // Get the spatial index
            var spatialIndex = GetSpatialIndex<T>(map, updateInterval);
            if (spatialIndex == null)
                return new List<T>();

            // Get things in radius
            var things = spatialIndex.GetThingsInRadius(center, radius);

            // Convert to the correct type
            var results = things.OfType<T>().ToList();

            // Record operation time
            RecordOperationTime($"GetNearbyThings<{typeof(T).Name}>", DateTime.Now.Ticks - startTick);

            return results;
        }

        /// <summary>
        /// Gets the K nearest things of type T around a position
        /// </summary>
        public static List<T> GetNearestThings<T>(Map map, IntVec3 center, int count, int updateInterval = TYPE_CACHE_UPDATE_INTERVAL) where T : Thing
        {
            // Get the spatial index
            var spatialIndex = GetSpatialIndex<T>(map, updateInterval);
            if (spatialIndex == null)
                return new List<T>();

            // Start with a reasonable radius and expand if needed
            float radius = 10f;
            List<Thing> things = new List<Thing>();

            // If we don't get enough things, expand the search radius
            while (things.Count < count && radius < 100f)
            {
                things = spatialIndex.GetThingsInRadius(center, radius);
                radius *= 1.5f; // Increase radius by 50% each time
            }

            // Sort by distance and take only what we need
            return things
                .OrderBy(t => (t.Position - center).LengthHorizontalSquared)
                .Take(count)
                .OfType<T>()
                .ToList();
        }

        #endregion

        #region Zone and Room Grouping

        // Cache of zone/area-based thing groups
        private static readonly Dictionary<int, Dictionary<string, List<Thing>>> _zoneGroupCache = new Dictionary<int, Dictionary<string, List<Thing>>>();
            
        // Cache of room-based thing groups
        private static readonly Dictionary<int, Dictionary<Room, List<Thing>>> _roomGroupCache = new Dictionary<int, Dictionary<Room, List<Thing>>>();
            
        // Update ticks for zone and room caches
        private static readonly Dictionary<int, int> _zoneUpdateTicks = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> _roomUpdateTicks = new Dictionary<int, int>();
        
        // Default update interval for zone and room caches
        private const int ZONE_UPDATE_INTERVAL = 500; // 8.3 seconds
        private const int ROOM_UPDATE_INTERVAL = 750; // 12.5 seconds
        
        /// <summary>
        /// Groups things by zone/area and returns all things in the specified zone
        /// </summary>
        public static List<T> GetThingsInZone<T>(Map map, Zone zone) where T : Thing
        {
            if (map == null || zone == null)
                return new List<T>();
                
            // Update the zone grouping if needed
            UpdateZoneGroupsIfNeeded(map);
            
            // Get the zone cache for this map
            int mapId = map.uniqueID;
            if (!_zoneGroupCache.TryGetValue(mapId, out var zoneCache))
                return new List<T>();
                
            // Get things in the specified zone
            string zoneKey = zone.ID.ToString();
            if (!zoneCache.TryGetValue(zoneKey, out var zoneThings))
                return new List<T>();
                
            // Return only things of the requested type
            return zoneThings.OfType<T>().ToList();
        }
        
        /// <summary>
        /// Groups things by room and returns all things in the specified room
        /// </summary>
        public static List<T> GetThingsInRoom<T>(Map map, Room room) where T : Thing
        {
            if (map == null || room == null)
                return new List<T>();
                
            // Update the room grouping if needed
            UpdateRoomGroupsIfNeeded(map);
            
            // Get the room cache for this map
            int mapId = map.uniqueID;
            if (!_roomGroupCache.TryGetValue(mapId, out var roomCache))
                return new List<T>();
                
            // Get things in the specified room
            if (!roomCache.TryGetValue(room, out var roomThings))
                return new List<T>();
                
            // Return only things of the requested type
            return roomThings.OfType<T>().ToList();
        }
        
        /// <summary>
        /// Gets all things of type T in the same room as the reference position
        /// </summary>
        public static List<T> GetThingsInSameRoom<T>(Map map, IntVec3 position) where T : Thing
        {
            if (map == null)
                return new List<T>();
                
            Room room = position.GetRoom(map);
            if (room == null)
                return new List<T>();
                
            return GetThingsInRoom<T>(map, room);
        }
        
        /// <summary>
        /// Updates zone groups for a map if necessary
        /// </summary>
        private static void UpdateZoneGroupsIfNeeded(Map map)
        {
            if (map == null)
                return;
                
            int mapId = map.uniqueID;
            int currentTick = Find.TickManager.TicksGame;
            
            // Check if an update is needed
            int lastUpdateTick = _zoneUpdateTicks.GetValueSafe(mapId, -ZONE_UPDATE_INTERVAL);
            if (currentTick - lastUpdateTick < ZONE_UPDATE_INTERVAL)
                return;
                
            // Record update time
            _zoneUpdateTicks[mapId] = currentTick;
            
            // Clear or create the zone cache for this map
            if (!_zoneGroupCache.TryGetValue(mapId, out var zoneCache))
            {
                zoneCache = new Dictionary<string, List<Thing>>();
                _zoneGroupCache[mapId] = zoneCache;
            }
            else
            {
                zoneCache.Clear();
            }
            
            // Build a dictionary of things by position
            Dictionary<IntVec3, Thing> thingsByPos = new Dictionary<IntVec3, Thing>();
            foreach (Thing thing in map.listerThings.AllThings)
            {
                // Skip things that can't be hauled/processed
                if (thing.def.category != ThingCategory.Item && !(thing is Building))
                    continue;
                    
                thingsByPos[thing.Position] = thing;
            }
            
            // Process each zone in the map
            foreach (Zone zone in map.zoneManager.AllZones)
            {
                string zoneKey = zone.ID.ToString();
                var zoneThings = new List<Thing>();
                
                // Check each cell in the zone for things
                foreach (IntVec3 cell in zone.cells)
                {
                    if (thingsByPos.TryGetValue(cell, out Thing thing))
                    {
                        zoneThings.Add(thing);
                    }
                }
                
                zoneCache[zoneKey] = zoneThings;
            }
            
            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Updated zone groups for map {mapId} ({zoneCache.Count} zones)");
            }
        }
        
        /// <summary>
        /// Updates room groups for a map if necessary
        /// </summary>
        private static void UpdateRoomGroupsIfNeeded(Map map)
        {
            if (map == null)
                return;
                
            int mapId = map.uniqueID;
            int currentTick = Find.TickManager.TicksGame;
            
            // Check if an update is needed
            int lastUpdateTick = _roomUpdateTicks.GetValueSafe(mapId, -ROOM_UPDATE_INTERVAL);
            if (currentTick - lastUpdateTick < ROOM_UPDATE_INTERVAL)
                return;
                
            // Record update time
            _roomUpdateTicks[mapId] = currentTick;
            
            // Clear or create the room cache for this map
            if (!_roomGroupCache.TryGetValue(mapId, out var roomCache))
            {
                roomCache = new Dictionary<Room, List<Thing>>();
                _roomGroupCache[mapId] = roomCache;
            }
            else
            {
                roomCache.Clear();
            }
            
            // Group things by room
            foreach (Thing thing in map.listerThings.AllThings)
            {
                // Skip things that can't be hauled/processed
                if (thing.def.category != ThingCategory.Item && !(thing is Building))
                    continue;
                    
                Room room = thing.GetRoom();
                if (room == null)
                    continue;
                    
                if (!roomCache.TryGetValue(room, out var roomThings))
                {
                    roomThings = new List<Thing>();
                    roomCache[room] = roomThings;
                }
                
                roomThings.Add(thing);
            }
            
            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Updated room groups for map {mapId} ({roomCache.Count} rooms)");
            }
        }
        
        #endregion
        
        #region Reusable Validation Filters
        
        /// <summary>
        /// Generic filter delegate for things
        /// </summary>
        public delegate bool ThingFilter<T>(T thing, Pawn pawn = null) where T : Thing;
        
        /// <summary>
        /// Common filters for items
        /// </summary>
        public static class ItemFilters
        {
            /// <summary>
            /// Checks if an item is valid for hauling
            /// </summary>
            public static bool IsValidForHauling(Thing thing, Pawn pawn = null)
            {
                if (thing == null || thing.Destroyed || !thing.Spawned ||
                    thing.IsForbidden(Faction.OfPlayer))
                    return false;
                    
                // Must be an item
                if (thing.def.category != ThingCategory.Item)
                    return false;
                    
                // Check if the pawn can reserve it
                if (pawn != null && !pawn.CanReserve(thing))
                    return false;
                    
                return true;
            }
            
            /// <summary>
            /// Checks if an item needs to be hauled to storage
            /// </summary>
            public static bool NeedsHauling(Thing thing, Pawn pawn = null)
            {
                if (!IsValidForHauling(thing, pawn))
                    return false;
                    
                // Skip things already reserved
                if (pawn != null && thing.Map.reservationManager.IsReservedByAnyoneOf(thing, pawn.Faction))
                    return false;
                    
                // Check if it's already in storage
                SlotGroup slotGroup = thing.GetSlotGroup();
                if (slotGroup != null && slotGroup.parent != null && !slotGroup.parent.Accepts(thing))
                    return true;
                    
                return !thing.IsInValidStorage();
            }
            
            /// <summary>
            /// Checks if an item is a valid ingredient for bills
            /// </summary>
            public static bool IsValidIngredient(Thing thing, Pawn pawn = null)
            {
                if (!IsValidForHauling(thing, pawn))
                    return false;
                    
                // Most items can be ingredients
                return true;
            }
            
            /// <summary>
            /// Checks if an item is valid for crafting
            /// </summary>
            public static bool IsValidForCrafting(Thing thing, Pawn pawn = null)
            {
                if (!IsValidForHauling(thing, pawn))
                    return false;
                    
                // Check if the item is a crafting material
                return thing.def.IsStuff || thing.def.IsMedicine;
            }
        }
        
        /// <summary>
        /// Common filters for buildings
        /// </summary>
        public static class BuildingFilters
        {
            /// <summary>
            /// Checks if a building is valid for refueling
            /// </summary>
            public static bool NeedsRefueling(Thing thing, Pawn pawn = null)
            {
                if (thing == null || thing.Destroyed || !thing.Spawned)
                    return false;
                    
                // Must be a building
                if (!(thing is Building))
                    return false;
                    
                // Must have a refuelable comp
                CompRefuelable refuelable = thing.TryGetComp<CompRefuelable>();
                if (refuelable == null || !refuelable.ShouldAutoRefuelNow)
                    return false;
                    
                // Check if the pawn can refuel it
                if (pawn != null && !RefuelWorkGiverUtility.CanRefuel(pawn, thing, false))
                    return false;
                    
                return true;
            }
            
            /// <summary>
            /// Checks if a building is valid for repairing
            /// </summary>
            public static bool NeedsRepairing(Thing thing, Pawn pawn = null)
            {
                if (thing == null || thing.Destroyed || !thing.Spawned)
                    return false;
                    
                // Must be a building
                if (!(thing is Building))
                    return false;
                    
                // Must be damaged
                if (thing.HitPoints >= thing.MaxHitPoints)
                    return false;
                    
                // Check if the pawn can repair it
                if (pawn != null && !pawn.CanReserve(thing))
                    return false;
                    
                return true;
            }
            
            /// <summary>
            /// Checks if a building is a valid workbench with bills
            /// </summary>
            public static bool HasActiveBills(Thing thing, Pawn pawn = null)
            {
                if (thing == null || thing.Destroyed || !thing.Spawned)
                    return false;
                    
                // Must be a bill giver
                if (!(thing is IBillGiver billGiver))
                    return false;
                    
                // Check for bills
                if (billGiver.BillStack == null || billGiver.BillStack.Count == 0)
                    return false;
                    
                // Check for valid bills
                if (!billGiver.BillStack.Bills.Any(bill => bill.ShouldDoNow()))
                    return false;
                    
                // Check if the pawn can use it
                if (pawn != null && !pawn.CanReserve(thing))
                    return false;
                    
                return true;
            }
        }
        
        /// <summary>
        /// Common filters for pawns
        /// </summary>
        public static class PawnFilters
        {
            /// <summary>
            /// Checks if a pawn needs rescuing
            /// </summary>
            public static bool NeedsRescuing(Pawn pawn, Pawn rescuer = null)
            {
                if (pawn == null || pawn.Destroyed || !pawn.Spawned || pawn.Dead)
                    return false;
                    
                // Must be incapacitated
                if (!pawn.Downed)
                    return false;
                    
                // Skip pawns already being rescued
                if (pawn.health.hediffSet.HasHediff(HediffDefOf.BloodLoss))
                    return true;
                    
                // Check if the rescuer can rescue
                if (rescuer != null && !rescuer.CanReserve(pawn))
                    return false;
                    
                return true;
            }
            
            /// <summary>
            /// Checks if a pawn needs medical attention
            /// </summary>
            public static bool NeedsMedicalAttention(Pawn pawn, Pawn doctor = null)
            {
                if (pawn == null || pawn.Destroyed || !pawn.Spawned || pawn.Dead)
                    return false;

                // Check if the pawn needs treatment
                if (!HealthAIUtility.ShouldSeekMedicalRest(pawn) && !pawn.health.HasHediffsNeedingTend())
                    return false;
                    
                // Check if the doctor can treat the pawn
                if (doctor != null && !doctor.CanReserve(pawn))
                    return false;
                    
                return true;
            }
            
            /// <summary>
            /// Checks if a pawn is a valid target for warden activities
            /// </summary>
            public static bool IsValidPrisonerForWarden(Pawn prisoner, Pawn warden = null)
            {
                if (prisoner == null || prisoner.Destroyed || !prisoner.Spawned)
                    return false;
                    
                // Must be a prisoner
                if (!prisoner.IsPrisonerOfColony)
                    return false;
                    
                // Skip prisoners in mental states
                if (prisoner.InAggroMentalState)
                    return false;
                    
                // Check if the warden can interact with the prisoner
                if (warden != null && !warden.CanReserve(prisoner))
                    return false;
                    
                return true;
            }
        }
        
        /// <summary>
        /// Combines multiple filters with AND logic (all must pass)
        /// </summary>
        public static ThingFilter<T> And<T>(params ThingFilter<T>[] filters) where T : Thing
        {
            return (thing, pawn) =>
            {
                foreach (var filter in filters)
                {
                    if (!filter(thing, pawn))
                        return false;
                }
                return true;
            };
        }
        
        /// <summary>
        /// Combines multiple filters with OR logic (any must pass)
        /// </summary>
        public static ThingFilter<T> Or<T>(params ThingFilter<T>[] filters) where T : Thing
        {
            return (thing, pawn) =>
            {
                foreach (var filter in filters)
                {
                    if (filter(thing, pawn))
                        return true;
                }
                return false;
            };
        }
        
        /// <summary>
        /// Inverts a filter
        /// </summary>
        public static ThingFilter<T> Not<T>(ThingFilter<T> filter) where T : Thing
        {
            return (thing, pawn) => !filter(thing, pawn);
        }
        
        #endregion
        
        #region Integrated Pre-filtering
        
        /// <summary>
        /// Gets pre-filtered targets based on multiple criteria
        /// </summary>
        public static List<T> GetFilteredTargets<T>(
            Map map,
            Pawn pawn,
            ThingFilter<T> filter,
            IntVec3? position = null,
            float? radius = null,
            Zone zone = null,
            Room room = null) where T : Thing
        {
            // Start with an appropriate subset based on provided constraints
            List<T> initialTargets;
            
            if (position.HasValue && radius.HasValue)
            {
                // Use spatial index for position+radius
                initialTargets = GetNearbyThings<T>(map, position.Value, radius.Value);
            }
            else if (zone != null)
            {
                // Use zone grouping
                initialTargets = GetThingsInZone<T>(map, zone);
            }
            else if (room != null)
            {
                // Use room grouping
                initialTargets = GetThingsInRoom<T>(map, room);
            }
            else if (position.HasValue)
            {
                // Use room containing the position
                initialTargets = GetThingsInSameRoom<T>(map, position.Value);
                
                // If no room (outside), fall back to spatial index with default radius
                if (initialTargets.Count == 0)
                {
                    initialTargets = GetNearbyThings<T>(map, position.Value, 30f);
                }
            }
            else
            {
                // No constraints provided, use all things of type T
                initialTargets = GetThingsByType<T>(map);
            }
            
            // Apply the filter
            if (filter != null)
            {
                return initialTargets
                    .Where(thing => filter(thing, pawn))
                    .ToList();
            }
            
            return initialTargets;
        }
        
        /// <summary>
        /// Gets the best target from filtered results based on distance
        /// </summary>
        public static T GetBestTarget<T>(
            List<T> targets,
            Pawn pawn,
            IntVec3? referencePos = null) where T : Thing
        {
            if (targets == null || targets.Count == 0 || pawn == null)
                return null;
                
            // Use provided reference position or pawn's position
            IntVec3 refPos = referencePos ?? pawn.Position;
            
            // Find closest valid target
            return targets
                .OrderBy(t => (t.Position - refPos).LengthHorizontalSquared)
                .FirstOrDefault();
        }

        #endregion

        #region Memory/Cache Management

        // Add this new method to the Utility_TargetPrefilteringManager class in the Memory/Cache Management region
        /// <summary>
        /// Cleans up invalid targets for a specific map
        /// This removes references to despawned or destroyed things from the cache
        /// </summary>
        public static void CleanupInvalidTargetsForMap(Map map)
        {
            if (map == null)
                return;

            int mapId = map.uniqueID;
            bool cleanedEntries = false;

            // Clean spatial indices
            if (_spatialIndices.TryGetValue(mapId, out var spatialIndices))
            {
                foreach (var pair in spatialIndices)
                {
                    // Nothing to do - spatial indices are rebuilt periodically already
                    // Just reset the update tick so it rebuilds on next access
                    if (_spatialIndexUpdateTicks.TryGetValue(mapId, out var updateTicks))
                    {
                        foreach (var typePair in updateTicks)
                        {
                            updateTicks[typePair.Key] = 0; // Force update on next access
                        }
                        cleanedEntries = true;
                    }
                }
            }

            // Clean thing type caches
            if (_thingsByTypeCaches.TryGetValue(mapId, out var typeCache))
            {
                foreach (var typePair in typeCache.ToList())
                {
                    var thingsToKeep = typePair.Value
                        .Where(t => t != null && !t.Destroyed && t.Spawned)
                        .ToList();

                    if (thingsToKeep.Count < typePair.Value.Count)
                    {
                        typeCache[typePair.Key] = thingsToKeep;
                        cleanedEntries = true;
                    }
                }

                if (_thingsByTypeUpdateTicks.ContainsKey(mapId))
                {
                    _thingsByTypeUpdateTicks[mapId] = 0; // Force update on next access
                }
            }

            // Clean zone groups
            if (_zoneGroupCache.TryGetValue(mapId, out var zoneCache))
            {
                foreach (var zonePair in zoneCache.ToList())
                {
                    var thingsToKeep = zonePair.Value
                        .Where(t => t != null && !t.Destroyed && t.Spawned)
                        .ToList();

                    if (thingsToKeep.Count < zonePair.Value.Count)
                    {
                        zoneCache[zonePair.Key] = thingsToKeep;
                        cleanedEntries = true;
                    }
                }

                if (_zoneUpdateTicks.ContainsKey(mapId))
                {
                    _zoneUpdateTicks[mapId] = 0; // Force update on next access
                }
            }

            // Clean room groups
            if (_roomGroupCache.TryGetValue(mapId, out var roomCache))
            {
                foreach (var roomPair in roomCache.ToList())
                {
                    var thingsToKeep = roomPair.Value
                        .Where(t => t != null && !t.Destroyed && t.Spawned)
                        .ToList();

                    if (thingsToKeep.Count < roomPair.Value.Count)
                    {
                        roomCache[roomPair.Key] = thingsToKeep;
                        cleanedEntries = true;
                    }
                }

                if (_roomUpdateTicks.ContainsKey(mapId))
                {
                    _roomUpdateTicks[mapId] = 0; // Force update on next access
                }
            }

            if (cleanedEntries && Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Cleaned up invalid cache entries for map {mapId}");
            }
        }

        /// <summary>
        /// Cleans up data for a specific map
        /// </summary>
        public static void CleanupMapData(int mapId)
        {
            // Clean spatial indices
            if (_spatialIndices.ContainsKey(mapId))
                _spatialIndices.Remove(mapId);
                
            if (_spatialIndexUpdateTicks.ContainsKey(mapId))
                _spatialIndexUpdateTicks.Remove(mapId);
                
            // Clean zone groups
            if (_zoneGroupCache.ContainsKey(mapId))
                _zoneGroupCache.Remove(mapId);
                
            if (_zoneUpdateTicks.ContainsKey(mapId))
                _zoneUpdateTicks.Remove(mapId);
                
            // Clean room groups
            if (_roomGroupCache.ContainsKey(mapId))
                _roomGroupCache.Remove(mapId);
                
            if (_roomUpdateTicks.ContainsKey(mapId))
                _roomUpdateTicks.Remove(mapId);
        }
        
        /// <summary>
        /// Resets all caches
        /// </summary>
        public static void ResetAllCaches()
        {
            _spatialIndices.Clear();
            _spatialIndexUpdateTicks.Clear();
            _zoneGroupCache.Clear();
            _zoneUpdateTicks.Clear();
            _roomGroupCache.Clear();
            _roomUpdateTicks.Clear();
        }

        #endregion

        #region QuadTree Implementation

        /// <summary>
        /// QuadTree implementation for more efficient spatial queries
        /// </summary>
        public class QuadTree
        {
            private class QuadNode
            {
                public RectInt bounds;
                public List<Thing> things = new List<Thing>();
                public QuadNode[] children = null;
                public const int MAX_THINGS = 16;
                public const int MAX_DEPTH = 8;

                public QuadNode(RectInt bounds)
                {
                    this.bounds = bounds;
                }

                public bool Insert(Thing thing, int depth)
                {
                    // Reject things outside bounds
                    if (!bounds.Contains(new IntVec2(thing.Position.x, thing.Position.z)))
                        return false;

                    // If we're not at leaf and have children, insert into children
                    if (children != null)
                    {
                        bool inserted = false;
                        for (int i = 0; i < 4; i++)
                        {
                            if (children[i].Insert(thing, depth + 1))
                            {
                                inserted = true;
                                break;
                            }
                        }

                        // If couldn't insert into any child (at edge cases), insert here
                        if (!inserted)
                        {
                            things.Add(thing);
                        }
                        return true;
                    }

                    // Add to this node if we're at max depth or have space
                    if (depth >= MAX_DEPTH || things.Count < MAX_THINGS)
                    {
                        things.Add(thing);
                        return true;
                    }

                    // Otherwise split and redistribute
                    Split();

                    // Move existing things into children
                    var existingThings = things.ToList();
                    things.Clear();

                    foreach (var existingThing in existingThings)
                    {
                        bool inserted = false;
                        for (int i = 0; i < 4; i++)
                        {
                            if (children[i].Insert(existingThing, depth + 1))
                            {
                                inserted = true;
                                break;
                            }
                        }

                        // If couldn't insert into any child, keep in this node
                        if (!inserted)
                        {
                            things.Add(existingThing);
                        }
                    }

                    // Try to insert the new thing
                    return Insert(thing, depth);
                }

                private void Split()
                {
                    int subWidth = bounds.Width / 2;
                    int subHeight = bounds.Height / 2;
                    int x = bounds.minX;
                    int y = bounds.minZ;

                    children = new QuadNode[4];

                    // top left
                    children[0] = new QuadNode(new RectInt(x, y, subWidth, subHeight));
                    // top right
                    children[1] = new QuadNode(new RectInt(x + subWidth, y, subWidth, subHeight));
                    // bottom left
                    children[2] = new QuadNode(new RectInt(x, y + subHeight, subWidth, subHeight));
                    // bottom right
                    children[3] = new QuadNode(new RectInt(x + subWidth, y + subHeight, subWidth, subHeight));
                }

                public void Query(IntVec3 center, float radius, List<Thing> results)
                {
                    // Skip if bounds don't intersect query circle
                    if (!IntersectsCircle(bounds, center.x, center.z, radius))
                        return;

                    // Add things from this node
                    float radiusSq = radius * radius;
                    foreach (var thing in things)
                    {
                        if (thing == null || thing.Destroyed || !thing.Spawned)
                            continue;

                        // Check actual distance
                        if ((thing.Position - center).LengthHorizontalSquared <= radiusSq)
                        {
                            results.Add(thing);
                        }
                    }

                    // Query children if they exist
                    if (children != null)
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            children[i].Query(center, radius, results);
                        }
                    }
                }

                private bool IntersectsCircle(RectInt rect, int circleX, int circleZ, float radius)
                {
                    // Find closest point on rectangle to circle center
                    float closestX = Math.Max(rect.minX, Math.Min(circleX, rect.maxX));
                    float closestZ = Math.Max(rect.minZ, Math.Min(circleZ, rect.maxZ));

                    // Calculate distance from closest point to circle center
                    float distanceX = closestX - circleX;
                    float distanceZ = closestZ - circleZ;

                    // Return true if distance is less than radius
                    return (distanceX * distanceX + distanceZ * distanceZ) <= (radius * radius);
                }

                public void Clear()
                {
                    things.Clear();
                    if (children != null)
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            children[i].Clear();
                        }
                        children = null;
                    }
                }
            }

            private QuadNode root;
            private readonly int _maxItems;

            /// <summary>
            /// Creates a new quad tree for a map
            /// </summary>
            public QuadTree(Map map, int maxItems = 10000)
            {
                // Create a root node with the map bounds
                root = new QuadNode(new RectInt(0, 0, map.Size.x, map.Size.z));
                _maxItems = maxItems;
            }

            /// <summary>
            /// Add a thing to the quad tree
            /// </summary>
            public void Add(Thing thing)
            {
                if (thing == null || !thing.Spawned)
                    return;

                root.Insert(thing, 0);
            }

            /// <summary>
            /// Add multiple things to the quad tree
            /// </summary>
            public void AddRange<T>(IEnumerable<T> things) where T : Thing
            {
                if (things == null)
                    return;

                int count = 0;
                foreach (var thing in things)
                {
                    Add(thing);
                    if (++count >= _maxItems)
                        break;
                }
            }

            /// <summary>
            /// Query for things within radius of a point
            /// </summary>
            public List<Thing> QueryRange(IntVec3 center, float radius)
            {
                List<Thing> results = new List<Thing>();
                root.Query(center, radius, results);
                return results;
            }

            /// <summary>
            /// Clear the quad tree
            /// </summary>
            public void Clear()
            {
                root.Clear();
            }
        }

        /// <summary>
        /// Simple rectangle structure for quad tree
        /// </summary>
        public struct RectInt
        {
            public int minX;
            public int minZ;
            public int Width;
            public int Height;

            public int maxX => minX + Width;
            public int maxZ => minZ + Height;

            public RectInt(int minX, int minZ, int width, int height)
            {
                this.minX = minX;
                this.minZ = minZ;
                this.Width = width;
                this.Height = height;
            }

            public bool Contains(IntVec2 point)
            {
                return point.x >= minX && point.x < maxX &&
                       point.z >= minZ && point.z < maxZ;
            }
        }

        public struct IntVec2
        {
            public int x;
            public int z;

            public IntVec2(int x, int z)
            {
                this.x = x;
                this.z = z;
            }
        }

        #endregion

        #region Performance Metrics

        private static readonly Dictionary<string, long> _operationTimes = new Dictionary<string, long>();
        private static readonly Dictionary<string, int> _operationCounts = new Dictionary<string, int>();

        /// <summary>
        /// Records the time taken for an operation
        /// </summary>
        public static void RecordOperationTime(string operation, long ticks)
        {
            if (!Prefs.DevMode)
                return;

            if (!_operationTimes.ContainsKey(operation))
            {
                _operationTimes[operation] = 0;
                _operationCounts[operation] = 0;
            }

            _operationTimes[operation] += ticks;
            _operationCounts[operation]++;
        }

        /// <summary>
        /// Gets performance metrics for logging or debugging
        /// </summary>
        public static Dictionary<string, string> GetPerformanceMetrics()
        {
            var results = new Dictionary<string, string>();

            foreach (var kvp in _operationTimes)
            {
                string operation = kvp.Key;
                long totalTicks = kvp.Value;
                int count = _operationCounts[operation];

                if (count == 0)
                    continue;

                double avgMs = (double)totalTicks / count / TimeSpan.TicksPerMillisecond;
                results[operation] = $"Count: {count}, Avg: {avgMs:F3}ms";
            }

            return results;
        }

        /// <summary>
        /// Reset performance metrics
        /// </summary>
        public static void ResetPerformanceMetrics()
        {
            _operationTimes.Clear();
            _operationCounts.Clear();
        }

        #endregion

        #region Job-Specific Query Methods

        /// <summary>
        /// Gets the closest refuelable building that needs fuel
        /// </summary>
        public static Building GetClosestRefuelableBuilding(Map map, IntVec3 position, Pawn worker)
        {
            // Get buildings needing fuel near the position
            var buildings = GetNearbyThings<Building>(map, position, 30f);

            // Filter and sort by distance
            return buildings
                .Where(b => BuildingFilters.NeedsRefueling(b, worker))
                .OrderBy(b => (b.Position - position).LengthHorizontalSquared)
                .FirstOrDefault();
        }

        /// <summary>
        /// Gets the closest item that needs hauling within the specified radius
        /// </summary>
        public static Thing GetClosestHaulableItem(Map map, IntVec3 position, Pawn hauler, float radius = 25f)
        {
            // Use our optimized spatial query
            var items = GetNearbyThings<Thing>(map, position, radius);

            // Filter and sort by distance
            return items
                .Where(t => ItemFilters.NeedsHauling(t, hauler))
                .OrderBy(t => (t.Position - position).LengthHorizontalSquared)
                .FirstOrDefault();
        }

        /// <summary>
        /// Gets items needed for active bills on workbenches in room or nearby
        /// </summary>
        public static List<Thing> GetItemsForActiveBills(Map map, IntVec3 position, Pawn worker, float radius = 40f)
        {
            // First get all workbenches with active bills
            var workbenches = GetNearbyThings<Building>(map, position, radius)
                .Where(b => BuildingFilters.HasActiveBills(b, worker))
                .ToList();

            if (workbenches.Count == 0)
                return new List<Thing>();

            // Now find ingredients for those bills
            var items = GetNearbyThings<Thing>(map, position, radius)
                .Where(t => ItemFilters.IsValidIngredient(t, worker))
                .ToList();

            return items;
        }

        #endregion

        #region Debug Visualization

        /// <summary>
        /// Draws debug visualization of the spatial partitioning
        /// </summary>
        public static void DrawSpatialDebug<T>(Map map) where T : Thing
        {
            if (!Prefs.DevMode)
                return;

            // Get the spatial index
            var spatialIndex = GetSpatialIndex<T>(map);

            // Draw grid cells or quad tree nodes
            // (Implementation depends on whether you want to visualize quad tree or grid cells)
        }

        #endregion
    }
}