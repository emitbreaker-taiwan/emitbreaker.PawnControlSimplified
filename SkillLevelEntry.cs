using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Defines a simulated skill level for a specific skill.
    /// Used inside NonHumanlikePawnControlExtension.simulatedSkills.
    /// </summary>
    public class SkillLevelEntry
    {
        public string skill; // Use skill defName as string
        public int level;
    }
}
