using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for plant harvesting operations
    /// </summary>
    public class JobModule_Growing_GrowerHarvest : JobModule_Growing
    {
        // Cache for harvestable plants
        private static readonly Dictionary<int, List<Plant>> _harvestableCache = new Dictionary<int, List<Plant>>();
        private static readonly Dictionary<int, Dictionary<Plant, bool>> _plantReachabilityCache = new Dictionary<int, Dictionary<Plant, bool>>();
        private static int _lastLocalUpdateTick = -999;

        // Module metadata
        public override string UniqueID => "Growing_Harvest";
        public override float Priority => 5.8f; // Higher priority than sowing
        public override string Category => "Growing";

        // Constants for harvesting jobs
        private const float MAX_HARVEST_WORK_PER_JOB = 2400f;
        private const int MAX_PLANTS_PER_JOB = 40;

        // These ThingRequestGroups are what this module cares about
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups =>
            new HashSet<ThingRequestGroup> { ThingRequestGroup.Plant };

        /// <summary>
        /// Reset any static data when language changes or game reloads
        /// </summary>
        public override void ResetStaticData()
        {
            base.ResetStaticData();
            _lastLocalUpdateTick = -999;
            _harvestableCache.Clear();
            _plantReachabilityCache.Clear();
        }

        /// <summary>
        /// Filter function to identify plants for harvesting
        /// </summary>
        public override bool ShouldProcessGrowingTarget(Plant plant, Map map)
        {
            if (plant == null || !plant.Spawned || map == null || plant.IsBurning())
                return false;

            // Basic harvestability check
            if (!plant.HarvestableNow || plant.LifeStage != PlantLifeStage.Mature || !plant.CanYieldNow())
                return false;

            // Check for designated harvest plants
            if (map.designationManager.DesignationOn(plant, DesignationDefOf.HarvestPlant) != null)
                return true;

            // Check for plants in growing zones that match the zone's crop type
            Zone_Growing growZone = map.zoneManager.ZoneAt(plant.Position) as Zone_Growing;
            if (growZone != null)
            {
                ThingDef plantDef = growZone.GetPlantDefToGrow();
                if (plantDef != null && plant.def == plantDef)
                    return true;
            }

            // Check for plants in hydroponics
            Building_PlantGrower plantGrower = map.edificeGrid[plant.Position] as Building_PlantGrower;
            if (plantGrower != null)
            {
                ThingDef plantDef = plantGrower.GetPlantDefToGrow();
                if (plantDef != null && plant.def == plantDef)
                    return true;
            }

            // Skip wild plants
            return false;
        }

        /// <summary>
        /// Validates if the pawn can harvest this plant
        /// </summary>
        public override bool ValidateGrowingJob(Plant plant, Pawn grower)
        {
            if (plant == null || grower == null || !plant.Spawned || !grower.Spawned)
                return false;

            // Skip if plant work is disabled
            if (grower.WorkTagIsDisabled(WorkTags.PlantWork))
                return false;

            // Skip if growing work type isn't active
            if (!WorkTypeApplies(grower))
                return false;

            // Check basic reachability
            if (plant.IsForbidden(grower) || !grower.CanReserve(plant) ||
                !grower.CanReach(plant, PathEndMode.Touch, grower.NormalMaxDanger()))
                return false;

            // Ensure plant is still harvestable
            if (!plant.HarvestableNow || plant.LifeStage != PlantLifeStage.Mature || !plant.CanYieldNow())
                return false;

            // Check if pawn is willing to harvest this plant
            if (!PlantUtility.PawnWillingToCutPlant_Job(plant, grower))
                return false;

            return true;
        }

        /// <summary>
        /// Create a job to harvest the plant and similar nearby plants
        /// </summary>
        protected override Job CreateGrowingJob(Pawn grower, Plant plant)
        {
            Job job = JobMaker.MakeJob(JobDefOf.Harvest);
            job.AddQueuedTarget(TargetIndex.A, plant);

            // Add additional nearby harvestable plants of the same type
            Room startingRoom = plant.Position.GetRoom(plant.Map);
            ThingDef wantedPlantDef = WorkGiver_Grower.CalculateWantedPlantDef(plant.Position, plant.Map);
            float totalHarvestWork = plant.def.plant.harvestWork;
            int plantsAdded = 1;

            // Try to find additional plants in the same room
            foreach (Plant otherPlant in _harvestableCache[plant.Map.uniqueID])
            {
                // Skip the already-added target plant
                if (otherPlant == plant)
                    continue;

                // Skip if we've reached our limits
                if (plantsAdded >= MAX_PLANTS_PER_JOB || totalHarvestWork > MAX_HARVEST_WORK_PER_JOB)
                    break;

                // Skip if plant is not in the same room
                if (otherPlant.Position.GetRoom(plant.Map) != startingRoom)
                    continue;

                // Check if this plant is valid for the job
                if (!ValidateGrowingJob(otherPlant, grower))
                    continue;

                // Only add plants of the wanted type in growing zones
                Zone_Growing zone = otherPlant.Position.GetZone(plant.Map) as Zone_Growing;
                if (zone != null && otherPlant.def != wantedPlantDef)
                    continue;

                // Add this plant to the job
                job.AddQueuedTarget(TargetIndex.A, otherPlant);
                totalHarvestWork += otherPlant.def.plant.harvestWork;
                plantsAdded++;
            }

            // Sort targets by distance for efficiency
            if (job.targetQueueA != null && job.targetQueueA.Count >= 3)
            {
                job.targetQueueA.SortBy<LocalTargetInfo, int>((targ) => targ.Cell.DistanceToSquared(grower.Position));
            }

            Utility_DebugManager.LogNormal($"{grower.LabelShort} created job to harvest {plantsAdded} plants starting with {plant.Label}");
            return job;
        }

        /// <summary>
        /// Update cache with plants that need harvesting
        /// </summary>
        public override void UpdateCache(Map map, List<Plant> targetCache)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastLocalUpdateTick + CacheUpdateInterval ||
                !_harvestableCache.ContainsKey(mapId))
            {
                // Initialize or clear lists
                if (!_harvestableCache.ContainsKey(mapId))
                    _harvestableCache[mapId] = new List<Plant>();
                else
                    _harvestableCache[mapId].Clear();

                // Clear reachability cache too
                if (_plantReachabilityCache.ContainsKey(mapId))
                    _plantReachabilityCache[mapId].Clear();
                else
                    _plantReachabilityCache[mapId] = new Dictionary<Plant, bool>();

                // 1) Add designated plants first
                foreach (var des in map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.HarvestPlant))
                {
                    if (des.target.Thing is Plant plant && ShouldProcessGrowingTarget(plant, map))
                    {
                        _harvestableCache[mapId].Add(plant);
                    }
                }

                // 2) Add plants in growing zones and hydroponics
                foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Plant))
                {
                    Plant plant = thing as Plant;
                    if (plant == null || _harvestableCache[mapId].Contains(plant))
                        continue;

                    if (ShouldProcessGrowingTarget(plant, map))
                    {
                        _harvestableCache[mapId].Add(plant);
                    }
                }

                // Update target cache for providers
                if (targetCache != null)
                {
                    targetCache.Clear();
                    targetCache.AddRange(_harvestableCache[mapId]);
                }

                _lastLocalUpdateTick = currentTick;

                // Set whether module has targets
                SetHasTargets(map, _harvestableCache[mapId].Count > 0);
            }
            else if (targetCache != null)
            {
                // Just update the provided target cache from our cached list
                targetCache.Clear();
                if (_harvestableCache.ContainsKey(mapId))
                {
                    targetCache.AddRange(_harvestableCache[mapId]);
                }
            }
        }

        /// <summary>
        /// Find a harvestable plant for the given grower
        /// </summary>
        public Plant FindBestHarvestTarget(Pawn grower)
        {
            if (grower?.Map == null) return null;

            // Update the cache first
            UpdateCache(grower.Map, null);

            int mapId = grower.Map.uniqueID;
            if (!_harvestableCache.ContainsKey(mapId) || _harvestableCache[mapId].Count == 0)
                return null;

            // Use distance bucketing for efficient selection
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                grower,
                _harvestableCache[mapId],
                (plant) => (plant.Position - grower.Position).LengthHorizontalSquared,
                new float[] { 225f, 625f, 1600f } // 15, 25, 40 tiles
            );

            // Find the best plant to harvest
            return Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                grower,
                (plant, pawn) => ValidateGrowingJob(plant, pawn),
                _plantReachabilityCache[mapId]
            );
        }

        /// <summary>
        /// Create a job for harvesting for a specific grower
        /// </summary>
        public Job CreateHarvestJob(Pawn grower)
        {
            if (grower == null) return null;

            // Find a valid harvest target
            Plant targetPlant = FindBestHarvestTarget(grower);
            if (targetPlant == null) return null;

            // Create the job
            return CreateGrowingJob(grower, targetPlant);
        }
    }
}