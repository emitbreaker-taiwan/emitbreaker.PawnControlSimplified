using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Provides runtime stat injection via invisible Hediffs for modded pawns with injected skills.
    /// </summary>
    public static class Utility_StatManager
    {
        // Mapping SkillDef to relevant StatDefs and their injected values
        public static readonly Dictionary<SkillDef, Dictionary<StatDef, float>> skillToStatMap = new Dictionary<SkillDef, Dictionary<StatDef, float>>
        {
            {
                SkillDefOf.Plants, new Dictionary<StatDef, float>
                {
                    { StatDefOf.PlantWorkSpeed, 1.0f },
                    { StatDefOf.PlantHarvestYield, 0.75f },
                    { StatDefOf.DrugHarvestYield, 0.5f }
                }
            },
            {
                SkillDefOf.Mining, new Dictionary<StatDef, float>
                {
                    { StatDef.Named("MiningSpeed"), 1.0f },
                    { StatDef.Named("DeepDrillingSpeed"), 0.8f },
                    { StatDef.Named("SmoothingSpeed"), 0.6f }
                }
            },
            {
                SkillDefOf.Construction, new Dictionary<StatDef, float>
                {
                    { StatDef.Named("ConstructionSpeed"), 1.0f },
                    { StatDef.Named("SmoothingSpeed"), 0.5f }
                }
            },
            {
                SkillDefOf.Cooking, new Dictionary<StatDef, float>
                {
                    { StatDef.Named("DrugCookingSpeed"), 0.1f },
                    { StatDef.Named("CookSpeed"), 0.1f },
                    { StatDef.Named("ButcheryFleshEfficiency"), 0.8f }
                }
            },
            {
                SkillDefOf.Crafting, new Dictionary<StatDef, float>
                {
                    { StatDef.Named("WorkSpeedGlobal"), 1.0f },
                    { StatDef.Named("GeneralLaborSpeed"), 1.0f },
                }
            },
            {
                SkillDefOf.Medicine, new Dictionary<StatDef, float>
                {
                    { StatDef.Named("MedicalTendSpeed"), 0.75f },
                    { StatDef.Named("SurgerySuccessChanceFactor"), 0.5f }
                }
            },
            {
                SkillDefOf.Animals, new Dictionary<StatDef, float>
                {
                    { StatDef.Named("AnimalGatherSpeed"), 0.8f },
                    { StatDef.Named("TameAnimalChance"), 0.6f }
                }
            },
            {
                SkillDefOf.Shooting, new Dictionary<StatDef, float>
                {
                    { StatDef.Named("ShootingAccuracyPawn"), 0.1f },
                    { StatDef.Named("AimingDelayFactor"), -0.05f }
                }
            },
            {
                SkillDefOf.Melee, new Dictionary<StatDef, float>
                {
                    { StatDef.Named("MeleeHitChance"), 0.2f },
                    { StatDef.Named("MeleeDPS"), 0.3f }
                }
            }
        };
        private static readonly HashSet<int> _hediffInjectedPawnIDs = new HashSet<int>();

        public static bool HasAlreadyInjected(Pawn pawn)
        {
            return _hediffInjectedPawnIDs.Contains(pawn.thingIDNumber);
        }

        public static void MarkAsInjected(Pawn pawn)
        {
            _hediffInjectedPawnIDs.Add(pawn.thingIDNumber);
        }

        /// <summary>
        /// Determines which skills need stat injection, based on injectedSkills and actual skill levels.
        /// </summary>
        public static List<SkillDef> GetSkillsNeedingStatSupport(Pawn pawn)
        {
            var result = new List<SkillDef>();
            if (pawn == null || pawn.skills == null || pawn.def == null)
                return result;

            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null)
                return result;

            HashSet<SkillDef> seen = new HashSet<SkillDef>();

            if (modExtension.injectedSkills != null)
            {
                foreach (var skillEntry in modExtension.injectedSkills)
                {
                    SkillDef skillDef = Utility_Common.SkillDefNamed(skillEntry.skill);
                    if (skillDef != null && seen.Add(skillDef))
                    {
                        result.Add(skillDef);
                    }
                }
            }

            foreach (var skill in pawn.skills.skills)
            {
                if (skill?.Level > 0 && seen.Add(skill.def))
                {
                    result.Add(skill.def);
                }
            }

            return result;
        }

        /// <summary>
        /// Injects the XML-based PawnControl_StatStub HediffDef which uses HediffComp_StatBridge
        /// to dynamically apply stat offsets based on pawn skills.
        /// </summary>
        public static void InjectConsolidatedStatHediff(Pawn pawn)
        {
            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);

            if (HasAlreadyInjected(pawn))
            {
                if (Prefs.DevMode && modExtension.debugMode)
                    Log.Message($"[PawnControl] Skipped injection: {pawn.LabelShort} already processed.");
                return;
            }

            if (Prefs.DevMode && modExtension.debugMode)
            {
                Log.Message($"[PawnControl] Stat consolidation attempt for {pawn.LabelShort} ({pawn.def?.defName ?? "null"})");
            }

            if (pawn == null)
            {
                if (Prefs.DevMode && modExtension.debugMode)
                    Log.Error("[PawnControl] Cannot inject stat hediff: pawn is null");
                return;
            }

            if (pawn.def == null)
            {
                if (Prefs.DevMode && modExtension.debugMode)
                    Log.Error($"[PawnControl] Cannot inject stat hediff for {pawn.LabelShort}: pawn.def is null");
                return;
            }

            if (pawn.skills == null)
            {
                if (Prefs.DevMode && modExtension.debugMode)
                    Log.Warning($"[PawnControl] Cannot inject stat hediff for {pawn.LabelShort}: pawn.skills is null");
                return;
            }

            if (pawn.health == null || pawn.health.hediffSet == null)
            {
                if (Prefs.DevMode && modExtension.debugMode)
                    Log.Error($"[PawnControl] Cannot inject stat hediff for {pawn.LabelShort}: health system is null");
                return;
            }

            var skillsToCheck = GetSkillsNeedingStatSupport(pawn);
            if (skillsToCheck.Count == 0)
            {
                if (Prefs.DevMode && modExtension.debugMode)
                    Log.Message($"[PawnControl] No skills need stat support for {pawn.LabelShort}");
                return;
            }

            // Get the XML-defined stub hediff
            HediffDef statStubDef = DefDatabase<HediffDef>.GetNamed("PawnControl_StatStub", false);

            if (statStubDef == null)
            {
                if (Prefs.DevMode && modExtension.debugMode)
                    Log.Error("[PawnControl] PawnControl_StatStub HediffDef not found in DefDatabase! Make sure it's defined in XML.");
                return;
            }

            // Check if the pawn already has the stat hediff
            if (!pawn.health.hediffSet.HasHediff(statStubDef))
            {
                try
                {
                    // Create the hediff - HediffComp_StatBridge will handle the stat modifications
                    var hediff = HediffMaker.MakeHediff(statStubDef, pawn);
                    hediff.Severity = 0.1f; // Needs to be non-zero to be active

                    pawn.health.AddHediff(hediff);

                    if (Prefs.DevMode && modExtension.debugMode)
                    {
                        Log.Message($"[PawnControl] XML-based stat hediff '{statStubDef.defName}' injected into {pawn.LabelShort}");
                        Log.Message($"[PawnControl] Skills with stat support: {skillsToCheck.Count}");
                        foreach (var skillDef in skillsToCheck)
                        {
                            Log.Message($"  - {skillDef.defName}");
                        }
                    }                    
                }
                catch (Exception ex)
                {
                    Log.Error($"[PawnControl] Error adding hediff to {pawn.LabelShort}: {ex}");
                }
            }
            else if (Prefs.DevMode && modExtension.debugMode)
            {
                Log.Message($"[PawnControl] {pawn.LabelShort} already has the stat hediff.");
            }

            MarkAsInjected(pawn);
        }

        /// <summary>
        /// Check if the PawnControl_StatStub HediffDef exists in the DefDatabase
        /// </summary>
        public static void CheckStatHediffDefExists()
        {
            var hediffDef = DefDatabase<HediffDef>.GetNamed("PawnControl_StatStub", false);

            if (hediffDef == null)
            {
                Log.Error("[PawnControl] CRITICAL ERROR: PawnControl_StatStub HediffDef not found in DefDatabase!");
            }
            else
            {
                Log.Message($"[PawnControl] PawnControl_StatStub HediffDef found in DefDatabase: {hediffDef.defName}");

                // Check if it has the required components
                if (hediffDef.comps == null || !hediffDef.comps.Any(c => c is HediffCompProperties_StatBridge))
                {
                    Log.Error("[PawnControl] PawnControl_StatStub HediffDef is missing HediffCompProperties_StatBridge!");
                }

                if (hediffDef.stages == null || hediffDef.stages.Count == 0 || hediffDef.stages[0].statOffsets == null)
                {
                    Log.Error("[PawnControl] PawnControl_StatStub HediffDef has no stages or statOffsets!");
                }
            }
        }
    }
}
