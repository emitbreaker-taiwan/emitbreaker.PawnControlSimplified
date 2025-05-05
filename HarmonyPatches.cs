using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Jobs;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;

namespace emitbreaker.PawnControl
{
    public class HarmonyPatches
    {
        /// <summary>
        /// Patch #1 Step 1. Animal Race replacement by using cache.
        /// This class contains Harmony patches for modifying and extending the behavior of RimWorld's core systems.
        /// It includes patches for race properties, work tab injection, job assignment, and other advanced pawn control features.
        /// </summary>
        [HarmonyPatch(typeof(RaceProperties), nameof(RaceProperties.Animal), MethodType.Getter)]
        public static class Patch_RaceProperties_Animal
        {
            private static readonly Dictionary<ThingDef, bool> forcedAnimalCache = new Dictionary<ThingDef, bool>();

            public static void Postfix(RaceProperties __instance, ref bool __result)
            {
                if (__result)
                {
                    return;
                }

                PawnKindDef pawnKind = __instance.AnyPawnKind;
                if (pawnKind == null)
                {
                    return;
                }

                ThingDef raceDef = pawnKind.race;
                if (raceDef == null)
                {
                    return;
                }

                // ✅ Try fast cache lookup
                if (forcedAnimalCache.TryGetValue(raceDef, out bool isForcedAnimal))
                {
                    __result = isForcedAnimal;
                    return;
                }

                // ✅ Cache miss, check mod extension
                isForcedAnimal = Utility_CacheManager.GetModExtension(raceDef)?.forceIdentity == ForcedIdentityType.ForceAnimal;
                forcedAnimalCache[raceDef] = isForcedAnimal;

                if (isForcedAnimal)
                {
                    __result = true;
                    // Optional: Debug log
                    // if (Prefs.DevMode)
                    // {
                    //     Log.Message($"[PawnControl] ForceAnimal override applied: {raceDef.defName}");
                    // }
                }
            }

            private static void CodeBackupOriginal(RaceProperties __instance, ref bool __result)
            {
                if (__result) return; // Already animal, no debug needed

                PawnKindDef pawnKind = __instance.AnyPawnKind;
                if (pawnKind == null)
                {
                    return;
                }

                ThingDef raceDef = pawnKind.race;
                if (raceDef == null)
                {
                    return;
                }

                if (Utility_IdentityManager.IsForcedAnimal(raceDef))
                {
                    if (Prefs.DevMode)
                    {
                        Log.Message($"[PawnControl] ForceAnimal override applied: Race '{raceDef.defName}' was treated as Animal.");
                    }
                    __result = true;
                }
            }
        }

        /// <summary>
        /// Patch #1 Step 2. Humanlike Race replacement by using cache.
        /// This file contains Harmony patches for modifying and extending the behavior of RimWorld's core systems.
        /// It includes patches for race properties, work tab injection, job assignment, and other advanced pawn control features.
        /// These patches enable enhanced control over non-humanlike pawns, including work prioritization, think tree overrides, and identity-based behavior customization.
        /// </summary>
        //[HarmonyPatch(typeof(RaceProperties), nameof(RaceProperties.Humanlike), MethodType.Getter)]
        //public static class Patch_RaceProperties_Humanlike
        //{
        //    private static readonly Dictionary<ThingDef, bool> forcedHumanlikeCache = new Dictionary<ThingDef, bool>();

        //    public static void Postfix(RaceProperties __instance, ref bool __result)
        //    {
        //        if (__result)
        //        {
        //            return; // ✅ Already humanlike
        //        }

        //        PawnKindDef pawnKind = __instance.AnyPawnKind;
        //        if (pawnKind == null)
        //        {
        //            return; // ✅ No pawnKind
        //        }

        //        ThingDef raceDef = pawnKind.race;
        //        if (raceDef == null)
        //        {
        //            return; // ✅ No raceDef
        //        }

        //        if (forcedHumanlikeCache.TryGetValue(raceDef, out bool isForcedHumanlike))
        //        {
        //            __result = isForcedHumanlike;
        //            return;
        //        }

        //        isForcedHumanlike = Utility_CacheManager.GetModExtension(raceDef)?.forceHumanlike == true;
        //        forcedHumanlikeCache[raceDef] = isForcedHumanlike;

        //        if (isForcedHumanlike)
        //        {
        //            __result = true;
        //        }
        //    }
        //}

        /// <summary>  
        /// Patch #1 Step 3. Mechanoid Race replacement by using cache.
        /// Patch for RaceProperties.IsMechanoid to allow dynamic mechanoid behavior based on mod extensions.  
        /// </summary>
        //[HarmonyPatch(typeof(RaceProperties), nameof(RaceProperties.IsMechanoid), MethodType.Getter)]
        //public static class Patch_RaceProperties_IsMechanoid
        //{
        //    private static readonly Dictionary<ThingDef, bool> forcedMechanoidCache = new Dictionary<ThingDef, bool>();

        //    public static void Postfix(RaceProperties __instance, ref bool __result)
        //    {
        //        if (__result)
        //        {
        //            return; // ✅ Already mechanoid
        //        }

        //        PawnKindDef pawnKind = __instance.AnyPawnKind;
        //        if (pawnKind == null)
        //        {
        //            return; // ✅ No pawnKind
        //        }

        //        ThingDef raceDef = pawnKind.race;
        //        if (raceDef == null)
        //        {
        //            return; // ✅ No raceDef
        //        }

        //        if (forcedMechanoidCache.TryGetValue(raceDef, out bool isForcedMechanoid))
        //        {
        //            __result = isForcedMechanoid;
        //            return;
        //        }

        //        isForcedMechanoid = Utility_CacheManager.GetModExtension(raceDef)?.forceMechanoid == true;
        //        forcedMechanoidCache[raceDef] = isForcedMechanoid;

