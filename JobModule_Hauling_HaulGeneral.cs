using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for general hauling tasks - lowest priority, handles anything else haulable
    /// </summary>
    public class JobModule_Hauling_HaulGeneral : JobModule_Hauling
    {
        public override string UniqueID => "HaulGeneral";
        public override float Priority => 1.0f; // Lowest priority - do general hauling last
        public override string Category => "GeneralHauling"; // Added category for consistency
        public override int CacheUpdateInterval => 240; // Update every 4 seconds (less frequent for performance)

        private const int MAX_ITEMS_TO_CACHE = 1000; // Limit cache size for performance

        // Static caches for map-specific data persistence
        private static readonly Dictionary<int, List<Thing>> _haulableItemsCache = new Dictionary<int, List<Thing>>();
        private static readonly Dictionary<int, Dictionary<Thing, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Thing, bool>>();
        private static int _lastCacheUpdateTick = -999;        
        //private static int _lastSectorProcessed = 0;

        // Tracking for which cells we've already processed
        private static readonly HashSet<int> _processedCellIndices = new HashSet<int>();

        // Designated items are checked more frequently
        private static int _lastDesignationUpdateTick = -999;
        private const int DESIGNATION_UPDATE_INTERVAL = 60; // Check designations more often (1 second)

        // These ThingRequestGroups are what this module cares about
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups =>
            new HashSet<ThingRequestGroup> { ThingRequestGroup.HaulableEver };

        public override void UpdateCache(Map map, List<Thing> targetCache)
        {
            base.UpdateCache(map, targetCache);

            if (map == null) return;
            int mapId = map.uniqueID;
            bool hasTargets = false;

            int currentTick = Find.TickManager.TicksGame;

            // Initialize caches if needed
            if (!_haulableItemsCache.ContainsKey(mapId))
                _haulableItemsCache[mapId] = new List<Thing>();

            if (!_reachabilityCache.ContainsKey(mapId))
                _reachabilityCache[mapId] = new Dictionary<Thing, bool>();

            // Check haul designations more frequently - these are explicitly marked by players
            if (currentTick > _lastDesignationUpdateTick + DESIGNATION_UPDATE_INTERVAL)
            {
                // Process designated items (player has explicitly marked these for hauling)
                UpdateDesignatedItems(map, targetCache, mapId);
                _lastDesignationUpdateTick = currentTick;
            }

            // For auto-hauling (lower priority), use progressive sector-based updates
            UpdateCacheProgressively(
                map,
                targetCache,
                ref _lastCacheUpdateTick,
                RelevantThingRequestGroups,
                thing => {
                    // Skip if already in cache or over max count
                    if (_haulableItemsCache[mapId].Contains(thing) || _haulableItemsCache[mapId].Count >= MAX_ITEMS_TO_CACHE)
                        return false;

                    // Make sure it's a valid hauling candidate
                    if (thing.def.alwaysHaulable && !thing.IsInValidStorage() && IsAutomaticallyHaulable(thing))
                    {
                        // Check if there's a valid storage destination
                        if (StoreUtility.TryFindBestBetterStorageFor(thing, null, map,
                            StoreUtility.CurrentStoragePriorityOf(thing), null, out _, out _))
                        {
                            return true;
                        }
                    }
                    return false;
                },
                _haulableItemsCache,
                CacheUpdateInterval
            );

            SetHasTargets(map, hasTargets || (targetCache != null && targetCache.Count > 0));
        }

        /// <summary>
        /// Updates cache with designated items (higher priority update)
        /// </summary>
        private void UpdateDesignatedItems(Map map, List<Thing> targetCache, int mapId)
        {
            // Skip if no haul designations
            if (!map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.Haul))
                return;

            // Clear designated items from cache to refresh them
            _haulableItemsCache[mapId].RemoveAll(t =>
                t.Spawned && t.Map == map &&
                map.designationManager.DesignationOn(t, DesignationDefOf.Haul) != null);

            int count = _haulableItemsCache[mapId].Count;

            // Process all haul designations
            foreach (Designation designation in map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Haul))
            {
                if (count >= MAX_ITEMS_TO_CACHE)
                    break;

                Thing thing = designation.target.Thing;
                if (thing != null && !thing.Destroyed && thing.Spawned && IsAutomaticallyHaulable(thing))
                {
                    // Add to both caches
                    _haulableItemsCache[mapId].Add(thing);
                    targetCache.Add(thing);
                    count++;
                }
            }
        }

        /// <summary>
        /// Helper method to check if a thing can be automatically hauled without a specific pawn
        /// </summary>
        private bool IsAutomaticallyHaulable(Thing thing)
        {
            if (thing == null) return false;

            try
            {
                // Basic checks that don't require a specific pawn
                return thing.def.alwaysHaulable &&
                       thing.stackCount > 0 &&
                       thing.def.EverHaulable &&
                       thing.GetInnerIfMinified() == thing;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error checking if item is haulable: {ex.Message}");
                return false;
            }
        }

        public override bool ShouldHaulItem(Thing item, Map map)
        {
            if (item == null || map == null || !item.Spawned) return false;

            int mapId = map.uniqueID;

            // Quick check from cache first
            if (_haulableItemsCache.ContainsKey(mapId) && _haulableItemsCache[mapId].Contains(item))
                return true;

            try
            {
                // Otherwise do a full check
                return (item.Map.designationManager.DesignationOn(item, DesignationDefOf.Haul) != null ||
                       (item.def.alwaysHaulable && !item.IsInValidStorage())) &&
                       IsAutomaticallyHaulable(item);
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in ShouldHaulItem: {ex.Message}");
                return false;
            }
        }

        public override bool ValidateHaulingJob(Thing thing, Pawn hauler)
        {
            if (thing == null || hauler == null || !thing.Spawned || !hauler.Spawned)
                return false;

            try
            {
                // Check faction interaction
                if (!Utility_JobGiverManager.IsValidFactionInteraction(thing, hauler, requiresDesignator: false))
                    return false;

                if (thing.IsForbidden(hauler) ||
                    !hauler.CanReserve(thing, 1, -1) ||
                    !hauler.CanReach(thing, PathEndMode.Touch, hauler.NormalMaxDanger()))
                    return false;

                if (!HaulAIUtility.PawnCanAutomaticallyHaul(hauler, thing, false))
                    return false;

                // Check for valid storage location
                IntVec3 storeCell;
                IHaulDestination haulDestination;
                return StoreUtility.TryFindBestBetterStorageFor(thing, hauler, hauler.Map,
                    StoreUtility.CurrentStoragePriorityOf(thing), hauler.Faction, out storeCell, out haulDestination);
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error validating haul job: {ex.Message}");
                return false;
            }
        }

        protected override Job CreateHaulingJob(Pawn hauler, Thing thing)
        {
            if (thing == null || hauler == null || !thing.Spawned || !hauler.Spawned)
                return null;

            try
            {
                IntVec3 storeCell;
                IHaulDestination haulDestination;
                if (StoreUtility.TryFindBestBetterStorageFor(thing, hauler, hauler.Map,
                    StoreUtility.CurrentStoragePriorityOf(thing), hauler.Faction, out storeCell, out haulDestination))
                {
                    Job job = HaulAIUtility.HaulToCellStorageJob(hauler, thing, storeCell, false);

                    if (job != null)
                    {
                        // Remove haul designation if it exists
                        hauler.Map.designationManager.RemoveAllDesignationsOn(thing, false);

                        Utility_DebugManager.LogNormal($"{hauler.LabelShort} created job to haul {thing.LabelCap} to storage");
                    }

                    return job;
                }
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating haul job: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public override void ResetStaticData()
        {
            Utility_CacheManager.ResetJobGiverCache(_haulableItemsCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
            _lastDesignationUpdateTick = -999;
            //_lastSectorProcessed = 0;
            _processedCellIndices.Clear();
        }
    }
}