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
    public class PawnControl_JobDriver_PlantHarvest : PawnControl_JobDriver_PlantWork
    {
        // Define what happens to the plant after harvest
        protected override PlantDestructionMode PlantDestructionMode => PlantDestructionMode.Chop;

        // No XP needed for non-skill pawns
        protected override void Init()
        {
            xpPerTick = 0f;
        }

        // Cleanup designation once harvesting is done
        protected override Toil PlantWorkDoneToil()
        {
            return Toils_General.RemoveDesignationsOnThing(TargetIndex.A, DesignationDefOf.HarvestPlant);
        }
    }
}