        //        if (isForcedMechanoid)
        //        {
        //            __result = true;
        //        }
        //    }
        //}

        /// <summary>
        /// Patch #2 Work Tab Injection
        /// Step 1: Inject Work Tab (with Scoped IsColonist Override)
        /// </summary>
        [HarmonyPatch(typeof(MainTabWindow_Work), "Pawns", MethodType.Getter)]
        public static class Patch_MainTabWindow_Work_Pawns
        {
            public static bool Prefix(ref IEnumerable<Pawn> __result)
            {
                using (new Utility_IdentityManager_ScopedFlagContext(FlagScopeTarget.IsColonist))
                {
                    var taggedPawns = Utility_CacheManager.GetEffectiveColonistLikePawns(Find.CurrentMap);

                    // If all are vanilla humanlike and have no mod extension — fully fallback to vanilla
                    bool isPureVanillaHumanlike = taggedPawns.All(p => p.RaceProps.Humanlike && Utility_CacheManager.GetModExtension(p.def) == null);

                    if (isPureVanillaHumanlike)
                    {
                        return true; // ✅ Let vanilla logic handle both pawn list and columns
                    }

                    // Otherwise, use injected pawn list
                    foreach (var pawn in taggedPawns)
                    {
                        if (pawn != null && pawn.guest == null)
                        {
                            pawn.guest = new Pawn_GuestTracker(pawn);
                        }
                    }

                    __result = taggedPawns;

                    // CRITICAL FIX: Don't filter work columns at all!
                    // This ensures all work types remain visible in the work tab

                    /* ⛔ REMOVED FILTERING: 
                    var workTableDef = DefDatabase<PawnTableDef>.GetNamedSilentFail("Work");
                    if (workTableDef != null)
                    {
                        workTableDef.columns = workTableDef.columns
                            .Where(col => IsWorkColumnSupported(col, taggedPawns))
                            .ToList();
                    }
                    */

                    // Instead, make sure all pawns have proper work settings initialized
                    foreach (var pawn in taggedPawns)
                    {
                        if (pawn != null && Utility_CacheManager.GetModExtension(pawn.def) != null)
                        {
                            Utility_WorkSettingsManager.EnsureWorkSettingsInitialized(pawn);
                        }
                    }

                    if (Prefs.DevMode)
                    {
                        Log.Message("[PawnControl] Work tab showing all work columns for mixed colony");
                    }

                    return false; // ✅ Skip vanilla only when we injected __result
                }
            }

            private static bool IsWorkColumnSupported(PawnColumnDef col, IEnumerable<Pawn> taggedPawns)
            {
                if (col == null || col.workType == null)
                {
                    return true; // ✅ Allow non-work columns always
                }

                string tag = ManagedTags.AllowWorkPrefix + col.workType.defName;
                bool columnEnabled = false;

                foreach (Pawn pawn in taggedPawns)
                {
                    if (pawn == null || pawn.def == null || pawn.def.race == null)
                    {
                        continue;
                    }

                    if (pawn.workSettings?.WorkIsActive(col.workType) == true)
                    {
                        columnEnabled = true;
                    }

                    var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                    if (modExtension != null)
                    {
                        if (pawn.workSettings == null || !pawn.workSettings.EverWork)
                        {
                            Utility_WorkSettingsManager.EnsureWorkSettingsInitialized(pawn);
                            columnEnabled = true;
                        }
                    }
                }

                return columnEnabled;
            }
        }

        // Step 2: Hook WrokTypeIsDisabled since modded pawn does not have actual skill.
        [HarmonyPatch(typeof(Pawn), nameof(Pawn.WorkTypeIsDisabled))]
        public static class Patch_Pawn_WorkTypeIsDisabled
        {
            [HarmonyPrefix]
            public static bool Prefix(Pawn __instance, WorkTypeDef w, ref bool __result)
            {
                var modExtension = Utility_CacheManager.GetModExtension(__instance.def);
                if (modExtension != null)
                {
                    var allowed = Utility_TagManager.WorkEnabled(__instance.def, w.defName.ToString());

                    __result = !allowed;
                    return false; // ⛔ Skip vanilla logic
                }

                return true; // ✅ Use vanilla fallback
            }
        }

        // Step 3: Hook IsIncapableOfWholeWorkType since modded pawn has no capabilities.
        [HarmonyPatch(typeof(PawnColumnWorker_WorkPriority), "IsIncapableOfWholeWorkType")]
        public static class Patch_IsIncapableOfWholeWorkType_ByTag
        {
            public static bool Prefix(Pawn p, WorkTypeDef work, ref bool __result)
            {
                if (p == null || work == null) return true;

                // ✅ Respect identity system
                if (!Utility_IdentityManager.MatchesIdentityFlags(p, PawnIdentityFlags.IsColonist))
                    return true;

                // ✅ If work is allowed via tags, override incapability
                var modExtension = Utility_CacheManager.GetModExtension(p.def);
                if (modExtension != null)
                {
                    string tag = ManagedTags.AllowWorkPrefix + work.defName;
                    if (Utility_TagManager.WorkEnabled(p.def, tag))
                    {
                        __result = false; // ✅ Not incapable
                        return false;
                    }
                }

                return true; // Fall back to vanilla check
            }
        }

        // Step 4: Tooltip Consistency
        [HarmonyPatch(typeof(WidgetsWork), nameof(WidgetsWork.TipForPawnWorker))]
        public static class Patch_WidgetsWork_TipForPawnWorker
        {
            public static bool Prefix(Pawn p, WorkTypeDef wDef, bool incapableBecauseOfCapacities, ref string __result)
            {
                if (!p.RaceProps.Humanlike && p.skills == null)
                {
                    __result = $"Simulated {wDef.label} skill: {Utility_SkillManager.SetInjectedSkillLevel(p)}";
                    return false;
                }
                return true;
            }
        }

