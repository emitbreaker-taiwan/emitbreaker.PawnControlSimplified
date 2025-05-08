using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Defines an injected passion for a specific skill (string name based).
    /// Used inside NonHumanlikePawnControlExtension.injectedPassions.
    /// </summary>
    public class SkillPassionEntry : IExposable
    {
        public string skill; // Use skill defName as string
        public Passion passion;

        public void ExposeData()
        {
            Scribe_Values.Look(ref skill, "skill");
            Scribe_Values.Look(ref passion, "passion");
        }
    }
}
