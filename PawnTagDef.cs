using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Wraps a NonHumanlikePawnControlExtension loaded from XML so we can look it up by defName.
    /// </summary>
    public class PawnTagDef : Def
    {
        /// <summary>
        /// The preset data to apply when this def is selected.
        /// </summary>
        public RaceTypeFlag targetRaceType; /// The race type to apply this preset (Humanlike, Animal, ToolUser, etc.)
        public NonHumanlikePawnControlExtension modExtension;
    }
}