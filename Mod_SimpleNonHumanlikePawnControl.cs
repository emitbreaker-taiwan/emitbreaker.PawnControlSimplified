using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Verse.AI.Group;
using Verse.AI;
using System.Reflection;
using static emitbreaker.PawnControl.HarmonyPatches;
using UnityEngine;

namespace emitbreaker.PawnControl
{
    public class Mod_SimpleNonHumanlikePawnControl : Mod
    {
        public override string SettingsCategory()
        {
            return "PawnControl_ModName".Translate();
        }

        public Mod_SimpleNonHumanlikePawnControl(ModContentPack content) : base(content)
        {
            var harmony = new Harmony("emitbreaker.PawnControl");

            if (GetSettings<ModSettings_SimpleNonHumanlikePawnControl>().harmonyPatchAll)
            {
                harmony.PatchAll();
                PatchVFEAIJobGiver(harmony);
            }

            // Optional LordToil patches
            PatchLordToilIfExists(harmony, "LordToil_DefendAndExpand", 28f);
            PatchLordToilIfExists(harmony, "LordToil_StageThenAttack", 20f);
            PatchLordToilIfExists(harmony, "LordToil_RaidSmart", 32f);
            PatchLordToilIfExists(harmony, "LordToil_MechClusterDefend", 18f);
            PatchBiotechMechClusterDefend(harmony);

            LongEventHandler.ExecuteWhenFinished(() =>
            {
                TryPatchApparelFilter(harmony);
            });
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            float y = 0f;
            float spacing = 35f;

            y += spacing;

            List<ThingDef> allNonHAR = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(def => def.race != null && !Utility_HARCompatibility.IsHARRace(def))
                .ToList();

            int injected = allNonHAR.Count(def => Utility_ModExtensionResolver.HasPhysicalModExtension(def));

            Widgets.Label(new Rect(inRect.x, y, inRect.width, 30f),
                "PawnControl_ModSettings_AutoInjectSummary".Translate(injected, allNonHAR.Count));
            y += spacing;

            // Inject button: inject virtual -> physical if no physical modExtension exists
            if (Widgets.ButtonText(new Rect(inRect.x, y, 420f, 30f), "PawnControl_ModSettings_InjectNow".Translate()))
            {
                int count = 0;
                foreach (var def in allNonHAR)
                {
                    if (Utility_NonHumanlikePawnControl.TryInjectVirtualAsPhysical(def))
                        count++;
                }
                Messages.Message($"[PawnControl] Injected to {count} races.", MessageTypeDefOf.TaskCompletion);
            }
            y += spacing;

            // Remove button: removes only runtime-injected modExtensions
            if (Widgets.ButtonText(new Rect(inRect.x, y, 420f, 30f), "PawnControl_ModSettings_RemoveInjected".Translate()))
            {
                int removed = 0;
                foreach (var def in allNonHAR)
                {
                    if (def.modExtensions != null)
                    {
                        int before = def.modExtensions.Count;
                        def.modExtensions.RemoveAll(ext =>
                            ext is NonHumanlikePawnControlExtension physical &&
                            physical.isRuntimeInjected);
                        if (def.modExtensions.Count < before) removed++;
                    }
                }
                Messages.Message("PawnControl_ModSettings_DeinjectedDone".Translate(removed), MessageTypeDefOf.NeutralEvent);
            }
            y += spacing;

            // Selector dialog
            if (Widgets.ButtonText(new Rect(inRect.x, y, 420f, 30f), "PawnControl_ModSettings_OpenSelector".Translate()))
            {
                Find.WindowStack.Add(new Dialog_SelectNonHumanlikeRace());
            }
            y += spacing;

            Widgets.Label(new Rect(inRect.x, y, 420f, 26f), "PawnControl_ModSettings_ApplyPresetToActive".Translate());
            y += 30f;

            // Preset dropdown
            IEnumerable<Widgets.DropdownMenuElement<PawnTagPreset>> PresetOptions()
            {
                foreach (var preset in PawnTagPresetManager.LoadAllPresets())
                {
                    yield return new Widgets.DropdownMenuElement<PawnTagPreset>
                    {
                        option = new FloatMenuOption(preset.name, () =>
                        {
                            Utility_NonHumanlikePawnControl.TryApplyPresetToActiveTagEditor(preset);
                        }),
                        payload = preset
                    };
                }
            }

            Widgets.Dropdown<object, PawnTagPreset>(
                new Rect(inRect.x, y, 420f, 30f),
                null,
                _ => null,
                _ => PresetOptions(),
                "PawnControl_SelectPreset".Translate()
            );
        }

