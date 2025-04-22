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
using static System.Net.Mime.MediaTypeNames;
using KTrie;

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
        private static readonly Lazy<Pawn> lazyDummyPawnForSkills = new Lazy<Pawn>(CreateDummyPawnForSkills);
        private static Pawn dummyPawnForSkills => lazyDummyPawnForSkills.Value;
        private static readonly Dictionary<string, string> resolvedTagCache = new Dictionary<string, string>();
        private static List<ThingDef> cachedRaceDefs;

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
                {
                    return set;
                }

                List<string> normalized = NormalizeTagList(effectiveTags);
                foreach (string tag in normalized)
                {
                    set.Add(tag);
                    EnsurePawnTagDefExists(tag);
                }

                // ✅ Ensure legacy force flags are still represented
                if (set.Contains(ManagedTags.ForceAnimal)) set.Add(ManagedTags.ForceAnimal);
                if (set.Contains(ManagedTags.ForceDraftable)) set.Add(ManagedTags.ForceDraftable);
                if (set.Contains(ManagedTags.ForceTrainerTab)) set.Add(ManagedTags.ForceTrainerTab);
                if (set.Contains(ManagedTags.ForceWork)) set.Add(ManagedTags.ForceWork);

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
                    else if (def.GetModExtension<VirtualNonHumanlikePawnControlExtension>() != null)
                    {
                        tagCache[def] = Build(def);
                    }
                }
                Log.Message("[PawnControl] Tag cache rebuilt for " + tagCache.Count + " pawns.");
            }

            public static void EnsurePawnTagDefExists(string tag) => Utility_TagCatalog.EnsureTagDefExists(tag, "Auto", "Auto-created from tag string.");

            public static void SyncModExtension(DefModExtension modExtension)
            {
                if (modExtension == null)
                {
                    return;
                }

                // Sync Physical ModExtension
                if (modExtension is NonHumanlikePawnControlExtension physicalModExtension)
                {
                    if (physicalModExtension.tags == null)
                    {
                        physicalModExtension.tags = new List<string>();
                    }

                    // Sync ForceAnimal between Enum and String-based Tag
                    if (physicalModExtension.tags.Contains(ManagedTags.ForceAnimal))
                    {
                        physicalModExtension.forceAnimal = true;
                    }
                    else if (physicalModExtension.forceAnimal == true && !physicalModExtension.tags.Contains(ManagedTags.ForceAnimal))
                    {
                        physicalModExtension.tags.Add(ManagedTags.ForceAnimal);
                    }

                    // Sync ForceDraftable between Enum and String-based Tag
                    if (physicalModExtension.tags.Contains(ManagedTags.ForceDraftable))
                    {
                        physicalModExtension.forceDraftable = true;
                    }
                    else if (physicalModExtension.forceDraftable == true && !physicalModExtension.tags.Contains(ManagedTags.ForceDraftable))
                    {
                        physicalModExtension.tags.Add(ManagedTags.ForceDraftable);
                    }

                    // Sync ForceTrainerTab between Enum and String-based Tag
                    if (physicalModExtension.tags.Contains(ManagedTags.ForceTrainerTab))
                    {
                        physicalModExtension.forceTrainerTab = true;
                    }
                    else if (physicalModExtension.forceTrainerTab == true && !physicalModExtension.tags.Contains(ManagedTags.ForceTrainerTab))
                    {
                        physicalModExtension.tags.Add(ManagedTags.ForceTrainerTab);
                    }

                    // Sync ForceWork between Enum and String-based Tag
                    if (physicalModExtension.tags.Contains(ManagedTags.ForceWork))
                    {
                        physicalModExtension.forceWork = true;
                    }
                    else if (physicalModExtension.forceWork == true && !physicalModExtension.tags.Contains(ManagedTags.ForceWork))
                    {
                        physicalModExtension.tags.Add(ManagedTags.ForceWork);
                    }

                    // Sync pawnTagDefs to tags
                    if (physicalModExtension.pawnTagDefs != null)
                    {
                        // Merge + normalize
                        var combined = physicalModExtension.tags.Concat(physicalModExtension.pawnTagDefs.Select(d => d.defName));
                        physicalModExtension.tags = NormalizeTagList(combined);

                        // Regenerate pawnTagDefs from tags — for UI/tooltip/color use
                        RegeneratePawnTagDefsFromStrings(physicalModExtension.tags, out physicalModExtension.pawnTagDefs);
                    }
                }

                // Sync Virtual ModExtension
                if (modExtension is VirtualNonHumanlikePawnControlExtension virtualModExtension)
                {
                    if (virtualModExtension.tags == null)
                    {
                        virtualModExtension.tags = new List<string>();
                    }

                    // Sync ForceAnimal between Enum and String-based Tag
                    if (virtualModExtension.tags.Contains(ManagedTags.ForceAnimal))
                    {
                        virtualModExtension.forceAnimal = true;
                    }
                    else if (virtualModExtension.forceAnimal == true && !virtualModExtension.tags.Contains(ManagedTags.ForceAnimal))
                    {
                        virtualModExtension.tags.Add(ManagedTags.ForceAnimal);
                    }

                    // Sync ForceDraftable between Enum and String-based Tag
                    if (virtualModExtension.tags.Contains(ManagedTags.ForceDraftable))
                    {
                        virtualModExtension.forceDraftable = true;
                    }
                    else if (virtualModExtension.forceDraftable == true && !virtualModExtension.tags.Contains(ManagedTags.ForceDraftable))
                    {
                        virtualModExtension.tags.Add(ManagedTags.ForceDraftable);
                    }

                    // Sync ForceTrainerTab between Enum and String-based Tag
                    if (virtualModExtension.tags.Contains(ManagedTags.ForceTrainerTab))
                    {
                        virtualModExtension.forceTrainerTab = true;
                    }
                    else if (virtualModExtension.forceTrainerTab == true && !virtualModExtension.tags.Contains(ManagedTags.ForceTrainerTab))
                    {
                        virtualModExtension.tags.Add(ManagedTags.ForceTrainerTab);
                    }

                    // Sync ForceWork between Enum and String-based Tag
                    if (virtualModExtension.tags.Contains(ManagedTags.ForceWork))
                    {
                        virtualModExtension.forceWork = true;
                    }
                    else if (virtualModExtension.forceWork == true && !virtualModExtension.tags.Contains(ManagedTags.ForceWork))
                    {
                        virtualModExtension.tags.Add(ManagedTags.ForceWork);
                    }

                    // Sync pawnTagDefs to tags
                    if (virtualModExtension.pawnTagDefs != null)
                    {
                        // Merge + normalize
                        var combined = virtualModExtension.tags.Concat(virtualModExtension.pawnTagDefs.Select(d => d.defName));
                        virtualModExtension.tags = NormalizeTagList(combined);

                        // Regenerate pawnTagDefs from tags — for UI/tooltip/color use
                        RegeneratePawnTagDefsFromStrings(virtualModExtension.tags, out virtualModExtension.pawnTagDefs);
                    }
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

            public static bool HasEnumTag(ThingDef def, PawnEnumTags tagEnum)
            {
                return HasTag(def, tagEnum.ToString().Trim());
            }
        }

        public static void RegeneratePawnTagDefsFromStrings(List<string> tagStrings, out List<PawnTagDef> resolvedDefs)
        {
            resolvedDefs = new List<PawnTagDef>();
            foreach (var tag in tagStrings)
            {
                Utility_TagCatalog.EnsureTagDefExists(tag);
                var def = DefDatabase<PawnTagDef>.GetNamedSilentFail(tag);
                if (def != null)
                {
                    resolvedDefs.Add(def);
                }
            }
        }

        public static ThinkTreeDef GetCachedMainThinkTree(Pawn pawn)
        {
            if (pawn == null || pawn.def == null)
            {
                return null;
            }

            ThinkTreeDef result;
            if (cachedMainTrees.TryGetValue(pawn.def, out result))
            {
                return result;
            }

            var physicalModExtension = pawn.def.GetModExtension<NonHumanlikePawnControlExtension>();

            if (physicalModExtension != null)
            {
                GetCachedMainThinkTreeInner(pawn, physicalModExtension, out result);
            }

            var virtualModExtension = pawn.def.GetModExtension<VirtualNonHumanlikePawnControlExtension>();

            if (virtualModExtension != null)
            {
                GetCachedMainThinkTreeInner(pawn, virtualModExtension, out result);
            }

            cachedMainTrees[pawn.def] = result;
            return result;
        }

        private static ThinkTreeDef GetCachedMainThinkTreeInner(Pawn pawn, DefModExtension modExtension, out ThinkTreeDef result)
        {
            result = null;

            if (modExtension == null)
            {
                return result;
            }            

            if (modExtension is NonHumanlikePawnControlExtension physicalModExtension)
            {
                if (physicalModExtension.overrideThinkTreeMain != null)
                {
                    result = physicalModExtension.overrideThinkTreeMain;
                }
                else
                {
                    foreach (var kv in fallbackTagToTreeMain)
                    {
                        if (Tags.HasTag(pawn.def, kv.Key))
                        {
                            result = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(kv.Value);
                            if (result != null)
                            {
                                break;
                            }
                        }
                    }
                }
            }

            return result;
        }

        public static ThinkTreeDef GetCachedConstantThinkTree(Pawn pawn)
        {
            if (pawn == null || pawn.def == null) return null;

            ThinkTreeDef result;
            if (cachedConstantTrees.TryGetValue(pawn.def, out result))
                return result;

            var physicalModExtension = pawn.def.GetModExtension<NonHumanlikePawnControlExtension>();

            if (physicalModExtension != null)
            {
                GetCachedConstantThinkTreeInner(pawn, physicalModExtension, out result);
            }

            var virtualModExtension = pawn.def.GetModExtension<VirtualNonHumanlikePawnControlExtension>();

            if (virtualModExtension != null)
            {
                GetCachedConstantThinkTreeInner(pawn, virtualModExtension, out result);
            }

            cachedConstantTrees[pawn.def] = result;
            return result;
        }

        private static ThinkTreeDef GetCachedConstantThinkTreeInner(Pawn pawn, DefModExtension modExtension, out ThinkTreeDef result)
        {
            result = null;

            if (modExtension == null)
            {
                return result;
            }

            if (modExtension is NonHumanlikePawnControlExtension physicalModExtension)
            {
                if (physicalModExtension.overrideThinkTreeConstant != null)
                {
                    result = physicalModExtension.overrideThinkTreeConstant;
                }
                else
                {
                    foreach (var kv in fallbackTagToTreeMain)
                    {
                        if (Tags.HasTag(pawn.def, kv.Key))
                        {
                            result = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(kv.Value);
                            if (result != null)
                            {
                                break;
                            }
                        }
                    }
                }
            }

            return result;
        }

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
            if (modExtension is NonHumanlikePawnControlExtension physicalModExtension)
            {
                Tags.SyncModExtension(physicalModExtension);
            }
            if (modExtension is VirtualNonHumanlikePawnControlExtension virtualModExtension)
            {
                Tags.SyncModExtension(virtualModExtension);
            }
        }

        /// <summary>
        /// Retrieves suggested tags for a ThingDef, using cache if available.
        /// </summary>
        public static List<PawnTagDef> GetSuggestedTags(ThingDef def)
        {
            if (def == null)
            {
                return new List<PawnTagDef>();
            }

            if (suggestedTagCache.TryGetValue(def, out var cached))
            {
                return cached;
            }

            var thisExt = def.GetModExtension<VirtualNonHumanlikePawnControlExtension>();
            if (thisExt == null)
            {
                return new List<PawnTagDef>();
            }

            // Cache resolved current tags (once)
            HashSet<string> currentTags = new HashSet<string>(NormalizeTagList(Tags.Get(def)));

            Dictionary<string, int> usageCount = new Dictionary<string, int>();
            HashSet<string> suggestedTagNames = new HashSet<string>();

            foreach (ThingDef other in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (other == def || !Utility_TagCatalog.IsSimilarRace(def, other))
                {
                    continue;
                }

                var otherExt = other.GetModExtension<VirtualNonHumanlikePawnControlExtension>();
                if (otherExt?.tags == null)
                {
                    continue;
                }

                foreach (string resolved in NormalizeTagList(otherExt.tags))
                {
                    if (currentTags.Contains(resolved))
                    {
                        continue;
                    }

                    if (usageCount.TryGetValue(resolved, out int count))
                    {
                        usageCount[resolved] = count + 1;
                    }
                    else
                    {
                        usageCount[resolved] = 1;
                    }

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
            {
                return resolved;
            }

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

            var physicalModExtension = def.GetModExtension<NonHumanlikePawnControlExtension>();
            if (physicalModExtension != null)
            {
                RefreshTagCache(def, physicalModExtension); // Rebuilds the actual tag logic cache
            }

            var virtualModExtension = def.GetModExtension<VirtualNonHumanlikePawnControlExtension>();
            if (virtualModExtension != null)
            {
                RefreshTagCache(def, virtualModExtension); // Rebuilds the actual tag logic cache
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

            var resolvedTags = new HashSet<string>(NormalizeTagList(Tags.Get(pawn.def)));

            string specificTag = ResolveTagPriority(ManagedTags.AllowWorkPrefix + workType.defName);
            return resolvedTags.Contains(specificTag) || resolvedTags.Contains(ManagedTags.AllowAllWork);
        }

        public static bool IsWorkTypeEnabledForRace(ThingDef def, WorkTypeDef workType)
        {
            if (def == null || def.race?.Humanlike == true)
                return true;

            var resolvedTags = Tags.Get(def).Select(ResolveTagPriority).ToHashSet();

            string specificTag = ResolveTagPriority(ManagedTags.AllowWorkPrefix + workType.defName);
            return resolvedTags.Contains(specificTag) || resolvedTags.Contains(ManagedTags.AllowAllWork);
        }

        public static void ApplyTaggedWorkPriorities(Pawn pawn)
        {
            if (pawn?.workSettings == null || pawn.def == null)
                return;

            // Get resolved tags once and reuse
            var resolvedTags = new HashSet<string>(NormalizeTagList(Tags.Get(pawn.def)));

            foreach (WorkTypeDef workType in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                string tag = ResolveTagPriority(ManagedTags.AllowWorkPrefix + workType.defName);

                // Also accept AllowAllWork
                if (resolvedTags.Contains(tag) || resolvedTags.Contains(ManagedTags.AllowAllWork))
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
            {
                return 0;
            }

            var physicalModExtension = pawn.def.GetModExtension<NonHumanlikePawnControlExtension>();
            var virtualModExtension = pawn.def.GetModExtension<VirtualNonHumanlikePawnControlExtension>();

            if (physicalModExtension != null && physicalModExtension.baseSkillLevelOverride.HasValue)
            {
                return physicalModExtension.baseSkillLevelOverride.Value;
            }

            if (virtualModExtension != null && virtualModExtension.baseSkillLevelOverride.HasValue)
            {
                return virtualModExtension.baseSkillLevelOverride.Value;
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
                if (physicalModExtension != null)
                {
                    return physicalModExtension.skillLevelToolUser;
                }
                if (virtualModExtension != null)
                {
                    return virtualModExtension.skillLevelToolUser;
                }
                return 10;
            }

            if (race.Animal)
            {
                // Heuristic: some modders may use trainability to reflect “complexity”
                if (race.trainability == TrainabilityDefOf.Advanced)
                {
                    if (physicalModExtension != null)
                    {
                        return physicalModExtension.skillLevelAnimalAdvanced;
                    }
                    if (virtualModExtension != null)
                    {
                        return virtualModExtension.skillLevelAnimalAdvanced;
                    }
                    return 5;
                }
                if (race.trainability == TrainabilityDefOf.Intermediate)
                {
                    if (physicalModExtension != null)
                    {
                        return physicalModExtension.skillLevelAnimalIntermediate;
                    }
                    if (virtualModExtension != null)
                    {
                        return virtualModExtension.skillLevelAnimalIntermediate;
                    }
                    return 3;
                }
                else 
                {
                    if (physicalModExtension != null)
                    {
                        return physicalModExtension.skillLevelAnimalBasic;
                    }
                    if (virtualModExtension != null)
                    {
                        return virtualModExtension.skillLevelAnimalBasic;
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

        public static List<ThingDef> GetEligibleNonHumanlikeRaces(string searchText = null, Func<ThingDef, bool> additionalFilter = null)
        {
            try
            {
                // Refresh the cache if it is null
                if (cachedRaceDefs == null)
                {
                    Log.Message("[PawnControl] Refreshing cachedRaceDefs...");

                    // Fetch the eligible non-humanlike races from DefDatabase
                    cachedRaceDefs = DefDatabase<ThingDef>.AllDefsListForReading
                        .Where(def =>
                            def.race != null &&
                            def.label != null &&
                            !Utility_HARCompatibility.IsHARRace(def) &&
                            !def.race.Humanlike &&
                            def.GetModExtension<NonHumanlikePawnControlExtension>() == null)
                        .OrderBy(def => def.label)
                        .ToList();
                }

                // Apply the search text filter if provided
                IEnumerable<ThingDef> filteredDefs = cachedRaceDefs;

                if (!string.IsNullOrEmpty(searchText))
                {
                    if (Utility_NonHumanlikePawnControl.DebugMode())
                    {
                        Log.Message($"[PawnControl] Filtering by searchText: {searchText}");
                    }

                    filteredDefs = filteredDefs.Where(def => def.label != null && def.label.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0);
                }

                // Apply additional custom filter if provided
                if (additionalFilter != null)
                {
                    if (Utility_NonHumanlikePawnControl.DebugMode())
                    {
                        Log.Message("[PawnControl] Applying additional filter...");
                    }
                    filteredDefs = filteredDefs.Where(additionalFilter);
                }

                return filteredDefs.ToList();
            }
            catch (Exception ex)
            {
                Log.Error($"[PawnControl] Exception in GetEligibleNonHumanlikeRaces: {ex}");
                return new List<ThingDef>();
            }
        }

        public static void RefreshEligibleNonHumanlikeRacesCache()
        {
            try
            {
                // Only refresh if the cache is null or outdated
                if (cachedRaceDefs == null || cachedRaceDefs.Count == 0)
                {
                    if (Utility_NonHumanlikePawnControl.DebugMode())
                    {
                        Log.Message("[PawnControl] Refreshing cachedRaceDefs...");
                    }

                    // Fetch the eligible non-humanlike races from DefDatabase
                    cachedRaceDefs = DefDatabase<ThingDef>.AllDefsListForReading
                        .Where(def =>
                            def.race != null &&
                            def.label != null &&
                            !Utility_HARCompatibility.IsHARRace(def) &&
                            !def.race.Humanlike &&
                            def.GetModExtension<NonHumanlikePawnControlExtension>() == null)
                        .OrderBy(def => def.label)
                        .ToList();
                    if (Utility_NonHumanlikePawnControl.DebugMode())
                    {
                        Log.Message($"[PawnControl] Cached {cachedRaceDefs.Count} eligible non-humanlike races.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[PawnControl] Exception while refreshing cachedRaceDefs: {ex}");
                cachedRaceDefs = new List<ThingDef>(); // Reset the cache to avoid further issues
            }
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

        public static void ClearResolvedTagCache()
        {
            resolvedTagCache.Clear();
        }
    }
}
