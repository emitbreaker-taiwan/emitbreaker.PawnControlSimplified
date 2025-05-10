using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Combined module for plant cutting and harvesting tasks
    /// </summary>
    public class JobModule_PlantCutting_PlantsCut : JobModule_PlantCutting
    {
        // We need a field rather than a property to use with ref parameters
        private static int _lastLocalUpdateTick = -999;

        // Cache for targets
        private static readonly Dictionary<int, List<Plant>> _targetCache =
            new Dictionary<int, List<Plant>>();

        // Module metadata
        public override string UniqueID => "PlantCutting_PlantsCut";
        public override float Priority => 5.8f;
        public override string Category => "PlantCutting";

        /// <summary>
        /// Reset any static data when language changes or game reloads
        /// </summary>
        public override void ResetStaticData()
        {
            base.ResetStaticData();
            _lastLocalUpdateTick = -999;
            _targetCache.Clear();
        }

        /// <summary>
        /// Check if this plant should be processed for cutting or harvesting
        /// </summary>
        public override bool ShouldProcessPlant(Plant plant, Map map)
        {
            if (plant == null || !plant.Spawned || map == null || plant.IsBurning())
                return false;

            // Skip trees (handled by ExtractTree)
            if (plant.def.plant.IsTree)
                return false;

            // Check for harvest designation
            if (map.designationManager.DesignationOn(plant, DesignationDefOf.HarvestPlant) != null)
            {
                // Only include if it's actually harvestable
                return plant.HarvestableNow;
            }

            // Check for cut designation
            if (map.designationManager.DesignationOn(plant, DesignationDefOf.CutPlant) != null)
                return true;

            // Check if plant is in a growing zone that allows cutting
            var zone = map.zoneManager.ZoneAt(plant.Position) as Zone_Growing;
            if (zone != null && zone.allowCut)
                return true;

            return false;
        }

        /// <summary>
        /// Validate if pawn can cut/harvest this plant
        /// </summary>
        protected override bool ValidatePlantCuttingJob(Plant plant, Pawn pawn)
        {
            if (plant == null || pawn == null || !plant.Spawned || !pawn.Spawned)
                return false;

            // Check if plant work is enabled
            if (pawn.WorkTagIsDisabled(WorkTags.PlantWork))
                return false;

            // Check if pawn is assigned to plant cutting
            if (!WorkTypeApplies(pawn))
                return false;

            // Skip if plant is forbidden
            if (plant.IsForbidden(pawn))
                return false;

            // Skip if plant is claimed by someone else
            if (!pawn.CanReserve(plant))
                return false;

            // Check if pawn is willing to cut this plant type
            if (!PlantUtility.PawnWillingToCutPlant_Job(plant, pawn))
                return false;

            // Check if pawn can reach the plant
            return pawn.CanReach(plant, PathEndMode.Touch, pawn.NormalMaxDanger());
        }

        /// <summary>
        /// Create a job to cut or harvest this plant based on its designation
        /// </summary>
        protected override Job CreatePlantCuttingJob(Pawn pawn, Plant plant)
        {
            // Check for harvest designation first (higher priority)
            if (plant.Map.designationManager.DesignationOn(plant, DesignationDefOf.HarvestPlant) != null && plant.HarvestableNow)
            {
                Job job = JobMaker.MakeJob(JobDefOf.HarvestDesignated, plant);
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to harvest {plant.Label} at {plant.Position}");
                return job;
            }

            // Otherwise, it's a cut job
            Job cutJob = JobMaker.MakeJob(JobDefOf.CutPlantDesignated, plant);
            Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to cut {plant.Label} at {plant.Position}");
            return cutJob;
        }

        /// <summary>
        /// Update the cache of plants to cut or harvest
        /// </summary>
        public override void UpdateCache(Map map, List<Plant> targetCache)
        {
            if (map == null) return;

            // Clear the target cache
            targetCache.Clear();

            // Use both designated collections for efficiency
            // 1. First, process harvest designations (higher priority)
            foreach (Designation designation in map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.HarvestPlant))
            {
                if (designation.target.Thing is Plant plant && ShouldProcessPlant(plant, map))
                {
                    targetCache.Add(plant);
                }
            }

            // 2. Then process cut designations
            foreach (Designation designation in map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.CutPlant))
            {
                if (designation.target.Thing is Plant plant && ShouldProcessPlant(plant, map))
                {
                    // Avoid duplicates
                    if (!targetCache.Contains(plant))
                    {
                        targetCache.Add(plant);
                    }
                }
            }

            // 3. Check growing zones with allowCut enabled
            if (map.zoneManager.AllZones != null)
            {
                foreach (var zone in map.zoneManager.AllZones.OfType<Zone_Growing>())
                {
                    if (!zone.allowCut) continue;

                    foreach (var cell in zone.Cells)
                    {
                        var plant = cell.GetPlant(map);
                        if (plant != null && !plant.def.plant.IsTree && !targetCache.Contains(plant) && ShouldProcessPlant(plant, map))
                            targetCache.Add(plant);
                    }
                }
            }

            // Store in the static cache for future reference
            int mapId = map.uniqueID;
            if (!_targetCache.ContainsKey(mapId))
                _targetCache[mapId] = new List<Plant>();
            else
                _targetCache[mapId].Clear();

            _targetCache[mapId].AddRange(targetCache);
            _lastLocalUpdateTick = Find.TickManager.TicksGame;

            // Set whether this module has any targets
            SetHasTargets(map, targetCache.Count > 0);
        }
    }
}