        private void PatchLordToilIfExists(Harmony harmony, string typeName, float radius)
        {
            var type = AccessTools.TypeByName(typeName);
            if (type != null)
            {
                var method = AccessTools.Method(type, "UpdateAllDuties");
                if (method != null)
                {
                    harmony.Patch(method, postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(HarmonyPatches.AssignGenericDefendDuty)));
                }
            }
        }

        private void PatchBiotechMechClusterDefend(Harmony harmony)
        {
            Type mechToilType = AccessTools.TypeByName("RimWorld.LordToil_MechClusterDefend");
            if (mechToilType != null)
            {
                MethodInfo updateAllDuties = AccessTools.Method(mechToilType, "UpdateAllDuties");
                MethodInfo postfix = typeof(PatchImpl_MechClusterDefend).GetMethod(nameof(PatchImpl_MechClusterDefend.Postfix));

                if (updateAllDuties != null && postfix != null)
                {
                    harmony.Patch(updateAllDuties, postfix: new HarmonyMethod(postfix));
                    Log.Message("[PawnControl] Patched MechClusterDefend.UpdateAllDuties");
                }
            }
        }

        private void TryPatchApparelFilter(Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(
                    typeof(PawnApparelGenerator),
                    nameof(PawnApparelGenerator.GenerateStartingApparelFor),
                    new Type[] { typeof(Pawn), typeof(PawnGenerationRequest) });

                var postfix = AccessTools.Method(typeof(HarmonyPatches), "Postfix_PawnApparelGenerator");

                if (target != null && postfix != null)
                {
                    harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                    Log.Message("[PawnControl] Patched PawnApparelGenerator.GenerateStartingApparelFor");
                }
                else
                {
                    Log.Warning("[PawnControl] Could not find method to patch: PawnApparelGenerator.GenerateStartingApparelFor");
                }
            }
            catch (Exception ex)
            {
                Log.Error("[PawnControl] Exception during apparel patch: " + ex);
            }
        }

        public static void PatchVFEAIJobGiver(Harmony harmony)
        {
            var vfeJobGiverType = AccessTools.TypeByName("VFECore.JobGiver_WorkVFE");
            if (vfeJobGiverType != null)
            {
                var method = AccessTools.Method(vfeJobGiverType, "TryGiveJob", new Type[] { typeof(Pawn) });
                if (method != null)
                {
                    harmony.Patch(method,
                        prefix: new HarmonyMethod(typeof(Patch_VFEAI_TryGiveJob), nameof(Patch_VFEAI_TryGiveJob.Prefix)));
                    Log.Message("[PawnControl] Patched VFECore.JobGiver_WorkVFE.TryGiveJob");
                }
                else
                {
                    Log.Warning("[PawnControl] Method TryGiveJob not found in VFECore.JobGiver_WorkVFE");
                }
            }
            else
            {
                Log.Warning("[PawnControl] Type VFECore.JobGiver_WorkVFE not found");
            }
        }
    }

    public static class PatchImpl_MechClusterDefend
    {
        public static void Postfix(object __instance)
        {
            if (!ModsConfig.BiotechActive || __instance == null) return;

            LordToil toil = __instance as LordToil;
            if (toil?.lord == null) return;

            var beaconDuty = Utility_CacheManager.GetDuty("MechDefendBeacon");
            var escortDuty = Utility_CacheManager.GetDuty("EscortCommander");

            foreach (var pawn in toil.lord.ownedPawns)
            {
                if (!Utility_NonHumanlikePawnControl.PawnChecker(pawn)) continue;
                if (!Utility_CacheManager.Tags.HasTag(pawn.def, ManagedTags.AutoDraftInjection)) continue;

                if (Utility_CacheManager.Tags.HasTag(pawn.def, ManagedTags.Mech_DefendBeacon) && beaconDuty != null)
                    pawn.mindState.duty = new PawnDuty(beaconDuty, toil.FlagLoc, 18f);
                else if (Utility_CacheManager.Tags.HasTag(pawn.def, ManagedTags.Mech_EscortCommander) && escortDuty != null)
                    pawn.mindState.duty = new PawnDuty(escortDuty, toil.FlagLoc, 22f);
                else
                    pawn.mindState.duty = new PawnDuty(DutyDefOf.Defend, toil.FlagLoc, 20f);
            }
        }
    }
}
