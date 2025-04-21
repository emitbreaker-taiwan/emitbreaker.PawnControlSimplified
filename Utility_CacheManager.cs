using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse.AI;
using Verse;
using System.Security.Cryptography;
using RimWorld;
using System.Reflection;
using System.Runtime.Serialization;

namespace emitbreaker.PawnControl
{
    public static class Utility_CacheManager
    {
        private static readonly Dictionary<ThingDef, ThinkTreeDef> cachedMainTrees = new Dictionary<ThingDef, ThinkTreeDef>();
        private static readonly Dictionary<ThingDef, ThinkTreeDef> cachedConstantTrees = new Dictionary<ThingDef, ThinkTreeDef>();
        private static readonly Dictionary<ThingDef, bool> cachedApparelRestriction = new Dictionary<ThingDef, bool>();
        private static readonly Dictionary<Tuple<ThingDef, float>, bool> massFitCache = new Dictionary<Tuple<ThingDef, float>, bool>();
        private static readonly Dictionary<string, DutyDef> dutyCache = new Dictionary<string, DutyDef>();
        public static HashSet<string> allKnownTagNames = new HashSet<string>();
        private static readonly Dictionary<ThingDef, List<PawnTagDef>> suggestedTagCache = new Dictionary<ThingDef, List<PawnTagDef>>();
        private static readonly Dictionary<SkillDef, SkillRecord> simulatedSkillCache = new Dictionary<SkillDef, SkillRecord>();
        private static readonly Pawn dummyPawnForSkills = CreateDummyPawnForSkills();
        private static readonly Dictionary<string, string> resolvedTagCache = new Dictionary<string, string>();

        private static readonly Dictionary<string, string> fallbackTagToTreeMain = new Dictionary<string, string>
            {
                { "UseThinkTree_VFEPatroller", "VFE_Patroller" },
                { "UseThinkTree_MechCombat",   "MechCombat" },
                { "UseThinkTree_CEAssault",    "CE_AssaultTree" }
            };

        private static readonly Dictionary<string, string> fallbackTagToTreeConstant = new Dictionary<string, string>
            {
                { "UseConstantTree_SimpleIdle", "ConstantSimple" },
                { "UseConstantTree_VFEDrone",   "VFE_DroneConstant" }
            };

        public static class Tags
        {
            private static readonly Dictionary<ThingDef, HashSet<string>> tagCache = new Dictionary<ThingDef, HashSet<string>>();
            public static HashSet<string> Get(ThingDef def)
            {
                if (!tagCache.TryGetValue(def, out var set))
                {
                    set = Build(def);
                    tagCache[def] = set;
                }
                return set;
            }

            private static HashSet<string> Build(ThingDef def)
            {
                HashSet<string> set = new HashSet<string>();

                // ✅ NEW: Always use resolver
                List<string> effectiveTags = Utility_ModExtensionResolver.GetEffectiveTags(def);
                if (effectiveTags == null || effectiveTags.Count == 0)
                    return set;

                foreach (string tag in effectiveTags)
                {
                    if (string.IsNullOrWhiteSpace(tag)) continue;
                    set.Add(tag);
                    EnsurePawnTagDefExists(tag);
                }

                // ✅ Ensure legacy force flags are still represented
                if (set.Contains("ForceAnimal")) set.Add("ForceAnimal");
                if (set.Contains("ForceDraftable")) set.Add("ForceDraftable");
                if (set.Contains("ForceTrainerTab")) set.Add("ForceTrainerTab");
                if (set.Contains("ForceWork")) set.Add("ForceWork");

                return set;
            }

            public static void Invalidate(ThingDef def)
            {
                tagCache.Remove(def);
            }

            public static void Rebuild()
            {
                tagCache.Clear();
                List<ThingDef> allDefs = DefDatabase<ThingDef>.AllDefsListForReading;
                for (int i = 0; i < allDefs.Count; i++)
                {
                    ThingDef def = allDefs[i];
                    if (def.GetModExtension<NonHumanlikePawnControlExtension>() != null)
                    {
                        tagCache[def] = Build(def);
                    }
                }
                Log.Message("[PawnControl] Tag cache rebuilt for " + tagCache.Count + " pawns.");
            }

            public static void EnsurePawnTagDefExists(string tag)
            {
                if (string.IsNullOrWhiteSpace(tag)) return;

                if (DefDatabase<PawnTagDef>.GetNamedSilentFail(tag) == null)
                {
                    PawnTagDef def = new PawnTagDef();
                    def.defName = tag;
                    def.label = tag;
                    def.description = "Auto-created from tag string.";
                    def.category = "Auto";
                    DefDatabase<PawnTagDef>.Add(def);
                }
            }

