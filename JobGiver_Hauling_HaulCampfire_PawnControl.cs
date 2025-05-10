using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to work at campfires with bills.
    /// Uses the Hauling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Hauling_HaulCampfire_PawnControl : ThinkNode_JobGiver
    {
        // Cache for campfires that have bills - using Building_WorkTable since campfires implement IBillGiver
        private static readonly Dictionary<int, List<Building_WorkTable>> _campfiresCache = new Dictionary<int, List<Building_WorkTable>>();
        private static readonly Dictionary<int, Dictionary<Building_WorkTable, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Building_WorkTable, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 300; // Update every 5 seconds

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        // Get WorkGiverDef for Campfire
        private static WorkGiverDef CampfireWorkGiver()
        {
            if (Utility_Common.WorkGiverDefNamed("DoBillsHaulCampfire") != null)
            {
                return Utility_Common.WorkGiverDefNamed("DoBillsHaulCampfire");
            }
            else
            {
                Utility_DebugManager.LogError("WorkGiverDef DoBillsHaulCampfire not found.");
                return null;
            }
        }

        public override float GetPriority(Pawn pawn)
        {
            // Working at campfire is moderately important
            return 5.4f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<Plant>(
                pawn,
                "Hauling",
                (p, forced) => {
                    // Update plant cache
                    UpdateCampfiresCacheSafely(p.Map);

                    // Find and create a job for cutting plants with VALID DESIGNATORS ONLY
                    return Utility_JobGiverManager.TryCreateBillGiverJob(p, _campfiresCache, _reachabilityCache);
                },
                debugJobDesc: "haul to campfire assignment");
        }

        /// <summary>
        /// Updates the cache of campfires with active bills
        /// </summary>
        private void UpdateCampfiresCacheSafely(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_campfiresCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_campfiresCache.ContainsKey(mapId))
                    _campfiresCache[mapId].Clear();
                else
                    _campfiresCache[mapId] = new List<Building_WorkTable>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Building_WorkTable, bool>();

                // Get campfire defs from the workgiver's fixedBillGiverDefs
                var workGiver = CampfireWorkGiver();
                if (workGiver != null && workGiver.fixedBillGiverDefs != null && workGiver.fixedBillGiverDefs.Count > 0)
                {
                    foreach (ThingDef campfireDef in workGiver.fixedBillGiverDefs)
                    {
                        foreach (Building building in map.listerBuildings.AllBuildingsColonistOfDef(campfireDef))
                        {
                            // Make sure it's a Building_WorkTable which implements IBillGiver
                            Building_WorkTable workTable = building as Building_WorkTable;
                            if (workTable != null && workTable.Spawned)
                            {
                                if (workTable.BillStack != null && workTable.BillStack.AnyShouldDoNow)
                                {
                                    _campfiresCache[mapId].Add(workTable);
                                }
                            }
                        }
                    }
                }
                else
                {
                    // Fallback to using Campfire if for some reason we can't get the defs from workgiver
                    ThingDef fallbackDef = ThingDef.Named("Campfire");
                    if (fallbackDef != null)
                    {
                        foreach (Building building in map.listerBuildings.AllBuildingsColonistOfDef(fallbackDef))
                        {
                            Building_WorkTable workTable = building as Building_WorkTable;
                            if (workTable != null && workTable.Spawned &&
                                workTable.BillStack != null && workTable.BillStack.AnyShouldDoNow)
                            {
                                _campfiresCache[mapId].Add(workTable);
                            }
                        }
                    }

                    Utility_DebugManager.LogWarning("Could not find DoBillsHaulCampfire WorkGiverDef, using fallback method for campfires");
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_campfiresCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_HaulCampfire_PawnControl";
        }
    }
}