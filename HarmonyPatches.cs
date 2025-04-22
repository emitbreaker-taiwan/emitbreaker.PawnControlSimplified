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

namespace emitbreaker.PawnControl
{
    public class HarmonyPatches
    {
        // === Utility Methods ===
        public static void EnsureDrafterInjected(Pawn p)
        {
            if (Utility_DraftSupport.ShouldInjectDrafter(p) && p.drafter == null)
            {
                p.drafter = new Pawn_DraftController(p);
            }
        }

        public static void ApplyLordDutyForGroup(IEnumerable<Pawn> pawns, LordToil toil, DutyDef fallback, float defaultRadius)
        {
            foreach (var p in pawns)
            {
                Utility_LordDutyResolver.TryAssignLordDuty(p, toil, fallback, toil.FlagLoc, defaultRadius);
            }
        }

        // Vanilla
        // Change ToolUser to Animal
        [HarmonyPatch(typeof(RaceProperties), nameof(RaceProperties.Animal), MethodType.Getter)]
        public static class Patch_RaceProperties_Animal
        {
            public static void Postfix(RaceProperties __instance, ref bool __result)
            {
                if (__result) return;

                ThingDef raceDef = __instance.AnyPawnKind?.race;
                if (raceDef != null && Utility_NonHumanlikePawnControl.IsForceAnimal(raceDef))
                {
                    __result = true;
                }
            }
        }

        //Work tag override logic
        [HarmonyPatch(typeof(Pawn), nameof(Pawn.WorkTypeIsDisabled))]
        public static class Patch_Pawn_WorkTypeIsDisabled
        {
            private static readonly MethodInfo overrideCheck = AccessTools.Method(
                typeof(Utility_CacheManager),
                nameof(Utility_CacheManager.IsWorkTypeEnabledForRace));

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                if (overrideCheck == null)
                {
                    Log.Error("[PawnControl] Failed to locate IsWorkTypeEnabledForRace for WorkTypeIsDisabled patch.");
                }

                var code = new List<CodeInstruction>(instructions);
                for (int i = 0; i < code.Count; i++)
                {
                    yield return code[i];

                    if (code[i].opcode == OpCodes.Ret)
                    {
                        // Inject: if (IsWorkTypeEnabledForRace(this.def, workType)) return false;

                        yield return new CodeInstruction(OpCodes.Ldarg_0); // this (Pawn)
                        yield return new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(Pawn), nameof(Pawn.def))); // this.def
                        yield return new CodeInstruction(OpCodes.Ldarg_1); // WorkTypeDef
                        yield return new CodeInstruction(OpCodes.Call, overrideCheck); // call IsWorkTypeEnabledForRace(def, workType)

                        Label skip = code[i].labels.FirstOrDefault(); // preserve any label on the original return
                        yield return new CodeInstruction(OpCodes.Brfalse_S, skip); // if false, continue original return
                        yield return new CodeInstruction(OpCodes.Ldc_I4_0); // return false
                        yield return new CodeInstruction(OpCodes.Ret);
                    }
                }
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

                if (Utility_CacheManager.Tags.HasTag(pawn.def, ManagedTags.AllowAllWork))
                {
                    // Initialize work settings and assign tagged work priorities
                    initMethod.Invoke(__instance, null);
                    Utility_CacheManager.ApplyTaggedWorkPriorities(pawn);
                    return false; // skip vanilla
                }

