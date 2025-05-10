using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for hauling corpses to graves and other storage
    /// </summary>
    public class JobModule_Hauling_HaulCorpses : JobModule_Hauling
    {
        public override string UniqueID => "HaulCorpses";
        public override float Priority => 9.0f; // High priority - corpses are important
        public override string Category => "SanitationHauling"; // Added category for consistency
        public override int CacheUpdateInterval => 120; // Update every 2 seconds

        private const int MAX_CORPSES = 100; // Limit for performance

        // Static caches for map-specific data persistence
        private static readonly Dictionary<int, List<Thing>> _corpseCache = new Dictionary<int, List<Thing>>();
        private static readonly Dictionary<int, Dictionary<Thing, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Thing, bool>>();
        private static int _lastCacheUpdateTick = -999;

        // Separate update ticks for human vs animal corpses
        private static int _lastHumanCorpseUpdateTick = -999;
        private static int _lastAnimalCorpseUpdateTick = -999;

        // We check human corpses more frequently than animal corpses
        private const int HUMAN_CORPSE_UPDATE_INTERVAL = 90;  // Every 1.5 seconds
        private const int ANIMAL_CORPSE_UPDATE_INTERVAL = 180; // Every 3 seconds

        // These ThingRequestGroups are what this module cares about
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups =>
            new HashSet<ThingRequestGroup> { ThingRequestGroup.Corpse };

        public override void UpdateCache(Map map, List<Thing> targetCache)
        {
            base.UpdateCache(map, targetCache);

            if (map == null) return;
            int mapId = map.uniqueID;
            bool hasTargets = false;

            int currentTick = Find.TickManager.TicksGame;

            // Initialize caches if needed
            if (!_corpseCache.ContainsKey(mapId))
                _corpseCache[mapId] = new List<Thing>();

            if (!_reachabilityCache.ContainsKey(mapId))
                _reachabilityCache[mapId] = new Dictionary<Thing, bool>();

            // Check for explicitly designated corpses first (these are highest priority)
            UpdateDesignatedCorpses(map, targetCache, mapId);

            // Check for human corpses more frequently (higher priority)
            if (currentTick > _lastHumanCorpseUpdateTick + HUMAN_CORPSE_UPDATE_INTERVAL)
            {
                // Use progressive cache update for human corpses
                UpdateHumanCorpses(map, targetCache, mapId);
                _lastHumanCorpseUpdateTick = currentTick;
            }

            // Check for animal corpses less frequently (lower priority)
            if (currentTick > _lastAnimalCorpseUpdateTick + ANIMAL_CORPSE_UPDATE_INTERVAL)
            {
                // Use progressive cache update for animal corpses
                UpdateAnimalCorpses(map, targetCache, mapId);
                _lastAnimalCorpseUpdateTick = currentTick;
            }

            // Use the base class's progressive cache update for general scanning
            UpdateCacheProgressively(
                map,
                targetCache,
                ref _lastCacheUpdateTick,
                RelevantThingRequestGroups,
                thing => {
                    // Skip if we've already processed this corpse through other means
                    if (_corpseCache[mapId].Contains(thing))
                        return false;

                    // Make sure it's a valid corpse
                    if (thing is Corpse corpse &&
                        corpse.Spawned &&
                        !HaulAIUtility.IsInHaulableInventory(corpse) &&
                        !corpse.IsInValidStorage())
                    {
                        bool isForbidden = false;
                        try { isForbidden = corpse.IsForbidden(Faction.OfPlayer); }
                        catch { isForbidden = false; }

                        if (isForbidden)
                            return false;

                        // Check if already being hauled by a non-player animal
                        Pawn firstReserver = map.physicalInteractionReservationManager.FirstReserverOf(corpse);
                        if (firstReserver != null && firstReserver.RaceProps.Animal && firstReserver.Faction != Faction.OfPlayer)
                            return false;

                        // Valid corpse to haul
                        return true;
                    }
                    return false;
                },
                _corpseCache,
                CacheUpdateInterval
            );

            SetHasTargets(map, hasTargets || (targetCache != null && targetCache.Count > 0));
        }

        /// <summary>
        /// Update cache with corpses that have explicit haul designations
        /// </summary>
        private void UpdateDesignatedCorpses(Map map, List<Thing> targetCache, int mapId)
        {
            foreach (Designation designation in map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Haul))
            {
                if (designation.target.Thing is Corpse corpse &&
                    corpse.Spawned &&
                    !HaulAIUtility.IsInHaulableInventory(corpse))
                {
                    bool isForbidden = false;
                    try { isForbidden = corpse.IsForbidden(Faction.OfPlayer); }
                    catch { isForbidden = false; }

                    if (!isForbidden && !_corpseCache[mapId].Contains(corpse))
                    {
                        _corpseCache[mapId].Add(corpse);
                        targetCache.Add(corpse);
                    }
                }
            }
        }

        /// <summary>
        /// Update cache with human corpses in the home area
        /// </summary>
        private void UpdateHumanCorpses(Map map, List<Thing> targetCache, int mapId)
        {
            // Limit the number of cells we check each update for performance
            int cellsChecked = 0;
            const int MAX_CELLS_PER_UPDATE = 400;

            foreach (var cell in map.areaManager.Home.ActiveCells)
            {
                if (_corpseCache[mapId].Count >= MAX_CORPSES || cellsChecked >= MAX_CELLS_PER_UPDATE)
                    break;

                cellsChecked++;

                List<Thing> thingsHere = cell.GetThingList(map);
                for (int i = 0; i < thingsHere.Count; i++)
                {
                    if (_corpseCache[mapId].Count >= MAX_CORPSES)
                        break;

                    if (thingsHere[i] is Corpse corpse &&
                        corpse.Spawned &&
                        !HaulAIUtility.IsInHaulableInventory(corpse) &&
                        corpse.InnerPawn.RaceProps.Humanlike &&
                        !corpse.IsInValidStorage())
                    {
                        try
                        {
                            bool isForbidden = false;
                            try { isForbidden = corpse.IsForbidden(Faction.OfPlayer); }
                            catch { isForbidden = false; }

                            if (!isForbidden && !_corpseCache[mapId].Contains(corpse))
                            {
                                // Check if already being hauled by animal
                                Pawn firstReserver = map.physicalInteractionReservationManager.FirstReserverOf(corpse);
                                if (firstReserver == null ||
                                    !firstReserver.RaceProps.Animal ||
                                    firstReserver.Faction == Faction.OfPlayer)
                                {
                                    _corpseCache[mapId].Add(corpse);
                                    targetCache.Add(corpse);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Utility_DebugManager.LogError($"Error processing human corpse for hauling: {ex}");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Update cache with animal corpses in the home area
        /// </summary>
        private void UpdateAnimalCorpses(Map map, List<Thing> targetCache, int mapId)
        {
            // Limit the number of cells we check each update for performance
            int cellsChecked = 0;
            const int MAX_CELLS_PER_UPDATE = 300;

            foreach (var cell in map.areaManager.Home.ActiveCells)
            {
                if (_corpseCache[mapId].Count >= MAX_CORPSES || cellsChecked >= MAX_CELLS_PER_UPDATE)
                    break;

                cellsChecked++;

                List<Thing> thingsHere = cell.GetThingList(map);
                for (int i = 0; i < thingsHere.Count; i++)
                {
                    if (_corpseCache[mapId].Count >= MAX_CORPSES)
                        break;

                    if (thingsHere[i] is Corpse corpse &&
                        corpse.Spawned &&
                        !HaulAIUtility.IsInHaulableInventory(corpse) &&
                        !corpse.InnerPawn.RaceProps.Humanlike &&
                        !corpse.IsInValidStorage())
                    {
                        try
                        {
                            bool isForbidden = false;
                            try { isForbidden = corpse.IsForbidden(Faction.OfPlayer); }
                            catch { isForbidden = false; }

                            if (!isForbidden && !_corpseCache[mapId].Contains(corpse))
                            {
                                // Check if already being hauled by animal
                                Pawn firstReserver = map.physicalInteractionReservationManager.FirstReserverOf(corpse);
                                if (firstReserver == null ||
                                    !firstReserver.RaceProps.Animal ||
                                    firstReserver.Faction == Faction.OfPlayer)
                                {
                                    _corpseCache[mapId].Add(corpse);
                                    targetCache.Add(corpse);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Utility_DebugManager.LogError($"Error processing animal corpse for hauling: {ex}");
                        }
                    }
                }
            }
        }

        public override bool ShouldHaulItem(Thing item, Map map)
        {
            if (!(item is Corpse corpse) || !corpse.Spawned || HaulAIUtility.IsInHaulableInventory(corpse))
                return false;

            int mapId = map.uniqueID;

            // Check from cache first for better performance
            if (_corpseCache.ContainsKey(mapId) && _corpseCache[mapId].Contains(item))
                return true;

            try
            {
                // Safe forbidden check
                bool isForbidden = false;
                try { isForbidden = corpse.IsForbidden(Faction.OfPlayer); }
                catch { isForbidden = false; }

                if (isForbidden)
                    return false;

                // Check for haul designation
                if (map.designationManager.DesignationOn(corpse, DesignationDefOf.Haul) != null)
                    return true;

                // Check if it's not in valid storage
                if (!corpse.IsInValidStorage())
                {
                    // Check if already being hauled by a non-player animal
                    Pawn firstReserver = map.physicalInteractionReservationManager.FirstReserverOf(corpse);
                    if (firstReserver != null &&
                        firstReserver.RaceProps.Animal &&
                        firstReserver.Faction != Faction.OfPlayer)
                        return false;

                    return true;
                }
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in ShouldHaulItem for corpse: {ex}");
            }

            return false;
        }

        public override bool ValidateHaulingJob(Thing thing, Pawn hauler)
        {
            try
            {
                if (!(thing is Corpse corpse))
                    return false;

                // Check faction interaction
                if (!Utility_JobGiverManager.IsValidFactionInteraction(thing, hauler, requiresDesignator: false))
                    return false;

                if (thing.IsForbidden(hauler))
                    return false;

                if (!hauler.CanReserve(thing, 1, -1) ||
                    !hauler.CanReach(thing, PathEndMode.Touch, hauler.NormalMaxDanger()))
                    return false;

                // Skip if already being hauled by an animal
                Pawn firstReserver = hauler.Map.physicalInteractionReservationManager.FirstReserverOf(thing);
                if (firstReserver != null && firstReserver.RaceProps.Animal && firstReserver.Faction != Faction.OfPlayer)
                    return false;

                // Skip if pawn can't automatically haul this
                if (!HaulAIUtility.PawnCanAutomaticallyHaul(hauler, thing, false))
                    return false;

                // Check for general storage
                IntVec3 storageCell;
                IHaulDestination haulDestination;
                return StoreUtility.TryFindBestBetterStorageFor(thing, hauler, hauler.Map,
                    StoreUtility.CurrentStoragePriorityOf(thing), hauler.Faction, out storageCell, out haulDestination);
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error validating corpse hauling job: {ex}");
                return false;
            }
        }

        protected override Job CreateHaulingJob(Pawn hauler, Thing thing)
        {
            try
            {
                if (!(thing is Corpse corpse))
                    return null;

                // Try to find storage location for the corpse
                IntVec3 storeCell;
                IHaulDestination haulDestination;
                if (StoreUtility.TryFindBestBetterStorageFor(corpse, hauler, hauler.Map,
                    StoreUtility.CurrentStoragePriorityOf(corpse), hauler.Faction, out storeCell, out haulDestination))
                {
                    Job job = null;

                    // Handle graves
                    if (haulDestination is Building_Grave grave)
                    {
                        // Use the grave directly from haulDestination
                        job = JobMaker.MakeJob(JobDefOf.HaulToContainer, corpse, grave);
                        job.count = 1;

                        Utility_DebugManager.LogNormal($"{hauler.LabelShort} created job to bury {corpse.InnerPawn.LabelShort}");
                    }
                    else
                    {
                        // Standard storage
                        job = HaulAIUtility.HaulToCellStorageJob(hauler, corpse, storeCell, false);

                        if (job != null)
                        {
                            Utility_DebugManager.LogNormal($"{hauler.LabelShort} created job to haul {corpse.InnerPawn.LabelShort} to stockpile");
                        }
                    }

                    // Remove haul designation if it exists (only if job was created successfully)
                    if (job != null)
                    {
                        hauler.Map.designationManager.RemoveAllDesignationsOn(corpse, false);
                    }

                    return job;
                }
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating corpse hauling job: {ex}");
            }

            return null;
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public override void ResetStaticData()
        {
            Utility_CacheManager.ResetJobGiverCache(_corpseCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
            _lastHumanCorpseUpdateTick = -999;
            _lastAnimalCorpseUpdateTick = -999;
        }
    }
}