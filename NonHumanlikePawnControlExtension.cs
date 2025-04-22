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
    /// Add this ModExtension to any non-humanlike ThingDef (Pawn) to enable advanced control behaviors.
    /// </summary>
    public class NonHumanlikePawnControlExtension : DefModExtension
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

        // === CE / Mech tag compatibility ===
        [NoTranslate]
        public string mechDutyTag = null;

        // === Work filtering ===
        [NoTranslate]
        public List<string> allowedWorkTypes = new List<string>();

        [NoTranslate]
        public List<string> blockedWorkTypes = new List<string>();

        // === Lord duty customization ===
        public List<LordDutyMapping> lordDutyMappings;
        [NoTranslate]
        public string defaultDutyDef;
        public float defaultDutyRadius = -1f;

        // === Apparel / Weapon filtering ===
        public List<BodyTypeDef> allowedBodyTypes;
        public bool restrictApparelByBodyType = true;
        public bool restrictWeaponsByMass = true;

        // === ThinkTree overrides ===
        public ThinkTreeDef overrideThinkTreeMain;
        public ThinkTreeDef overrideThinkTreeConstant;

        // === Vehicle-specific override ===
        public ThinkTreeDef forcedThinkTreeVehicle;

        // Optional override for default skill simulation level
        public int? baseSkillLevelOverride;
        public int skillLevelToolUser = 10; 
        public int skillLevelAnimalAdvanced = 5;
        public int skillLevelAnimalIntermediate = 3;
        public int skillLevelAnimalBasic = 1;

        public bool isRuntimeInjected = false;
    }
}
