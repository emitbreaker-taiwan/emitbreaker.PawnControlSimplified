using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace emitbreaker.PawnControl
{
    public class VirtualNonHumanlikePawnControlExtension : DefModExtension
    {
        /// <summary>General purpose tag list for behavior modifiers.</summary>
        [NoTranslate]
        public List<string> tags = new List<string>();

        /// <summary>Optional list of PawnTagDefs to reference instead of raw strings.</summary>
        public List<PawnTagDef> pawnTagDefs;

        // === Capability flags ===
        public bool? forceAnimal;
        public bool? forceDraftable;
        public bool? forceWork;
        public bool? forceTrainerTab;
        public bool autoDraftInject;

        // === Work filtering ===
        [NoTranslate]
        public List<string> allowedWorkTypes = new List<string>();

        [NoTranslate]
        public List<string> blockedWorkTypes = new List<string>();

        // Optional override for default skill simulation level
        public int? baseSkillLevelOverride;
        public int skillLevelToolUser = 10;
        public int skillLevelAnimalAdvanced = 5;
        public int skillLevelAnimalIntermediate = 3;
        public int skillLevelAnimalBasic = 1;
    }
}