            public static void SyncModExtension(NonHumanlikePawnControlExtension ext)
            {
                if (ext == null) return;

                // Sync tags to pawnTagDefs
                if (ext.tags != null)
                {
                    foreach (string tag in ext.tags)
                    {
                        var defFromTag = DefDatabase<PawnTagDef>.GetNamedSilentFail(tag);
                        if (defFromTag != null && (ext.pawnTagDefs == null || !ext.pawnTagDefs.Contains(defFromTag)))
                        {
                            if (ext.pawnTagDefs == null) ext.pawnTagDefs = new List<PawnTagDef>();
                            ext.pawnTagDefs.Add(defFromTag);
                        }
                    }
                }

                // Sync pawnTagDefs to tags
                if (ext.pawnTagDefs != null)
                {
                    foreach (var defTag in ext.pawnTagDefs)
                    {
                        if (defTag != null && (ext.tags == null || !ext.tags.Contains(defTag.defName)))
                        {
                            if (ext.tags == null) ext.tags = new List<string>();
                            ext.tags.Add(defTag.defName);
                        }
                    }
                }

                if (ext.tags != null)
                {
                    if (ext.tags.Contains("ForceAnimal")) ext.forceAnimal = true;
                    if (ext.tags.Contains("ForceDraftable")) ext.forceDraftable = true;
                    if (ext.tags.Contains("ForceTrainerTab")) ext.forceTrainerTab = true;
                    if (ext.tags.Contains("ForceWork")) ext.forceWork = true;
                }
            }

            public static void LogAllTags(Pawn pawn)
            {
                HashSet<string> tags = Get(pawn.def);
                Log.Message("[PawnControl] Tags for " + pawn.def.defName + ": " + string.Join(", ", tags));
            }

            public static bool HasTag(ThingDef def, string tag)
            {
                return Get(def).Contains(tag);
            }

            public static bool HasTag(ThingDef def, PawnTag tagEnum)
            {
                return HasTag(def, tagEnum.ToString());
            }

            public static string ToTagString(PawnTag tagEnum)
            {
                return tagEnum.ToString();
            }

            public static PawnTag? ToEnum(string tag)
            {
                PawnTag result;
                if (Enum.TryParse<PawnTag>(tag, out result))
                {
                    return result;
                }
                return null;
            }
        }

        public static ThinkTreeDef GetCachedMainThinkTree(Pawn pawn)
        {
            if (pawn == null || pawn.def == null) return null;

            ThinkTreeDef result;
            if (cachedMainTrees.TryGetValue(pawn.def, out result))
                return result;

            var modExtension = Utility_NonHumanlikePawnControl.GetExtension(pawn.def);
            if (modExtension != null && modExtension.overrideThinkTreeMain != null)
            {
                result = modExtension.overrideThinkTreeMain;
            }
            else
            {
                foreach (var kv in fallbackTagToTreeMain)
                {
                    if (Tags.HasTag(pawn.def, kv.Key))
                    {
                        result = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(kv.Value);
                        if (result != null) break;
                    }
                }
            }

            cachedMainTrees[pawn.def] = result;
            return result;
        }

        public static ThinkTreeDef GetCachedConstantThinkTree(Pawn pawn)
        {
            if (pawn == null || pawn.def == null) return null;

            ThinkTreeDef result;
            if (cachedConstantTrees.TryGetValue(pawn.def, out result))
                return result;

            var ext = Utility_NonHumanlikePawnControl.GetExtension(pawn.def);
            if (ext != null && ext.overrideThinkTreeConstant != null)
            {
                result = ext.overrideThinkTreeConstant;
            }
            else
            {
                foreach (var kv in fallbackTagToTreeConstant)
                {
                    if (Tags.HasTag(pawn.def, kv.Key))
                    {
                        result = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(kv.Value);
                        if (result != null) break;
                    }
                }
            }

            cachedConstantTrees[pawn.def] = result;
            return result;
        }

        public static bool IsApparelRestricted(Pawn pawn)
        {
            if (pawn == null || pawn.def == null) return false;

            bool result;
            if (!cachedApparelRestriction.TryGetValue(pawn.def, out result))
            {
                var modExtension = Utility_NonHumanlikePawnControl.GetExtension(pawn.def);
                result = modExtension != null && modExtension.restrictApparelByBodyType;
                cachedApparelRestriction[pawn.def] = result;
            }
            return result;
        }

