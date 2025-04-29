using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse.Sound;
using System.Reflection;
using Verse.AI.Group;

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

                    var workTableDef = DefDatabase<PawnTableDef>.GetNamedSilentFail("Work");
                    if (workTableDef != null)
                    {

                        workTableDef.columns = workTableDef.columns
                            .Where(col => IsWorkColumnSupported(col, taggedPawns))
                            .ToList();
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

            //// Patch 2: Safe Compare
            //[HarmonyPrefix]
            //[HarmonyPatch(nameof(PawnColumnWorker_WorkPriority.Compare))]
            //public static bool Compare_Prefix(PawnColumnWorker_WorkPriority __instance, Pawn a, Pawn b, ref int __result)
            //{
            //    var modExtensionPawnA = Utility_CacheManager.GetModExtension(a.def);
            //    var modExtensionPawnB = Utility_CacheManager.GetModExtension(b.def);

            //    if (modExtensionPawnA == null && modExtensionPawnB == null)
            //    {
            //        return true;
            //    }

            //    float valA = Utility_SkillManager.GetWorkPrioritySortingValue(a, __instance.def.workType);
            //    float valB = Utility_SkillManager.GetWorkPrioritySortingValue(b, __instance.def.workType);
            //    __result = valA.CompareTo(valB);
            //    return false;
            //}
        }

        //// Step 6: Force render image if pawn is not pure Humanlike
        //[HarmonyPatch(typeof(WidgetsWork), "DrawWorkBoxBackground")]
        //public static class Patch_WidgetsWork_DrawWorkBoxBackground
        //{
        //    public static bool Prefix(Rect rect, Pawn p, WorkTypeDef workDef)
        //    {
        //        if (p == null || workDef == null)
        //        {
        //            return true;
        //        }

        //        float skillAvg = Utility_SkillManager.SafeAverageOfRelevantSkillsFor(p, workDef);

        //        // === Background interpolation
        //        Texture2D baseTex;
        //        Texture2D blendTex;
        //        float blendFactor;

        //        if (skillAvg < 4f)
        //        {
        //            baseTex = WidgetsWork.WorkBoxBGTex_Awful;
        //            blendTex = WidgetsWork.WorkBoxBGTex_Bad;
        //            blendFactor = skillAvg / 4f;
        //        }
        //        else if (skillAvg <= 14f)
        //        {
        //            baseTex = WidgetsWork.WorkBoxBGTex_Bad;
        //            blendTex = WidgetsWork.WorkBoxBGTex_Mid;
        //            blendFactor = (skillAvg - 4f) / 10f;
        //        }
        //        else
        //        {
        //            baseTex = WidgetsWork.WorkBoxBGTex_Mid;
        //            blendTex = WidgetsWork.WorkBoxBGTex_Excellent;
        //            blendFactor = (skillAvg - 14f) / 6f;
        //        }

        //        GUI.DrawTexture(rect, baseTex);
        //        GUI.color = new Color(1f, 1f, 1f, blendFactor);
        //        GUI.DrawTexture(rect, blendTex);

        //        // === Dangerous work warning (only for Humanlike pawns with Ideo)
        //        if (p.RaceProps != null && p.RaceProps.Humanlike && p.Ideo != null && p.Ideo.IsWorkTypeConsideredDangerous(workDef))
        //        {
        //            GUI.color = Color.white;
        //            GUI.DrawTexture(rect, WidgetsWork.WorkBoxOverlay_PreceptWarning);
        //        }

        //        // === Incompetent skill warning (if active but skill is low)
        //        if (workDef.relevantSkills != null && workDef.relevantSkills.Count > 0 && skillAvg <= 2f)
        //        {
        //            if (p.workSettings != null && p.workSettings.WorkIsActive(workDef))
        //            {
        //                GUI.color = Color.white;
        //                GUI.DrawTexture(rect.ContractedBy(2f), WidgetsWork.WorkBoxOverlay_Warning);
        //            }
        //        }

        //        // === Passion icon
        //        Passion passion = Passion.None;

        //        if (p.skills != null)
        //        {
        //            passion = p.skills.MaxPassionOfRelevantSkillsFor(workDef);
        //        }
        //        else
        //        {
        //            SkillDef skill = null;
        //            if (workDef.relevantSkills != null && workDef.relevantSkills.Count > 0)
        //            {
        //                skill = workDef.relevantSkills[0]; // no LINQ FirstOrDefault
        //            }
        //            passion = Utility_SkillManager.SetInjectedPassion(p.def, skill);
        //        }

        //        if ((int)passion > 0)
        //        {
        //            GUI.color = new Color(1f, 1f, 1f, 0.4f);
        //            Rect passionRect = rect;
        //            passionRect.xMin = rect.center.x;
        //            passionRect.yMin = rect.center.y;

        //            if (passion == Passion.Minor)
        //            {
        //                GUI.DrawTexture(passionRect, WidgetsWork.PassionWorkboxMinorIcon);
        //            }
        //            else if (passion == Passion.Major)
        //            {
        //                GUI.DrawTexture(passionRect, WidgetsWork.PassionWorkboxMajorIcon);
        //            }
        //        }

        //        GUI.color = Color.white;
        //        return false; // Skip original
        //    }
        //}

        //[HarmonyPatch(typeof(WidgetsWork), nameof(WidgetsWork.DrawWorkBoxFor))]
        //public static class Patch_WidgetsWork_DrawWorkBoxFor
        //{
        //    public static bool Prefix(float x, float y, Pawn p, WorkTypeDef wType, bool incapableBecauseOfCapacities)
        //    {
        //        if (p == null || p.def == null || p.def.race == null)
        //        {
        //            return true; // Allow vanilla drawing if invalid
        //        }

        //        if (!Utility_ThinkTreeManager.HasAllowWorkTag(p.def))
        //        {
        //            return true; // Allow vanilla drawing if not PawnControl target
        //        }

        //        // ✅ Always rescue pawn's workSettings before any access
        //        Utility_WorkSettingsManager.SafeEnsurePawnReadyForWork(p);

        //        if (p.WorkTypeIsDisabled(wType))
        //        {
        //            if (p.IsWorkTypeDisabledByAge(wType, out var minAgeRequired))
        //            {
        //                Rect rect = new Rect(x, y, 25f, 25f);
        //                if (Event.current.type == EventType.MouseDown && Mouse.IsOver(rect))
        //                {
        //                    Messages.Message("MessageWorkTypeDisabledAge".Translate(p, p.ageTracker.AgeBiologicalYears, wType.labelShort, minAgeRequired), p, MessageTypeDefOf.RejectInput, historical: false);
        //                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
        //                }
        //                GUI.DrawTexture(rect, ContentFinder<Texture2D>.Get("UI/Widgets/WorkBoxBG_AgeDisabled"));
        //            }
        //            return false;
        //        }

        //        Rect rect2 = new Rect(x, y, 25f, 25f);
        //        if (incapableBecauseOfCapacities)
        //        {
        //            GUI.color = new Color(1f, 0.3f, 0.3f);
        //        }

        //        var method = AccessTools.Method(typeof(WidgetsWork), "DrawWorkBoxBackground");
        //        method.Invoke(null, new object[] { rect2, p, wType });
        //        GUI.color = Color.white;

        //        if (Find.PlaySettings.useWorkPriorities)
        //        {
        //            int priority = p.workSettings.GetPriority(wType);
        //            if (priority > 0)
        //            {
        //                Text.Anchor = TextAnchor.MiddleCenter;
        //                GUI.color = WidgetsWork.ColorOfPriority(priority);
        //                Widgets.Label(rect2.ContractedBy(-3f), priority.ToStringCached());
        //                GUI.color = Color.white;
        //                Text.Anchor = TextAnchor.UpperLeft;
        //            }

        //            if (Event.current.type == EventType.MouseDown && Mouse.IsOver(rect2))
        //            {
        //                int newPriority = priority;

        //                if (Event.current.button == 0) // Left click
        //                {
        //                    newPriority = (priority - 1 + 5) % 5;
        //                }
        //                else if (Event.current.button == 1) // Right click
        //                {
        //                    newPriority = (priority + 1) % 5;
        //                }

        //                p.workSettings.SetPriority(wType, newPriority);
        //                SoundDefOf.DragSlider.PlayOneShotOnCamera();

        //                if (newPriority > 0)
        //                {
        //                    float avgSkill = Utility_SkillManager.SafeAverageOfRelevantSkillsFor(p, wType);
        //                    if (wType.relevantSkills.Any() && avgSkill <= 2f)
        //                    {
        //                        SoundDefOf.Crunch.PlayOneShotOnCamera();
        //                    }

        //                    if (p.Ideo != null && p.Ideo.IsWorkTypeConsideredDangerous(wType))
        //                    {
        //                        Messages.Message("MessageIdeoOpposedWorkTypeSelected".Translate(p, wType.gerundLabel), p, MessageTypeDefOf.CautionInput, historical: false);
        //                        SoundDefOf.DislikedWorkTypeActivated.PlayOneShotOnCamera();
        //                    }
        //                }

        //                Event.current.Use();
        //                PlayerKnowledgeDatabase.KnowledgeDemonstrated(ConceptDefOf.WorkTab, KnowledgeAmount.SpecificInteraction);
        //                PlayerKnowledgeDatabase.KnowledgeDemonstrated(ConceptDefOf.ManualWorkPriorities, KnowledgeAmount.SmallInteraction);
        //            }

        //            return false;
        //        }

        //        if (p.workSettings.GetPriority(wType) > 0)
        //        {
        //            GUI.DrawTexture(rect2, WidgetsWork.WorkBoxCheckTex);
        //        }

        //        if (!Widgets.ButtonInvisible(rect2))
        //        {
        //            return false;
        //        }

        //        if (p.workSettings.GetPriority(wType) > 0)
        //        {
        //            p.workSettings.SetPriority(wType, 0);
        //            SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
        //        }
        //        else
        //        {
        //            p.workSettings.SetPriority(wType, 3);
        //            SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
        //            if (wType.relevantSkills.Any())
        //            {
        //                // Get the simulated average skill for the pawn
        //                float avgSkill = Utility_SkillManager.SafeAverageOfRelevantSkillsFor(p, wType);

        //                // Check if the simulated skill is below the threshold
        //                if (avgSkill <= 2f)
        //                {
        //                    SoundDefOf.Crunch.PlayOneShotOnCamera();
        //                }
        //            }

        //            if (p.Ideo != null && p.Ideo.IsWorkTypeConsideredDangerous(wType))
        //            {
        //                Messages.Message("MessageIdeoOpposedWorkTypeSelected".Translate(p, wType.gerundLabel), p, MessageTypeDefOf.CautionInput, historical: false);
        //                SoundDefOf.DislikedWorkTypeActivated.PlayOneShotOnCamera();
        //            }
        //        }

        //        PlayerKnowledgeDatabase.KnowledgeDemonstrated(ConceptDefOf.WorkTab, KnowledgeAmount.SpecificInteraction);

        //        return false; // Fully replace
        //    }
        //}

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

        /// <summary>
        /// Patch to allow non-humanlike pawns with mod extension to bypass WorkGiver restrictions.
        /// </summary>
        [HarmonyPatch(typeof(JobGiver_Work), "PawnCanUseWorkGiver")]
        public static class Patch_JobGiver_Work_PawnCanUseWorkGiver
        {
            [HarmonyPrefix]
            public static bool Prefix(Pawn pawn, WorkGiver giver, ref bool __result)
            {
                if (pawn == null || giver == null)
                {
                    return true; // fallback to vanilla
                }

                var modExtension = Utility_CacheManager.GetModExtension(pawn.def);

                if (modExtension != null) // Customize field as needed
                {
                    if (giver.def != null && Utility_TagManager.WorkEnabled(pawn.def, giver.def.defName))
                    {
                        __result = true; // forcibly allow this WorkGiver
                        return false; // skip original method
                    }
                }

                return true; // allow vanilla if not extension-controlled
            }
        }



        // Debugs for Patch 2 Iterations
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
                if (!Prefs.DevMode)
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
        /// Debug patch for JobGiver_Work's PawnCanUseWorkGiver method.
        /// Logs when an animal or other entity with work tags tries to use a WorkGiver.
        /// This helps troubleshoot which WorkGivers are being considered for tagged pawns,
        /// only executing for pawns that have AllowWork or BlockWork tags defined.
        /// </summary>
        [HarmonyPatch(typeof(JobGiver_Work), "PawnCanUseWorkGiver")]
        public static class Patch_JobGiver_Work_PawnCanUseWorkGiver_Debug
        {
            [HarmonyPrefix]
            public static void Prefix(Pawn pawn, WorkGiver giver, ref bool __result)
            {
                // Skip if not in dev mode - only dump in development to avoid log spam
                if (!Prefs.DevMode)
                {
                    return;
                }

                if (pawn == null || giver == null)
                {
                    return;
                }

                if (!Utility_ThinkTreeManager.HasAllowOrBlockWorkTag(pawn.def))
                {
                    return;
                }

                Log.Message($"[PawnControl] Checking if {pawn.LabelShort} can use WorkGiver: {giver.def.defName}");
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
                if (!Prefs.DevMode)
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

        private class ObsolatedMethod
        {
                ////Work tag override logic
                //[HarmonyPatch(typeof(Pawn), nameof(Pawn.WorkTypeIsDisabled))]
                //public static class Patch_Pawn_WorkTypeIsDisabled
                //{
                //    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
                //    {
                //        var code = new List<CodeInstruction>(instructions);
                //        for (int i = 0; i < code.Count; i++)
                //        {
                //            yield return code[i];

                //            if (code[i].opcode == OpCodes.Ret)
                //            {
                //                // Inject: if (IsWorkTypeAllowed(this.def, workType)) return false;

                //                yield return new CodeInstruction(OpCodes.Ldarg_0); // this (Pawn)
                //                yield return new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(Pawn), nameof(Pawn.def))); // this.def
                //                yield return new CodeInstruction(OpCodes.Ldarg_1); // WorkTypeDef
                //                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch_Pawn_WorkTypeIsDisabled), nameof(IsWorkTypeAllowed))); // call IsWorkTypeAllowed

                //                Label skip = code[i].labels.FirstOrDefault(); // preserve any label on the original return
                //                yield return new CodeInstruction(OpCodes.Brfalse_S, skip); // if false, continue original return
                //                yield return new CodeInstruction(OpCodes.Ldc_I4_0); // return false
                //                yield return new CodeInstruction(OpCodes.Ret);
                //            }
                //        }
                //    }

                //    private static bool IsWorkTypeAllowed(ThingDef def, WorkTypeDef workType)
                //    {
                //        if (def == null || def.race?.Humanlike == true)
                //            return true;

                //        var modExtension = Utility_CacheManager.GetModExtension(def);
                //        if (modExtension == null)
                //            return false;

                //        // Check if the work type is allowed based on tags
                //        string workTag = ManagedTags.AllowWorkPrefix + workType.defName;
                //        return modExtension.tags?.Contains(workTag) == true || modExtension.tags?.Contains(ManagedTags.AllowAllWork) == true;
                //    }
                //}

                //[HarmonyPatch(typeof(Pawn_WorkSettings), nameof(Pawn_WorkSettings.EnableAndInitialize))]
                //public static class Patch_Pawn_WorkSettings_EnableAndInitialize
                //{
                //    // Cache private field: private Pawn pawn;
                //    private static readonly FieldInfo pawnField = AccessTools.Field(typeof(Pawn_WorkSettings), "pawn");

                //    // Cache internal method: EnableAndInitializeIfNotAlreadyInitialized
                //    private static readonly MethodInfo initMethod = AccessTools.Method(typeof(Pawn_WorkSettings), "EnableAndInitializeIfNotAlreadyInitialized");

                //    public static bool Prefix(object __instance)
                //    {
                //        if (__instance == null || pawnField == null || initMethod == null)
                //        {
                //            Log.Warning("[PawnControl] EnableAndInitialize patch skipped due to missing reflection targets.");
                //            return true;
                //        }

                //        Pawn pawn = pawnField.GetValue(__instance) as Pawn;
                //        if (pawn == null) return true;

                //        if (pawn.RaceProps.Humanlike)
                //            return true; // use vanilla logic

                //        var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                //        if (modExtension == null || modExtension.tags == null || !modExtension.tags.Contains(ManagedTags.AllowAllWork))
                //            return true;

                //        // Initialize work settings
                //        initMethod.Invoke(__instance, null);

                //        // Assign work priorities based on tags
                //        foreach (var workType in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                //        {
                //            string workTag = ManagedTags.AllowWorkPrefix + workType.defName;
                //            if (modExtension.tags.Contains(workTag) || modExtension.tags.Contains(ManagedTags.AllowAllWork))
                //            {
                //                if (!pawn.WorkTypeIsDisabled(workType))
                //                {
                //                    pawn.workSettings.SetPriority(workType, 3); // Default priority
                //                }
                //            }
                //        }

                //        return false; // Skip vanilla logic
                //    }
                //}

                //// Show non-humanlike if tagged
                //[HarmonyPatch(typeof(MainTabWindow_Work), "Pawns", MethodType.Getter)]
                //public static class Patch_MainTabWindow_Work_Pawns
                //{
                //    public static void Postfix(ref IEnumerable<Pawn> __result)
                //    {
                //        // Filter the pawns to include only those that should appear in the work tab
                //        __result = __result.Where(ShouldAppearInWorkTab);
                //    }

                //    private static bool ShouldAppearInWorkTab(Pawn pawn)
                //    {
                //        if (pawn == null || pawn.def == null)
                //        {
                //            return false;
                //        }

                //        // Humanlike pawns always appear in the work tab
                //        if (pawn.RaceProps.Humanlike)
                //        {
                //            return true;
                //        }

                //        // Check if the pawn has the required mod extension
                //        var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                //        if (modExtension == null)
                //        {
                //            return false;
                //        }

                //        // Check if the pawn is tagged to appear in the work tab
                //        return modExtension.tags?.Contains(ManagedTags.AllowAllWork) == true ||
                //               modExtension.tags?.Any(tag => tag.StartsWith(ManagedTags.AllowWorkPrefix)) == true;
                //    }
                //}

                //// Patch #2 Step 7.8: IsPrisoner Hooking with Scoped Mitigation Strategy
                //[HarmonyPatch(typeof(Pawn), nameof(Pawn.IsPrisoner), MethodType.Getter)]
                //[HarmonyPriority(Priority.First)]
                //public static class Patch_Pawn_IsPrisoner
                //{
                //    public static void Postfix(Pawn __instance, ref bool __result)
                //    {
                //        if (__result) return;

                //        if (__instance == null || __instance.def == null || __instance.def.race == null)
                //        {
                //            return;
                //        }

                //        if (Utility_IdentityManager.IsFlagOverridden(FlagScopeTarget.IsPrisoner) &&
                //            Utility_IdentityManager.MatchesIdentityFlags(__instance, PawnIdentityFlags.IsPrisoner))
                //        {
                //            __result = true;
                //        }
                //    }
                //}

                //// Patch #2 Step 7.9: IsSlave Hooking with Scoped Mitigation Strategy
                //[HarmonyPatch(typeof(Pawn), nameof(Pawn.IsSlave), MethodType.Getter)]
                //[HarmonyPriority(Priority.First)]
                //public static class Patch_Pawn_IsSlave
                //{
                //    public static void Postfix(Pawn __instance, ref bool __result)
                //    {
                //        if (__result) return;

                //        if (__instance == null || __instance.def == null || __instance.def.race == null)
                //        {
                //            return;
                //        }

                //        if (Utility_IdentityManager.IsFlagOverridden(FlagScopeTarget.IsSlave) &&
                //            Utility_IdentityManager.MatchesIdentityFlags(__instance, PawnIdentityFlags.IsSlave))
                //        {
                //            __result = true;
                //        }
                //    }
                //}

                //// Patch #2 Step 7.10: IsGuest Hooking with Scoped Mitigation Strategy
                //[HarmonyPatch(typeof(Pawn_GuestTracker), nameof(Pawn_GuestTracker.GuestStatus), MethodType.Getter)]
                //[HarmonyPriority(Priority.First)]
                //public static class Patch_Pawn_IsGuest
                //{
                //    public static void Postfix(Pawn_GuestTracker __instance, ref GuestStatus __result)
                //    {
                //        // Skip if override flag is not active
                //        if (!Utility_IdentityManager.IsFlagOverridden(FlagScopeTarget.IsGuest))
                //            return;

                //        // Resolve pawn via reflection
                //        Pawn pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
                //        if (pawn == null || pawn.def == null || pawn.Faction != Faction.OfPlayer)
                //            return;

                //        // Centralized identity check
                //        if (Utility_IdentityManager.MatchesIdentityFlags(pawn, PawnIdentityFlags.IsGuest))
                //        {
                //            __result = GuestStatus.Guest;
                //        }
                //    }
                //}

                //// Patch #2 Step 7.11: IsQuestLodger Hooking with Scoped Mitigation Strategy
                //[HarmonyPatch(typeof(QuestUtility), nameof(QuestUtility.IsQuestLodger))]
                //[HarmonyPriority(Priority.First)]
                //public static class Patch_QuestUtility_IsQuestLodger
                //{
                //    public static void Postfix(Pawn p, ref bool __result)
                //    {
                //        if (__result) return;

                //        if (p == null || p.def == null || p.def.race == null)
                //        {
                //            return;
                //        }

                //        if (Utility_IdentityManager.IsFlagOverridden(FlagScopeTarget.IsQuestLodger) &&
                //            Utility_IdentityManager.MatchesIdentityFlags(p, PawnIdentityFlags.IsQuestLodger))
                //        {
                //            __result = true;
                //        }
                //    }
                //}

                //// Patch #2 Step 7.12: IsWildMan Hooking with Scoped Mitigation Strategy
                //[HarmonyPatch(typeof(WildManUtility), nameof(WildManUtility.IsWildMan))]
                //[HarmonyPriority(Priority.First)]
                //public static class Patch_Pawn_IsWildMan
                //{
                //    public static void Postfix(Pawn __instance, ref bool __result)
                //    {
                //        if (__result) return;

                //        if (Utility_IdentityManager.IsFlagOverridden(FlagScopeTarget.IsWildMan) &&
                //            Utility_IdentityManager.MatchesIdentityFlags(__instance, PawnIdentityFlags.IsWildMan))
                //        {
                //            __result = true;
                //        }
                //    }
                //}

                //// Patch #2 Step 7.1 & 7.2: IsColonist Hooking with Scoped Mitigation Strategy
                //[HarmonyPatch(typeof(Pawn), nameof(Pawn.IsColonist), MethodType.Getter)]
                //[HarmonyPriority(Priority.First)]
                //public static class Patch_Pawn_IsColonist
                //{
                //    public static void Postfix(Pawn __instance, ref bool __result)
                //    {
                //        if (__result)
                //        {
                //            return;
                //        }

                //        if (__instance == null || __instance.def == null || __instance.def.race == null)
                //        {
                //            return;
                //        }

                //        if (Utility_IdentityManager.IsFlagOverridden(FlagScopeTarget.IsColonist) &&
                //            Utility_IdentityManager.MatchesIdentityFlags(__instance, PawnIdentityFlags.IsColonist))
                //        {
                //            __result = true;
                //        }
                //    }
                //}

                ///// <summary>
                ////[HarmonyPatch(typeof(Pawn), nameof(Pawn.IsMutant), MethodType.Getter)]
                ////[HarmonyPriority(Priority.First)]
                ////public static class Patch_Pawn_IsMutant
                ////{
                ////    public static void Postfix(Pawn __instance, ref bool __result)
                ////    {
                ////        if (__result)
                ////            return;

                ////        if (!Utility_IdentityManager.IsFlagOverridden(FlagScopeTarget.IsMutant))
                ////            return;

                ////        if (Utility_IdentityManager.MatchesIdentityFlags(__instance, PawnIdentityFlags.IsMutant))
                ////        {
                ////            __result = true;
                ////        }
                ////    }
                ////} 
                ///// </summary>
        }
    }

}