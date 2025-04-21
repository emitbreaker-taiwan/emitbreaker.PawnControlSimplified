using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimWorld;
using UnityEngine;
using Verse;

namespace emitbreaker.PawnControl
{
    public class Utility_NonHumanlikePawnControl
    {
        private static readonly Dictionary<ThingDef, NonHumanlikePawnControlExtension> cache =
            new Dictionary<ThingDef, NonHumanlikePawnControlExtension>();

        private static readonly HashSet<ThingDef> ForceAnimalDefs = new HashSet<ThingDef>();
        public static readonly HashSet<ThingDef> ForceDraftableDefs = new HashSet<ThingDef>();
        public static readonly HashSet<ThingDef> ForceWorkDefs = new HashSet<ThingDef>();
        public static readonly HashSet<ThingDef> ForceTrainerDefs = new HashSet<ThingDef>();

        public static bool IsForceAnimal(ThingDef def) => ForceAnimalDefs.Contains(def);

        public static NonHumanlikePawnControlExtension GetExtension(ThingDef def)
        {
            if (def == null) return null;
            if (!cache.TryGetValue(def, out var ext))
            {
                ext = def.GetModExtension<NonHumanlikePawnControlExtension>();
                cache[def] = ext;
            }
            return ext;
        }

        public static bool HasTag(ThingDef def, string tag)
        {
            return Utility_CacheManager.HasTag(def, tag);
        }

        public static bool ShouldDraftInject(Pawn pawn)
        {
            if (!PawnChecker(pawn))
            {
                return false;
            }
            foreach (var tag in Utility_CacheManager.GetTags(pawn.def))
            {
                if (TagEnumHelper.ToEnum(tag) == PawnTag.AutoDraftInjection)
                {
                    return true;
                }
            }
            return false;
        }