        public static bool IsWeaponFitCached(Pawn pawn, ThingDef weapon)
        {
            if (pawn == null || weapon == null) return false;

            var key = new Tuple<ThingDef, float>(weapon, pawn.def.race.baseBodySize);
            bool result;

            if (!massFitCache.TryGetValue(key, out result))
            {
                float ratio = weapon.BaseMass / (pawn.def.race.baseBodySize > 0f ? pawn.def.race.baseBodySize : 1f);
                result = ratio <= 3.0f;
                massFitCache[key] = result;
            }

            return result;
        }

        public static bool HasTag(ThingDef def, string tag)
        {
            return Tags.Get(def).Contains(tag);
        }

        public static bool HasTag(ThingDef def, PawnTag tagEnum)
        {
            return HasTag(def, tagEnum.ToString());
        }

        public static HashSet<string> GetTags(ThingDef def)
        {
            return Tags.Get(def);
        }

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

        public static void RefreshTagCache(ThingDef def, DefModExtension modExtension)
        {
            Tags.Invalidate(def);
            if (modExtension is NonHumanlikePawnControlExtension physical)
                Tags.SyncModExtension(physical); // Only sync on physical
        }

        /// <summary>
        /// Retrieves suggested tags for a ThingDef, using cache if available.
        /// </summary>
        public static List<PawnTagDef> GetSuggestedTags(ThingDef def)
        {
            if (def == null) return new List<PawnTagDef>();

            if (suggestedTagCache.TryGetValue(def, out var cached))
                return cached;

            var thisExt = def.GetModExtension<NonHumanlikePawnControlExtension>();
            if (thisExt == null)
                return new List<PawnTagDef>();

            // Cache resolved current tags (once)
            HashSet<string> currentTags = Tags.Get(def).Select(ResolveTagPriority).ToHashSet();

            Dictionary<string, int> usageCount = new Dictionary<string, int>();
            HashSet<string> suggestedTagNames = new HashSet<string>();

            foreach (ThingDef other in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (other == def || !Utility_TagCatalog.IsSimilarRace(def, other))
                    continue;

                var otherExt = other.GetModExtension<NonHumanlikePawnControlExtension>();
                if (otherExt?.tags == null)
                    continue;

                foreach (string tag in otherExt.tags)
                {
                    string resolved = ResolveTagPriority(tag);
                    if (string.IsNullOrWhiteSpace(resolved) || currentTags.Contains(resolved))
                        continue;

                    if (usageCount.TryGetValue(resolved, out int count))
                        usageCount[resolved] = count + 1;
                    else
                        usageCount[resolved] = 1;

                    suggestedTagNames.Add(resolved);
                }
            }

            // Build final sorted result
            List<PawnTagDef> result = suggestedTagNames
                .OrderByDescending(t => usageCount[t])
                .Select(t => DefDatabase<PawnTagDef>.GetNamedSilentFail(t))
                .Where(d => d != null)
                .ToList();

            suggestedTagCache[def] = result; // ✅ Save to cache
            return result;
        }

        /// <summary>
        /// Resolves tag conflict between enum-defined, PawnTagDef, and raw string tags.
        /// Priority: Enum > PawnTagDef > string
        /// </summary>
        public static string ResolveTagPriority(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return null;

            string normalized = tag.Trim().ToLowerInvariant();

            // 🔄 Use cache if available
            if (resolvedTagCache.TryGetValue(normalized, out var resolved))
                return resolved;

            // 1. Enum tag takes priority
            if (Utility_TagCatalog.IsEnumTag(normalized))
            {
                resolvedTagCache[normalized] = normalized;
                return normalized;
            }

            // 2. XML-defined PawnTagDef
            var def = DefDatabase<PawnTagDef>.AllDefs.FirstOrDefault(d =>
                d.defName != null && d.defName.Equals(normalized, StringComparison.OrdinalIgnoreCase));
            if (def != null)
            {
                resolvedTagCache[normalized] = def.defName;
                return def.defName;
            }

            // 3. Fallback to normalized raw
            resolvedTagCache[normalized] = normalized;
            return normalized;
        }

        public static bool IsResolvedTagActive(string tag, List<string> selectedTags)
        {
            string resolved = ResolveTagPriority(tag);
            return selectedTags.Contains(resolved);
        }

        /// <summary>
        /// Applies tag type priority (Enum > Def > String) and removes duplicates.
        /// Use this for syncing selectedTags and preset application.
        /// </summary>
        public static List<string> NormalizeTagList(IEnumerable<string> rawTags)
        {
            HashSet<string> seen = new HashSet<string>();
            List<string> result = new List<string>();

            foreach (string tag in rawTags)
            {
                string resolved = ResolveTagPriority(tag);
                if (!string.IsNullOrWhiteSpace(resolved) && seen.Add(resolved))
                {
                    result.Add(resolved);
                }
            }

            return result;
        }