                return true;
            }
        }

        // Show non-humanlike if tagged
        [HarmonyPatch(typeof(MainTabWindow_Work), nameof(MainTabWindow_Work.DoWindowContents))]
        public static class Patch_MainTabWindow_Work_DoWindowContents
        {
            static readonly MethodInfo getHumanlike = AccessTools.PropertyGetter(typeof(RaceProperties), nameof(RaceProperties.Humanlike));
            static readonly MethodInfo checkTagged = AccessTools.Method(typeof(Utility_NonHumanlikePawnControl), nameof(Utility_NonHumanlikePawnControl.ShouldAppearInWorkTab));

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var code = instructions.ToList();
                for (int i = 0; i < code.Count; i++)
                {
                    if (code[i].opcode == OpCodes.Callvirt && code[i].operand as MethodInfo == getHumanlike)
                    {
                        yield return code[i - 1]; // Ldloc_X (pawn)
                        yield return new CodeInstruction(OpCodes.Call, checkTagged); // call ShouldAppearInWorkTab(pawn)
                        i++; // skip original call
                        continue;
                    }
                    yield return code[i];
                }
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
                Pawn pawn = __instance as Pawn;
                if (pawn == null) return;

                if (!Utility_CacheManager.Tags.Get(pawn.def).Any(tag => Utility_TagCatalog.ToEnum(tag) == PawnEnumTags.ForceTrainerTab))
                    return;

                if (pawn.training == null)
                    pawn.training = new Pawn_TrainingTracker(pawn);

                var tabs = __result?.ToList() ?? new List<InspectTabBase>();
                if (!tabs.Any(t => t is ITab_Pawn_Training))
                    tabs.Add(InspectTabManager.GetSharedInstance(typeof(ITab_Pawn_Training)));

                __result = tabs;
            }
        }

        [HarmonyPatch]
        public static class Patch_WorkGiver_InteractAnimal_HasJobOnThing
        {
            static MethodBase TargetMethod()
            {
                // Adjust this if the method signature is different (e.g. with 'bool forced')
                return AccessTools.Method(
                    typeof(WorkGiver_InteractAnimal),
                    "HasJobOnThing",
                    new Type[] { typeof(Pawn), typeof(Thing), typeof(bool) }  // ← Add 'bool' if the method has 'bool forced'
                );
            }

            public static void Postfix(Pawn pawn, Thing t, ref bool __result)
            {
                if (__result || pawn == null || t == null || !(t is Pawn target)) return;
                if (pawn.RaceProps.Humanlike) return;
                if (!Utility_CacheManager.Tags.HasTag(pawn.def, ManagedTags.ForceTrainerTab)) return;

                if (target.Faction == pawn.Faction &&
                    target.RaceProps.Animal &&
                    !target.Downed &&
                    ReservationUtility.CanReserve(pawn, target))
                {
                    __result = true;
                }
            }
        }

        // Inject Pawn_DraftController and apply ThinkTree overrides
        [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
        public static class Patch_Pawn_SpawnSetup
        {
            public static void Postfix(Pawn __instance, Map map, bool respawningAfterLoad)
            {
                if (__instance == null || !__instance.Spawned) return;

                if (Utility_VehicleFramework.IsVehiclePawn(__instance)) return;

                if (Utility_DraftSupport.ShouldInjectDrafter(__instance))
                {
                    __instance.drafter = new Pawn_DraftController(__instance);
                }

                // Optional: Apply ThinkTree overrides
                var mainTree = Utility_CacheManager.GetCachedMainThinkTree(__instance);
                var constTree = Utility_CacheManager.GetCachedConstantThinkTree(__instance);
                if (mainTree != null)
                    Traverse.Create(__instance.kindDef).Property("thinkTreeMain").SetValue(mainTree);
                if (constTree != null)
                    Traverse.Create(__instance.kindDef).Property("thinkTreeConstant").SetValue(constTree);
            }
        }

        //Ensure drafter is injected on existing pawns
        [HarmonyPatch(typeof(Map), nameof(Map.FinalizeInit))]
        public static class Patch_Map_FinalizeInit
        {
            public static void Postfix(Map __instance)
            {
                foreach (Pawn p in __instance.mapPawns.AllPawns)
                {
                    if (!Utility_VehicleFramework.IsVehiclePawn(p) && Utility_DraftSupport.ShouldInjectDrafter(p))
                    {
                        p.drafter = new Pawn_DraftController(p);
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

                if (!Utility_NonHumanlikePawnControl.ShouldDraftInject(__instance)) yield break;

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
                    __instance.pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced);
            }
        }

        //Fallback CE combat behavior
        [HarmonyPatch(typeof(ThinkNode_Priority), nameof(ThinkNode_Priority.TryIssueJobPackage))]
        public static class Patch_ThinkNodePriority_TryIssueJob
        {
            public static bool Prefix(Pawn pawn, JobIssueParams jobParams, ref ThinkResult __result, ThinkNode_Priority __instance)
            {
                if (!Utility_CECompatibility.CEActive) return true;
                if (!Utility_NonHumanlikePawnControl.PawnChecker(pawn) || !pawn.Drafted || pawn.mindState == null) return true;

                if (Utility_NonHumanlikePawnControl.HasTag(pawn.def, ManagedTags.AutoDraftInjection))
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
                    Utility_LordDutyResolver.TryAssignLordDuty(p, __instance, DutyDefOf.AssaultColony, IntVec3.Invalid);
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
                    Utility_LordDutyResolver.TryAssignLordDuty(p, __instance, DutyDefOf.Defend, __instance.FlagLoc, -1f);
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
                    Utility_LordDutyResolver.TryAssignLordDuty(p, __instance, DutyDefOf.DefendBase, __instance.FlagLoc, 25f);
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
                    if (!Utility_NonHumanlikePawnControl.PawnChecker(p)) continue;

                    if (Utility_NonHumanlikePawnControl.HasTag(p.def, ManagedTags.AutoDraftInjection))
                    {
                        var dutyDef = Utility_DraftSupport.ResolveSiegeDuty(p);
                        Utility_LordDutyResolver.TryAssignLordDuty(p, __instance, dutyDef, __instance.FlagLoc, 34f);
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

                Log.Message($"[PawnControl] Auto-whitelisted {dynamicWhitelist.Count} global-scanning WorkGiverDefs.");
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

        // === CE ===
        // Drafted Fallback Job Injection (Hold Position)
        [HarmonyPatch(typeof(Pawn), nameof(Pawn.Tick))]
        public static class Patch_Pawn_Tick_HoldCombat
        {
            public static void Postfix(Pawn __instance)
            {
                if (!Utility_CECompatibility.CEActive || !Utility_NonHumanlikePawnControl.PawnChecker(__instance))
                    return;

                if (__instance.Drafted && __instance.jobs?.curJob == null &&
                    __instance.mindState?.duty == null &&
                    __instance.mindState?.lastJobTag != JobTag.MiscWork)
                {
                    Job holdJob = JobMaker.MakeJob(JobDefOf.Wait_Combat, __instance.Position);
                    holdJob.expiryInterval = 120;
                    holdJob.checkOverrideOnExpire = true;
                    __instance.jobs.StartJob(holdJob, JobCondition.InterruptForced, null, false);
                }
            }
        }

        // Drafted AI JobTree Override (CE fallback)
        [HarmonyPatch(typeof(ThinkNode_Priority), nameof(ThinkNode_Priority.TryIssueJobPackage))]
        public static class Patch_ThinkNodePriority_DraftedOverride
        {
            public static bool Prefix(Pawn pawn, JobIssueParams jobParams, ref ThinkResult __result, ThinkNode_Priority __instance)
            {
                if (!Utility_CECompatibility.CEActive || !pawn.Drafted || pawn.mindState == null)
                    return true;

                if (!Utility_NonHumanlikePawnControl.HasTag(pawn.def, ManagedTags.AutoDraftInjection))
                    return true;

                if (pawn.mindState.duty == null &&
                    (pawn.jobs?.curJob == null || pawn.jobs.curJob.def == JobDefOf.Wait_Combat))
                {
                    Job hold = JobMaker.MakeJob(JobDefOf.Wait_Combat, pawn.Position);
                    hold.expiryInterval = 240;
                    hold.checkOverrideOnExpire = true;
                    __result = new ThinkResult(hold, __instance, JobTag.Misc);
                    return false;
                }

                return true;
            }
        }

        // === HAR ===
        // Restrict Apparel by BodyType (used in PawnApparelGenerator Patch)
        public static void Postfix_PawnApparelGenerator(Pawn pawn, PawnGenerationRequest request)
        {
            if (pawn == null || pawn.apparel == null || pawn.apparel.WornApparel == null)
            {
                return;
            }

            if (!Utility_CacheManager.IsApparelRestricted(pawn))
            {
                return;
            }

            var physicalModExtension = pawn.def.GetModExtension<NonHumanlikePawnControlExtension>();
            var virtualModExtension = pawn.def.GetModExtension<VirtualNonHumanlikePawnControlExtension>();

            if (physicalModExtension != null)
            {
                if (!physicalModExtension.restrictApparelByBodyType)
                {
                    return;
                }

                if (!Utility_HARCompatibility.IsAllowedBodyType(pawn, physicalModExtension.allowedBodyTypes))
                {
                    pawn.apparel.WornApparel.Clear();
                }
            }
        }

        public static void AssignGenericDefendDuty(LordToil __instance)
        {
            foreach (Pawn p in __instance.lord.ownedPawns)
            {
                Utility_LordDutyResolver.TryAssignLordDuty(p, __instance, DutyDefOf.Defend, __instance.FlagLoc, 28f);
            }
        }

        // === VFE ===
        // Disable VFE-AI Work ThinkTree Jobs on incompatible pawns
        public static class Patch_VFEAI_TryGiveJob
        {
            public static bool Prefix(Pawn pawn, ref Job __result)
            {
                if (!Utility_VFECompatibility.VFEActive)
                    return true;

                if (!Utility_NonHumanlikePawnControl.PawnChecker(pawn))
                    return true;

                if (pawn.Drafted && Utility_CECompatibility.IsCECombatBusy(pawn))
                {
                    __result = null;
                    return false;
                }

                if (Utility_CacheManager.Tags.HasTag(pawn.def, "DisableVFEAIJobs"))
                {
                    __result = null;
                    return false;
                }

                return true; // continue with original TryGiveJob
            }
        }
    }
}