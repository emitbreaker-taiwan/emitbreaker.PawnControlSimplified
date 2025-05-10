using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for unloading the inventory of carriers like pack animals or shuttles
    /// </summary>
    public class JobModule_Hauling_UnloadCarriers : JobModule_Hauling
    {
        public override string UniqueID => "UnloadCarriers";
        public override float Priority => 5.8f; // Same as original JobGiver
        public override string Category => "Logistics";
        public override int CacheUpdateInterval => 120; // Update every 2 seconds

        // Static caches for map-specific data persistence
        private static readonly Dictionary<int, List<Pawn>> _unloadablePawnsCache = new Dictionary<int, List<Pawn>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _inventoryStatusCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static int _lastUpdateCacheTick = -999;

        // These ThingRequestGroups are what this module cares about
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups =>
            new HashSet<ThingRequestGroup> { ThingRequestGroup.Pawn };

        public override void UpdateCache(Map map, List<Thing> targetCache)
        {
            base.UpdateCache(map, targetCache);

            if (map == null) return;
            int mapId = map.uniqueID;
            bool hasTargets = false;

            int currentTick = Find.TickManager.TicksGame;

            // Initialize caches if needed
            if (!_unloadablePawnsCache.ContainsKey(mapId))
                _unloadablePawnsCache[mapId] = new List<Pawn>();

            if (!_reachabilityCache.ContainsKey(mapId))
                _reachabilityCache[mapId] = new Dictionary<Pawn, bool>();

            if (!_inventoryStatusCache.ContainsKey(mapId))
                _inventoryStatusCache[mapId] = new Dictionary<Pawn, bool>();

            // Only do a full update if needed
            if (currentTick > _lastUpdateCacheTick + CacheUpdateInterval ||
                !_unloadablePawnsCache.ContainsKey(mapId))
            {
                try
                {
                    // Clear existing caches for this map
                    _unloadablePawnsCache[mapId].Clear();
                    _reachabilityCache[mapId].Clear();
                    _inventoryStatusCache[mapId].Clear();

                    // Use the map's existing list for better performance
                    List<Pawn> unloadablePawns = map.mapPawns.SpawnedPawnsWhoShouldHaveInventoryUnloaded;
                    if (unloadablePawns != null && unloadablePawns.Count > 0)
                    {
                        foreach (Pawn carrier in unloadablePawns)
                        {
                            if (carrier != null && carrier.Spawned && 
                                carrier.inventory != null && 
                                carrier.inventory.UnloadEverything && 
                                carrier.inventory.innerContainer.Count > 0)
                            {
                                _unloadablePawnsCache[mapId].Add(carrier);
                                targetCache.Add(carrier);
                            }
                        }
                    }

                    if (_unloadablePawnsCache[mapId].Count > 0)
                    {
                        Utility_DebugManager.LogNormal(
                            $"Found {_unloadablePawnsCache[mapId].Count} pawns with inventory that needs unloading on map {map.uniqueID}");
                    }

                    _lastUpdateCacheTick = currentTick;
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"Error updating unloadable pawns cache: {ex}");
                }
            }
            else
            {
                // Just add the cached pawns to the target cache
                foreach (Pawn carrier in _unloadablePawnsCache[mapId])
                {
                    // Skip carriers that are no longer valid for unloading
                    if (!carrier.Spawned || !carrier.inventory.UnloadEverything || carrier.inventory.innerContainer.Count == 0)
                        continue;

                    targetCache.Add(carrier);
                }
            }

            SetHasTargets(map, hasTargets || (targetCache != null && targetCache.Count > 0));
        }

        public override bool ShouldHaulItem(Thing thing, Map map)
        {
            try
            {
                // We're looking for pawns, not items
                if (!(thing is Pawn carrier)) return false;
                if (carrier == null || map == null || !carrier.Spawned) return false;

                int mapId = map.uniqueID;

                // Check if it's in our cache first
                if (_unloadablePawnsCache.ContainsKey(mapId) && _unloadablePawnsCache[mapId].Contains(carrier))
                    return true;

                // Otherwise, check inventory status
                if (_inventoryStatusCache.ContainsKey(mapId) && 
                    _inventoryStatusCache[mapId].TryGetValue(carrier, out bool needsUnloading))
                {
                    return needsUnloading;
                }

                // If not in cache, do direct check
                bool shouldUnload = carrier.inventory != null && 
                                  carrier.inventory.UnloadEverything && 
                                  carrier.inventory.innerContainer.Count > 0;

                // Cache the result
                if (_inventoryStatusCache.ContainsKey(mapId))
                {
                    _inventoryStatusCache[mapId][carrier] = shouldUnload;
                }

                return shouldUnload;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in ShouldHaulItem for unloading carriers: {ex}");
                return false;
            }
        }

        public override bool ValidateHaulingJob(Thing thing, Pawn hauler)
        {
            try
            {
                if (!(thing is Pawn carrier)) return false;
                if (carrier == null || hauler == null || !carrier.Spawned || !hauler.Spawned)
                    return false;

                // Check faction interaction
                if (!Utility_JobGiverManager.IsValidFactionInteraction(carrier, hauler, requiresDesignator: false))
                    return false;

                int mapId = hauler.Map.uniqueID;

                // Skip carriers that no longer need unloading
                if (!carrier.inventory.UnloadEverything || carrier.inventory.innerContainer.Count == 0)
                    return false;

                // Skip if forbidden or unreachable
                if (carrier.IsForbidden(hauler) ||
                    !hauler.CanReserve(carrier) ||
                    !hauler.CanReach(carrier, PathEndMode.Touch, hauler.NormalMaxDanger()))
                    return false;

                // Just call the utility function directly
                return UnloadCarriersJobGiverUtility.HasJobOnThing(hauler, carrier, false);
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error validating unload carrier job: {ex}");
                return false;
            }
        }

        protected override Job CreateHaulingJob(Pawn hauler, Thing thing)
        {
            try
            {
                if (!(thing is Pawn carrier)) return null;
                
                // Create the unload job
                Job job = JobMaker.MakeJob(JobDefOf.UnloadInventory, carrier);
                Utility_DebugManager.LogNormal($"{hauler.LabelShort} created job to unload inventory of {carrier.LabelCap}");
                return job;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating unload carrier job: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public override void ResetStaticData()
        {
            Utility_CacheManager.ResetJobGiverCache(_unloadablePawnsCache, _reachabilityCache);

            foreach (var inventoryMap in _inventoryStatusCache.Values)
            {
                inventoryMap.Clear();
            }
            _inventoryStatusCache.Clear();

            _lastUpdateCacheTick = -999;
        }
    }
}