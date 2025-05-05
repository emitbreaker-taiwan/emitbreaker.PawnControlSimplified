using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace emitbreaker.PawnControl
{
    public class HediffCompProperties_StatBridge : HediffCompProperties
    {
        public HediffCompProperties_StatBridge() => compClass = typeof(HediffComp_StatBridge);
    }
}
