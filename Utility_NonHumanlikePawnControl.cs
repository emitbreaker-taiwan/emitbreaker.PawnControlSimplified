using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using static System.Net.Mime.MediaTypeNames;
using static UnityEngine.Scripting.GarbageCollector;

namespace emitbreaker.PawnControl
{
    public class Utility_NonHumanlikePawnControl
    {
        private static readonly Dictionary<ThingDef, DefModExtension> cache = new Dictionary<ThingDef, DefModExtension>();

        private static readonly HashSet<ThingDef> ForceAnimalDefs = new HashSet<ThingDef>();
        public static readonly HashSet<ThingDef> ForceDraftableDefs = new HashSet<ThingDef>();
        public static readonly HashSet<ThingDef> ForceWorkDefs = new HashSet<ThingDef>();
        public static readonly HashSet<ThingDef> ForceTrainerDefs = new HashSet<ThingDef>();

        public static bool IsForceAnimal(ThingDef def) => ForceAnimalDefs.Contains(def);

        public static bool DebugMode()
        {
            return LoadedModManager.GetMod<Mod_SimpleNonHumanlikePawnControl>().GetSettings<ModSettings_SimpleNonHumanlikePawnControl>().debugMode;
        }

        /// <summary>
        /// Inject virtual extensions to all non-HAR, non-humanlike races.
        /// Skips those with existing physical or virtual unless `force` is true.
        /// </summary>
        public static void InjectVirtualExtensions(bool force = false, bool showMessage = false)
        {
            int injectedCount = 0;

            List<ThingDef> allNonHAR = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(def => def.race != null && !Utility_HARCompatibility.IsHARRace(def) && !def.race.Humanlike)
                .ToList();

            foreach (var def in allNonHAR)
            {
                TryInjectVirtualModExtension(def, force, showMessage);
                injectedCount++;
            }

            if (showMessage)
            {
                Messages.Message($"[PawnControl] Injected control extension to {injectedCount} races.", MessageTypeDefOf.TaskCompletion);
            }
        }

        private static void TryInjectVirtualModExtension(ThingDef def, bool force = false, bool showMessage = false)
        {
            if (def == null || def.race.Humanlike)
            {
                return;
            }

            var hasPhysical = def.GetModExtension<NonHumanlikePawnControlExtension>();
            
            if (hasPhysical != null)
            {
                return;
            }

            var hasVirtual = def.GetModExtension<VirtualNonHumanlikePawnControlExtension>();

            if (!force && hasVirtual != null)
            {
                return;
            }

            if (def.modExtensions == null)
            {
                def.modExtensions = new List<DefModExtension>();
            }

            var injected = new VirtualNonHumanlikePawnControlExtension
            {
                tags = new List<string>(VirtualTagStorage.Instance.Get(def))
            };

            def.modExtensions.Add(injected);
        }

        public static void InjectVirtualExtensionsForEligibleRaces() => InjectVirtualExtensions(false, DebugMode());

        public static void InjectExtensionsToAllNonHAR() => InjectVirtualExtensions(true, DebugMode());

        public static void RemoveVirtualExtensions(bool force = false, bool showMessage = false)
        {
            int removedCount = 0;

            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.race == null || def.race.Humanlike) continue;
                if (Utility_HARCompatibility.IsHARRace(def)) continue;

                if (def.modExtensions == null) continue;

                bool hasPhysical = def.modExtensions.OfType<NonHumanlikePawnControlExtension>().Any();
                bool hasVirtual = def.modExtensions.OfType<VirtualNonHumanlikePawnControlExtension>().Any();

                if (!force && (!hasPhysical && !hasVirtual)) continue;

                // Remove the VirtualNonHumanlikePawnControlExtension if it exists
                var virtualExtension = def.modExtensions.OfType<VirtualNonHumanlikePawnControlExtension>().FirstOrDefault();
                if (virtualExtension != null)
                {
                    def.modExtensions.Remove(virtualExtension);
                    removedCount++;
                }
            }