        public static void PrefetchAll()
        {
            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.HasModExtension<NonHumanlikePawnControlExtension>())
                {
                    var modExtension = def.GetModExtension<NonHumanlikePawnControlExtension>();
                    cache[def] = modExtension;
                }
            }
        }

        public static void PrefetchAllTags()
        {
            ForceAnimalDefs.Clear();
            ForceDraftableDefs.Clear();
            ForceWorkDefs.Clear();
            ForceTrainerDefs.Clear();

            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (Utility_CacheManager.HasTag(def, NonHumanlikePawnControlTags.ForceAnimal))
                    ForceAnimalDefs.Add(def);

                if (Utility_CacheManager.HasTag(def, NonHumanlikePawnControlTags.ForceDraftable))
                    ForceDraftableDefs.Add(def);

                if (Utility_CacheManager.HasTag(def, NonHumanlikePawnControlTags.ForceWork))
                    ForceWorkDefs.Add(def);

                if (Utility_CacheManager.HasTag(def, NonHumanlikePawnControlTags.ForceTrainerTab))
                    ForceTrainerDefs.Add(def);
            }
        }

        public static string ResolveWorkTypeDefNameFromLabel(string label)
        {
            return DefDatabase<WorkTypeDef>.AllDefs
                .FirstOrDefault(w =>
                    w.labelShort != null &&
                    w.labelShort.Replace(" ", "").Equals(label.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)
                )?.defName;
        }

        public static bool PawnChecker(Pawn pawn)
        {
            if (pawn == null || pawn.RaceProps.Humanlike || pawn.Dead || !pawn.Spawned || pawn.IsDessicated())
            {
                return false;
            }

            if (Utility_VehicleFramework.IsVehiclePawn(pawn))
            {
                return false;
            }

            return true;
        }

        public static bool ShouldAppearInWorkTab(Pawn p)
        {
            return p.RaceProps.Humanlike || HasTag(p.def, NonHumanlikePawnControlTags.ForceWork);
        }

        public static bool IsApparelAllowedForPawn(Pawn pawn, ThingDef apparelDef)
        {
            if (pawn == null || apparelDef == null || !apparelDef.IsApparel) return false;

            var ext = GetExtension(pawn.def);
            if (ext == null) return true;

            if (ext.restrictApparelByBodyType)
            {
                if (!Utility_HARCompatibility.IsAllowedBodyType(pawn, ext.allowedBodyTypes))
                    return false;
            }

            // Add more rules here if needed (e.g., gender-specific, CE armor tags, etc.)
            return true;
        }

        public static ThinkTreeDef GetForcedThinkTreeDef(Pawn pawn)
        {
            return Utility_CacheManager.GetCachedMainThinkTree(pawn);
        }

        public static bool ApplyWorkTypeOverride(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn == null || workType == null || pawn.def == null) return false;

            var tags = Utility_CacheManager.Tags.Get(pawn.def);
            if (tags.NullOrEmpty()) return false;

            // 🔒 Handle global overrides first
            if (tags.Contains(NonHumanlikePawnControlTags.BlockAllWork)) return true;
            if (tags.Contains(NonHumanlikePawnControlTags.AllowAllWork)) return false;

            string allowTag = NonHumanlikePawnControlTags.AllowWorkPrefix + workType.defName;
            string blockTag = NonHumanlikePawnControlTags.BlockWorkPrefix + workType.defName;

            if (tags.Contains(blockTag) && Utility_TagCatalog.CanUseWorkTag(blockTag))
                return true;

            if (tags.Contains(allowTag) && Utility_TagCatalog.CanUseWorkTag(allowTag))
                return false;

            return false;
        }

        // Ensures the tag is defined once in DefDatabase<PawnTagDef>
        public static void EnsureTagDefExists(string tagName, string category = "Auto", string descriptionKey = "PawnControl_AutoDesc")
        {
            if (string.IsNullOrEmpty(tagName)) return;
            if (DefDatabase<PawnTagDef>.GetNamedSilentFail(tagName) != null) return;

            DefDatabase<PawnTagDef>.Add(new PawnTagDef
            {
                defName = tagName,
                label = tagName,
                description = descriptionKey.Translate(),
                category = category
            });
        }

        // === Applies preset to currently active editor, or shows fallback message ===
        public static void TryApplyPresetToActiveTagEditor(PawnTagPreset preset)
        {
            if (preset == null)
            {
                Messages.Message("PawnControl_NoPresetProvided".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            WindowStack stack = Find.WindowStack;
            Dialog_PawnTagEditor editor = stack?.WindowOfType<Dialog_PawnTagEditor>();

            if (editor?.SelectedModExtension != null)
            {
                var modExt = editor.SelectedModExtension;

                // Overwrite tags
                if (Utility_ModExtensionResolver.HasPhysicalModExtension(editor.SelectedDef))
                {
                    modExt.tags.Clear();
                    foreach (string tag in preset.tags)
                    {
                        if (!modExt.tags.Contains(tag))
                            modExt.tags.Add(tag);
                    }
                }
                else
                {
                    foreach (string tag in preset.tags)
                    {
                        Utility_ModExtensionResolver.AddTag(editor.SelectedDef, tag);
                    }
                }

                // Ensure tag cache and PawnTagDefs sync
                if (modExt != null && editor != null)
                {
                    Utility_CacheManager.RefreshTagCache(editor.SelectedDef, modExt);
                }

                Messages.Message("PawnControl_ApplyPresetSuccess".Translate(preset.name), MessageTypeDefOf.TaskCompletion, false);
                return;
            }

            // Fallback: No editor open
            Messages.Message("PawnControl_NoActiveEditor".Translate(), MessageTypeDefOf.RejectInput, false);

            // Optional: Export fallback to clipboard
            string export = string.Join("\n", preset.tags);
            GUIUtility.systemCopyBuffer = export;
            Log.Warning($"[PawnControl] Copied preset '{preset.name}' tags to clipboard as fallback.\n{export}");
        }

        public static bool ShouldBlockAllWork(Pawn pawn)
        {
            return Utility_CacheManager.Tags.Get(pawn.def).Contains("BlockAllWork");
        }

        public static bool ShouldAllowAllWork(Pawn pawn)
        {
            return Utility_CacheManager.Tags.Get(pawn.def).Contains("AllowAllWork");
        }

        public static void InjectExtensionsToAllNonHAR()
        {
            int injectedCount = 0;

            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.race == null) continue;
                if (Utility_HARCompatibility.IsHARRace(def)) continue;

                if (def.modExtensions != null && def.modExtensions.OfType<NonHumanlikePawnControlExtension>().Any())
                    continue;

                if (def.modExtensions == null)
                    def.modExtensions = new List<DefModExtension>();

                def.modExtensions.Add(new VirtualNonHumanlikePawnControlExtension());
                injectedCount++;
            }

            Messages.Message($"[PawnControl] Injected control extension to {injectedCount} races.", MessageTypeDefOf.TaskCompletion);
        }

        public static bool TryInjectVirtualAsPhysical(ThingDef def)
        {
            if (def == null)
            {
                return false;
            }

            // 🛑 Only inject if physical modExtension exists
            if (def.GetModExtension<NonHumanlikePawnControlExtension>() != null)
            {
                return false;
            }

            var modExtension = def.GetModExtension<VirtualNonHumanlikePawnControlExtension>();

            if (modExtension == null)
            {
                modExtension = new VirtualNonHumanlikePawnControlExtension();
            }

            def.modExtensions.Add(modExtension);

            modExtension = def.GetModExtension<VirtualNonHumanlikePawnControlExtension>();

            modExtension.tags = new List<string>(VirtualTagStorage.Instance.Get(def));

            return true;
        }

        public static List<string> EnsureModExtensionTagList(NonHumanlikePawnControlExtension modExtension)
        {
            if (modExtension == null)
                return null;

            if (modExtension.tags == null)
                modExtension.tags = new List<string>();

            return modExtension.tags;
        }

        public static bool IsValidRaceCandidate(ThingDef def)
        {
            if (def == null || def.race == null) return false;
            if (!typeof(Pawn).IsAssignableFrom(def.thingClass)) return false;

            // Avoid HAR alien races (optional)
            if (def.modExtensions != null && def.modExtensions.Any(ext => ext.GetType().Name.Contains("AlienRace"))) return false;

            return true;
        }

        private static ThingDef _activeTagEditorTarget;

        public static void SetActiveTagEditorTarget(ThingDef def)
        {
            _activeTagEditorTarget = def;
        }

        public static ThingDef GetActiveTagEditorTarget()
        {
            return _activeTagEditorTarget;
        }

        public static void ClearCache()
        {
            cache.Clear();      // extension cache
        }
    }
}