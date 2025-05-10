using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Base abstract class for all plant cutting job modules
    /// </summary>
    public abstract class JobModule_PlantCutting : JobModule<Plant>
    {
        // Simple fixed priority (higher number = higher priority)
        public override float Priority => 5.2f;

        /// <summary>
        /// Gets the work type this module is associated with
        /// </summary>
        public override string WorkTypeName => "PlantCutting";

        /// <summary>
        /// Fast filter check for plant cutters
        /// </summary>
        public override bool QuickFilterCheck(Pawn pawn)
        {
            return !pawn.WorkTagIsDisabled(WorkTags.PlantWork);
        }

        public override bool WorkTypeApplies(Pawn pawn)
        {
            // Quick check based on work settings
            return pawn?.workSettings?.WorkIsActive(WorkTypeDefOf.PlantCutting) == true;
        }

        /// <summary>
        /// Default cache update interval - 4 seconds for plant cutting jobs
        /// </summary>
        public override int CacheUpdateInterval => 240; // Update every 4 seconds

        /// <summary>
        /// Relevant ThingRequestGroups for plant cutting jobs
        /// </summary>
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups { get; } =
            new HashSet<ThingRequestGroup> { ThingRequestGroup.Plant };

        /// <summary>
        /// Filter function to identify targets for this job (specifically named for plant cutting jobs)
        /// </summary>
        public abstract bool ShouldProcessPlant(Plant plant, Map map);

        /// <summary>
        /// Filter function implementation that calls the plant cutting-specific method
        /// </summary>
        public override bool ShouldProcessTarget(Plant target, Map map)
            => ShouldProcessPlant(target, map);

        /// <summary>
        /// Validates if the pawn can perform this job on the target
        /// </summary>
        protected abstract bool ValidatePlantCuttingJob(Plant target, Pawn pawn);

        /// <summary>
        /// Validates job implementation that calls the plant cutting-specific method
        /// </summary>
        public override bool ValidateJob(Plant target, Pawn actor)
            => ValidatePlantCuttingJob(target, actor);

        /// <summary>
        /// Creates the job for the pawn to perform on the target plant
        /// </summary>
        public override Job CreateJob(Pawn actor, Plant target)
            => CreatePlantCuttingJob(actor, target);

        /// <summary>
        /// Plant cutting-specific implementation of job creation
        /// </summary>
        protected abstract Job CreatePlantCuttingJob(Pawn pawn, Plant plant);

        /// <summary>
        /// Helper method to check if a plant needs cutting or harvesting
        /// </summary>
        protected bool PlantNeedsCutting(Plant plant, Map map)
        {
            if (plant == null || !plant.Spawned || map == null)
                return false;

            // Check if plant is designated for cutting or harvesting
            if (map.designationManager.DesignationOn(plant, DesignationDefOf.CutPlant) != null ||
                map.designationManager.DesignationOn(plant, DesignationDefOf.HarvestPlant) != null)
                return true;

            // Check if plant is in a growing zone that allows cutting
            var zone = map.zoneManager.ZoneAt(plant.Position) as Zone_Growing;
            return zone != null && zone.allowCut;
        }

        /// <summary>
        /// Helper method to check if pawn is capable and willing to cut the plant
        /// </summary>
        protected bool CanCutPlant(Plant plant, Pawn pawn)
        {
            if (plant == null || pawn == null || !plant.Spawned || !pawn.Spawned || pawn.Map != plant.Map)
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
        /// Default cache update: collect every plant that
        /// satisfies ShouldProcessPlant. Uses progressive scanning for better performance.
        /// </summary>
        public override void UpdateCache(Map map, List<Plant> targetCache)
        {
            if (map == null) return;

            // Use progressive cache update with the appropriate filter
            UpdateCacheProgressively(
                map,
                targetCache,
                ref _lastUpdateTick,
                RelevantThingRequestGroups,
                plant => ShouldProcessPlant(plant, map),
                null,
                CacheUpdateInterval
            );
        }

        /// <summary>
        /// Alternative cache update with manual filtering by designation or zone
        /// </summary>
        protected void UpdateCacheWithDesignationsAndZones(Map map, List<Plant> targetCache)
        {
            if (map == null) return;

            targetCache.Clear();

            // 1) Add plants with cut/harvest designations
            foreach (var des in map.designationManager.AllDesignations)
            {
                if (des.def == DesignationDefOf.CutPlant || des.def == DesignationDefOf.HarvestPlant)
                {
                    if (des.target.Thing is Plant p && ShouldProcessPlant(p, map))
                        targetCache.Add(p);
                }
            }

            // 2) Add plants in growing zones with allowCut enabled
            foreach (var zone in map.zoneManager.AllZones.OfType<Zone_Growing>())
            {
                if (!zone.allowCut) continue;

                foreach (var cell in zone.Cells)
                {
                    var p = cell.GetPlant(map);
                    if (p != null && !targetCache.Contains(p) && ShouldProcessPlant(p, map))
                        targetCache.Add(p);
                }
            }
        }

        // Track last update tick for progressive updates
        private static int _lastUpdateTick = -999;

        /// <summary>
        /// Reset any static data when language changes or game reloads
        /// </summary>
        public override void ResetStaticData()
        {
            _lastUpdateTick = -999;
        }
    }
}