        public static void InvalidateTagCachesFor(ThingDef def)
        {
            if (def == null) return;

            var modExtension = def.GetModExtension<NonHumanlikePawnControlExtension>();
            if (modExtension != null)
            {
                RefreshTagCache(def, modExtension); // Rebuilds the actual tag logic cache
            }

            InvalidateSuggestedTags(def); // Clears and recomputes suggestion list cache
        }

        /// <summary>
        /// Invalidates the cached suggested tags for a specific ThingDef.
        /// </summary>
        public static void InvalidateSuggestedTags(ThingDef def)
        {
            if (def != null && suggestedTagCache.ContainsKey(def))
            {
                suggestedTagCache.Remove(def);
            }
        }

        /// <summary>
        /// Checks whether a pawn (including non-humanlike) is allowed to perform the given WorkType.
        /// Uses tag-based logic for non-humanlike pawns.
        /// </summary>
        public static bool IsWorkTypeEnabledForPawn(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn == null || workType == null)
                return false;

            if (pawn.RaceProps.Humanlike)
                return !pawn.WorkTypeIsDisabled(workType);

            var resolvedTags = Tags.Get(pawn.def).Select(ResolveTagPriority).ToHashSet();

            string specificTag = ResolveTagPriority("AllowWork_" + workType.defName);
            return resolvedTags.Contains(specificTag) || resolvedTags.Contains("AllowAllWork");
        }

        public static bool IsWorkTypeEnabledForRace(ThingDef def, WorkTypeDef workType)
        {
            if (def == null || def.race?.Humanlike == true)
                return true;

            var resolvedTags = Tags.Get(def).Select(ResolveTagPriority).ToHashSet();

            string specificTag = ResolveTagPriority("AllowWork_" + workType.defName);
            return resolvedTags.Contains(specificTag) || resolvedTags.Contains("AllowAllWork");
        }

        public static void ApplyTaggedWorkPriorities(Pawn pawn)
        {
            if (pawn?.workSettings == null || pawn.def == null)
                return;

            // Get resolved tags once and reuse
            var resolvedTags = Tags.Get(pawn.def).Select(ResolveTagPriority).ToHashSet();

            foreach (WorkTypeDef workType in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                string tag = ResolveTagPriority("AllowWork_" + workType.defName);

                // Also accept AllowAllWork
                if (resolvedTags.Contains(tag) || resolvedTags.Contains("AllowAllWork"))
                {
                    if (!pawn.WorkTypeIsDisabled(workType))
                    {
                        pawn.workSettings.SetPriority(workType, 3);
                    }
                }
            }
        }

        public static int GetSimulatedSkillLevel(Pawn pawn)
        {
            if (pawn == null || pawn.def == null)
                return 0;

            var ext = Utility_NonHumanlikePawnControl.GetExtension(pawn.def);
            if (ext != null && ext.baseSkillLevel.HasValue)
                return ext.baseSkillLevel.Value;

            var race = pawn.RaceProps;
            if (race == null)
                return 0;

            if (race.Humanlike)
            {
                return pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0; // fallback to real
            }

            if (race.IsMechanoid || race.ToolUser)
                return 10;

            if (race.Animal)
            {
                // Heuristic: some modders may use trainability to reflect “complexity”
                if (race.trainability == TrainabilityDefOf.Advanced) return 5;
                if (race.trainability == TrainabilityDefOf.Intermediate) return 3;
                return 1;
            }

            return 1; // Generic fallback
        }

        public static SkillRecord GetFakeSkill(Pawn pawn, SkillDef def)
        {
            if (simulatedSkillCache.TryGetValue(def, out var cached))
                return cached;

            int level = GetSimulatedSkillLevel(pawn);

            var skill = new SkillRecord(dummyPawnForSkills, def);
            skill.Level = level;

            simulatedSkillCache[def] = skill;
            return skill;
        }

        private static Pawn CreateDummyPawnForSkills()
        {
            Pawn dummy = (Pawn)Activator.CreateInstance(typeof(Pawn));
            dummy.def = ThingDefOf.Human; // use generic humanlike for compatibility
            dummy.story = new Pawn_StoryTracker(dummy);
            dummy.skills = new Pawn_SkillTracker(dummy);
            return dummy;
        }

        public static void ClearSimulatedSkillCache()
        {
            simulatedSkillCache.Clear();
            Log.Message("[PawnControl] Simulated skill cache cleared.");
        }

        public static void ClearResolvedTagCache()
        {
            resolvedTagCache.Clear();
        }
    }
}
