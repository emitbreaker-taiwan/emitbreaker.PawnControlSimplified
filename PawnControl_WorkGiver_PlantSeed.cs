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
    public class PawnControl_WorkGiver_PlantSeed : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Seed);

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            if (!ModsConfig.IdeologyActive && !ModsConfig.BiotechActive)
            {
                return !ModsConfig.AnomalyActive;
            }

            if (Utility_ThinkTreeManager.HasAllowOrBlockWorkTag(pawn.def))
            {
                return true;
            }

            return false;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            CompPlantable compPlantable = t.TryGetComp<CompPlantable>();
            if (compPlantable == null || !pawn.CanReserve(t, 1, 1, null, forced))
            {
                return false;
            }

            List<IntVec3> plantCells = compPlantable.PlantCells;
            for (int i = 0; i < plantCells.Count; i++)
            {
                if (pawn.CanReach(plantCells[i], PathEndMode.Touch, Danger.Deadly))
                {
                    Plant plant = plantCells[i].GetPlant(t.Map);
                    if (plant == null || CanDoCutJob(pawn, plant, forced))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            CompPlantable compPlantable = t.TryGetComp<CompPlantable>();
            if (compPlantable == null)
            {
                return null;
            }

            List<IntVec3> plantCells = compPlantable.PlantCells;
            for (int i = 0; i < plantCells.Count; i++)
            {
                if (pawn.CanReach(plantCells[i], PathEndMode.Touch, Danger.Deadly))
                {
                    Plant plant = plantCells[i].GetPlant(t.Map);
                    if (plant == null)
                    {
                        Job job = JobMaker.MakeJob(Utility_Common.JobDefNamed("PawnControl_PlantSeed"), t, plantCells[i]);
                        job.playerForced = forced;
                        job.plantDefToSow = compPlantable.Props.plantDefToSpawn;
                        job.count = 1;
                        return job;
                    }

                    if (CanDoCutJob(pawn, plant, forced))
                    {
                        return JobMaker.MakeJob(Utility_Common.JobDefNamed("PawnControl_CutPlant"), plant);
                    }
                }
            }

            return null;
        }

        private bool CanDoCutJob(Pawn pawn, Thing plant, bool forced)
        {
            if (!pawn.CanReserve(plant, 1, -1, null, forced))
            {
                return false;
            }

            if (plant.IsForbidden(pawn))
            {
                return false;
            }

            if (!Utility_ThinkTreeManager.PawnWillingToCutPlant_Job(plant, pawn))
            {
                return false;
            }

            return true;
        }
    }
}
