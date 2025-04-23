using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using static System.Net.Mime.MediaTypeNames;
using System.Reflection.Emit;
using System.Reflection;
using Verse.AI.Group;
using static emitbreaker.PawnControl.ManagedTags;
using System.Security.Cryptography;

namespace emitbreaker.PawnControl
{
    public class HarmonyPatches
    {
        //// === Utility Methods ===
        //public static void EnsureDrafterInjected(Pawn p)
        //{
        //    if (Utility_DraftSupport.ShouldInjectDrafter(p) && p.drafter == null)
        //    {
        //        p.drafter = new Pawn_DraftController(p);
        //    }
        //}

        //public static void ApplyLordDutyForGroup(IEnumerable<Pawn> pawns, LordToil toil, DutyDef fallback, float defaultRadius)
        //{
        //    foreach (var p in pawns)
        //    {
        //        Utility_LordDutyResolver.TryAssignLordDuty(p, toil, fallback, toil.FlagLoc, defaultRadius);
        //    }
        //}

        // Vanilla
        // Change ToolUser to Animal
        [HarmonyPatch(typeof(RaceProperties), nameof(RaceProperties.Animal), MethodType.Getter)]
        public static class Patch_RaceProperties_Animal
        {
            public static void Postfix(RaceProperties __instance, ref bool __result)
            {
                if (__result)
                {
                    return;
                }

                // Early exit if race is null
                var race = __instance.AnyPawnKind?.race;
                if (race == null)
                {
                    return;
                }

                // Try to get the mod extension once
                var modExtension = Utility_CacheManager.GetModExtension(race);
                if (modExtension == null)
                {
                    return;
                }

                // Use a local variable for better readability and to avoid multiple Contains checks
                bool isForceAnimalTagPresent = modExtension.tags?.Contains(ManagedTags.ForceAnimal) == true;

                // Combine checks for forceAnimal and the tag
                if (modExtension.forceAnimal == true || isForceAnimalTagPresent)
                {
                    __result = true;
                }
            }
        }

