using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns harvesting tasks to pawns with the Growing work type.
    /// Optimized for large colonies with many plants using distance-based bucketing.
    /// </summary>
    public class JobGiver_Growing_GrowerHarvest_PawnControl : ThinkNode_JobGiver
    {
        // Cache for harvestable plants to improve performance
        private static readonly Dictionary<int, List<Plant>> _harvestablePlantsCache = new Dictionary<int, List<Plant>>();
        private static readonly Dictionary<int, Dictionary<Plant, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Plant, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 250; // Update every ~4 seconds

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        // Maximum harvest work amount per job
        private const float MAX_HARVEST_WORK_PER_JOB = 2400f;

        // Maximum plants per job
        private const int MAX_PLANTS_PER_JOB = 40;

        public override float GetPriority(Pawn pawn)
        {
            // Harvesting is important for obtaining food and resources
            return 5.8f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // IMPORTANT: Only player pawns and slaves owned by player should harvest
            if (pawn.Faction != Faction.OfPlayer &&
                !(pawn.IsSlave && pawn.HostFaction == Faction.OfPlayer))
                return null;

            // Skip pawns in lords (e.g., in caravans, raids, etc.)
            if (pawn.GetLord() != null)
            {
                return null;
            }

            return Utility_JobGiverManager.StandardTryGiveJob<Plant>(
                pawn,
                "Growing",
                (p, forced) => {
                    // Update plant cache
                    UpdateHarvestablePlantsCache(p.Map);

                    // Find and create a job for cutting plants with VALID DESIGNATORS ONLY
                    return TryCreateHarvestJob(pawn, false);
                },
                debugJobDesc: "harvest assignment");
        }

        /// <summary>
        /// Updates the cache of plants to harvest.  Only those with a
        /// HarvestPlant designation or marked autoHarvestable.
        /// </summary>
        private void UpdateHarvestablePlantsCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL
                || !_harvestablePlantsCache.ContainsKey(mapId))
            {
                // Initialize or clear the list
                if (!_harvestablePlantsCache.ContainsKey(mapId))
                    _harvestablePlantsCache[mapId] = new List<Plant>();
                else
                    _harvestablePlantsCache[mapId].Clear();

                // Also clear reachability cache
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Plant, bool>();

                // 1) All player‐designated harvest plants
                foreach (var des in map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.HarvestPlant))
                {
                    if (des.target.Thing is Plant plant)
                        _harvestablePlantsCache[mapId].Add(plant);
                }

                // 2) Optional: truly wild, autoHarvestable plants (berries, mushrooms, etc.)
                //foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Plant))
                //{
                //    if (thing is Plant wildPlant
                //        && wildPlant.def.plant.autoHarvestable
                //        && wildPlant.HarvestableNow
                //        && wildPlant.LifeStage == PlantLifeStage.Mature
                //        && wildPlant.CanYieldNow())
                //    {
                //        _harvestablePlantsCache[mapId].Add(wildPlant);
                //    }
                //}

                // 3) CRITICAL FIX: Add plants in growing zones that are ready for harvest
                //    but ONLY for same-faction zones
                foreach (Zone zone in map.zoneManager.AllZones)
                {
                    if (!(zone is Zone_Growing growingZone))
                        continue;

                    // Get the plant def this zone wants to grow
                    ThingDef plantDef = growingZone.GetPlantDefToGrow();
                    if (plantDef == null)
                        continue;

                    foreach (IntVec3 cell in growingZone.Cells)
                    {
                        Plant plant = cell.GetPlant(map);
                        if (plant == null || _harvestablePlantsCache[mapId].Contains(plant))
                            continue;

                        // Only include plants that match the zone's desired crop AND are harvestable
                        if (plant.def == plantDef &&
                            plant.HarvestableNow &&
                            plant.LifeStage == PlantLifeStage.Mature &&
                            plant.CanYieldNow())
                        {
                            _harvestablePlantsCache[mapId].Add(plant);
                        }
                    }
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Create a job for harvesting plants using manager-driven bucket processing
        /// </summary>
        private Job TryCreateHarvestJob(Pawn pawn, bool forced = false)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_harvestablePlantsCache.ContainsKey(mapId) || _harvestablePlantsCache[mapId].Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                _harvestablePlantsCache[mapId],
                (plant) => (plant.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Find the best plant to start harvesting
            Plant targetPlant = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (plant, p) => {
                    // Skip if no longer harvestable
                    if (plant == null || plant.Destroyed || !plant.Spawned ||
                        !plant.HarvestableNow || plant.LifeStage != PlantLifeStage.Mature || !plant.CanYieldNow())
                        return false;

                    // Skip if not auto-harvestable and not forced
                    if (!plant.def.plant.autoHarvestable && !forced)
                    {
                        Zone_Growing zone = plant.Position.GetZone(plant.Map) as Zone_Growing;
                        if (zone == null || !zone.allowCut)
                            return false;
                    }

                    // Check if pawn is willing to cut this plant
                    if (!PlantUtility.PawnWillingToCutPlant_Job(plant, p))
                        return false;

                    // Check basic reachability
                    if (plant.IsForbidden(p) || !p.CanReserve(plant, 1, -1, null, forced))
                        return false;

                    return true;
                },
                _reachabilityCache
            );

            // Create job if target found
            if (targetPlant != null)
            {
                Job job = JobMaker.MakeJob(JobDefOf.Harvest);
                job.AddQueuedTarget(TargetIndex.A, targetPlant);

                // Add additional nearby harvestable plants of the same type
                Room startingRoom = targetPlant.Position.GetRoom(pawn.Map);
                ThingDef wantedPlantDef = WorkGiver_Grower.CalculateWantedPlantDef(targetPlant.Position, pawn.Map);
                float totalHarvestWork = targetPlant.def.plant.harvestWork;
                int plantsAdded = 1;

                // Try to find additional plants in the same room
                foreach (Plant plant in _harvestablePlantsCache[mapId])
                {
                    // Skip the already-added target plant
                    if (plant == targetPlant)
                        continue;

                    // Skip if we've reached our limits
                    if (plantsAdded >= MAX_PLANTS_PER_JOB || totalHarvestWork > MAX_HARVEST_WORK_PER_JOB)
                        break;

                    // Skip if plant is not in the same room
                    if (plant.Position.GetRoom(pawn.Map) != startingRoom)
                        continue;

                    // Check if this plant is valid for the job
                    if (!plant.HarvestableNow || plant.LifeStage != PlantLifeStage.Mature || !plant.CanYieldNow() ||
                        plant.IsForbidden(pawn) || !PlantUtility.PawnWillingToCutPlant_Job(plant, pawn) ||
                        !pawn.CanReserve(plant, 1, -1, null, forced))
                        continue;

                    // Only add plants of the wanted type in growing zones
                    Zone_Growing zone = plant.Position.GetZone(pawn.Map) as Zone_Growing;
                    if (zone != null && !zone.allowCut && plant.def != wantedPlantDef)
                        continue;

                    // Add this plant to the job
                    job.AddQueuedTarget(TargetIndex.A, plant);
                    totalHarvestWork += plant.def.plant.harvestWork;
                    plantsAdded++;
                }

                // Sort targets by distance if there are enough of them
                if (job.targetQueueA != null && job.targetQueueA.Count >= 3)
                {
                    job.targetQueueA.SortBy<LocalTargetInfo, int>((targ) => targ.Cell.DistanceToSquared(pawn.Position));
                }

                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to harvest {plantsAdded} plants starting with {targetPlant.Label}");
                return job;
            }

            return null;
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_harvestablePlantsCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_GrowerHarvest_PawnControl";
        }
    }
}