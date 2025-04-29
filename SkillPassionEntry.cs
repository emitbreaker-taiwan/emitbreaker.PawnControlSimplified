using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Defines an injected passion for a specific skill (string name based).
    /// Used inside NonHumanlikePawnControlExtension.injectedPassions.
    /// </summary>
    public class SkillPassionEntry
    {
        public string skill; // Use skill defName as string
        public Passion passion;
    }
}
