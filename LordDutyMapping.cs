using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Maps a LordToil type name to a custom duty + radius.
    /// </summary>
    public class LordDutyMapping : IExposable
    {
        public string lordToilClass;
        public string dutyDef;
        public float radius = -1f;

        public void ExposeData()
        {
            Scribe_Values.Look(ref lordToilClass, "lordToilClass");
            Scribe_Values.Look(ref dutyDef, "dutyDef");
            Scribe_Values.Look(ref radius, "radius", -1f);
        }
    }
}
