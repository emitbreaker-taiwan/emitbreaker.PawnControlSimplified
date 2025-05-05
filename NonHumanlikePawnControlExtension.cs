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
        /// <summary>
        /// Optional: Overrides the main ThinkTreeDef name to inject at static race-level.
        /// (Example: "PawnControl_WorkTreeTemplate")
        /// </summary>
        [NoTranslate]
        public string mainWorkThinkTreeDefName = null;
        [NoTranslate]
        public string constantThinkTreeDefName = null;

        /// <summary>
        /// Optional: List of additional think trees to be injected.
        /// (Example: "PawnControl_WorkTreeTemplate")
        /// </summary>
        [NoTranslate]
        public List<string> additionalMain = new List<string>();

        /// <summary>
        /// Optional: List of allowed tags (work types, skill overrides, etc).
        /// Follow the ManagedTags naming rule.
        /// </summary>
        [NoTranslate]
        public List<string> tags = new List<string>();

        /// <summary>
        /// Optional: PawnTagDef references to use in addition to simple string tags.
        /// Allow dynamic or modular tag grouping.
        /// </summary>
        [Obsolete("Replaced by tags")]
        public List<PawnTagDef> pawnTagDefs;

        /// <summary>
        /// Optional override for default skill simulation level - only for global settings and no need to change.
        /// baseSkillLevelOverride > simulatedSkills > skillLevel*
        /// </summary>
        // Override all injected skill level
        public int? baseSkillLevelOverride;
        // Override all injected skill level if original race is ToolUser
        public int skillLevelToolUser = 10;
        // Override all injected skill level if original race is animal and has Advanced Trainability
        public int skillLevelAnimalAdvanced = 5;
        // Override all injected skill level if original race is animal and has Intermediate Trainability
        public int skillLevelAnimalIntermediate = 3;
        // Override all injected skill level if original race is animal and has Basic Trainability
        public int skillLevelAnimalBasic = 1;

        /// <summary>
        /// Optional: Skill profile overrides (SkillDef -> Level mapping).
        /// Simulate specific skill levels for non-humanlike pawns.
        /// (If null or empty, fallback to default simulation.)
        /// </summary>
        public List<SkillLevelEntry> injectedSkills;

        /// <summary>
        /// Optional: Hard passion injections for relevant skills.
        /// Allows simulating natural minor/major passions.
        /// </summary>
        public List<SkillPassionEntry> injectedPassions;

        /// <summary>
        /// Optional: Forces race identity (e.g., Humanlike, Animal, Mechanoid).
        /// Used for overriding RimWorld's default RaceProps classification.
        /// </summary>
        public ForcedIdentityType? forceIdentity;

        // === Capability flags ===
        [Obsolete("Replaced by forceIdentity + ForcedIdentityType")]
        public bool forceAnimal = false;
        [Obsolete("Replaced by forceIdentity + ForcedIdentityType")]
        public bool forceHumanlike = false;
        [Obsolete("Replaced by forceIdentity + ForcedIdentityType")]
        public bool forceMechanoid = false;
        public bool forceDraftable = false;
        public bool forceWork = false;
        public bool forceTrainerTab = false;
        public bool autoDraftInject = false;

        // === Lord duty customization ===
        public List<LordDutyMapping> lordDutyMappings;
        [NoTranslate]
        public string defaultDutyDef;
        public float defaultDutyRadius = -1f;

        // === Apparel / Weapon filtering ===
        public List<BodyTypeDef> allowedBodyTypes;
        public bool restrictApparelByBodyType = true;
        public bool restrictWeaponsByMass = true;

        // === Vehicle-specific override ===
        //public ThinkTreeDef forcedThinkTreeVehicle;

        [Unsaved]
        public Dictionary<SkillDef, Passion> skillPassionDict;

        [Unsaved]
        public Dictionary<SkillDef, int> simulatedSkillDict;

        public bool debugMode = false;

        public void CacheSkillPassions()
        {
            if (injectedPassions == null || injectedPassions.Count == 0)
                return;

            skillPassionDict = new Dictionary<SkillDef, Passion>();
            foreach (var entry in injectedPassions)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.skill))
                    continue;

                SkillDef def = DefDatabase<SkillDef>.GetNamedSilentFail(entry.skill);
                if (def != null)
                {
                    skillPassionDict[def] = entry.passion;
                }
                else if (Prefs.DevMode)
                {
                    Log.Warning($"[PawnControl] SkillDef '{entry.skill}' not found for passion injection.");
                }
            }
        }

        public void CacheSimulatedSkillLevels()
        {
            if (injectedSkills == null || injectedSkills.Count == 0)
                return;

            simulatedSkillDict = new Dictionary<SkillDef, int>();
            foreach (var entry in injectedSkills)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.skill))
                    continue;

                SkillDef def = DefDatabase<SkillDef>.GetNamedSilentFail(entry.skill);
                if (def != null)
                {
                    simulatedSkillDict[def] = entry.level;
                }
                else if (Prefs.DevMode)
                {
                    Log.Warning($"[PawnControl] SkillDef '{entry.skill}' not found for skill simulation.");
                }
            }
        }

        /// <summary>
        /// Helper method to determine if this extension is interesting for debugging
        /// </summary>
        public bool IsInteresting()
        {
            return true; // You can add more specific criteria later if needed
        }
    }
}
