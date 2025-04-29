using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace emitbreaker.PawnControl
{
    public class PawnControl_JobDriver_PlantHarvest_Designated : PawnControl_JobDriver_PlantHarvest
    {
        protected override DesignationDef RequiredDesignation => DesignationDefOf.HarvestPlant;
    }
}
