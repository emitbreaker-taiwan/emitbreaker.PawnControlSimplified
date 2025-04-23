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
        // Cache to store the tag-check result per ThingDef
        private static readonly Dictionary<ThingDef, bool> cachedWorkHumanlikeStatus = new Dictionary<ThingDef, bool>();
        private static readonly Dictionary<ThingDef, bool> cachedApparelRestriction = new Dictionary<ThingDef, bool>();
        private static readonly Dictionary<RaceProperties, ThingDef> raceReverseMap = new Dictionary<RaceProperties, ThingDef>();

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
            if (def == null)
            {
                return null;
            }

            NonHumanlikePawnControlExtension cached;

            // === Check Cache First ===
            if (modExtensionCache.TryGetValue(def, out cached))
            {
                return cached;
            }

            // === Safe check against incomplete def ===
            NonHumanlikePawnControlExtension modExtension = null;
            try
            {
                // Defensive: modExtensions might not be initialized yet
                if (def.modExtensions != null)
                {
                    modExtension = def.GetModExtension<NonHumanlikePawnControlExtension>();
                }
            }
            catch (Exception ex)
            {
                if (Prefs.DevMode)
                {
                    Log.Warning($"[PawnControl] GetModExtension failed for def {def.defName}: {ex.Message}");
                }
            }

            modExtensionCache[def] = modExtension; // Safe to cache null for future checks

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

        // This method checks if the pawn needs to be treated as humanlike for work purposes.
        public static bool ShouldTreatAsHumanlikeForWork(ThingDef def)
        {
            if (def == null)
            {
                return false;
            }

            bool result;

            // === Check cache early ===
            if (cachedWorkHumanlikeStatus.TryGetValue(def, out result))
            {
                return result;
            }

            try
            {
                var modExtension = GetModExtension(def);

                if (modExtension == null || modExtension.tags == null)
                {
                    cachedWorkHumanlikeStatus[def] = false;
                    return false;
                }

                // === Tag-based work/humanlike activation ===
                bool checkOne = modExtension.tags.Contains(ManagedTags.AllowAllWork)
                                || modExtension.tags.Any(tag => tag != null && tag.StartsWith(ManagedTags.AllowWorkPrefix));

                bool checkTwo = modExtension.tags.Contains(ManagedTags.ForceDraftable)
                                || modExtension.forceDraftable;

                result = checkOne || checkTwo;
                cachedWorkHumanlikeStatus[def] = result;
                return result;
            }
            catch (Exception ex)
            {
                if (Prefs.DevMode)
                {
                    Log.Warning($"[PawnControl] Exception in ShouldTreatAsHumanlikeForWork for {def.defName}: {ex.Message}");
                }

                cachedWorkHumanlikeStatus[def] = false;
                return false;
            }
        }

        public static void PreloadWorkHumanlikeCache()
        {
            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.race != null && !d.race.Humanlike))
            {
                try
                {
                    ShouldTreatAsHumanlikeForWork(def); // triggers caching internally
                }
                catch (Exception ex)
                {
                    Log.Error($"[PawnControl] Preload error for {def.defName}: {ex.Message}");
                }
            }

            //Log.Message($"[PawnControl] Preloaded humanlike work cache with {cachedWorkHumanlikeStatus.Count} entries.");
        }

        // Safe Guard for above two codes when they called by Harmony Patch
        public static void BuildRaceReverseMap()
        {
            raceReverseMap.Clear();
            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                var race = def.race;
                if (race != null && !race.Humanlike && !raceReverseMap.ContainsKey(race))
                {
                    raceReverseMap[race] = def;
                }
            }
        }

        public static bool TryGetDefFromRace(RaceProperties race, out ThingDef def)
        {
            return raceReverseMap.TryGetValue(race, out def);
        }

        // This method checks if a pawn has restrictions on the apparel it can wear.
        // It uses a cached dictionary to store the result for each pawn's ThingDef to improve performance.
        // If the result is not cached, it retrieves the NonHumanlikePawnControlExtension for the pawn's ThingDef.
        // If the extension exists and the `restrictApparelByBodyType` flag is set to true, the pawn is restricted.
        // The result is then cached for future lookups and returned.

        public static bool IsApparelRestricted(Pawn pawn)
        {
            if (pawn == null || pawn.def == null) return false;

            bool result;
            if (!cachedApparelRestriction.TryGetValue(pawn.def, out result))
            {
                var physicalModExtension = pawn.def.GetModExtension<NonHumanlikePawnControlExtension>();

                if (physicalModExtension != null)
                {
                    result = physicalModExtension != null && physicalModExtension.restrictApparelByBodyType;
                }

                cachedApparelRestriction[pawn.def] = result;
            }
            return result;
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
