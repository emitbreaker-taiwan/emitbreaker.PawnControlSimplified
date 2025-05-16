using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Base class for plant cutting job givers with specialized cache management
    /// </summary>
    public abstract class JobGiver_PlantCutting_PawnControl : JobGiver_Scan_PawnControl
    {
        #region Configuration

        /// <summary>
        /// The work tag for plant cutting job givers
        /// </summary>
        public override string WorkTag => "PlantCutting";

        /// <summary>
        /// Distance thresholds for bucketing (15, 25, 40 tiles squared)
        /// </summary>
        protected virtual float[] DistanceThresholds => new float[] { 225f, 625f, 1600f };

        /// <summary>
        /// Required capabilities tag for non-humanlike pawns
        /// </summary>
        public override PawnEnumTags RequiredTag => PawnEnumTags.AllowWork_PlantCutting;

        /// <summary>
        /// The job definition for plant cutting
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.CutPlant;

        /// <summary>
        /// Cache update interval (in ticks)
        /// </summary>
        protected override int CacheUpdateInterval => base.CacheUpdateInterval;

        /// <summary>
        /// Plant cutting typically requires a zone or designated plant
        /// </summary>
        public override bool RequiresMapZoneorArea => true;

        /// <summary>
        /// Cache key suffix for plants needing cutting
        /// </summary>
        private const string PLANTS_CACHE_SUFFIX = "_Plants";

        #endregion

        #region Cache System

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_PlantCutting_PawnControl() : base()
        {
            // Base constructor already initializes the cache system with this job giver's type
        }

        /// <summary>
        /// Reset the cache - implements IResettableCache
        /// </summary>
        public override void Reset()
        {
            // Call base reset first
            base.Reset();

            // Clear any plant-specific caches for all maps
            foreach (int mapId in Find.Maps.Select(map => map.uniqueID))
            {
                string cacheKey = this.GetType().Name + PLANTS_CACHE_SUFFIX;
                var plantCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Plant>>(mapId);

                if (plantCache.ContainsKey(cacheKey))
                {
                    plantCache.Remove(cacheKey);
                }

                // Clear the update tick record too
                Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, -1);
            }

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Reset plant cache for {this.GetType().Name}");
            }
        }

        #endregion

        #region Core flow

        /// <summary>
        /// Job-specific cache update method that derived classes should override
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            if (map == null)
                yield break;

            // Get plants that need cutting
            List<Plant> plants = GetPlantsNeedingCutting(map).ToList();

            // Store in centralized cache
            StorePlantCache(map, plants);

            // Convert to Things for the base class caching system
            foreach (Plant plant in plants)
            {
                yield return plant;
            }
        }

        /// <summary>
        /// Gets targets for this plant cutting job giver
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null)
                yield break;

            // Get cached plants from centralized cache
            var plants = GetOrCreatePlantCache(map);

            // Return plants as targets
            foreach (Plant plant in plants)
            {
                if (plant != null && !plant.Destroyed && plant.Spawned)
                    yield return plant;
            }
        }

        /// <summary>
        /// Gets or creates a cache of plants needing cutting for a specific map
        /// </summary>
        protected List<Plant> GetOrCreatePlantCache(Map map)
        {
            if (map == null)
                return new List<Plant>();

            int mapId = map.uniqueID;
            string cacheKey = this.GetType().Name + PLANTS_CACHE_SUFFIX;

            // Try to get cached plants from the map cache manager
            var plantCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Plant>>(mapId);

            // Check if we need to update the cache
            int currentTick = Find.TickManager.TicksGame;
            int lastUpdateTick = Utility_MapCacheManager.GetLastCacheUpdateTick(mapId, cacheKey);

            if (currentTick - lastUpdateTick > CacheUpdateInterval ||
                !plantCache.TryGetValue(cacheKey, out List<Plant> plants) ||
                plants == null ||
                plants.Any(p => p == null || p.Destroyed || !p.Spawned))
            {
                // Cache is invalid or expired, rebuild it
                plants = GetPlantsNeedingCutting(map).ToList();

                // Store in the central cache
                StorePlantCache(map, plants);
            }

            return plants;
        }

        /// <summary>
        /// Store a list of plants in the centralized cache
        /// </summary>
        private void StorePlantCache(Map map, List<Plant> plants)
        {
            if (map == null)
                return;

            int mapId = map.uniqueID;
            string cacheKey = this.GetType().Name + PLANTS_CACHE_SUFFIX;
            int currentTick = Find.TickManager.TicksGame;

            var plantCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Plant>>(mapId);
            plantCache[cacheKey] = plants;
            Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, currentTick);

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Updated plant cache for {this.GetType().Name}, found {plants.Count} plants");
            }
        }

        #endregion

        #region Job Processing

        /// <summary>
        /// Process cached plant targets to find the best one
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            if (pawn == null || targets == null || targets.Count == 0)
                return null;

            // Use distance bucketing for more efficient selection
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn, 
                targets, 
                (plantTarget) => (plantTarget.Position - pawn.Position).LengthHorizontalSquared, 
                DistanceThresholds);

            // Get reachability cache from central cache system
            var reachabilityCache = Utility_MapCacheManager.GetOrCreateMapCache<int, Dictionary<Thing, bool>>(pawn.Map.uniqueID);

            // Create cache entry if needed
            int pawnId = pawn.thingIDNumber;
            if (!reachabilityCache.ContainsKey(pawnId))
                reachabilityCache[pawnId] = new Dictionary<Thing, bool>();

            // Find the best plant to process
            Thing bestTarget = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (plantTarget, p) => ValidatePlantTarget(plantTarget as Plant, p),
                WorkTag);

            // Create job if we found a valid target
            if (bestTarget != null && bestTarget is Plant plant)
            {
                return JobMaker.MakeJob(WorkJobDef, plant);
            }

            return null;
        }

        #endregion

        #region Plant‐selection helpers

        /// <summary>
        /// Get plants that need cutting on this map - derived classes can override
        /// with specialized selection logic
        /// </summary>
        protected virtual IEnumerable<Plant> GetPlantsNeedingCutting(Map map)
        {
            if (map == null)
                yield break;

            var plants = new List<Plant>();

            // 1) designated Cut/Harvest
            foreach (var des in map.designationManager.AllDesignations)
            {
                if ((des.def == DesignationDefOf.CutPlant || des.def == DesignationDefOf.HarvestPlant)
                    && des.target.Thing is Plant p)
                {
                    plants.Add(p);
                }
            }

            // 2) zone‐based cutting
            foreach (var zone in map.zoneManager.AllZones.OfType<Zone_Growing>())
            {
                if (!zone.allowCut) continue;
                ThingDef growDef = zone.GetPlantDefToGrow();

                foreach (var cell in zone.Cells)
                {
                    var plant = cell.GetPlant(map);
                    if (plant != null
                        && !plants.Contains(plant)
                        && (growDef == null || plant.def != growDef))
                    {
                        plants.Add(plant);
                    }
                }
            }

            // 3) cap to 200 entries for performance
            int plantCount = plants.Count;
            foreach (Plant plant in (plantCount > 200 ? plants.Take(200) : plants))
            {
                yield return plant;
            }
        }

        /// <summary>
        /// Validate if a plant target is valid for this specific pawn
        /// </summary>
        protected virtual bool ValidatePlantTarget(Plant plant, Pawn pawn)
        {
            if (plant == null || plant.Destroyed || !plant.Spawned)
                return false;

            // Let the base class handle faction-aware reservation checking
            bool isDesignated =
                pawn.Map.designationManager.DesignationOn(plant, DesignationDefOf.CutPlant) != null
                || pawn.Map.designationManager.DesignationOn(plant, DesignationDefOf.HarvestPlant) != null;

            if (isDesignated)
            {
                if (RequiresPlayerFaction && pawn.Faction != Faction.OfPlayer)
                    return false;
            }
            else
            {
                var zone = pawn.Map.zoneManager.ZoneAt(plant.Position) as Zone_Growing;
                if (zone == null || !zone.allowCut || (RequiresPlayerFaction && pawn.Faction != Faction.OfPlayer))
                    return false;
            }

            if (plant.IsForbidden(pawn) || !PlantUtility.PawnWillingToCutPlant_Job(plant, pawn))
                return false;

            return pawn.CanReserve((LocalTargetInfo)plant, 1, -1);
        }

        #endregion

        #region Debug

        /// <summary>
        /// Debug info for logging
        /// </summary>
        public override string ToString()
        {
            return $"JobGiver_PlantCutting_{this.GetType().Name}";
        }

        #endregion
    }
}