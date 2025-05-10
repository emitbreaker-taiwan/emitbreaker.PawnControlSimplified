using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to cremate corpses at electric crematoriums.
    /// Uses the Hauling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Hauling_Cremate_PawnControl : ThinkNode_JobGiver
    {
        // Cache for crematoriums that have bills - using Building_WorkTable which implements IBillGiver
        private static readonly Dictionary<int, List<Building_WorkTable>> _crematoriumCache = new Dictionary<int, List<Building_WorkTable>>();
        private static readonly Dictionary<int, Dictionary<Building_WorkTable, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Building_WorkTable, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 300; // Update every 5 seconds

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        // Get WorkGiverDef for cremation
        private static WorkGiverDef CremateWorkGiver()
        {
            if (Utility_Common.WorkGiverDefNamed("DoBillsCremate") != null)
            {
                return Utility_Common.WorkGiverDefNamed("DoBillsCremate");
            }
            else
            {
                Utility_DebugManager.LogError("WorkGiverDef DoBillsCremate not found.");
                return null;
            }
        }

        public override float GetPriority(Pawn pawn)
        {
            // Cremating is moderately important
            return 5.5f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // IMPORTANT: Only player pawns and slaves owned by player should operate crematoriums
            if (pawn.Faction != Faction.OfPlayer &&
                !(pawn.IsSlave && pawn.HostFaction == Faction.OfPlayer))
                return null;

            return Utility_JobGiverManagerOld.StandardTryGiveJob<Plant>(
                pawn,
                "Hauling",
                (p, forced) => {
                    // Update crematorium cache
                    UpdateCremateCacheSafely(p.Map);

                    // Find and create a job for cremating corpses
                    return Utility_JobGiverManagerOld.TryCreateBillGiverJob(p, _crematoriumCache, _reachabilityCache);
                },
                debugJobDesc: "cremate assignment",
                skipEmergencyCheck: false);
        }

        /// <summary>
        /// Updates the cache of crematoriums with active bills
        /// </summary>
        private void UpdateCremateCacheSafely(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_crematoriumCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_crematoriumCache.ContainsKey(mapId))
                    _crematoriumCache[mapId].Clear();
                else
                    _crematoriumCache[mapId] = new List<Building_WorkTable>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Building_WorkTable, bool>();

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
                            }
                        }
                    }

                    Utility_DebugManager.LogWarning("Could not find DoBillsCremate WorkGiverDef, using fallback method for crematoriums");
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_crematoriumCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_Cremate_PawnControl";
        }
    }
}