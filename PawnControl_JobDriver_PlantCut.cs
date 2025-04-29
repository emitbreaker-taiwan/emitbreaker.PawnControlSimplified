using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    public class PawnControl_JobDriver_PlantCut : PawnControl_JobDriver_PlantWork
    {
        protected override PlantDestructionMode PlantDestructionMode => PlantDestructionMode.Cut;

        protected override void Init()
        {

        }

        protected override Toil PlantWorkDoneToil()
        {
            return Toils_Interact.DestroyThing(TargetIndex.A);
        }
    }
}
