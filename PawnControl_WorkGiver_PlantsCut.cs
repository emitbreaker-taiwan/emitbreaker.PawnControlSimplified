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
    public class PawnControl_WorkGiver_PlantsCut : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override Danger MaxPathDanger(Pawn pawn)
        {
            return Danger.Deadly;
        }

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            if (Prefs.DevMode)
            {
                Log.Message($"[PawnControl Debug] PotentialWorkThingsGlobal called for {pawn.LabelShortCap}");
            }

            foreach (Designation item in pawn.Map.designationManager.designationsByDef[DesignationDefOf.CutPlant])
            {
                if (Prefs.DevMode)
                {
                    Log.Message($"[PawnControl Debug] Found CutPlant designation on {item.target.Thing.Label}");
                }
                yield return item.target.Thing;
            }

            foreach (Designation item2 in pawn.Map.designationManager.designationsByDef[DesignationDefOf.HarvestPlant])
            {
                if (Prefs.DevMode)
                {
                    Log.Message($"[PawnControl Debug] Found HarvestPlant designation on {item2.target.Thing.Label}");
                }
                yield return item2.target.Thing;
            }
        }

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            if (Prefs.DevMode)
            {
                Log.Message($"[PawnControl Debug] ShouldSkip called for {pawn.LabelShortCap} (forced={forced})");
            }

            if (Utility_ThinkTreeManager.HasAllowOrBlockWorkTag(pawn.def))
            {
                return true;
            }

            bool skip = !pawn.Map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.CutPlant) &&
                        !pawn.Map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.HarvestPlant);

            if (Prefs.DevMode)
            {
                Log.Message($"[PawnControl Debug] ShouldSkip result for {pawn.LabelShortCap}: {skip}");
            }

            return skip;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (t.def.category != ThingCategory.Plant)
            {
                if (Prefs.DevMode)
                {
                    Utility_DebugManager.PawnControl_WorkGiver_PlantsCut_JobOnThing_NotPlant(t);
                }
                return null;
            }

            if (!pawn.CanReserve(t, 1, -1, null, forced))
            {
                if (Prefs.DevMode)
                {
                    Utility_DebugManager.PawnControl_WorkGiver_PlantsCut_JobOnThing_CannotReverse(t, pawn);
                }
                return null;
            }

            if (t.IsForbidden(pawn))
            {
                if (Prefs.DevMode)
                {
                    Utility_DebugManager.PawnControl_WorkGiver_PlantsCut_JobOnThing_IsForbidden(t, pawn);
                }
                return null;
            }

            if (t.IsBurning())
            {
                if (Prefs.DevMode)
                {
                    Utility_DebugManager.PawnControl_WorkGiver_PlantsCut_JobOnThing_IsBurning(t);
                }
                return null;
            }

            if (!Utility_ThinkTreeManager.PawnWillingToCutPlant_Job(t, pawn))
            {
                if (Prefs.DevMode)
                {
                    Utility_DebugManager.PawnControl_WorkGiver_PlantsCut_JobOnThing_NotWilling(t, pawn);
                }
                return null;
            }

            foreach (Designation item in pawn.Map.designationManager.AllDesignationsOn(t))
            {
                if (item.def == DesignationDefOf.HarvestPlant)
                {
                    if (!((Plant)t).HarvestableNow)
                    {
                        if (Prefs.DevMode)
                        {
                            Utility_DebugManager.PawnControl_WorkGiver_PlantsCut_JobOnThing_NotHarvestable(t);
                        }
                        return null;
                    }
                    if (Prefs.DevMode)
                    {
                        Utility_DebugManager.PawnControl_WorkGiver_PlantsCut_JobOnThing_AssignHarvestJob(t, pawn);
                    }
                    return JobMaker.MakeJob(Utility_Common.JobDefNamed("PawnControl_HarvestDesignated"), t);
                }

                if (item.def == DesignationDefOf.CutPlant)
                {
                    if (Prefs.DevMode)
                    {
                        Utility_DebugManager.PawnControl_WorkGiver_PlantsCut_JobOnThing_AssignCutJob(t, pawn);
                    }
                    return JobMaker.MakeJob(Utility_Common.JobDefNamed("PawnControl_CutPlantDesignated"), t);
                }
            }
            if (Prefs.DevMode)
            {
                Utility_DebugManager.PawnControl_WorkGiver_PlantsCut_JobOnThing_NoDesignation(t);
            }
            return null;
        }

        public override string PostProcessedGerund(Job job)
        {
            if (job.def == Utility_Common.JobDefNamed("PawnControl_HarvestDesignated"))
                return "HarvestGerund".Translate();

            if (job.def == Utility_Common.JobDefNamed("PawnControl_CutPlantDesignated"))
                return "CutGerund".Translate();

            return def.gerund;
        }
    }
}
