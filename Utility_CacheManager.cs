using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse.AI;
using Verse;
using RimWorld;

namespace emitbreaker.PawnControl
{
    public static class Utility_CacheManager
    {
        private static readonly Dictionary<string, DutyDef> dutyCache = new Dictionary<string, DutyDef>();
        private static readonly Dictionary<ThingDef, NonHumanlikePawnControlExtension> modExtensionCache = new Dictionary<ThingDef, NonHumanlikePawnControlExtension>();
        private static readonly Dictionary<SkillDef, SkillRecord> simulatedSkillCache = new Dictionary<SkillDef, SkillRecord>();
        private static readonly Lazy<Pawn> lazyDummyPawnForSkills = new Lazy<Pawn>(CreateDummyPawnForSkills);
        public static readonly Dictionary<ThingDef, HashSet<string>> tagCache = new Dictionary<ThingDef, HashSet<string>>();
        private static Pawn dummyPawnForSkills => lazyDummyPawnForSkills.Value;

        public static DutyDef GetDuty(string defName)
        {
            DutyDef result;
            if (!dutyCache.TryGetValue(defName, out result))
            {
                result = DefDatabase<DutyDef>.GetNamedSilentFail(defName);
                dutyCache[defName] = result;
            }
            return result;
        }

        public static NonHumanlikePawnControlExtension GetModExtension(ThingDef def)
        {
            NonHumanlikePawnControlExtension modExtension;
            if (!modExtensionCache.TryGetValue(def, out modExtension))
            {
                modExtension = def.GetModExtension<NonHumanlikePawnControlExtension>();
                modExtensionCache[def] = modExtension;
            }
            return modExtension;
        }

        /// <summary>
        /// Checks whether a pawn (including non-humanlike) is allowed to perform the given WorkType.
        /// Uses tag-based logic for non-humanlike pawns.
        /// </summary>
        public static bool IsWorkTypeEnabledForPawn(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn == null || workType == null)
            {
                return false;
            }

            if (pawn.RaceProps.Humanlike)
            {
                return !pawn.WorkTypeIsDisabled(workType);
            }

            // Check for specific work type tag or AllowAllWork tag
            string specificTag = ManagedTags.AllowWorkPrefix + workType.defName;
            return Utility_TagManager.HasTag(pawn.def, specificTag) || Utility_TagManager.HasTag(pawn.def, ManagedTags.AllowAllWork);
        }

        //Skill level simulation
        public static int GetSimulatedSkillLevel(Pawn pawn)
        {
            if (pawn == null || pawn.def == null)
            {
                return 0;
            }

            var modExtension = GetModExtension(pawn.def);

            if (modExtension != null && modExtension.baseSkillLevelOverride.HasValue)
            {
                return modExtension.baseSkillLevelOverride.Value;
            }

            var race = pawn.RaceProps;
            if (race == null)
            {
                return 0;
            }

            if (race.Humanlike)
            {
                return pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0; // fallback to real
            }

            if (race.IsMechanoid || race.ToolUser)
            {
                if (modExtension != null)
                {
                    return modExtension.skillLevelToolUser;
                }
                return 10;
            }

            if (race.Animal)
            {
                // Heuristic: some modders may use trainability to reflect “complexity”
                if (race.trainability == TrainabilityDefOf.Advanced)
                {
                    if (modExtension != null)
                    {
                        return modExtension.skillLevelAnimalAdvanced;
                    }
                    return 5;
                }
                if (race.trainability == TrainabilityDefOf.Intermediate)
                {
                    if (modExtension != null)
                    {
                        return modExtension.skillLevelAnimalIntermediate;
                    }
                    return 3;
                }
                else
                {
                    if (modExtension != null)
                    {
                        return modExtension.skillLevelAnimalBasic;
                    }
                    return 1;
                }
            }

            return 1; // Generic fallback
        }

        public static SkillRecord GetFakeSkill(Pawn pawn, SkillDef def)
        {
            if (simulatedSkillCache.TryGetValue(def, out var cached))
            {
                return cached;
            }

            int level = GetSimulatedSkillLevel(pawn);

            var skill = new SkillRecord(dummyPawnForSkills, def);
            skill.Level = level;

            simulatedSkillCache[def] = skill;
            return skill;
        }

        private static Pawn CreateDummyPawnForSkills()
        {
            try
            {
                // Ensure DefOf types are initialized
                DefOfHelper.EnsureInitializedInCtor(typeof(ThingDefOf));

                Pawn dummy = (Pawn)Activator.CreateInstance(typeof(Pawn));
                dummy.def = ThingDefOf.Human; // Use generic humanlike for compatibility
                dummy.story = new Pawn_StoryTracker(dummy);
                dummy.skills = new Pawn_SkillTracker(dummy);
                return dummy;
            }
            catch (Exception ex)
            {
                Log.Error($"[PawnControl] Failed to create dummy pawn for skills: {ex}");
                return null;
            }
        }

        public static void ClearSimulatedSkillCache()
        {
            simulatedSkillCache.Clear();
            Log.Message("[PawnControl] Simulated skill cache cleared.");
        }

        public static void ClearModExtensionCache()
        {
            modExtensionCache.Clear();
            Log.Message("[PawnControl] Cleared modExtensionCache.");
        }
    }
}