            if (showMessage)
            {
                Messages.Message($"[PawnControl] Removed control extension from {removedCount} races.", MessageTypeDefOf.TaskCompletion);
            }
        }
        public static void RemoveVirtualExtensionsForEligibleRaces() => RemoveVirtualExtensions(false, DebugMode());

        public static void RemoveExtensionsToAllNonHAR() => RemoveVirtualExtensions(true, DebugMode());


        public static bool HasTag(ThingDef def, string tag)
        {
            return Utility_CacheManager.Tags.HasTag(def, tag);
        }

        public static bool ShouldDraftInject(Pawn pawn)
        {
            if (!PawnChecker(pawn))
            {
                return false;
            }
            foreach (var tag in Utility_CacheManager.Tags.Get(pawn.def))
            {
                if (Utility_TagCatalog.ToEnum(tag) == PawnEnumTags.AutoDraftInjection)
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
                var physicalModExtension = def.GetModExtension<NonHumanlikePawnControlExtension>();

                if (physicalModExtension != null)
                {
                    cache[def] = physicalModExtension;
                }

                var virtualModExtension = def.GetModExtension<VirtualNonHumanlikePawnControlExtension>();

                if (virtualModExtension != null)
                {                    
                    cache[def] = virtualModExtension;
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
                if (Utility_CacheManager.Tags.HasTag(def, ManagedTags.ForceAnimal))
                {
                    ForceAnimalDefs.Add(def);
                }

                if (Utility_CacheManager.Tags.HasTag(def, ManagedTags.ForceDraftable))
                {
                    ForceDraftableDefs.Add(def);
                }

                if (Utility_CacheManager.Tags.HasTag(def, ManagedTags.ForceWork))
                {
                    ForceWorkDefs.Add(def);
                }

                if (Utility_CacheManager.Tags.HasTag(def, ManagedTags.ForceTrainerTab))
                {
                    ForceTrainerDefs.Add(def);
                }
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
            return p.RaceProps.Humanlike || HasTag(p.def, ManagedTags.ForceWork);
        }

        public static bool IsApparelAllowedForPawn(Pawn pawn, ThingDef apparelDef)
        {
            if (pawn == null || apparelDef == null || !apparelDef.IsApparel) return false;

            var physicalModExtension = pawn.def.GetModExtension<NonHumanlikePawnControlExtension>();

            if (physicalModExtension != null)
            {
                if (physicalModExtension.restrictApparelByBodyType)
                {
                    if (!Utility_HARCompatibility.IsAllowedBodyType(pawn, physicalModExtension.allowedBodyTypes))
                    {
                        return false;
                    }
                }
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
            if (tags.Contains(ManagedTags.BlockAllWork)) return true;
            if (tags.Contains(ManagedTags.AllowAllWork)) return false;

            string allowTag = ManagedTags.AllowWorkPrefix + workType.defName;
            string blockTag = ManagedTags.BlockWorkPrefix + workType.defName;

            if (tags.Contains(blockTag) && Utility_TagCatalog.CanUseWorkTag(blockTag))
                return true;

            if (tags.Contains(allowTag) && Utility_TagCatalog.CanUseWorkTag(allowTag))
                return false;

            return false;
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
                var modExtension = editor.SelectedModExtension;

                // Overwrite tags
                if (modExtension is NonHumanlikePawnControlExtension physicalModExtension)
                {
                    foreach (string tag in preset.tags)
                    {
                        Utility_ModExtensionResolver.AddTag(editor.SelectedDef, tag);
                    }
                }
                else if (modExtension is VirtualNonHumanlikePawnControlExtension virtualModExtension)
                {
                    virtualModExtension.tags.Clear();
                    foreach (string tag in preset.tags)
                    {
                        if (!virtualModExtension.tags.Contains(tag))
                        {
                            virtualModExtension.tags.Add(tag);
                        }
                    }
                }

                // Ensure tag cache and PawnTagDefs sync
                if (modExtension != null && editor != null)
                {
                    Utility_CacheManager.RefreshTagCache(editor.SelectedDef, modExtension);
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
            if (def == null || def.race == null)
            {
                return false;
            }

            if (def.defName.Contains("Corpse") || def.thingCategories?.Any(cat => cat.defName == "Corpses") == true)
            {
                return false;
            }

            if (!typeof(Pawn).IsAssignableFrom(def.thingClass))
            {
                return false;
            }

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