        //Work tag override logic
        [HarmonyPatch(typeof(Pawn), nameof(Pawn.WorkTypeIsDisabled))]
        public static class Patch_Pawn_WorkTypeIsDisabled
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var code = new List<CodeInstruction>(instructions);
                for (int i = 0; i < code.Count; i++)
                {
                    yield return code[i];

                    if (code[i].opcode == OpCodes.Ret)
                    {
                        // Inject: if (IsWorkTypeAllowed(this.def, workType)) return false;

                        yield return new CodeInstruction(OpCodes.Ldarg_0); // this (Pawn)
                        yield return new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(Pawn), nameof(Pawn.def))); // this.def
                        yield return new CodeInstruction(OpCodes.Ldarg_1); // WorkTypeDef
                        yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch_Pawn_WorkTypeIsDisabled), nameof(IsWorkTypeAllowed))); // call IsWorkTypeAllowed

                        Label skip = code[i].labels.FirstOrDefault(); // preserve any label on the original return
                        yield return new CodeInstruction(OpCodes.Brfalse_S, skip); // if false, continue original return
                        yield return new CodeInstruction(OpCodes.Ldc_I4_0); // return false
                        yield return new CodeInstruction(OpCodes.Ret);
                    }
                }
            }

            private static bool IsWorkTypeAllowed(ThingDef def, WorkTypeDef workType)
            {
                if (def == null || def.race?.Humanlike == true)
                    return true;

                var modExtension = Utility_CacheManager.GetModExtension(def);
                if (modExtension == null)
                    return false;

                // Check if the work type is allowed based on tags
                string workTag = ManagedTags.AllowWorkPrefix + workType.defName;
                return modExtension.tags?.Contains(workTag) == true || modExtension.tags?.Contains(ManagedTags.AllowAllWork) == true;
            }
        }

        [HarmonyPatch(typeof(Pawn_WorkSettings), nameof(Pawn_WorkSettings.EnableAndInitialize))]
        public static class Patch_Pawn_WorkSettings_EnableAndInitialize
        {
            // Cache private field: private Pawn pawn;
            private static readonly FieldInfo pawnField = AccessTools.Field(typeof(Pawn_WorkSettings), "pawn");

            // Cache internal method: EnableAndInitializeIfNotAlreadyInitialized
            private static readonly MethodInfo initMethod = AccessTools.Method(typeof(Pawn_WorkSettings), "EnableAndInitializeIfNotAlreadyInitialized");

            public static bool Prefix(object __instance)
            {
                if (__instance == null || pawnField == null || initMethod == null)
                {
                    Log.Warning("[PawnControl] EnableAndInitialize patch skipped due to missing reflection targets.");
                    return true;
                }

                Pawn pawn = pawnField.GetValue(__instance) as Pawn;
                if (pawn == null) return true;

                if (pawn.RaceProps.Humanlike)
                    return true; // use vanilla logic

                var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                if (modExtension == null || modExtension.tags == null || !modExtension.tags.Contains(ManagedTags.AllowAllWork))
                    return true;

                // Initialize work settings
                initMethod.Invoke(__instance, null);

                // Assign work priorities based on tags
                foreach (var workType in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                {
                    string workTag = ManagedTags.AllowWorkPrefix + workType.defName;
                    if (modExtension.tags.Contains(workTag) || modExtension.tags.Contains(ManagedTags.AllowAllWork))
                    {
                        if (!pawn.WorkTypeIsDisabled(workType))
                        {
                            pawn.workSettings.SetPriority(workType, 3); // Default priority
                        }
                    }
                }

                return false; // Skip vanilla logic
            }
        }

        // Show non-humanlike if tagged
        [HarmonyPatch(typeof(MainTabWindow_Work), "Pawns", MethodType.Getter)]
        public static class Patch_MainTabWindow_Work_Pawns
        {
            public static void Postfix(ref IEnumerable<Pawn> __result)
            {
                // Filter the pawns to include only those that should appear in the work tab
                __result = __result.Where(ShouldAppearInWorkTab);
            }

            private static bool ShouldAppearInWorkTab(Pawn pawn)
            {
                if (pawn == null || pawn.def == null)
                {
                    return false;
                }

                // Humanlike pawns always appear in the work tab
                if (pawn.RaceProps.Humanlike)
                {
                    return true;
                }

                // Check if the pawn has the required mod extension
                var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                if (modExtension == null)
                {
                    return false;
                }

                // Check if the pawn is tagged to appear in the work tab
                return modExtension.tags?.Contains(ManagedTags.AllowAllWork) == true ||
                       modExtension.tags?.Any(tag => tag.StartsWith(ManagedTags.AllowWorkPrefix)) == true;
            }
        }


        // Inject training tab for non-humanlike pawns
        [HarmonyPatch]
        public static class Patch_ThingWithComps_GetInspectTabs
        {
            public static MethodBase TargetMethod()
            {
                // Manually resolve method from base type
                return AccessTools.Method(typeof(Thing), "GetInspectTabs");
            }

            public static void Postfix(Thing __instance, ref IEnumerable<InspectTabBase> __result)
            {
                // Only apply to pawns
                if (!(__instance is Pawn pawn)) return;

                // Check if the pawn's ThingDef has the required mod extension and tag
                var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                if (modExtension == null || modExtension.tags == null || !modExtension.tags.Contains(ManagedTags.ForceTrainerTab))
                    return;

                // Ensure the pawn has a training tracker
                if (pawn.training == null)
                    pawn.training = new Pawn_TrainingTracker(pawn);

                // Add the training tab if it doesn't already exist
                var tabs = __result?.ToList() ?? new List<InspectTabBase>();
                if (!tabs.Any(t => t is ITab_Pawn_Training))
                    tabs.Add(InspectTabManager.GetSharedInstance(typeof(ITab_Pawn_Training)));

                __result = tabs;
            }
        }

        [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
        public static class Patch_Pawn_SpawnSetup
        {
            public static void Postfix(Pawn __instance, Map map, bool respawningAfterLoad)
            {
                if (__instance == null || !__instance.Spawned)
                {
                    //Log.Warning("[PawnControl] SpawnSetup skipped: Pawn is null or not spawned.");
                    return;
                }

                // Check if the pawn is a vehicle
                if (Utility_VehicleFramework.IsVehiclePawn(__instance))
                {
                    //Log.Message($"[PawnControl] SpawnSetup skipped: {__instance.LabelShort} is a vehicle.");
                    return;
                }

                // Inject Pawn_DraftController
                if (Utility_DrafterManager.ShouldInjectDrafter(__instance))
                {
                    //Log.Message($"[PawnControl] Injecting Pawn_DraftController for {__instance.LabelShort}.");
                    __instance.drafter = new Pawn_DraftController(__instance);
                }

                // Apply ThinkTree overrides
                try
                {
                    ThinkTreeDef mainTree = null;
                    ThinkTreeDef constTree = null;

                    // Retrieve ThinkTree overrides from the pawn's def
                    if (__instance.def != null)
                    {
                        var modExtension = __instance.def.GetModExtension<NonHumanlikePawnControlExtension>();
                        if (modExtension != null)
                        {
                            mainTree = modExtension.overrideThinkTreeMain;
                            constTree = modExtension.overrideThinkTreeConstant;
                        }
                    }

                    if (mainTree != null)
                    {
                        //Log.Message($"[PawnControl] Applying main ThinkTree override for {__instance.LabelShort}.");
                        Traverse.Create(__instance.kindDef).Property("thinkTreeMain").SetValue(mainTree);
                    }

                    if (constTree != null)
                    {
                        //Log.Message($"[PawnControl] Applying constant ThinkTree override for {__instance.LabelShort}.");
                        Traverse.Create(__instance.kindDef).Property("thinkTreeConstant").SetValue(constTree);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[PawnControl] Error applying ThinkTree overrides for {__instance.LabelShort}: {ex.Message}");
                }
            }
        }


        //Ensure drafter is injected on existing pawns
        [HarmonyPatch(typeof(Map), nameof(Map.FinalizeInit))]
        public static class Patch_Map_FinalizeInit
        {
            public static void Postfix(Map __instance)
            {
                foreach (Pawn pawn in __instance.mapPawns.AllPawns)
                {
                    if (!Utility_VehicleFramework.IsVehiclePawn(pawn) && Utility_DrafterManager.ShouldInjectDrafter(pawn))
                    {
                        pawn.drafter = new Pawn_DraftController(pawn);
                    }
                }
            }
        }

        // Add draft toggle for injected non-humanlike pawns
        [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
        public static class Patch_Pawn_GetGizmos
        {
            public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
            {
                if (Utility_VehicleFramework.IsVehiclePawn(__instance)) yield break;

                foreach (var g in __result) yield return g;

                if (!Utility_DrafterManager.ShouldInjectDrafter(__instance)) yield break;

                if (Utility_CECompatibility.CEActive && Utility_CECompatibility.IsCECombatBusy(__instance))
                {
                    var cmd = new Command_Action
                    {
                        defaultLabel = "Busy",
                        defaultDesc = "This unit is currently engaged in combat.",
                        icon = TexCommand.Draft,
                        action = () => { }
                    };
                    cmd.Disable("In Combat");
                    yield return cmd;
                    yield break;
                }

                yield return new Command_Toggle
                {
                    defaultLabel = __instance.Drafted ? "Undraft" : "Draft",
                    defaultDesc = "Toggles draft status.",
                    isActive = () => __instance.Drafted,
                    toggleAction = () => __instance.drafter.Drafted = !__instance.Drafted,
                    icon = TexCommand.Draft
                };
            }
        }

        // Setter – Interrupt job on undraft
        [HarmonyPatch(typeof(Pawn_DraftController), nameof(Pawn_DraftController.Drafted), MethodType.Setter)]
        public static class Patch_Pawn_DraftController_Drafted
        {
            public static void Postfix(Pawn_DraftController __instance, bool value)
            {
                if (!value)
                {
                    __instance.pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced);
                }
            }
        }

        //Fallback CE combat behavior
        [HarmonyPatch(typeof(ThinkNode_Priority), nameof(ThinkNode_Priority.TryIssueJobPackage))]
        public static class Patch_ThinkNodePriority_TryIssueJob
        {
            public static bool Prefix(Pawn pawn, JobIssueParams jobParams, ref ThinkResult __result, ThinkNode_Priority __instance)
            {
                if (!Utility_CECompatibility.CEActive) return true;
                if (!Utility_Common.PawnChecker(pawn) || !pawn.Drafted || pawn.mindState == null) return true;

                if (Utility_TagManager.HasTag(pawn.def, ManagedTags.AutoDraftInjection))
                {
                    if (pawn.mindState.duty == null && (pawn.jobs?.curJob == null || pawn.jobs.curJob.def == JobDefOf.Wait_Combat))
                    {
                        Job hold = JobMaker.MakeJob(JobDefOf.Wait_Combat, pawn.Position);
                        hold.expiryInterval = 240;
                        hold.checkOverrideOnExpire = true;
                        __result = new ThinkResult(hold, __instance, JobTag.Misc);
                        return false;
                    }
                }

                return true;
            }
        }

        // Inject duty for drafted AI pawns
        [HarmonyPatch(typeof(LordToil_AssaultColony), nameof(LordToil_AssaultColony.UpdateAllDuties))]
        public static class Patch_LordToil_AssaultColony
        {
            public static void Postfix(LordToil_AssaultColony __instance)
            {
                foreach (var p in __instance.lord.ownedPawns)
                {
                    Utility_LordDutyManager.TryAssignLordDuty(p, __instance, DutyDefOf.AssaultColony, IntVec3.Invalid);
                }
            }
        }

        [HarmonyPatch(typeof(LordToil_DefendPoint), nameof(LordToil_DefendPoint.UpdateAllDuties))]
        public static class Patch_LordToil_DefendPoint
        {
            public static void Postfix(LordToil_DefendPoint __instance)
            {
                foreach (var p in __instance.lord.ownedPawns)
                {
                    Utility_LordDutyManager.TryAssignLordDuty(p, __instance, DutyDefOf.Defend, __instance.FlagLoc, -1f);
                }
            }
        }

        [HarmonyPatch(typeof(LordToil_DefendBase), nameof(LordToil_DefendBase.UpdateAllDuties))]
        public static class Patch_LordToil_DefendBase
        {
            public static void Postfix(LordToil_DefendBase __instance)
            {
                foreach (var p in __instance.lord.ownedPawns)
                {
                    Utility_LordDutyManager.TryAssignLordDuty(p, __instance, DutyDefOf.DefendBase, __instance.FlagLoc, 25f);
                }
            }
        }

        [HarmonyPatch(typeof(LordToil_Siege), nameof(LordToil_Siege.UpdateAllDuties))]
        public static class Patch_LordToil_Siege
        {
            public static void Postfix(LordToil_Siege __instance)
            {
                foreach (var p in __instance.lord.ownedPawns)
                {
                    if (!Utility_Common.PawnChecker(p)) continue;

                    if (Utility_TagManager.HasTag(p.def, ManagedTags.AutoDraftInjection))
                    {
                        var dutyDef = Utility_DrafterManager.ResolveSiegeDuty(p);
                        Utility_LordDutyManager.TryAssignLordDuty(p, __instance, dutyDef, __instance.FlagLoc, 34f);
                    }
                }
            }
        }


        [HarmonyPatch(typeof(Pawn_SkillTracker), nameof(Pawn_SkillTracker.GetSkill))]
        public static class Patch_Pawn_SkillTracker_GetSkill
        {
            private static readonly FieldInfo pawnField = AccessTools.Field(typeof(Pawn_SkillTracker), "pawn");

            public static bool Prefix(Pawn_SkillTracker __instance, SkillDef skillDef, ref SkillRecord __result)
            {
                if (__instance == null || skillDef == null) return true;

                Pawn pawn = pawnField.GetValue(__instance) as Pawn;
                if (pawn == null || pawn.skills == null)
                    return true;

                // If humanlike, use real skill logic
                if (pawn.RaceProps?.Humanlike == true)
                    return true;

                // Otherwise, simulate
                __result = Utility_CacheManager.GetFakeSkill(pawn, skillDef);
                return false;
            }
        }

        [HarmonyPatch(typeof(WorkGiver_Scanner), nameof(WorkGiver_Scanner.PotentialWorkThingsGlobal))]
        public static class Patch_WorkGiverScanner_PotentialWorkThingsGlobal
        {
            private static HashSet<string> dynamicWhitelist;

            static Patch_WorkGiverScanner_PotentialWorkThingsGlobal()
            {
                dynamicWhitelist = new HashSet<string>();

                foreach (WorkGiverDef def in DefDatabase<WorkGiverDef>.AllDefsListForReading)
                {
                    if (def?.giverClass == null || def.workType == null) continue;

                    if (typeof(WorkGiver_Scanner).IsAssignableFrom(def.giverClass))
                    {
                        var method = AccessTools.Method(def.giverClass, nameof(WorkGiver_Scanner.PotentialWorkThingsGlobal));
                        if (method != null && method.DeclaringType != typeof(WorkGiver_Scanner))
                        {
                            dynamicWhitelist.Add(def.defName);
                        }
                    }
                }

                //Log.Message($"[PawnControl] Auto-whitelisted {dynamicWhitelist.Count} global-scanning WorkGiverDefs.");
            }

            public static bool Prefix(WorkGiver_Scanner __instance, Pawn pawn, ref IEnumerable<Thing> __result)
            {
                if (__instance?.def == null || pawn?.def == null)
                    return true;

                if (pawn.RaceProps.Humanlike)
                    return true;

                if (!dynamicWhitelist.Contains(__instance.def.defName))
                    return true;

                if (!Utility_CacheManager.IsWorkTypeEnabledForPawn(pawn, __instance.def.workType))
                {
                    if (Prefs.DevMode)
                        Log.Message($"[PawnControl] Blocked {pawn.def.defName} from {__instance.def.defName} (no work tag)");

                    __result = new List<Thing>();
                    return false;
                }

                // 🧠 Check simulated skill if skill requirement exists
                List<SkillDef> relevantSkills = __instance.def.workType?.relevantSkills;
                if (relevantSkills != null && relevantSkills.Count > 0)
                {
                    foreach (var skillDef in relevantSkills)
                    {
                        int simulatedLevel = Utility_CacheManager.GetFakeSkill(pawn, skillDef).levelInt;
                        if (simulatedLevel < 4)
                        {
                            if (Prefs.DevMode)
                                Log.Message($"[PawnControl] {pawn.def.defName} lacks required skill ({skillDef.defName}={simulatedLevel}) for {__instance.def.defName}");

                            __result = new List<Thing>();
                            return false;
                        }
                    }
                }

                return true;
            }
        }

        //// === CE ===
        //// Drafted Fallback Job Injection (Hold Position)
        //[HarmonyPatch(typeof(Pawn), nameof(Pawn.Tick))]
        //public static class Patch_Pawn_Tick_HoldCombat
        //{
        //    public static void Postfix(Pawn __instance)
        //    {
        //        if (!Utility_CECompatibility.CEActive || !Utility_NonHumanlikePawnControl.PawnChecker(__instance))
        //            return;

        //        if (__instance.Drafted && __instance.jobs?.curJob == null &&
        //            __instance.mindState?.duty == null &&
        //            __instance.mindState?.lastJobTag != JobTag.MiscWork)
        //        {
        //            Job holdJob = JobMaker.MakeJob(JobDefOf.Wait_Combat, __instance.Position);
        //            holdJob.expiryInterval = 120;
        //            holdJob.checkOverrideOnExpire = true;
        //            __instance.jobs.StartJob(holdJob, JobCondition.InterruptForced, null, false);
        //        }
        //    }
        //}

        //// Drafted AI JobTree Override (CE fallback)
        //[HarmonyPatch(typeof(ThinkNode_Priority), nameof(ThinkNode_Priority.TryIssueJobPackage))]
        //public static class Patch_ThinkNodePriority_DraftedOverride
        //{
        //    public static bool Prefix(Pawn pawn, JobIssueParams jobParams, ref ThinkResult __result, ThinkNode_Priority __instance)
        //    {
        //        if (!Utility_CECompatibility.CEActive || !pawn.Drafted || pawn.mindState == null)
        //            return true;

        //        if (!Utility_NonHumanlikePawnControl.HasTag(pawn.def, ManagedTags.AutoDraftInjection))
        //            return true;

        //        if (pawn.mindState.duty == null &&
        //            (pawn.jobs?.curJob == null || pawn.jobs.curJob.def == JobDefOf.Wait_Combat))
        //        {
        //            Job hold = JobMaker.MakeJob(JobDefOf.Wait_Combat, pawn.Position);
        //            hold.expiryInterval = 240;
        //            hold.checkOverrideOnExpire = true;
        //            __result = new ThinkResult(hold, __instance, JobTag.Misc);
        //            return false;
        //        }

        //        return true;
        //    }
        //}

        //// === HAR ===
        //// Restrict Apparel by BodyType (used in PawnApparelGenerator Patch)
        //public static void Postfix_PawnApparelGenerator(Pawn pawn, PawnGenerationRequest request)
        //{
        //    if (pawn == null || pawn.apparel == null || pawn.apparel.WornApparel == null)
        //    {
        //        return;
        //    }

        //    if (!Utility_CacheManager.IsApparelRestricted(pawn))
        //    {
        //        return;
        //    }

        //    var physicalModExtension = pawn.def.GetModExtension<NonHumanlikePawnControlExtension>();
        //    var virtualModExtension = pawn.def.GetModExtension<VirtualNonHumanlikePawnControlExtension>();

        //    if (physicalModExtension != null)
        //    {
        //        if (!physicalModExtension.restrictApparelByBodyType)
        //        {
        //            return;
        //        }

        //        if (!Utility_HARCompatibility.IsAllowedBodyType(pawn, physicalModExtension.allowedBodyTypes))
        //        {
        //            pawn.apparel.WornApparel.Clear();
        //        }
        //    }
        //}

        //public static void AssignGenericDefendDuty(LordToil __instance)
        //{
        //    foreach (Pawn p in __instance.lord.ownedPawns)
        //    {
        //        Utility_LordDutyResolver.TryAssignLordDuty(p, __instance, DutyDefOf.Defend, __instance.FlagLoc, 28f);
        //    }
        //}

        //// === VFE ===
        //// Disable VFE-AI Work ThinkTree Jobs on incompatible pawns
        //public static class Patch_VFEAI_TryGiveJob
        //{
        //    public static bool Prefix(Pawn pawn, ref Job __result)
        //    {
        //        if (!Utility_VFECompatibility.VFEActive)
        //            return true;

        //        if (!Utility_NonHumanlikePawnControl.PawnChecker(pawn))
        //            return true;

        //        if (pawn.Drafted && Utility_CECompatibility.IsCECombatBusy(pawn))
        //        {
        //            __result = null;
        //            return false;
        //        }

        //        if (Utility_CacheManager.Tags.HasTag(pawn.def, "DisableVFEAIJobs"))
        //        {
        //            __result = null;
        //            return false;
        //        }

        //        return true; // continue with original TryGiveJob
        //    }
        //}
    }
}