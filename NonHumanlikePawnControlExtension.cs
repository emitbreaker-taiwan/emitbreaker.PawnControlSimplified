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
    public class NonHumanlikePawnControlExtension : DefModExtension, IExposable
    {
        // Declare private dictionary fields at the class level first
        public Dictionary<SkillDef, Passion> _skillPassionDict;
        public Dictionary<SkillDef, int> _simulatedSkillDict;

        // In the constructor or a dedicated initialization method
        public NonHumanlikePawnControlExtension()
        {
            // Pre-allocate collections with reasonable capacity
            tags = new List<string>(25);
            _skillPassionDict = new Dictionary<SkillDef, Passion>(8);
            _simulatedSkillDict = new Dictionary<SkillDef, int>(8);
        }

        /// <summary>
        /// Optional: Overrides the main ThinkTreeDef name to inject at static race-level.
        /// (Example: "PawnControl_WorkTreeTemplate")
        /// </summary>
        [NoTranslate]
        public string mainWorkThinkTreeDefName = null;
        [NoTranslate]
        public string constantThinkTreeDefName = null;
        [NoTranslate]
        public string originalMainWorkThinkTreeDefName = null;
        [NoTranslate]
        public string originalConstantThinkTreeDefName = null;

        // === Vehicle-specific override ===
        //[NoTranslate]
        //public ThinkTreeDef mainWorkThinkTreeDefNameVehicle;
        //[NoTranslate]
        //public ThinkTreeDef constantThinkTreeDefNameVehicle;

        /// <summary>
        /// Optional: List of allowed tags (work types, skill overrides, etc).
        /// Follow the ManagedTags naming rule.
        /// </summary>
        [NoTranslate]
        public List<string> tags = new List<string>();

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
        public bool forceDraftable = false;
        public bool forceEquipWeapon = false;
        public bool forceWearApparel = false;

        // === Lord duty customization ===

        // === Apparel / Weapon filtering ===
        public List<BodyTypeDef> allowedBodyTypes;
        public bool restrictApparelByBodyType = true;

        public bool debugMode = false;
        public bool fromXML = false;
        public bool toBeRemoved = false;

        public bool ignoreCapability = true;

        // Implementation of IExposable for saving/loading
        public void ExposeData()
        {
            // Existing code
            Scribe_Values.Look(ref mainWorkThinkTreeDefName, "mainWorkThinkTreeDefName");
            Scribe_Values.Look(ref constantThinkTreeDefName, "constantThinkTreeDefName");
            Scribe_Values.Look(ref originalMainWorkThinkTreeDefName, "originalMainWorkThinkTreeDefName");
            Scribe_Values.Look(ref originalConstantThinkTreeDefName, "originalConstantThinkTreeDefName");

            Scribe_Collections.Look(ref tags, "tags", LookMode.Value);

            // Handle nullable int properly
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                bool hasOverride = baseSkillLevelOverride.HasValue;
                Scribe_Values.Look(ref hasOverride, "hasSkillOverride", false);
                if (hasOverride)
                {
                    int value = baseSkillLevelOverride.Value;
                    Scribe_Values.Look(ref value, "baseSkillLevelOverride");
                }
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                bool hasOverride = false;
                Scribe_Values.Look(ref hasOverride, "hasSkillOverride", false);
                if (hasOverride)
                {
                    int value = 0;
                    Scribe_Values.Look(ref value, "baseSkillLevelOverride");
                    baseSkillLevelOverride = value;
                }
                else
                {
                    baseSkillLevelOverride = null;
                }
            }

            Scribe_Values.Look(ref skillLevelToolUser, "skillLevelToolUser", 0);
            Scribe_Values.Look(ref skillLevelAnimalAdvanced, "skillLevelAnimalAdvanced", 0);
            Scribe_Values.Look(ref skillLevelAnimalIntermediate, "skillLevelAnimalIntermediate", 0);
            Scribe_Values.Look(ref skillLevelAnimalBasic, "skillLevelAnimalBasic", 0);

            // MISSING CODE: Save and load injected skills and passions
            Scribe_Collections.Look(ref injectedSkills, "injectedSkills", LookMode.Deep);
            Scribe_Collections.Look(ref injectedPassions, "injectedPassions", LookMode.Deep);

            // Make sure lists are property initialized after loading
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (injectedSkills == null)
                    injectedSkills = new List<SkillLevelEntry>();

                if (injectedPassions == null)
                    injectedPassions = new List<SkillPassionEntry>();

                if (tags == null)
                    tags = new List<string>();
            }

            Scribe_Values.Look(ref forceIdentity, "forceIdentity");
            Scribe_Values.Look(ref forceDraftable, "forceDraftable", false);
            Scribe_Values.Look(ref forceEquipWeapon, "forceEquipWeapon", false);
            Scribe_Values.Look(ref forceWearApparel, "forceWearApparel", false);

            Scribe_Values.Look(ref restrictApparelByBodyType, "restrictApparelByBodyType", false);
            Scribe_Collections.Look(ref allowedBodyTypes, "allowedBodyTypes", LookMode.Def);

            Scribe_Values.Look(ref debugMode, "debugMode", false);
            Scribe_Values.Look(ref fromXML, "fromXML", true);
            Scribe_Values.Look(ref toBeRemoved, "toBeRemoved", false);
            Scribe_Values.Look(ref ignoreCapability, "ignoreCapability", true);

            // Re-cache after loading
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                CacheSkillPassions();
                CacheSimulatedSkillLevels();
            }
        }

        public Dictionary<SkillDef, Passion> SkillPassionDict
        {
            get
            {
                if (_skillPassionDict == null)
                    CacheSkillPassions();
                return _skillPassionDict;
            }
        }

        public Dictionary<SkillDef, int> SimulatedSkillDict
        {
            get
            {
                if (_simulatedSkillDict == null)
                    CacheSimulatedSkillLevels();
                return _simulatedSkillDict;
            }
        }

        public void CacheSkillPassions()
        {
            if (injectedPassions == null || injectedPassions.Count == 0)
                return;

            _skillPassionDict = new Dictionary<SkillDef, Passion>();
            foreach (var entry in injectedPassions)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.skill))
                    continue;

                SkillDef def = DefDatabase<SkillDef>.GetNamedSilentFail(entry.skill);
                if (def != null)
                {
                    _skillPassionDict[def] = entry.passion;
                }
                else
                {
                    Utility_DebugManager.LogWarning($"[PawnControl] SkillDef '{entry.skill}' not found for passion injection.");
                }
            }
        }

        public void CacheSimulatedSkillLevels()
        {
            try
            {
                if (_simulatedSkillDict == null)
                    _simulatedSkillDict = new Dictionary<SkillDef, int>();
                else
                    _simulatedSkillDict.Clear();

                if (injectedSkills == null || injectedSkills.Count == 0)
                    return;

                foreach (var entry in injectedSkills)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.skill))
                        continue;

                    SkillDef def = DefDatabase<SkillDef>.GetNamedSilentFail(entry.skill);
                    if (def != null)
                    {
                        _simulatedSkillDict[def] = entry.level;
                        Utility_DebugManager.LogNormal($"Cached skill {def.defName} at level {entry.level}");
                    }
                    else
                    {
                        Utility_DebugManager.LogWarning($"[PawnControl] SkillDef '{entry.skill}' not found for skill simulation.");
                    }
                }
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error in CacheSimulatedSkillLevels: {ex.Message}");
            }
        }
    }
}