        // Step 5: Unified safe rendering + sorting + initialization
        [HarmonyPatch(typeof(PawnColumnWorker_WorkPriority))]
        public static class Patch_PawnColumnWorker_WorkPriority_SafeAccess
        {
            // Patch 1: Safe DoCell for non-humanlike pawns
            [HarmonyPrefix]
            [HarmonyPatch(nameof(PawnColumnWorker_WorkPriority.DoCell))]
            public static bool DoCell_Prefix(PawnColumnWorker_WorkPriority __instance, Rect rect, Pawn pawn, PawnTable table)
            {
                if (pawn == null || pawn.def?.race == null)
                {
                    return true; // ✅ Not a modded colonist-like pawn, proceed vanilla
                }

                if (!Utility_IdentityManager.MatchesIdentityFlags(pawn, PawnIdentityFlags.IsColonist))
                {
                    return true; // ✅ Not eligible
                }

                if (!pawn.Spawned || pawn.Dead)
                {
                    return true; // ✅ Skip dead or unspawned pawns
                }

                // ✅ Full safe initialization, including ThinkTree and WorkSettings
                Utility_WorkSettingsManager.EnsureWorkSettingsInitialized(pawn);

                return true; // ✅ Allow vanilla to continue drawing priority box
            }

            // Patch 2: Safe Compare
            [HarmonyPrefix]
            [HarmonyPatch(nameof(PawnColumnWorker_WorkPriority.Compare))]
            public static bool Compare_Prefix(PawnColumnWorker_WorkPriority __instance, Pawn a, Pawn b, ref int __result)
            {
                if (a.skills != null && b.skills != null)
                {
                    return true;
                }

                var modExtensionPawnA = Utility_CacheManager.GetModExtension(a.def);
                var modExtensionPawnB = Utility_CacheManager.GetModExtension(b.def);

                if (modExtensionPawnA == null && modExtensionPawnB == null)
                {
                    return true;
                }

                float valA = Utility_SkillManager.GetWorkPrioritySortingValue(a, __instance.def.workType);
                float valB = Utility_SkillManager.GetWorkPrioritySortingValue(b, __instance.def.workType);
                __result = valA.CompareTo(valB);
                return false;
            }
        }

        // Step 6: Force render image if pawn is not pure Humanlike
        [HarmonyPatch(typeof(WidgetsWork), "DrawWorkBoxBackground")]
        public static class Patch_WidgetsWork_DrawWorkBoxBackground
        {
            public static bool Prefix(Rect rect, Pawn p, WorkTypeDef workDef)
            {
                if (p == null || workDef == null)
                {
                    return true;
                }

                float skillAvg = Utility_SkillManager.SafeAverageOfRelevantSkillsFor(p, workDef);

                // === Background interpolation
                Texture2D baseTex;
                Texture2D blendTex;
                float blendFactor;

                if (skillAvg < 4f)
                {
                    baseTex = WidgetsWork.WorkBoxBGTex_Awful;
                    blendTex = WidgetsWork.WorkBoxBGTex_Bad;
                    blendFactor = skillAvg / 4f;
                }
                else if (skillAvg <= 14f)
                {
                    baseTex = WidgetsWork.WorkBoxBGTex_Bad;
                    blendTex = WidgetsWork.WorkBoxBGTex_Mid;
                    blendFactor = (skillAvg - 4f) / 10f;
                }
                else
                {
                    baseTex = WidgetsWork.WorkBoxBGTex_Mid;
                    blendTex = WidgetsWork.WorkBoxBGTex_Excellent;
                    blendFactor = (skillAvg - 14f) / 6f;
                }

                GUI.DrawTexture(rect, baseTex);
                GUI.color = new Color(1f, 1f, 1f, blendFactor);
                GUI.DrawTexture(rect, blendTex);

                // === Dangerous work warning (only for Humanlike pawns with Ideo)
                if (p.RaceProps != null && p.RaceProps.Humanlike && p.Ideo != null && p.Ideo.IsWorkTypeConsideredDangerous(workDef))
                {
                    GUI.color = Color.white;
                    GUI.DrawTexture(rect, WidgetsWork.WorkBoxOverlay_PreceptWarning);
                }

                // === Incompetent skill warning (if active but skill is low)
                if (workDef.relevantSkills != null && workDef.relevantSkills.Count > 0 && skillAvg <= 2f)
                {
                    if (p.workSettings != null && p.workSettings.WorkIsActive(workDef))
                    {
                        GUI.color = Color.white;
                        GUI.DrawTexture(rect.ContractedBy(2f), WidgetsWork.WorkBoxOverlay_Warning);
                    }
                }

                // === Passion icon
                Passion passion = Passion.None;

                if (p.skills != null)
                {
                    passion = p.skills.MaxPassionOfRelevantSkillsFor(workDef);
                }
                else
                {
                    SkillDef skill = null;
                    if (workDef.relevantSkills != null && workDef.relevantSkills.Count > 0)
                    {
                        skill = workDef.relevantSkills[0]; // no LINQ FirstOrDefault
                    }
                    passion = Utility_SkillManager.SetInjectedPassion(p.def, skill);
                }

                if ((int)passion > 0)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.4f);
                    Rect passionRect = rect;
                    passionRect.xMin = rect.center.x;
                    passionRect.yMin = rect.center.y;

                    if (passion == Passion.Minor)
                    {
                        GUI.DrawTexture(passionRect, WidgetsWork.PassionWorkboxMinorIcon);
                    }
                    else if (passion == Passion.Major)
                    {
                        GUI.DrawTexture(passionRect, WidgetsWork.PassionWorkboxMajorIcon);
                    }
                }

