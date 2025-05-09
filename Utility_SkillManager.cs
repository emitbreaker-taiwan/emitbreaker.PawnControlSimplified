﻿using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace emitbreaker.PawnControl
{
    public static class Utility_SkillManager
    {
        public static bool NeedsStatInjection(Pawn pawn)
        {
            if (pawn == null || pawn.def == null || pawn.health == null || pawn.health.hediffSet == null)
                return false;

            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null)
            {
                return false;
            }

            // Already injected? Then skip.
            return !pawn.health.hediffSet.HasHediff(HediffDef.Named("PawnControl_StatStub"));
        }

        /// <summary>
        /// Safely simulate a skill for a non-humanlike pawn.
        /// Uses per-pawn and per-skill caching. Compatible with mod extension overrides.
        /// </summary>
        public static int SetInjectedSkillLevel(Pawn pawn, SkillDef skill = null)
        {
            if (pawn == null || pawn.def == null)
            {
                return 0;
            }

            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);

            if (modExtension == null)
            {
                return 0; // No mod extension, no skill level
            }

            if (modExtension != null && modExtension.baseSkillLevelOverride.HasValue)
            {
                return modExtension.baseSkillLevelOverride.Value;
            }

            var race = pawn.RaceProps;
            if (race == null)
            {
                return 0;
            }

            if (race.Humanlike)
            {
                SkillRecord record = pawn.skills.GetSkill(skill);
                return CalculateInjectedSkillLevel(pawn, modExtension, skill, record);
            }

            if (race.IsMechanoid || race.ToolUser)
            {
                // Try to attach real skills if missing and it's appropriate
                if (pawn.skills == null && ShouldHaveSkills(pawn))
                {
                    ForceAttachSkillTrackerIfMissing(pawn);
                    SkillRecord record = pawn.skills.GetSkill(skill);
                    return CalculateInjectedSkillLevel(pawn, modExtension, skill, record);
                }
            }

            if (race.Animal)
            {
                // Try to attach real skills if missing and it's appropriate
                if (pawn.skills == null && ShouldHaveSkills(pawn))
                {
                    ForceAttachSkillTrackerIfMissing(pawn);
                    SkillRecord record = pawn.skills.GetSkill(skill);
                    return CalculateInjectedSkillLevel(pawn, modExtension, skill, record);
                }
            }

            return 0; // Generic fallback
        }

        private static int CalculateInjectedSkillLevel(Pawn pawn, NonHumanlikePawnControlExtension modExtension, SkillDef skill, SkillRecord record)
        {
            if (pawn == null || pawn.RaceProps == null || modExtension == null)
            {
                return 0;
            }

            RaceProperties raceProps = pawn.RaceProps;

            // First check for global override
            if (modExtension?.baseSkillLevelOverride.HasValue == true)
            {
                return modExtension.baseSkillLevelOverride.Value;
            }

            // Then check for existing skill record
            else if (record != null && record.levelInt > 0)
            {
                return (int)record.Level;
            }

            // Check for specific skill in injected skills
            else if (skill != null && modExtension.injectedSkills != null && modExtension.injectedSkills.Count > 0)
            {
                foreach (var entry in modExtension.injectedSkills)
                {
                    if (Utility_Common.SkillDefNamed(entry.skill) == skill)
                    {
                        return entry.level;
                    }
                }

                // NEW: Check if SimulatedSkillDict is already populated and use that
                if (modExtension.SimulatedSkillDict != null &&
                    modExtension.SimulatedSkillDict.TryGetValue(skill, out int level))
                {
                    return level;
                }
            }

            // For general skill checks (when no specific skill is requested)
            // First check if the dictionary is populated at all
            else if (skill == null && modExtension.injectedSkills != null && modExtension.injectedSkills.Count > 0)
            {
                // If no specific skill requested, use the average or max of all injected skills
                if (modExtension.SimulatedSkillDict != null && modExtension.SimulatedSkillDict.Count > 0)
                {
                    return (int)modExtension.SimulatedSkillDict.Values.Average();
                }

                // Fallback to first skill in the list if dictionary not populated
                return modExtension.injectedSkills[0].level;
            }

            // Traditional race-based fallbacks
            else if (raceProps.Humanlike)
            {
                return skill != null ? (int)pawn.skills?.GetSkill(skill)?.Level : 0;
            }
            else if (raceProps.ToolUser)
            {
                return modExtension.skillLevelToolUser > 0 ? modExtension.skillLevelToolUser : 10;
            }
            else if (raceProps.Animal)
            {
                if (raceProps.trainability == TrainabilityDefOf.Advanced)
                {
                    return modExtension.skillLevelAnimalAdvanced > 0 ? modExtension.skillLevelAnimalAdvanced : 5;
                }
                if (raceProps.trainability == TrainabilityDefOf.Intermediate)
                {
                    return modExtension.skillLevelAnimalIntermediate > 0 ? modExtension.skillLevelAnimalIntermediate : 3;
                }
                else
                {
                    return modExtension.skillLevelAnimalBasic > 0 ? modExtension.skillLevelAnimalBasic : 3;
                }
            }

            return 0; // Fallback to default skill level
        }

        public static Passion SetInjectedPassion(ThingDef def, SkillDef skillDef)
        {
            var modExtension = Utility_CacheManager.GetModExtension(def);
            if (modExtension?.SkillPassionDict == null)
            {
                return Passion.None;
            }

            if (modExtension.SkillPassionDict.TryGetValue(skillDef, out var passion))
            {
                Utility_DebugManager.LogNormal($"Injected {passion} passion for {skillDef.defName} in {def.defName}.");
                return passion;
            }

            return Passion.None;
        }

        /// <summary>
        /// Forcibly attaches a real SkillTracker and initializes skills for non-humanlike pawns.
        /// </summary>
        public static void ForceAttachSkillTrackerIfMissing(Pawn pawn)
        {
            if (pawn == null || pawn.skills != null)
            {
                return; // Already has skills, no need to modify
            }

            try
            {
                // Before skill injection
                if (pawn.story == null)
                {
                    pawn.story = new Pawn_StoryTracker(pawn);
                }

                if (pawn.story.traits == null)
                {
                    pawn.story.traits = new TraitSet(pawn);
                }

                pawn.skills = new Pawn_SkillTracker(pawn);

                foreach (var skillDef in DefDatabase<SkillDef>.AllDefsListForReading)
                {
                    var skillRecord = new SkillRecord(pawn, skillDef);

                    // Set base level and passion
                    skillRecord.Level = SetInjectedSkillLevel(pawn, skillDef);
                    skillRecord.passion = SetInjectedPassion(pawn.def, skillDef);

                    // Mandatory Vanilla fields initialization
                    skillRecord.xpSinceLastLevel = 0f;
                    skillRecord.xpSinceMidnight = 0f;

                    // Manually register into skills list
                    pawn.skills.skills.Add(skillRecord);
                }

                // Add this after skills are attached
                // Update work settings to match the new skills if they already exist
                if (pawn.workSettings != null)
                {
                    // Notify the work settings that skill capabilities may have changed
                    pawn.workSettings.Notify_DisabledWorkTypesChanged();
                }

                Utility_DebugManager.LogNormal($"Attached real SkillTracker to {pawn.LabelCap} ({pawn.def.defName}).");
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Failed to attach SkillTracker: {ex}");
            }
        }

        /// <summary>
        /// Ensures that all non-humanlike pawns that should have skills get proper skill trackers.
        /// </summary>
        public static void AttachSkillTrackersToPawnsSafely()
        {
            try
            {
                // Only run if a game is loaded
                if (Current.Game == null)
                    return;

                int attachedCount = 0;
                int statInjectionCount = 0;

                Utility_DebugManager.LogNormal($"Beginning skill and stat injection process...");

                // Process all pawns in all maps
                foreach (var map in Current.Game.Maps)
                {
                    foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                    {
                        // Skip pawns that already have skills, are dead, or don't need skills
                        if (pawn == null || pawn.Dead || pawn.Destroyed)
                        {
                            continue;
                        }

                        var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                        if (modExtension == null)
                        {
                            continue; // Skip if no mod extension is found
                        }

                        // Attach skill tracker if this pawn should be able to do work
                        if (ShouldHaveSkills(pawn))
                        {
                            // Check if we need to inject skills
                            if (pawn.skills == null)
                            {
                                ForceAttachSkillTrackerIfMissing(pawn);
                                attachedCount++;
                            }

                            // Now ensure stats are properly injected (always try, even if skills already exist)
                            try
                            {
                                Utility_DebugManager.LogNormal($"Attempting stat injection for {pawn.LabelShort} ({pawn.def.defName})");
                                ForceAttachSkillTrackerIfMissing(pawn);
                                Utility_StatManager.InjectConsolidatedStatHediff(pawn);
                                Utility_WorkSettingsManager.EnsureWorkSettingsInitialized(pawn); // ✅ centralized
                                statInjectionCount++;
                            }
                            catch (Exception ex)
                            {
                                Utility_DebugManager.LogError($"Error during stat injection for {pawn.LabelShort}: {ex}");
                            }
                        }
                    }
                }

                // Process world pawns too (caravans, etc.)
                if (Find.WorldPawns != null)
                {
                    foreach (var pawn in Find.WorldPawns.AllPawnsAliveOrDead)
                    {
                        // Skip pawns that already have skills, are dead, or don't need skills
                        if (pawn == null || pawn.Dead || pawn.Destroyed)
                        {
                            continue;
                        }

                        var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                        if (modExtension == null)
                        {
                            continue; // Skip if no mod extension is found
                        }

                        // Attach skill tracker if this pawn should be able to do work
                        if (ShouldHaveSkills(pawn))
                        {
                            // Check if we need to inject skills
                            if (pawn.skills == null)
                            {
                                ForceAttachSkillTrackerIfMissing(pawn);
                                attachedCount++;
                            }

                            // Now ensure stats are properly injected (always try, even if skills already exist)
                            try
                            {
                                Utility_DebugManager.LogNormal($"Attempting stat injection for {pawn.LabelShort} ({pawn.def.defName})");
                                ForceAttachSkillTrackerIfMissing(pawn);
                                Utility_StatManager.InjectConsolidatedStatHediff(pawn);
                                Utility_WorkSettingsManager.EnsureWorkSettingsInitialized(pawn); // ✅ centralized
                                statInjectionCount++;
                            }
                            catch (Exception ex)
                            {
                                Utility_DebugManager.LogError($"Error during stat injection for {pawn.LabelShort}: {ex}");
                            }
                        }
                    }
                }

                if ((attachedCount > 0 || statInjectionCount > 0))
                {
                    Utility_DebugManager.LogNormal($"Process complete: Attached skill trackers to {attachedCount} pawns, injected stats for {statInjectionCount} pawns.");
                }
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error in AttachSkillTrackersToPawnsSafely: {ex}");
            }
        }

        /// <summary>
        /// Determines if a pawn should have skills attached based on its characteristics.
        /// </summary>

        public static bool ShouldHaveSkills(Pawn pawn)
        {
            // Skip null pawns or those without valid race properties
            if (pawn?.RaceProps == null || pawn.def == null)
            {
                return false;
            }

            if (pawn.RaceProps.Humanlike)
            {
                return false;
            }

            // Check for any managed extension that indicates this pawn should work
            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null)
            {
                return false;
            }

            if (Utility_TagManager.WorkEnabled(pawn.def, ManagedTags.AllowAllWork))
            {
                Utility_DebugManager.LogNormal($"Injected all skills to {pawn.LabelShort}.");
                return true;
            }
            else if(Utility_TagManager.WorkEnabled(pawn.def, ManagedTags.BlockAllWork))
            {
                Utility_DebugManager.LogNormal($"Blocked all skills to {pawn.LabelShort}.");
                return false;
            }
            else
            {
                if (Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_Construction.ToString()))
                {
                    Utility_DebugManager.LogNormal($"Injected {SkillDefOf.Construction.label} to {pawn.LabelShort}.");
                    return true;
                }
                if (Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_Growing.ToString()) || Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_PlantCutting.ToString()))
                {
                    Utility_DebugManager.LogNormal($"Injected {SkillDefOf.Plants.label} to {pawn.LabelShort}.");
                    return true;
                }
                if (Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_Research.ToString()) || Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_DarkStudy.ToString()))
                {
                    Utility_DebugManager.LogNormal($"Injected {SkillDefOf.Intellectual.label} to {pawn.LabelShort}.");
                    return true;
                }
                if (Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_Mining.ToString()))
                {
                    Utility_DebugManager.LogNormal($"Injected {SkillDefOf.Mining.label} to {pawn.LabelShort}.");
                    return true;
                }
                if (Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_Hunting.ToString()))
                {
                    Utility_DebugManager.LogNormal($"Injected {SkillDefOf.Shooting.label} to {pawn.LabelShort}.");
                    return true;
                }
                //if (Utility_TagManager.WorkEnabled(pawn.def, ManagedTags.AllowWorkPrefix + SkillDefOf.Melee.defName))
                //{
                //    Utility_DebugManager.LogNormal($"[PawnControl] Injected {SkillDefOf.Melee.label} to {pawn.LabelShort}.");
                //    return true;
                //}
                if (Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_Warden.ToString()) || Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_Childcare.ToString()))
                {
                    Utility_DebugManager.LogNormal($"Injected {SkillDefOf.Social.label} to {pawn.LabelShort}.");
                    return true;
                }
                if (Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_Hunting.ToString()) || Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_Handling.ToString()))
                {
                    Utility_DebugManager.LogNormal($"Injected {SkillDefOf.Animals.label} to {pawn.LabelShort}.");
                    return true;
                }
                if (Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_Cooking.ToString()))
                {
                    Utility_DebugManager.LogNormal($"Injected {SkillDefOf.Cooking.label} to {pawn.LabelShort}.");
                    return true;
                }
                if (Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_Doctor.ToString()))
                {
                    Utility_DebugManager.LogNormal($"Injected {SkillDefOf.Medicine.label} to {pawn.LabelShort}.");
                    return true;
                }
                if (Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_Art.ToString()))
                {
                    Utility_DebugManager.LogNormal($"Injected {SkillDefOf.Artistic.label} to {pawn.LabelShort}.");
                    return true;
                }
                if (Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_Crafting.ToString()) || Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_Smithing.ToString()) || Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_Tailoring.ToString()))
                {
                    Utility_DebugManager.LogNormal($"Injected {SkillDefOf.Crafting.label} to {pawn.LabelShort}.");
                    return true;
                }
            }

            // Default: don't attach skills to standard non-humanlike pawns
            return false;
        }

        public static float GetWorkPrioritySortingValue(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn == null || workType == null)
                return -2f;

            if (pawn.workSettings == null || !pawn.workSettings.EverWork)
                return -2f;

            if (pawn.WorkTypeIsDisabled(workType))
                return -1f;

            // Simulate for non-humanlike
            if (!pawn.RaceProps?.Humanlike ?? true)
            {
                return SetInjectedSkillLevel(pawn);
            }

            // Use real value if available
            return pawn.skills?.AverageOfRelevantSkillsFor(workType) ?? 0f;
        }

        // Scoped fallback for PawnColumnWorker sorting/priority only
        public static float SafeAverageOfRelevantSkillsFor(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn?.skills != null)
                return pawn.skills.AverageOfRelevantSkillsFor(workType);

            return SetInjectedSkillLevel(pawn);
        }
    }
}
