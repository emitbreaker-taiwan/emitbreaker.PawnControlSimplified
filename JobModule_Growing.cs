using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Base abstract class for all plant growing job modules
    /// </summary>
    public abstract class JobModule_Growing : JobModule<Plant>
    {
        // Simple fixed priority (higher number = higher priority)
        public override float Priority => 5.8f;

        /// <summary>
        /// Gets the work type this module is associated with
        /// </summary>
        public override string WorkTypeName => "Growing";

        /// <summary>
        /// Fast filter check for growers
        /// </summary>
        public override bool QuickFilterCheck(Pawn pawn)
        {
            return !pawn.WorkTagIsDisabled(WorkTags.PlantWork);
        }

        public override bool WorkTypeApplies(Pawn pawn)
        {
            // Quick check based on work settings
            return pawn?.workSettings?.WorkIsActive(WorkTypeDefOf.Growing) == true;
        }

        /// <summary>
        /// Default cache update interval - 5 seconds for growing jobs
        /// </summary>
        public override int CacheUpdateInterval => 300; // Update every 5 seconds

        /// <summary>
        /// Translation strings used by growing jobs
        /// </summary>
        protected static string CantSowCavePlantBecauseOfLightTrans;
        protected static string CantSowCavePlantBecauseUnroofedTrans;

        /// <summary>
        /// Initialize or reset translation strings
        /// </summary>
        public override void ResetStaticData()
        {
            _lastUpdateTick = -999;
            CantSowCavePlantBecauseOfLightTrans = (string)"CantSowCavePlantBecauseOfLight".Translate();
            CantSowCavePlantBecauseUnroofedTrans = (string)"CantSowCavePlantBecauseUnroofed".Translate();
        }

        /// <summary>
        /// Filter function to identify plants for growing jobs 
        /// </summary>
        public abstract bool ShouldProcessGrowingTarget(Plant plant, Map map);

        /// <summary>
        /// Filter function implementation that calls the growing-specific method
        /// </summary>
        public override bool ShouldProcessTarget(Plant plant, Map map)
            => ShouldProcessGrowingTarget(plant, map);

        /// <summary>
        /// Validates if the pawn can perform this growing job on the target
        /// </summary>
        public abstract bool ValidateGrowingJob(Plant plant, Pawn grower);

        /// <summary>
        /// Validates job implementation that calls the growing-specific method
        /// </summary>
        public override bool ValidateJob(Plant plant, Pawn actor)
            => ValidateGrowingJob(plant, actor);

        /// <summary>
        /// Creates the job for the grower to perform on the target
        /// </summary>
        public override Job CreateJob(Pawn actor, Plant plant)
            => CreateGrowingJob(actor, plant);

        /// <summary>
        /// Growing-specific implementation of job creation
        /// </summary>
        protected abstract Job CreateGrowingJob(Pawn grower, Plant plant);

        /// <summary>
        /// Helper method to check if a cell needs sowing
        /// </summary>
        protected bool CellNeedsSowing(IntVec3 cell, Map map)
        {
            if (!cell.InBounds(map))
                return false;

            // Check if the cell is in a growing zone with sowing allowed
            Zone_Growing growZone = map.zoneManager.ZoneAt(cell) as Zone_Growing;
            if (growZone != null && growZone.allowSow && growZone.GetPlantDefToGrow() != null)
            {
                return IsGoodSowingCell(cell, map, growZone.GetPlantDefToGrow());
            }

            // Check if the cell is in a hydroponics basin or planter
            Building_PlantGrower plantGrower = map.edificeGrid[cell] as Building_PlantGrower;
            if (plantGrower != null && plantGrower.GetPlantDefToGrow() != null && plantGrower.CanAcceptSowNow())
            {
                return IsGoodSowingCell(cell, map, plantGrower.GetPlantDefToGrow());
            }

            return false;
        }

        /// <summary>
        /// Helper method to check if a cell is suitable for sowing
        /// </summary>
        protected bool IsGoodSowingCell(IntVec3 cell, Map map, ThingDef plantDef)
        {
            if (plantDef == null || !PlantUtility.GrowthSeasonNow(cell, map, true))
                return false;

            // Check if cell already has the plant we want
            List<Thing> thingList = cell.GetThingList(map);
            for (int i = 0; i < thingList.Count; i++)
            {
                if (thingList[i].def == plantDef)
                    return false;
            }

            // Check for blocking things that need to be cleared
            for (int i = 0; i < thingList.Count; i++)
            {
                if (thingList[i].def.BlocksPlanting())
                    return true; // We need to clear something first
            }

            // Additional checks for special plant types
            if (plantDef.plant.cavePlant)
            {
                if (!cell.Roofed(map))
                    return false;
                if (map.glowGrid.GroundGlowAt(cell, true) > 0.0f)
                    return false;
            }

            // Check if plant interferes with roof
            if (plantDef.plant.interferesWithRoof && cell.Roofed(map))
                return false;

            // Final check - can we plant this here?
            return plantDef.CanNowPlantAt(cell, map);
        }

        /// <summary>
        /// Helper method to check if a plant needs harvesting
        /// </summary>
        protected bool PlantReadyToHarvest(Plant plant)
        {
            return plant != null && plant.Spawned &&
                   !plant.IsForbidden(Faction.OfPlayer) &&
                   plant.HarvestableNow &&
                   !PlantUtility.TreeMarkedForExtraction(plant);
        }

        /// <summary>
        /// Helper method to check if pawn can do growing work at the cell
        /// </summary>
        protected bool CanWorkAtCell(IntVec3 cell, Pawn grower)
        {
            if (!cell.InBounds(grower.Map) || cell.IsForbidden(grower))
                return false;

            // Check if cell can be reserved
            if (!grower.CanReserve(cell))
                return false;

            // Check if pawn can reach the cell
            return grower.CanReach(cell, PathEndMode.Touch, grower.NormalMaxDanger());
        }

        /// <summary>
        /// Helper method to determine what plant should be grown at a cell
        /// </summary>
        protected ThingDef GetPlantDefToGrow(IntVec3 cell, Map map)
        {
            // Check growing zones first
            Zone_Growing zone = map.zoneManager.ZoneAt(cell) as Zone_Growing;
            if (zone?.allowSow == true)
                return zone.GetPlantDefToGrow();

            // Check plant growers (hydroponics, etc.)
            Building_PlantGrower grower = map.edificeGrid[cell] as Building_PlantGrower;
            if (grower != null)
                return grower.GetPlantDefToGrow();

            return null;
        }

        /// <summary>
        /// Check if a pawn meets the skill requirements to sow a specific plant
        /// </summary>
        protected bool PawnHasEnoughSkillToSow(Pawn pawn, ThingDef plantDef)
        {
            if (plantDef == null || pawn == null || plantDef.plant.sowMinSkill <= 0)
                return true;

            int skillLevel = pawn.skills?.GetSkill(SkillDefOf.Plants).Level
                            ?? pawn.RaceProps.mechFixedSkillLevel;

            return skillLevel >= plantDef.plant.sowMinSkill;
        }

        /// <summary>
        /// Default cache update for growing targets
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
                plant => ShouldProcessTarget(plant, map),
                null,
                CacheUpdateInterval
            );
        }

        /// <summary>
        /// Override with specific ThingRequestGroups to scan
        /// </summary>
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups { get; } =
            new HashSet<ThingRequestGroup> { ThingRequestGroup.Plant };

        // Track last update tick for progressive updates
        private static int _lastUpdateTick = -999;
    }
}