                GUI.color = Color.white;
                return false; // Skip original
            }
        }

        [HarmonyPatch(typeof(WidgetsWork), nameof(WidgetsWork.DrawWorkBoxFor))]
        public static class Patch_WidgetsWork_DrawWorkBoxFor
        {
            public static bool Prefix(float x, float y, Pawn p, WorkTypeDef wType, bool incapableBecauseOfCapacities)
            {
                if (p == null || p.def == null || p.def.race == null)
                {
                    return true; // Allow vanilla drawing if invalid
                }

                if (!Utility_ThinkTreeManager.HasAllowWorkTag(p.def))
                {
                    return true; // Allow vanilla drawing if not PawnControl target
                }

                // ✅ Always rescue pawn's workSettings before any access
                Utility_WorkSettingsManager.SafeEnsurePawnReadyForWork(p);

                if (p.WorkTypeIsDisabled(wType))
                {
                    if (p.IsWorkTypeDisabledByAge(wType, out var minAgeRequired))
                    {
                        Rect rect = new Rect(x, y, 25f, 25f);
                        if (Event.current.type == EventType.MouseDown && Mouse.IsOver(rect))
                        {
                            Messages.Message("MessageWorkTypeDisabledAge".Translate(p, p.ageTracker.AgeBiologicalYears, wType.labelShort, minAgeRequired), p, MessageTypeDefOf.RejectInput, historical: false);
                            SoundDefOf.ClickReject.PlayOneShotOnCamera();
                        }
                        GUI.DrawTexture(rect, ContentFinder<Texture2D>.Get("UI/Widgets/WorkBoxBG_AgeDisabled"));
                    }
                    return false;
                }

                Rect rect2 = new Rect(x, y, 25f, 25f);
                if (incapableBecauseOfCapacities)
                {
                    GUI.color = new Color(1f, 0.3f, 0.3f);
                }

                var method = AccessTools.Method(typeof(WidgetsWork), "DrawWorkBoxBackground");
                method.Invoke(null, new object[] { rect2, p, wType });
                GUI.color = Color.white;

                if (Find.PlaySettings.useWorkPriorities)
                {
                    int priority = p.workSettings.GetPriority(wType);
                    if (priority > 0)
                    {
                        Text.Anchor = TextAnchor.MiddleCenter;
                        GUI.color = WidgetsWork.ColorOfPriority(priority);
                        Widgets.Label(rect2.ContractedBy(-3f), priority.ToStringCached());
                        GUI.color = Color.white;
                        Text.Anchor = TextAnchor.UpperLeft;
                    }

                    if (Event.current.type == EventType.MouseDown && Mouse.IsOver(rect2))
                    {
                        int newPriority = priority;

                        if (Event.current.button == 0) // Left click
                        {
                            newPriority = (priority - 1 + 5) % 5;
                        }
                        else if (Event.current.button == 1) // Right click
                        {
                            newPriority = (priority + 1) % 5;
                        }

                        p.workSettings.SetPriority(wType, newPriority);
                        SoundDefOf.DragSlider.PlayOneShotOnCamera();

                        if (newPriority > 0)
                        {
                            float avgSkill = Utility_SkillManager.SafeAverageOfRelevantSkillsFor(p, wType);
                            if (wType.relevantSkills.Any() && avgSkill <= 2f)
                            {
                                SoundDefOf.Crunch.PlayOneShotOnCamera();
                            }

                            if (p.Ideo != null && p.Ideo.IsWorkTypeConsideredDangerous(wType))
                            {
                                Messages.Message("MessageIdeoOpposedWorkTypeSelected".Translate(p, wType.gerundLabel), p, MessageTypeDefOf.CautionInput, historical: false);
                                SoundDefOf.DislikedWorkTypeActivated.PlayOneShotOnCamera();
                            }
                        }

                        Event.current.Use();
                        PlayerKnowledgeDatabase.KnowledgeDemonstrated(ConceptDefOf.WorkTab, KnowledgeAmount.SpecificInteraction);
                        PlayerKnowledgeDatabase.KnowledgeDemonstrated(ConceptDefOf.ManualWorkPriorities, KnowledgeAmount.SmallInteraction);
                    }

                    return false;
                }

                if (p.workSettings.GetPriority(wType) > 0)
                {
                    GUI.DrawTexture(rect2, WidgetsWork.WorkBoxCheckTex);
                }

                if (!Widgets.ButtonInvisible(rect2))
                {
                    return false;
                }

                if (p.workSettings.GetPriority(wType) > 0)
                {
                    p.workSettings.SetPriority(wType, 0);
                    SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
                }
                else
                {
                    p.workSettings.SetPriority(wType, 3);
                    SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                    if (wType.relevantSkills.Any())
                    {
                        // Get the simulated average skill for the pawn
                        float avgSkill = Utility_SkillManager.SafeAverageOfRelevantSkillsFor(p, wType);

                        // Check if the simulated skill is below the threshold
                        if (avgSkill <= 2f)
                        {
                            SoundDefOf.Crunch.PlayOneShotOnCamera();
                        }
                    }

                    if (p.Ideo != null && p.Ideo.IsWorkTypeConsideredDangerous(wType))
                    {
                        Messages.Message("MessageIdeoOpposedWorkTypeSelected".Translate(p, wType.gerundLabel), p, MessageTypeDefOf.CautionInput, historical: false);
                        SoundDefOf.DislikedWorkTypeActivated.PlayOneShotOnCamera();
                    }
                }

                PlayerKnowledgeDatabase.KnowledgeDemonstrated(ConceptDefOf.WorkTab, KnowledgeAmount.SpecificInteraction);

                return false; // Fully replace
            }
        }

        // Step 7: Think Tree Injection patches
        [HarmonyPatch(typeof(Map), nameof(Map.FinalizeInit))]
        public static class Patch_Map_FinalizeInit_FullInitialize
        {
            // Outline: Ensure FullInitializeAllEligiblePawns runs once after each map finishes initializing
            [HarmonyPostfix]
            public static void Postfix(Map __instance)
            {
                if (__instance == null)
                {
                    return;
                }

                // 🔥 Force rebuild identity flags if somehow not yet preloaded
                if (!Utility_IdentityManager.IsIdentityFlagsPreloaded)
                {
                    Utility_IdentityManager.BuildIdentityFlagCache(true);
                }

                // ✅ Safe-guard: Only if identity flags were already preloaded
                if (Utility_IdentityManager.IsIdentityFlagsPreloaded)
                {
                    Utility_WorkSettingsManager.FullInitializeAllEligiblePawns(__instance, forceLock: true);

                    if (Prefs.DevMode)
                    {
                        Log.Message($"[PawnControl] FullInitializeAllEligiblePawns triggered after FinalizeInit for map '{__instance.uniqueID}'.");
                    }
                }
                else
                {
                    if (Prefs.DevMode)
                    {
                        Log.Warning($"[PawnControl] Identity flags were not preloaded before FinalizeInit of map '{__instance.uniqueID}'. Skipping full initialize.");
                    }
                }
            }
        }

        // Step 8: Disable Bio tab specifically for non-humanlike pawns with our mod extension
        [HarmonyPatch(typeof(ITab_Pawn_Character), nameof(ITab_Pawn_Character.IsVisible), MethodType.Getter)]
        public static class Patch_ITab_Pawn_Character_IsVisible
        {
            // Cache the FieldInfo for ITab.selThing to avoid repeated reflection lookups
            private static readonly FieldInfo selThingField = AccessTools.Field(typeof(ITab), "SelThing");

            // Prefix: if we decide to hide, set __result=false and skip the original
            [HarmonyPrefix]
            public static bool Prefix(ITab_Pawn_Character __instance, ref bool __result)
            {
                try
                {
                    // If we couldn't find the field, bail out to vanilla
                    if (selThingField == null)
                        return true;

                    // Get the selected thing (Pawn or Corpse)
                    var thing = selThingField.GetValue(__instance) as Thing;
                    if (thing == null)
                        return true; // no selection, let vanilla decide

                    // Unwrap Pawn from either direct selection or corpse
                    var pawn = thing as Pawn ?? (thing as Corpse)?.InnerPawn;
                    if (pawn == null || pawn.def == null)
                    {
                        return true; // not a pawn, or something's wrong
                    }

                    // FOCUSED SCOPE: Check if this pawn has our mod extension AND is non-humanlike
                    var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                    if (modExtension == null)
                    {
                        return true;
                    }

                    if (!pawn.RaceProps.Humanlike)
                    {
                        // Only for non-humanlike pawns with our mod extension
                        __result = false;
                        if (Prefs.DevMode)
                        {
                            Log.Message($"[PawnControl] Hidden Bio tab for modded non-humanlike pawn: {pawn.LabelShort}");
                        }
                        return false; // Skip original method
                    }

                    // For all other pawns, let vanilla handle it
                    return true;
                }
                catch (Exception ex)
                {
                    // Log error but don't change tab visibility - maintain compatibility
                    if (Prefs.DevMode)
                        Log.Error($"[PawnControl] Error checking Bio tab visibility: {ex}");
                    return true;
                }
            }

            // Minimal postfix as backup only to catch missed cases of our specific pawns
            [HarmonyPostfix]
            public static void Postfix(ITab_Pawn_Character __instance, ref bool __result)
            {
                // Only run if the tab is being shown
                if (!__result)
                    return;

                try
                {
                    // Get the selected pawn
                    Thing selThing = selThingField?.GetValue(__instance) as Thing;
                    if (selThing == null) return;

                    Pawn pawn = selThing as Pawn ?? (selThing as Corpse)?.InnerPawn;
                    if (pawn == null || pawn.def == null) return;

                    // FOCUSED SCOPE: Double-check only non-humanlike with our mod extension
                    var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                    if (modExtension != null && !pawn.RaceProps.Humanlike)
                    {
                        __result = false;
                        if (Prefs.DevMode)
                            Log.Message($"[PawnControl] Backup catch: Hidden Bio tab for modded non-humanlike: {pawn.LabelShort}");
                    }
                }
                catch
                {
                    // Ignore any errors in the postfix - maintain compatibility
                }
            }
        }

        // Step 9: Inject stat hediff during pawn generation
        [HarmonyPatch(typeof(PawnGenerator))]
        [HarmonyPatch("GeneratePawn")]
        [HarmonyPatch(new Type[] { typeof(PawnGenerationRequest) })]
        public static class Patch_PawnGenerator_GeneratePawn
        {
            [HarmonyPostfix]
            public static void Postfix(Pawn __result)
            {
                if (__result == null || __result.def == null || __result.RaceProps.Humanlike)
                    return;

                var ext = Utility_CacheManager.GetModExtension(__result.def);
                if (ext == null)
                    return;

                Utility_SkillManager.ForceAttachSkillTrackerIfMissing(__result);

                if (!__result.health.hediffSet.HasHediff(HediffDef.Named("PawnControl_StatStub")) && !Utility_StatManager.HasAlreadyInjected(__result))
                {
                    Utility_SkillManager.ForceAttachSkillTrackerIfMissing(__result);
                    Utility_StatManager.InjectConsolidatedStatHediff(__result);
                    Utility_WorkSettingsManager.EnsureWorkSettingsInitialized(__result); // ✅ centralized
                }
            }
        }

        [HarmonyPatch(typeof(Pawn))]
        [HarmonyPatch("SpawnSetup")]
        [HarmonyPatch(new Type[] { typeof(Map), typeof(bool) })]
        public static class Patch_Pawn_SpawnSetup
        {
            [HarmonyPostfix]
            public static void Postfix(Pawn __instance)
            {
                if (__instance == null || __instance.Dead || __instance.Destroyed)
                    return;

                // Check for both mod extension and work tags
                var modExtension = Utility_CacheManager.GetModExtension(__instance.def);
                bool hasTags = Utility_ThinkTreeManager.HasAllowOrBlockWorkTag(__instance.def);

                // CRITICAL FIX: Ensure mind state is active for any eligible pawn immediately
                if (__instance.mindState != null &&
                    __instance.workSettings != null &&
                    __instance.workSettings.EverWork &&
                    (modExtension != null || hasTags))
                {
                    __instance.mindState.Active = true;

                    if (Prefs.DevMode && __instance.def.defName.Contains("Snotling"))
                    {
                        Log.Message($"[PawnControl] Activated {__instance.LabelShort}'s mind during SpawnSetup");
                    }
                }

                // Skip the rest of processing for humanlike pawns
                if (__instance.RaceProps.Humanlike)
                    return;

                // Skip if no mod extension (but we've still handled mind activation above if it had tags)
                if (modExtension == null)
                    return;

                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    if (__instance.health?.hediffSet == null || !__instance.Spawned)
                        return;

                    // ✅ Ensure hediff is injected only once, and allow mutation/removal logic to run inside the HediffComp
                    if (!__instance.health.hediffSet.HasHediff(HediffDef.Named("PawnControl_StatStub")))
                    {
                        Utility_SkillManager.ForceAttachSkillTrackerIfMissing(__instance);

                        if (Prefs.DevMode)
                        {
                            // ✅ Log thinker state
                            Utility_DebugManager.DumpThinkerStatus(__instance);

                            // ✅ Add diagnostic information for work givers
                            Utility_DebugManager.DiagnoseWorkGiversForPawn(__instance);
                        }

                        Utility_StatManager.InjectConsolidatedStatHediff(__instance);
                        Utility_WorkSettingsManager.EnsureWorkSettingsInitialized(__instance); // ✅ centralized
                    }
                });
            }
        }

        // Step 10: Individual patches for necessary WorkGiver, JobGiver or JobDriver overrides
        /// <summary>
        /// Outline: If a pawn has no native BeatFire verb (e.g. non‐humanlike),
        /// fall back to our custom Verb_BeatFire via TryStartCastOn.
        /// </summary>
        [HarmonyPatch(typeof(Pawn_NativeVerbs), nameof(Pawn_NativeVerbs.TryBeatFire))]
        public static class Patch_Pawn_NativeVerbs_TryBeatFire_Fallback
        {
            private const string LOG_KEY = "PawnControl.NativeVerbs.TryBeatFire";

            // Prefix intercepts TryBeatFire; returns false to skip original when we handle it.
            static bool Prefix(Pawn_NativeVerbs __instance, Fire targetFire, ref bool __result)
            {
                // 1️⃣ If vanilla has a BeatFire verb, let original run
                if (__instance.BeatFireVerb != null)
                    return true;

                // 2️⃣ Get the pawn from IVerbOwner.ConstantCaster
                var owner = (IVerbOwner)__instance;
                var pawn = owner.ConstantCaster as Pawn;
                if (pawn == null)
                {
                    __result = false;
                    return false;
                }

                // 3️⃣ Apply only to our mod’s target pawns
                var ext = Utility_CacheManager.GetModExtension(pawn.def);
                bool hasTags = Utility_ThinkTreeManager.HasAllowOrBlockWorkTag(pawn.def);
                if (pawn.RaceProps.Humanlike || (ext == null && !hasTags))
                    return true;

                // 4️⃣ Instantiate and start our custom fire‐beat verb
                var verb = new Verb_BeatFire { caster = pawn };
                __result = !pawn.stances.FullBodyBusy
                           && verb.TryStartCastOn((LocalTargetInfo)targetFire);

                if (__result && Prefs.DevMode)
                    Log.Message($"{LOG_KEY}: {pawn.LabelShort} used fallback BeatFire on {targetFire}");

                // 5️⃣ Skip the original (prevents “has no beat fire verb” error)
                return false;
            }
        }




        // Step X: Inject plant-cut override inside GenConstruct.HandleBlockingThingJob
        [HarmonyPatch(typeof(GenConstruct), nameof(GenConstruct.HandleBlockingThingJob))]
        public static class Patch_GenConstruct_HandleBlockingThingJob_PlantCutOverride
        {
            [HarmonyPostfix]
            public static void Postfix(ref Job __result, Thing constructible, Pawn worker, bool forced = false)
            {
                // ✅ Skip if vanilla already assigned a job
                if (__result != null || constructible == null || worker == null)
                    return;

                // ✅ Only apply to modded non-humanlike pawns
                if (worker.RaceProps.Humanlike || !Utility_TagManager.WorkEnabled(worker.def, "CutPlant"))
                    return;

                // ✅ Scan for blocking thing and check plant
                Thing blocker = GenConstruct.FirstBlockingThing(constructible, worker);
                if (blocker == null || blocker.def.category != ThingCategory.Plant)
                    return;

                // ✅ Respect reachability
                if (!worker.CanReserveAndReach(blocker, PathEndMode.ClosestTouch, worker.NormalMaxDanger(), 1, -1, null, forced))
                    return;

                // ✅ Allow cutting plant as override
                __result = JobMaker.MakeJob(JobDefOf.CutPlant, blocker);
                if (Prefs.DevMode)
                {
                    Log.Message($"[PawnControl] Forced CutPlant job for {worker.LabelShort} on {blocker.Label}");
                }
            }
        }

        [HarmonyPatch(typeof(Toils_Haul), "JumpToCarryToNextContainerIfPossible")]
        public static class Patch_Toils_Haul_JumpToCarryToNextContainerIfPossible
        {
            [HarmonyPrefix]
            public static bool Prefix(ref Toil __result, Toil carryToContainerToil, TargetIndex primaryTargetInd)
            {
                __result = Utility_JobManager.GeneratePatchedToil(carryToContainerToil, primaryTargetInd);
                return false; // Skip vanilla
            }
        }

        [HarmonyPatch(typeof(GenConstruct), nameof(GenConstruct.CanConstruct), new Type[]
        { typeof(Thing), typeof(Pawn), typeof(bool), typeof(bool), typeof(JobDef) })]
        public static class Patch_GenConstruct_CanConstruct
        {
            [HarmonyPrefix]
            public static bool Prefix(
                Thing t,
                Pawn p,
                bool checkSkills,
                bool forced,
                JobDef jobForReservation,
                ref bool __result)
            {
                if (p.RaceProps.Humanlike || Utility_CacheManager.GetModExtension(p.def) == null)
                    return true; // ✅ Let vanilla run if humanlike or no mod extension

                // ⛔ Skip workSettings requirement check, as modded animals may not have any
                if (GenConstruct.FirstBlockingThing(t, p) != null)
                {
                    __result = false;
                    return false;
                }

                // Reservation logic
                if (jobForReservation != null)
                {
                    if (!p.Spawned || !p.Map.reservationManager.OnlyReservationsForJobDef(t, jobForReservation) ||
                        !p.CanReach(t, PathEndMode.Touch, forced ? Danger.Deadly : p.NormalMaxDanger()))
                    {
                        __result = false;
                        return false;
                    }
                }
                else if (!p.CanReserveAndReach(t, PathEndMode.Touch, forced ? Danger.Deadly : p.NormalMaxDanger(), 1, -1, null, forced))
                {
                    __result = false;
                    return false;
                }

                // Burning check
                if (t.IsBurning())
                {
                    __result = false;
                    return false;
                }

                // Skip skill checks for animals unless explicitly supported
                if (checkSkills && !p.RaceProps.Humanlike)
                {
                    // Skip silently: animals often have no skills
                }

                // Ideology: skip check for animals (avoiding JobFailReason spam)
                if (!p.RaceProps.Humanlike)
                {
                    // Example: building religious altar might block colonists, but not modded pawns
                }

                // Blueprint-specific checks (turret, attachment, etc.)
                if ((t.def.IsBlueprint || t.def.IsFrame) && t.def.entityDefToBuild is ThingDef thingDef)
                {
                    if (thingDef.building != null && thingDef.building.isAttachment)
                    {
                        Thing wallAttachedTo = GenConstruct.GetWallAttachedTo(t);
                        if (wallAttachedTo == null || wallAttachedTo.def.IsBlueprint || wallAttachedTo.def.IsFrame)
                        {
                            __result = false;
                            return false;
                        }
                    }

                    // History event checks (skip for animals)
                    // Not needed: animals aren't tracked for tales
                }

                __result = true;
                return false; // ✅ Skip vanilla fully, after full checks
            }
        }

        // Debuggers

        // Add this patch to monitor ThinkTree usage - forcibly disabled for release builds
        [HarmonyPatch(typeof(ThinkNode_PrioritySorter), nameof(ThinkNode_PrioritySorter.TryIssueJobPackage))]
        public static class Patch_ThinkNode_PrioritySorter_Monitor
        {
            [HarmonyPrefix]
            public static void Prefix(ThinkNode_PrioritySorter __instance, Pawn pawn)
            {
                if (!Prefs.DevMode || Prefs.DevMode)
                    return;

                if (pawn?.def == null)
                    return;

                // Only for monitored pawns in dev mode
                bool hasJobGiverWorkNonHumanlike = false;

                if (__instance.subNodes != null)
                {
                    foreach (var node in __instance.subNodes)
                    {
                        if (node is JobGiver_WorkNonHumanlike)
                        {
                            hasJobGiverWorkNonHumanlike = true;
                            Log.Message($"[PawnControl] Found JobGiver_WorkNonHumanlike in ThinkTree for {pawn.LabelShort}");
                            break;
                        }
                    }

                    if (!hasJobGiverWorkNonHumanlike && pawn.def.defName.Contains("Snotling"))
                    {
                        Log.Warning($"[PawnControl] WARNING: JobGiver_WorkNonHumanlike NOT found in ThinkTree for {pawn.LabelShort}!");
                    }
                }

                // Always check mind state here as a backup
                if (pawn.mindState != null && !pawn.mindState.Active &&
                    pawn.workSettings != null && pawn.workSettings.EverWork)
                {
                    pawn.mindState.Active = true;
                    Log.Warning($"[PawnControl] Had to activate {pawn.LabelShort}'s mind during ThinkNode_PrioritySorter");
                }
            }
        }

        /// <summary>
        /// Diagnostic patch to track when our stat hediff injection method is called - is forcibly dissabled for release builds
        /// </summary>
        [HarmonyPatch(typeof(Utility_StatManager), nameof(Utility_StatManager.InjectConsolidatedStatHediff))]
        public static class Patch_Utility_StatManager_InjectConsolidatedStatHediff
        {
            [HarmonyPrefix]
            public static bool Prefix(Pawn pawn)
            {
                if (!Prefs.DevMode || Prefs.DevMode)
                    return true;

                Log.Message($"[PawnControl] DEBUG: InjectConsolidatedStatHediff CALLED for {pawn?.LabelShort ?? "null"} (Humanlike: {pawn?.RaceProps?.Humanlike.ToString() ?? "null"})");
                return true; // Continue with the original method
            }

            [HarmonyPostfix]
            public static void Postfix(Pawn pawn)
            {
                if (!Prefs.DevMode || Prefs.DevMode)
                    return;

                Log.Message($"[PawnControl] DEBUG: InjectConsolidatedStatHediff COMPLETED for {pawn?.LabelShort ?? "null"}");

                // Check if the pawn actually has any hediffs with our def name
                if (pawn?.health?.hediffSet?.hediffs != null)
                {
                    bool hasStatHediff = false;
                    foreach (var hediff in pawn.health.hediffSet.hediffs)
                    {
                        if (hediff?.def?.defName == "PawnControl_StatStub")
                        {
                            hasStatHediff = true;
                            Log.Message("[PawnControl] DEBUG: Found PawnControl_StatStub hediff on pawn");
                            break;
                        }
                    }

                    if (!hasStatHediff)
                    {
                        Log.Warning("[PawnControl] DEBUG: After injection, NO PawnControl_StatStub hediff found on pawn!");
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
        public static class Patch_Pawn_GetGizmos_StatDebug
        {
            [HarmonyPostfix]
            public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
            {
                foreach (var gizmo in __result)
                {
                    yield return gizmo;
                }

                // ✅ Null check
                if (__instance == null || __instance.RaceProps == null)
                {
                    yield break;
                }

                // ✅ Dev mode check
                if (!Prefs.DevMode || __instance?.def == null)
                {
                    yield break;
                }

                // ✅ Only show if pawn has mod extension
                var modExtension = Utility_CacheManager.GetModExtension(__instance.def);
                if (modExtension == null)
                    yield break;

                yield return new Command_Action
                {
                    defaultLabel = "PawnControl_LogStat",
                    defaultDesc = "Log race + stat mutation info.",
                    icon = TexCommand.SelectShelf, // built-in dev icon
                    action = () => Utility_DebugManager.LogRaceAndPawnStats(__instance)
                };

                yield return new Command_Action
                {
                    defaultLabel = "PawnControl_ValidateStat",
                    defaultDesc = "Validate current stat values against race base.",
                    icon = TexCommand.ClearPrioritizedWork,
                    action = () => Utility_DebugManager.StatMutationValidator.Validate(__instance)
                };
            }
        }

        // Debugs for Patch 2 Iterations - disabled for release builds
        /// <summary>
        /// Debug patch for ThinkNode_PrioritySorter's job package creation process.
        /// This patch adds detailed logging for pawns with work-related tags when they enter the priority sorter.
        /// It captures the full hierarchy of think nodes being processed and dumps them to the log for debugging purposes.
        /// Only runs when the pawn has allowed work tags configured via the mod.
        /// </summary>
        [HarmonyPatch(typeof(ThinkNode_PrioritySorter), nameof(ThinkNode_PrioritySorter.TryIssueJobPackage))]
        public static class Patch_ThinkNode_PrioritySorter_DebugJobs
        {
            [HarmonyPrefix]
            public static void Prefix(ThinkNode_PrioritySorter __instance, Pawn pawn)
            {
                // Skip if not in dev mode - only dump in development to avoid log spam
                if (!Prefs.DevMode || Prefs.DevMode)
                {
                    return;
                }

                if (pawn == null || __instance == null)
                {
                    return;
                }

                if (!Utility_ThinkTreeManager.HasAllowWorkTag(pawn.def))
                {
                    return;
                }

                Log.Message($"[PawnControl] {pawn.LabelShort} entering PrioritySorter...");

                if (__instance.subNodes != null)
                {
                    Utility_DebugManager.SafeExpandAndDump(
                        __instance,
                        expandBeforeDump: true,
                        label: $"{pawn.LabelShort}_PrioritySorterDebug",
                        showFullDump: true
                    );
                }
            }
        }

        /// <summary>
        /// Patch to dump ThinkTree information when a pawn spawns.
        /// This patch captures and logs the ThinkTree structure for pawns that have PawnControl tags
        /// when they spawn in the world. It helps with debugging by showing the complete ThinkNode
        /// hierarchy for controlled pawns, revealing how job priorities and work capabilities are 
        /// structured. Only executes in development mode to avoid log spam in normal gameplay.
        /// </summary>
        [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
        public static class Patch_Pawn_SpawnSetup_DumpThinkTree
        {
            [HarmonyPostfix]
            public static void Postfix(Pawn __instance, Map map, bool respawningAfterLoad)
            {
                // Skip if not in dev mode - only dump in development to avoid log spam
                if (!Prefs.DevMode || Prefs.DevMode)
                {
                    return;
                }

                // Only process pawns that have PawnControl tags
                if (!Utility_ThinkTreeManager.HasAllowOrBlockWorkTag(__instance.def))
                {
                    return;
                }

                // Wait for the next tick to ensure thinker is fully initialized
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    try
                    {
                        if (__instance?.thinker?.MainThinkNodeRoot != null)
                        {
                            Log.Message($"[PawnControl] Dumping ThinkTree for newly spawned pawn {__instance.LabelShort}");
                            Utility_DebugManager.DumpPawnThinkTreeDetailed(__instance);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[PawnControl] Error during ThinkTree dump for {__instance?.LabelShort}: {ex}");
                    }
                });
            }
        }

        // Add a Gizmo for on-demand validation
        [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
        public static class Patch_Pawn_GetGizmos_DebugThinkTree
        {
            [HarmonyPostfix]
            public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
            {
                foreach (var gizmo in __result)
                {
                    yield return gizmo;
                }

                if (!Prefs.DevMode)
                    yield break;

                // Only show for pawns with our mod extension
                if (__instance?.def == null || !Utility_ThinkTreeManager.HasAllowOrBlockWorkTag(__instance.def))
                    yield break;

                yield return new Command_Action
                {
                    defaultLabel = "Debug: Validate ThinkTree",
                    defaultDesc = "Validate this pawn's ThinkTree configuration",
                    action = () => Utility_ThinkTreeManager.ValidateThinkTree(__instance),
                };
            }
        }
    }
}