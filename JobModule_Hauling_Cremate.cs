using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for cremating corpses at electric crematoriums
    /// </summary>
    public class JobModule_Hauling_Cremate : JobModule_Hauling
    {
        public override string UniqueID => "Cremate";
        public override float Priority => 5.5f; // Same as original JobGiver
        public override string Category => "BillJobs";
        public override int CacheUpdateInterval => 300; // Update every 5 seconds

        // Static caches for map-specific data persistence
        private static readonly Dictionary<int, List<Building_WorkTable>> _crematoriumCache = new Dictionary<int, List<Building_WorkTable>>();
        private static readonly Dictionary<int, Dictionary<Building_WorkTable, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Building_WorkTable, bool>>();
        private static readonly Dictionary<int, Dictionary<Building_WorkTable, bool>> _billsCache = new Dictionary<int, Dictionary<Building_WorkTable, bool>>();
        private static int _lastUpdateCacheTick = -999;

        // These ThingRequestGroups are what this module cares about
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups =>
            new HashSet<ThingRequestGroup> { ThingRequestGroup.BuildingArtificial };

        // Get WorkGiverDef for cremation
        private static WorkGiverDef CremateWorkGiver()
        {
            WorkGiverDef workGiver = Utility_Common.WorkGiverDefNamed("DoBillsCremate");
            if (workGiver == null)
            {
                Utility_DebugManager.LogError("WorkGiverDef DoBillsCremate not found.");
            }
            return workGiver;
        }

        public override void UpdateCache(Map map, List<Thing> targetCache)
        {
            base.UpdateCache(map, targetCache);

            if (map == null) return;
            int mapId = map.uniqueID;
            bool hasTargets = false;

            int currentTick = Find.TickManager.TicksGame;

            // Initialize caches if needed
            if (!_crematoriumCache.ContainsKey(mapId))
                _crematoriumCache[mapId] = new List<Building_WorkTable>();

            if (!_reachabilityCache.ContainsKey(mapId))
                _reachabilityCache[mapId] = new Dictionary<Building_WorkTable, bool>();

            if (!_billsCache.ContainsKey(mapId))
                _billsCache[mapId] = new Dictionary<Building_WorkTable, bool>();

            // Only do a full update if needed
            if (currentTick > _lastUpdateCacheTick + CacheUpdateInterval ||
                !_crematoriumCache.ContainsKey(mapId))
            {
                try
                {
                    // Clear existing caches for this map
                    _crematoriumCache[mapId].Clear();
                    _reachabilityCache[mapId].Clear();
                    _billsCache[mapId].Clear();

                    // Get the crematorium defs from the workgiver's fixedBillGiverDefs
                    var workGiver = CremateWorkGiver();
                    if (workGiver != null && workGiver.fixedBillGiverDefs != null && workGiver.fixedBillGiverDefs.Count > 0)
                    {
                        foreach (ThingDef crematoriumDef in workGiver.fixedBillGiverDefs)
                        {
                            foreach (Building building in map.listerBuildings.AllBuildingsColonistOfDef(crematoriumDef))
                            {
                                // Cast to Building_WorkTable which implements IBillGiver
                                Building_WorkTable workTable = building as Building_WorkTable;
                                if (workTable != null && workTable.Spawned)
                                {
                                    if (workTable.BillStack != null && workTable.BillStack.AnyShouldDoNow)
                                    {
                                        _crematoriumCache[mapId].Add(workTable);
                                        targetCache.Add(workTable);
                                        
                                        // Cache bills status
                                        _billsCache[mapId][workTable] = true;
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        // Fallback to using ElectricCrematorium if for some reason we can't get the defs from workgiver
                        ThingDef fallbackDef = ThingDef.Named("ElectricCrematorium");
                        if (fallbackDef != null)
                        {
                            foreach (Building building in map.listerBuildings.AllBuildingsColonistOfDef(fallbackDef))
                            {
                                Building_WorkTable workTable = building as Building_WorkTable;
                                if (workTable != null && workTable.Spawned &&
                                    workTable.BillStack != null && workTable.BillStack.AnyShouldDoNow)
                                {
                                    _crematoriumCache[mapId].Add(workTable);
                                    targetCache.Add(workTable);
                                    
                                    // Cache bills status
                                    _billsCache[mapId][workTable] = true;
                                }
                            }
                        }
                    }

                    if (_crematoriumCache[mapId].Count > 0)
                    {
                        Utility_DebugManager.LogNormal(
                            $"Found {_crematoriumCache[mapId].Count} crematoriums with active bills on map {map.uniqueID}");
                    }

                    _lastUpdateCacheTick = currentTick;
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"Error updating crematoriums cache: {ex}");
                }
            }
            else
            {
                // Just add the cached crematoriums to the target cache
                foreach (Building_WorkTable workTable in _crematoriumCache[mapId])
                {
                    // Skip crematoriums that are no longer valid
                    if (!workTable.Spawned || workTable.BillStack == null || !workTable.BillStack.AnyShouldDoNow)
                        continue;

                    targetCache.Add(workTable);
                }
            }

            SetHasTargets(map, hasTargets || (targetCache != null && targetCache.Count > 0));
        }

        public override bool ShouldHaulItem(Thing thing, Map map)
        {
            try
            {
                if (!(thing is Building_WorkTable workTable)) return false;
                if (workTable == null || map == null || !workTable.Spawned) return false;

                int mapId = map.uniqueID;

                // Check if it's in our cache first
                if (_crematoriumCache.ContainsKey(mapId) && _crematoriumCache[mapId].Contains(workTable))
                    return true;

                // Check bills cache
                if (_billsCache.ContainsKey(mapId) && 
                    _billsCache[mapId].TryGetValue(workTable, out bool hasBills))
                {
                    return hasBills;
                }

                // If not in cache, check if crematorium has active bills
                bool shouldProcess = workTable.BillStack != null && workTable.BillStack.AnyShouldDoNow;

                // Cache the result
                if (_billsCache.ContainsKey(mapId))
                {
                    _billsCache[mapId][workTable] = shouldProcess;
                }

                return shouldProcess;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in ShouldHaulItem for cremating: {ex}");
                return false;
            }
        }

        public override bool ValidateHaulingJob(Thing thing, Pawn hauler)
        {
            try
            {
                if (!(thing is Building_WorkTable workTable)) return false;
                if (workTable == null || hauler == null || !workTable.Spawned || !hauler.Spawned)
                    return false;

                // IMPORTANT: Only player pawns and slaves owned by player should operate crematoriums
                if (hauler.Faction != Faction.OfPlayer &&
                    !(hauler.IsSlave && hauler.HostFaction == Faction.OfPlayer))
                    return false;

                // Skip if no longer has valid bills
                if (workTable.BillStack == null || !workTable.BillStack.AnyShouldDoNow)
                    return false;

                // Skip if forbidden or unreachable
                if (workTable.IsForbidden(hauler) ||
                    !hauler.CanReserve(workTable) ||
                    !hauler.CanReach(workTable, PathEndMode.InteractionCell, hauler.NormalMaxDanger()))
                    return false;

                return true;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error validating cremation job: {ex}");
                return false;
            }
        }

        protected override Job CreateHaulingJob(Pawn hauler, Thing thing)
        {
            try
            {
                if (!(thing is Building_WorkTable workTable)) return null;

                // Use the utility method to create a job for cremation
                Job job = Utility_JobGiverManager.TryCreateBillJob(hauler, workTable);
                if (job != null)
                {
                    Utility_DebugManager.LogNormal($"{hauler.LabelShort} created job to work at crematorium");
                }
                return job;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating cremation job: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public override void ResetStaticData()
        {
            // Reset caches
            foreach (var mapCache in _crematoriumCache.Values)
            {
                mapCache.Clear();
            }
            _crematoriumCache.Clear();

            foreach (var reachabilityMap in _reachabilityCache.Values)
            {
                reachabilityMap.Clear();
            }
            _reachabilityCache.Clear();

            foreach (var billsMap in _billsCache.Values)
            {
                billsMap.Clear();
            }
            _billsCache.Clear();

            _lastUpdateCacheTick = -999;
        }
    }
}