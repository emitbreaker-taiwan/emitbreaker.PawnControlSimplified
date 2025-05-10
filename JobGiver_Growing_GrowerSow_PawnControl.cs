using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns sowing tasks to pawns with the Growing work type.
    /// Optimized for large colonies with many growing zones using distance-based bucketing.
    /// </summary>
    public class JobGiver_Growing_GrowerSow_PawnControl : ThinkNode_JobGiver
    {
        // Cache for cells that need sowing
        private static readonly Dictionary<int, List<IntVec3>> _sowableCellsCache = new Dictionary<int, List<IntVec3>>();
        private static readonly Dictionary<int, Dictionary<IntVec3, bool>> _reachabilityCache = new Dictionary<int, Dictionary<IntVec3, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 300; // Update every 5 seconds

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        // Static translation strings
        private static string CantSowCavePlantBecauseOfLightTrans;
        private static string CantSowCavePlantBecauseUnroofedTrans;

        public static void ResetStaticData()
        {
            CantSowCavePlantBecauseOfLightTrans = (string)"CantSowCavePlantBecauseOfLight".Translate();
            CantSowCavePlantBecauseUnroofedTrans = (string)"CantSowCavePlantBecauseUnroofed".Translate();
        }

        public override float GetPriority(Pawn pawn)
        {
            // Sowing is important for future food production
            return 5.7f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // IMPORTANT: Only player pawns and slaves owned by player should sow
            if (pawn.Faction != Faction.OfPlayer &&
                !(pawn.IsSlave && pawn.HostFaction == Faction.OfPlayer))
                return null;

            return Utility_JobGiverManagerOld.StandardTryGiveJob<Plant>(
                pawn,
                "Growing",
                (p, forced) => {
                    // Update plant cache
                    UpdateSowableCellsCache(p.Map);

                    // Find and create a job for cutting plants with VALID DESIGNATORS ONLY
                    return TryCreateSowJob(pawn, false);
                },
                debugJobDesc: "sowing assignment");
        }

        /// <summary>
        /// Updates the cache of cells that need sowing
        /// </summary>
        private void UpdateSowableCellsCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_sowableCellsCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_sowableCellsCache.ContainsKey(mapId))
                    _sowableCellsCache[mapId].Clear();
                else
                    _sowableCellsCache[mapId] = new List<IntVec3>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<IntVec3, bool>();

                // Find all zones that need sowing
                List<Zone_Growing> growingZones = new List<Zone_Growing>();
                foreach (Zone zone in map.zoneManager.AllZones)
                {
                    if (zone is Zone_Growing growingZone && growingZone.allowSow && growingZone.GetPlantDefToGrow() != null)
                    {
                        growingZones.Add(growingZone);
                    }
                }

                // Find all hydroponics basins that need sowing
                List<Building_PlantGrower> plantGrowers = new List<Building_PlantGrower>();
                foreach (Building building in map.listerBuildings.allBuildingsColonist)
                {
                    Building_PlantGrower grower = building as Building_PlantGrower;
                    if (grower != null && grower.GetPlantDefToGrow() != null && grower.CanAcceptSowNow())
                    {
                        plantGrowers.Add(grower);
                    }
                }

                // Process growing zones
                foreach (Zone_Growing zone in growingZones)
                {
                    if (!zone.allowSow) continue;

                    ThingDef plantDef = zone.GetPlantDefToGrow();
                    if (plantDef == null) continue;

                    foreach (IntVec3 cell in zone.Cells)
                    {
                        // Basic validation before adding to cache
                        if (CanSowAtCell(cell, map, plantDef, true))
                        {
                            _sowableCellsCache[mapId].Add(cell);
                        }
                    }
                }

                // Process hydroponics basins
                foreach (Building_PlantGrower grower in plantGrowers)
                {
                    ThingDef plantDef = grower.GetPlantDefToGrow();
                    if (plantDef == null) continue;

                    foreach (IntVec3 cell in grower.OccupiedRect())
                    {
                        // Basic validation before adding to cache
                        if (CanSowAtCell(cell, map, plantDef, true))
                        {
                            _sowableCellsCache[mapId].Add(cell);
                        }
                    }
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Basic check if a cell can be sown (for caching purposes)
        /// </summary>
        private bool CanSowAtCell(IntVec3 cell, Map map, ThingDef plantDef, bool quickCheck)
        {
            // Skip if no growth season
            if (!PlantUtility.GrowthSeasonNow(cell, map, true))
                return false;

            // Skip if already has the desired plant
            List<Thing> thingList = cell.GetThingList(map);
            for (int i = 0; i < thingList.Count; i++)
            {
                if (thingList[i].def == plantDef)
                    return false;
            }

            // Further checks only if not doing a quick validation
            if (!quickCheck)
            {
                // Check for blocking things
                for (int i = 0; i < thingList.Count; i++)
                {
                    if (thingList[i].def.BlocksPlanting())
                        return false;
                }

                // Cave plant checks
                if (plantDef.plant.cavePlant)
                {
                    if (!cell.Roofed(map))
                        return false;
                    if (map.glowGrid.GroundGlowAt(cell, true) > 0.0f)
                        return false;
                }

                // Roof interference check
                if (plantDef.plant.interferesWithRoof && cell.Roofed(map))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Create a sowing job using custom distance‐bucket logic.
        /// (Option A: no cutting; only sow in valid cells.)
        /// </summary>
        private Job TryCreateSowJob(Pawn pawn, bool forced = false)
        {
            if (pawn?.Map == null)
                return null;

            int mapId = pawn.Map.uniqueID;
            if (!_sowableCellsCache.ContainsKey(mapId) || _sowableCellsCache[mapId].Count == 0)
                return null;

            // Build distance buckets
            var buckets = CreateDistanceBucketsForCells(
                pawn,
                _sowableCellsCache[mapId],
                DISTANCE_THRESHOLDS
            );

            // Find the best cell to sow
            IntVec3 targetCell = FindFirstValidCell(buckets, pawn, forced);
            if (!targetCell.IsValid)
                return null;

            // Determine what to plant
            ThingDef plantDefToSow = WorkGiver_Grower.CalculateWantedPlantDef(targetCell, pawn.Map);
            if (plantDefToSow == null)
                return null;

            // Skill check
            if (plantDefToSow.plant.sowMinSkill > 0)
            {
                int skillLevel = pawn.skills?.GetSkill(SkillDefOf.Plants).Level
                                 ?? pawn.RaceProps.mechFixedSkillLevel;
                if (skillLevel < plantDefToSow.plant.sowMinSkill)
                {
                    JobFailReason.Is("UnderAllowedSkill".Translate(plantDefToSow.plant.sowMinSkill));
                    return null;
                }
            }

            // 🔄 Removed all “adjacentBlocker” checks and CutPlant returns

            // Create and return the sow job
            var job = JobMaker.MakeJob(JobDefOf.Sow, targetCell);
            job.plantDefToSow = plantDefToSow;
            Utility_DebugManager.LogNormal($"{pawn.LabelShort} created sow job for {plantDefToSow.label} at {targetCell}");
            return job;
        }

        private List<IntVec3>[] CreateDistanceBucketsForCells(Pawn pawn, IEnumerable<IntVec3> cells, float[] distanceThresholds)
        {
            if (pawn == null || cells == null || distanceThresholds == null)
                return null;

            // Initialize buckets
            List<IntVec3>[] buckets = new List<IntVec3>[distanceThresholds.Length + 1];
            for (int i = 0; i < buckets.Length; i++)
                buckets[i] = new List<IntVec3>();

            foreach (IntVec3 cell in cells)
            {
                // Get distance squared between pawn and cell
                float distSq = (cell - pawn.Position).LengthHorizontalSquared;

                // Assign to appropriate bucket
                int bucketIndex = distanceThresholds.Length; // Default to last bucket (furthest)
                for (int i = 0; i < distanceThresholds.Length; i++)
                {
                    if (distSq < distanceThresholds[i])
                    {
                        bucketIndex = i;
                        break;
                    }
                }

                buckets[bucketIndex].Add(cell);
            }

            return buckets;
        }

        /// <summary>
        /// Find the first valid cell for sowing
        /// </summary>
        private IntVec3 FindFirstValidCell(List<IntVec3>[] buckets, Pawn pawn, bool forced = false)
        {
            Map map = pawn.Map;

            // Process buckets from closest to farthest
            for (int b = 0; b < buckets.Length; b++)
            {
                if (buckets[b] == null || buckets[b].Count == 0)
                    continue;

                // Randomize within each distance band for better distribution
                buckets[b].Shuffle();

                // Check each cell in this distance band
                foreach (IntVec3 cell in buckets[b])
                {
                    // Similarly, check if there's a hydroponics basin and ensure it belongs to pawn's faction
                    Thing edifice = cell.GetEdifice(map);
                    if (edifice is Building_PlantGrower && edifice.Faction != pawn.Faction)
                        continue;

                    // Skip if forbidden
                    if (cell.IsForbidden(pawn))
                        continue;

                    // Skip if cannot reserve
                    if (!pawn.CanReserve(cell, 1, -1, null, forced))
                        continue;

                    // Skip if not growth season
                    if (!PlantUtility.GrowthSeasonNow(cell, map, true))
                        continue;

                    // Get plant def to grow
                    ThingDef plantDef = WorkGiver_Grower.CalculateWantedPlantDef(cell, map);
                    if (plantDef == null)
                        continue;

                    // Check if cell can actually be sown now
                    if (!ValidateCell(cell, plantDef, pawn, forced))
                        continue;

                    // This is a valid cell for sowing
                    return cell;
                }
            }

            return IntVec3.Invalid;
        }

        /// <summary>
        /// Validate if a cell can be sown
        /// </summary>
        private bool ValidateCell(IntVec3 cell, ThingDef plantDef, Pawn pawn, bool forced)
        {
            Map map = pawn.Map;
            List<Thing> thingList = cell.GetThingList(map);

            // Check if the cell already has the desired plant
            for (int i = 0; i < thingList.Count; i++)
            {
                Thing thing = thingList[i];
                if (thing.def == plantDef)
                    return false;
            }

            // Check for blueprints or frames
            bool hasStructure = false;
            for (int i = 0; i < thingList.Count; i++)
            {
                Thing thing = thingList[i];
                if ((thing is Blueprint || thing is Frame) && thing.Faction == pawn.Faction)
                {
                    hasStructure = true;
                    break;
                }
            }

            // If there's a structure, ensure fertility
            if (hasStructure)
            {
                Thing edifice = cell.GetEdifice(map);
                if (edifice == null || edifice.def.fertility < 0.0f)
                    return false;
            }

            // Check cave plant requirements
            if (plantDef.plant.cavePlant)
            {
                if (!cell.Roofed(map))
                {
                    JobFailReason.Is(CantSowCavePlantBecauseUnroofedTrans);
                    return false;
                }

                if (map.glowGrid.GroundGlowAt(cell, true) > 0.0f)
                {
                    JobFailReason.Is(CantSowCavePlantBecauseOfLightTrans);
                    return false;
                }
            }

            // Check if plant interferes with roof
            if (plantDef.plant.interferesWithRoof && cell.Roofed(map))
                return false;

            // Check for plants that block adjacent sowing
            Plant existingPlant = cell.GetPlant(map);
            if (existingPlant != null && existingPlant.def.plant.blockAdjacentSow)
            {
                if (!pawn.CanReserve(existingPlant, 1, -1, null, forced) || existingPlant.IsForbidden(pawn))
                    return false;

                Zone_Growing zone = cell.GetZone(map) as Zone_Growing;
                if (zone != null && !zone.allowCut)
                    return false;

                if (!PlantUtility.PawnWillingToCutPlant_Job(existingPlant, pawn))
                    return false;

                // We'll need to cut this plant first
                return true;
            }

            // Check if there are things blocking planting
            for (int i = 0; i < thingList.Count; i++)
            {
                Thing thing = thingList[i];
                if (thing.def.BlocksPlanting())
                {
                    if (!pawn.CanReserve(thing, 1, -1, null, forced))
                        return false;

                    // If it's a plant, check if we can cut it
                    if (thing.def.category == ThingCategory.Plant)
                    {
                        if (thing.IsForbidden(pawn))
                            return false;

                        Zone_Growing zone = cell.GetZone(map) as Zone_Growing;
                        if (zone != null && !zone.allowCut)
                            return false;

                        if (!PlantUtility.PawnWillingToCutPlant_Job(thing, pawn))
                            return false;

                        if (PlantUtility.TreeMarkedForExtraction(thing))
                            return false;

                        // We'd need to cut this plant first
                        return false;
                    }

                    // If it's haulable, we'd need to haul it first
                    return false;
                }
            }

            // Final checks for sowing
            return plantDef.CanNowPlantAt(cell, map) &&
                   PlantUtility.GrowthSeasonNow(cell, map, true) &&
                   pawn.CanReserve(cell, 1, -1, null, forced);
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_sowableCellsCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
            ResetStaticData();
        }

        public override string ToString()
        {
            return "JobGiver_GrowerSow_PawnControl";
        }
    }
}