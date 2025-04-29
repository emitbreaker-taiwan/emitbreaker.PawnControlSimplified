using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse.AI;
using Verse;

namespace emitbreaker.PawnControl
{
    public class PawnControl_WorkGiver_GrowerSow : WorkGiver_Grower
    {
        protected static string CantSowCavePlantBecauseOfLightTrans;
        protected static string CantSowCavePlantBecauseUnroofedTrans;

        public override PathEndMode PathEndMode => PathEndMode.ClosestTouch;

        public static void ResetStaticData()
        {
            CantSowCavePlantBecauseOfLightTrans = "CantSowCavePlantBecauseOfLight".Translate();
            CantSowCavePlantBecauseUnroofedTrans = "CantSowCavePlantBecauseUnroofed".Translate();
        }

        protected override bool ExtraRequirements(IPlantToGrowSettable settable, Pawn pawn)
        {
            if (!settable.CanAcceptSowNow()) return false;

            IntVec3 c;
            if (settable is Zone_Growing zone_Growing)
            {
                if (!zone_Growing.allowSow)
                {
                    return false;
                }

                c = zone_Growing.Cells[0];
            }
            else
            {
                c = ((Thing)settable).Position;
            }

            wantedPlantDef = CalculateWantedPlantDef(c, pawn.Map);
            if (wantedPlantDef == null)
            {
                return false;
            }

            return true;
        }

        public override Job JobOnCell(Pawn pawn, IntVec3 c, bool forced = false)
        {
            Map map = pawn.Map;
            if (c.IsForbidden(pawn))
            {
                return null;
            }

            if (!PlantUtility.GrowthSeasonNow(c, map, forSowing: true))
            {
                return null;
            }

            if (wantedPlantDef == null)
            {
                wantedPlantDef = CalculateWantedPlantDef(c, map);
                if (wantedPlantDef == null)
                {
                    return null;
                }
            }

            List<Thing> thingList = c.GetThingList(map);
            Zone_Growing zone_Growing = c.GetZone(map) as Zone_Growing;
            bool flag = false;
            for (int i = 0; i < thingList.Count; i++)
            {
                Thing thing = thingList[i];
                if (thing.def == wantedPlantDef)
                {
                    return null;
                }

                if ((thing is Blueprint || thing is Frame) && thing.Faction == pawn.Faction)
                {
                    flag = true;
                }
            }

            if (flag)
            {
                Thing edifice = c.GetEdifice(map);
                if (edifice == null || edifice.def.fertility < 0f)
                {
                    return null;
                }
            }

            if (wantedPlantDef.plant.cavePlant)
            {
                if (!c.Roofed(map))
                {
                    JobFailReason.Is(CantSowCavePlantBecauseUnroofedTrans);
                    return null;
                }

                if (map.glowGrid.GroundGlowAt(c, ignoreCavePlants: true) > 0f)
                {
                    JobFailReason.Is(CantSowCavePlantBecauseOfLightTrans);
                    return null;
                }
            }

            if (wantedPlantDef.plant.interferesWithRoof && c.Roofed(pawn.Map))
            {
                return null;
            }

            Plant plant = c.GetPlant(map);
            if (plant != null && plant.def.plant.blockAdjacentSow)
            {
                if (!pawn.CanReserve(plant, 1, -1, null, forced) || plant.IsForbidden(pawn))
                {
                    return null;
                }

                if (zone_Growing != null && !zone_Growing.allowCut)
                {
                    return null;
                }

                if (!Utility_ThinkTreeManager.PawnWillingToCutPlant_Job(plant, pawn))
                {
                    return null;
                }

                return JobMaker.MakeJob(Utility_Common.JobDefNamed("PawnControl_CutPlant"), plant);
            }

            Thing thing2 = PlantUtility.AdjacentSowBlocker(wantedPlantDef, c, map);
            if (thing2 != null)
            {
                if (thing2 is Plant plant2 && pawn.CanReserveAndReach(plant2, PathEndMode.Touch, Danger.Deadly, 1, -1, null, forced) && !plant2.IsForbidden(pawn))
                {
                    IPlantToGrowSettable plantToGrowSettable = plant2.Position.GetPlantToGrowSettable(plant2.Map);
                    if (plantToGrowSettable == null || plantToGrowSettable.GetPlantDefToGrow() != plant2.def)
                    {
                        Zone_Growing zone_Growing2 = c.GetZone(map) as Zone_Growing;
                        Zone_Growing zone_Growing3 = plant2.Position.GetZone(map) as Zone_Growing;
                        if ((zone_Growing2 != null && !zone_Growing2.allowCut) || (zone_Growing3 != null && !zone_Growing3.allowCut && plant2.def == zone_Growing3.GetPlantDefToGrow()))
                        {
                            return null;
                        }

                        if (PlantUtility.TreeMarkedForExtraction(plant2))
                        {
                            return null;
                        }

                        if (!Utility_ThinkTreeManager.PawnWillingToCutPlant_Job(plant2, pawn))
                        {
                            return null;
                        }

                        return JobMaker.MakeJob(Utility_Common.JobDefNamed("PawnControl_CutPlant"), plant2);
                    }
                }

                return null;
            }

            if (wantedPlantDef.plant.sowMinSkill > 0 && ((pawn.skills != null && Utility_SkillManager.SetInjectedSkillLevel(pawn, SkillDefOf.Plants) < wantedPlantDef.plant.sowMinSkill) || (pawn.IsColonyMech && pawn.RaceProps.mechFixedSkillLevel < wantedPlantDef.plant.sowMinSkill)))
            {
                JobFailReason.Is("UnderAllowedSkill".Translate(wantedPlantDef.plant.sowMinSkill), def.label);
                return null;
            }

            for (int j = 0; j < thingList.Count; j++)
            {
                Thing thing3 = thingList[j];
                if (!thing3.def.BlocksPlanting())
                {
                    continue;
                }

                if (!pawn.CanReserve(thing3, 1, -1, null, forced))
                {
                    return null;
                }

                if (thing3.def.category == ThingCategory.Plant)
                {
                    if (!thing3.IsForbidden(pawn))
                    {
                        if (zone_Growing != null && !zone_Growing.allowCut)
                        {
                            return null;
                        }

                        if (!Utility_ThinkTreeManager.PawnWillingToCutPlant_Job(thing3, pawn))
                        {
                            return null;
                        }

                        if (PlantUtility.TreeMarkedForExtraction(thing3))
                        {
                            return null;
                        }

                        return JobMaker.MakeJob(Utility_Common.JobDefNamed("PawnControl_CutPlant"), thing3);
                    }

                    return null;
                }

                if (thing3.def.EverHaulable)
                {
                    return HaulAIUtility.HaulAsideJobFor(pawn, thing3);
                }

                return null;
            }

            if (!wantedPlantDef.CanNowPlantAt(c, map) || !PlantUtility.GrowthSeasonNow(c, map, forSowing: true) || !pawn.CanReserve(c, 1, -1, null, forced))
            {
                return null;
            }

            Job job = JobMaker.MakeJob(Utility_Common.JobDefNamed("PawnControl_Sow"), c);
            job.plantDefToSow = wantedPlantDef;
            return job;
        }
    }
}
