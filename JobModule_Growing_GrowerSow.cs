using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for plant sowing operations
    /// </summary>
    public class JobModule_Growing_GrowerSow : JobModule_Growing
    {
        // Reference to the common implementation
        private readonly JobModule_Common_Grower_Adapter _commonImpl;

        // We need a field rather than a property to use with ref parameters
        private static int _lastLocalUpdateTick = -999;

        // Module metadata
        public override string UniqueID => "Growing_Sow";
        public override float Priority => 5.7f; // Same priority as the original JobGiver_GrowerSow
        public override string Category => "Growing";

        // These ThingRequestGroups are what this module cares about
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups =>
            new HashSet<ThingRequestGroup> { ThingRequestGroup.Plant };

        // Constructor initializes the common implementation
        public JobModule_Growing_GrowerSow()
        {
            _commonImpl = new JobModule_Common_Grower_Adapter(this);
        }

        /// <summary>
        /// Reset any static data when language changes or game reloads
        /// </summary>
        public override void ResetStaticData()
        {
            base.ResetStaticData();
            _commonImpl.ResetStaticData();
            _lastLocalUpdateTick = -999;
        }

        /// <summary>
        /// Filter function to identify plants for growing jobs 
        /// </summary>
        public override bool ShouldProcessGrowingTarget(Plant plant, Map map)
        {
            // We don't process existing plants in the sow module
            return false;
        }

        /// <summary>
        /// Validates if the pawn can perform this growing job on the target
        /// </summary>
        public override bool ValidateGrowingJob(Plant plant, Pawn grower)
        {
            // We don't process existing plants in the sow module
            return false;
        }

        /// <summary>
        /// Growing-specific implementation of job creation
        /// </summary>
        protected override Job CreateGrowingJob(Pawn grower, Plant plant)
        {
            // We don't process existing plants in the sow module
            return null;
        }

        /// <summary>
        /// Update cache with cells that need sowing
        /// </summary>
        public override void UpdateCache(Map map, List<Plant> targetCache)
        {
            // Clear the plant target cache since we use cells instead
            targetCache?.Clear();

            // Update our cells cache
            _commonImpl.UpdateGrowingCellCache(map);

            // Mark cache as updated
            _lastLocalUpdateTick = Find.TickManager.TicksGame;
        }

        /// <summary>
        /// Create a job to sow at a cell
        /// </summary>
        public Job CreateSowJob(Pawn grower)
        {
            return _commonImpl.CreateJobFor(grower);
        }

        /// <summary>
        /// Adapter class for JobModule_Common_Grower
        /// </summary>
        private class JobModule_Common_Grower_Adapter : JobModule_Common_Grower
        {
            private readonly JobModule_Growing_GrowerSow _outer;

            public JobModule_Common_Grower_Adapter(JobModule_Growing_GrowerSow outer)
            {
                _outer = outer;
            }

            // Required implementations from JobModuleCore
            public override string UniqueID => _outer.UniqueID + "_Common";
            public override float Priority => _outer.Priority;
            public override string WorkTypeName => _outer.WorkTypeName;
            public override string Category => _outer.Category;

            /// <summary>
            /// Check if this cell needs sowing
            /// </summary>
            protected override bool ShouldProcessGrowingCell(IntVec3 cell, Map map)
            {
                if (!cell.InBounds(map))
                    return false;

                // Check if the cell is in a growing zone with sowing allowed
                Zone_Growing growZone = map.zoneManager.ZoneAt(cell) as Zone_Growing;
                if (growZone != null && growZone.allowSow && growZone.GetPlantDefToGrow() != null)
                {
                    return ValidateSowingCell(cell, map, growZone.GetPlantDefToGrow());
                }

                // Check if the cell is in a hydroponics basin or planter
                Building_PlantGrower plantGrower = map.edificeGrid[cell] as Building_PlantGrower;
                if (plantGrower != null && plantGrower.GetPlantDefToGrow() != null && plantGrower.CanAcceptSowNow())
                {
                    return ValidateSowingCell(cell, map, plantGrower.GetPlantDefToGrow());
                }

                return false;
            }

            /// <summary>
            /// Check if a cell can be sown with the specific plant
            /// </summary>
            private bool ValidateSowingCell(IntVec3 cell, Map map, ThingDef plantDef)
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

                return true;
            }

            /// <summary>
            /// Check if a cell should be included in the growing cache
            /// </summary>
            protected override bool ValidateCellContents(IntVec3 cell, Map map, ThingDef plantDef)
            {
                if (plantDef == null)
                    return false;

                // Check growth season if required
                if (CheckGrowthSeasonNow && !PlantUtility.GrowthSeasonNow(cell, map, true))
                    return false;

                // Check for existing plants or obstacles
                List<Thing> thingList = cell.GetThingList(map);

                // Skip if exact plant type already exists
                for (int i = 0; i < thingList.Count; i++)
                {
                    if (thingList[i].def == plantDef)
                        return false;
                }

                // Either we need to clear obstacles first or the cell is ready for sowing
                return true;
            }

            /// <summary>
            /// Create a job to sow plants at this cell
            /// </summary>
            public override Job CreateGrowerJob(Pawn grower, IntVec3 cell)
            {
                ThingDef plantDef = GetPlantDefForCell(cell, grower.Map);
                if (plantDef == null)
                    return null;

                List<Thing> thingList = cell.GetThingList(grower.Map);

                // Check for plants that need to be cut first
                for (int i = 0; i < thingList.Count; i++)
                {
                    Thing thing = thingList[i];
                    if (thing.def.category == ThingCategory.Plant && thing.def.BlocksPlanting())
                    {
                        // We need to cut this plant first
                        if (thing.IsForbidden(grower) || !grower.CanReserve(thing, 1, -1, null, false))
                            return null;

                        if (!PlantUtility.PawnWillingToCutPlant_Job(thing, grower))
                            return null;

                        // Create the cut plant job
                        Job cutJob = JobMaker.MakeJob(JobDefOf.CutPlant, thing);
                        Utility_DebugManager.LogNormal($"{grower.LabelShort} created job to cut {thing.Label} before sowing");
                        return cutJob;
                    }
                }

                // Check for obstacles that need to be cleared
                for (int i = 0; i < thingList.Count; i++)
                {
                    if (thingList[i].def.BlocksPlanting())
                    {
                        if (thingList[i].def.category == ThingCategory.Filth)
                        {
                            // Create job to clean filth
                            Job cleanJob = JobMaker.MakeJob(JobDefOf.Clean, thingList[i]);
                            Utility_DebugManager.LogNormal($"{grower.LabelShort} created job to clean {thingList[i].Label} before sowing");
                            return cleanJob;
                        }
                        else if (thingList[i].def.EverHaulable)
                        {
                            // Create job to haul obstacle
                            Job haulJob = HaulAIUtility.HaulAsideJobFor(grower, thingList[i]);
                            if (haulJob != null)
                            {
                                Utility_DebugManager.LogNormal($"{grower.LabelShort} created job to haul {thingList[i].Label} before sowing");
                                return haulJob;
                            }
                        }
                    }
                }

                // Skill check for sowing
                if (plantDef.plant.sowMinSkill > 0)
                {
                    int skillLevel = grower.skills?.GetSkill(SkillDefOf.Plants).Level ?? 0;
                    if (skillLevel < plantDef.plant.sowMinSkill)
                    {
                        JobFailReason.Is(MissingSkillTrans, plantDef.plant.sowMinSkill.ToString());
                        return null;
                    }
                }

                // Special checks for cave plants
                if (plantDef.plant.cavePlant)
                {
                    if (!cell.Roofed(grower.Map))
                    {
                        JobFailReason.Is(CantSowCavePlantBecauseUnroofedTrans);
                        return null;
                    }
                    if (grower.Map.glowGrid.GroundGlowAt(cell, true) > 0.0f)
                    {
                        JobFailReason.Is(CantSowCavePlantBecauseOfLightTrans);
                        return null;
                    }
                }

                // Create the sow job
                Job job = JobMaker.MakeJob(JobDefOf.Sow, cell);
                job.plantDefToSow = plantDef;
                Utility_DebugManager.LogNormal($"{grower.LabelShort} created job to sow {plantDef.label} at {cell}");
                return job;
            }
        }
    }
}