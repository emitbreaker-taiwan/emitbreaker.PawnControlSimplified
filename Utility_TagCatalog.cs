using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace emitbreaker.PawnControl
{
    public static class Utility_TagCatalog
    {
        private static HashSet<string> enumTagsCache;
        private static readonly Dictionary<string, Func<bool>> dlcRequirements = new Dictionary<string, Func<bool>>
        {
            { "BlockWork_Childcare", () => ModsConfig.BiotechActive },
            { "AllowWork_Childcare", () => ModsConfig.BiotechActive },
            { "BlockWork_DarkStudy", () => ModsConfig.AnomalyActive },
            { "AllowWork_DarkStudy", () => ModsConfig.AnomalyActive }
        };

        /// <summary>
        /// Injects all enum-based tags into the known tag list.
        /// Applies DLC filters, registers as PawnTagDef if missing, and caches them.
        /// </summary>
        public static void InjectEnumTags(HashSet<string> knownTags)
        {
            if (enumTagsCache == null)
            {
                enumTagsCache = new HashSet<string>();
                foreach (string enumTag in Enum.GetNames(typeof(PawnEnumTags)))
                {
                    // ✅ Optional DLC check
                    if (dlcRequirements.TryGetValue(enumTag, out Func<bool> requirement))
                    {
                        if (!requirement()) continue;
                    }

                    enumTagsCache.Add(enumTag);

                    // ✅ Ensure Def exists (register if missing)
                    if (DefDatabase<PawnTagDef>.GetNamedSilentFail(enumTag) == null)
                    {
                        EnsureTagDefExists(enumTag, "Enum", "PawnControl_EnumDesc");
                    }
                }
            }

            // ✅ Inject into knownTags from cache
            foreach (var tag in enumTagsCache)
            {
                knownTags.Add(tag);
            }
        }

        // Ensures the tag is defined once in DefDatabase<PawnTagDef>
        public static void EnsureTagDefExists(string tagName, string category = "Auto", string descriptionKey = "PawnControl_AutoDesc")
        {
            if (string.IsNullOrWhiteSpace(tagName))
            {
                return;
            }

            string normalized = tagName.Trim();

            if (DefDatabase<PawnTagDef>.GetNamedSilentFail(normalized) != null)
            {
                return;
            }

            var newDef = new PawnTagDef
            {
                defName = normalized,
                label = normalized,
                description = descriptionKey.Translate(),
                category = category
            };

            // ✅ Optional: assign color if tag maps to enum
            if (TryGetEnum(normalized, out var tagEnum))
            {
                newDef.color = GetColorFor(tagEnum);
            }

            DefDatabase<PawnTagDef>.Add(newDef);
        }

        /// <summary>
        /// Apply work tag policy based on modExtension.tags (usually from PawnTagEditor).
        /// Handles AllowAllWork, BlockAllWork, and DLC-gated individual Allow/Block tags.
        /// </summary>
        public static void ApplyPawnWorkTagPolicy(Pawn pawn)
        {
            if (pawn == null || pawn.workSettings == null || pawn.def == null)
                return;

            var tags = Utility_CacheManager.Tags.Get(pawn.def);
            if (tags.NullOrEmpty())
                return;

            // === Full overrides
            if (tags.Contains("BlockAllWork"))
            {
                pawn.workSettings.DisableAll();
                return; // hard override
            }

            if (tags.Contains("AllowAllWork"))
            {
                pawn.workSettings.EnableAndInitialize();
                return;
            }

            // === Apply specific AllowWork_X and BlockWork_X entries
            foreach (string tag in tags)
            {
                if (tag.StartsWith("AllowWork_"))
                {
                    if (!CanUseWorkTag(tag)) continue;

                    string workName = tag.Substring("AllowWork_".Length);
                    WorkTypeDef def = DefDatabase<WorkTypeDef>.GetNamedSilentFail(workName);
                    if (def != null)
                    {
                        pawn.workSettings.EnableAndInitialize();
                        pawn.workSettings.SetPriority(def, 3);
                    }
                }

                if (tag.StartsWith("BlockWork_"))
                {
                    if (!CanUseWorkTag(tag)) continue;

                    string workName = tag.Substring("BlockWork_".Length);
                    WorkTypeDef def = DefDatabase<WorkTypeDef>.GetNamedSilentFail(workName);
                    if (def != null)
                    {
                        pawn.workSettings.Disable(def);
                    }
                }
            }
        }

        /// <summary>
        /// Prevent usage of tags requiring unavailable DLCs.
        /// </summary>
        public static bool CanUseWorkTag(string tag)
        {
            if (tag.Contains("Childcare") && !ModsConfig.BiotechActive)
            {
                return false;
            }

            if (tag.Contains("DarkStudy") && !ModsConfig.AnomalyActive)
            {
                return false;
            }

            return true;
        }

        public static bool IsEnumTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return false;
            return GetAllEnumTags().Contains(tag);
        }

        public static IEnumerable<string> GetAllEnumTags()
        {
            if (enumTagsCache == null)
            {
                enumTagsCache = new HashSet<string>(Enum.GetNames(typeof(PawnEnumTags)));
            }
            return enumTagsCache;
        }

        /// <summary>
        /// Determines whether two ThingDefs are similar enough to suggest tags between them.
        /// Similarity is based on body, intelligence, and flesh status.
        /// </summary>
        public static bool IsSimilarRace(ThingDef a, ThingDef b)
        {
            if (a?.race == null || b?.race == null)
                return false;

            return a.race.body == b.race.body &&
                   a.race.intelligence == b.race.intelligence &&
                   a.race.IsFlesh == b.race.IsFlesh;
        }

        public static PawnEnumTags ToEnum(string str) => TryGetEnum(str, out var tag) ? tag : PawnEnumTags.Unknown;

        private static bool TryGetEnum(string tagName, out PawnEnumTags tag)
        {
            return Enum.TryParse(tagName, true, out tag);
        }

        public static string ToString(PawnEnumTags tag) => tag.ToString();

        private static Color GetColorFor(PawnEnumTags tag)
        {
            if (tag == PawnEnumTags.ForceAnimal)
                return Color.green;
            if (tag == PawnEnumTags.ForceDraftable)
                return Color.red;
            if (tag == PawnEnumTags.ForceTrainerTab)
                return Color.cyan;
            if (tag == PawnEnumTags.ForceWork)
                return Color.yellow;

            return Color.gray;
        }
    }
}