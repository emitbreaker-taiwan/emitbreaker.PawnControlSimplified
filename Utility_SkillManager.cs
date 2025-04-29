using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace emitbreaker.PawnControl
{
    public static class Utility_SkillManager
    {
        // Scoped fallback for PawnColumnWorker sorting/priority only
        public static float SafeAverageOfRelevantSkillsFor(Pawn pawn, WorkTypeDef workType)
        {
            // First try to attach real skills if missing
            if (pawn != null && pawn.skills == null)
            {
                ForceAttachSkillTrackerIfMissing(pawn);
            }

            // Now try to use the real skills
            if (pawn?.skills != null)
            {
                return pawn.skills.AverageOfRelevantSkillsFor(workType);
            }

            return SetInjectedSkillLevel(pawn);
        }

        //Skill level simulation
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

            if (modExtension != null && modExtension.baseSkillLevelOverride.HasValue)
            {
                return modExtension.baseSkillLevelOverride.Value;
            }

            var race = pawn.RaceProps;
            if (race == null)
            {
                return 0;
            }

            if (race.Humanlike && modExtension == null)
            {
                return skill != null ? (int)pawn.skills?.GetSkill(skill)?.Level : 0; // fallback to real
            }

            if (race.IsMechanoid || race.ToolUser)
            {
                if (modExtension != null)
                {
                    // Try to attach real skills if missing and it's appropriate
                    if (pawn.skills == null && ShouldHaveSkills(pawn))
                    {
                        ForceAttachSkillTrackerIfMissing(pawn);

                        // If skills were attached and requested skill exists, use the real value
                        if (pawn.skills != null && skill != null)
                        {
                            SkillRecord record = pawn.skills.GetSkill(skill);
                            if (record != null)
                            {
                                return (int)record.Level;
                            }
                        }
                    }
                }
                return 10;
            }

            if (race.Animal)
            {
                if (modExtension != null)
                {
                    // Try to attach real skills if missing and it's appropriate
                    if (pawn.skills == null && ShouldHaveSkills(pawn))
                    {
                        ForceAttachSkillTrackerIfMissing(pawn);

                        // If skills were attached and requested skill exists, use the real value
                        if (pawn.skills != null && skill != null)
                        {
                            SkillRecord record = pawn.skills.GetSkill(skill);
                            if (record != null)
                            {
                                return (int)record.Level;
                            }
                        }
                    }
                }
                if (race.trainability == TrainabilityDefOf.Advanced)
                {
                    return 5;
                }
                if (race.trainability == TrainabilityDefOf.Intermediate)
                {
                    return 3;
                }
                else
                {
                    return 1;
                }
            }

            return 1; // Generic fallback
        }

        public static Passion SetInjectedPassion(ThingDef def, SkillDef skillDef)
        {
            var modExtension = Utility_CacheManager.GetModExtension(def);
            if (modExtension?.skillPassionDict == null)
            {
                return Passion.None;
            }

            if (modExtension.skillPassionDict.TryGetValue(skillDef, out var passion))
            {
                if (Prefs.DevMode)
                {
                    Log.Message($"[PawnControl] Injected {passion} passion for {skillDef.defName} in {def.defName}.");
                }
                return passion;
            }

            return Passion.None;
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

        /// <summary>
        /// Forcibly attaches a real SkillTracker and initializes skills for non-humanlike pawns.
        /// </summary>
        public static void ForceAttachSkillTrackerIfMissing(Pawn pawn)
        {
            if (pawn == null || pawn.skills != null)
                return; // Already has skills, no need to modify

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

                if (Prefs.DevMode)
                {
                    Log.Message($"[PawnControl] Attached real SkillTracker to {pawn.LabelCap} ({pawn.def.defName}).");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[PawnControl] Failed to attach SkillTracker: {ex}");
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

                // Process all pawns in all maps
                foreach (var map in Current.Game.Maps)
                {
                    foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                    {
                        // Skip pawns that already have skills, are dead, or don't need skills
                        if (pawn == null || pawn.Dead || pawn.Destroyed || pawn.skills != null)
                        {
                            continue;
                        }

                        if (Utility_CacheManager.GetModExtension(pawn.def) == null)
                        {
                            continue; // Skip if no mod extension is found
                        }

                        // Attach skill tracker if this pawn should be able to do work
                        if (ShouldHaveSkills(pawn))
                        {
                            ForceAttachSkillTrackerIfMissing(pawn);
                            attachedCount++;
                        }
                    }
                }

                // Process world pawns too (caravans, etc.)
                if (Find.WorldPawns != null)
                {
                    foreach (var pawn in Find.WorldPawns.AllPawnsAliveOrDead)
                    {
                        // Skip pawns that already have skills, are dead, or don't need skills
                        if (pawn == null || pawn.Dead || pawn.Destroyed || pawn.skills != null)
                        {
                            continue;
                        }

                        if (Utility_CacheManager.GetModExtension(pawn.def) == null)
                        {
                            continue; // Skip if no mod extension is found
                        }

                        // Attach skill tracker if this pawn should be able to do work
                        if (ShouldHaveSkills(pawn))
                        {
                            ForceAttachSkillTrackerIfMissing(pawn);
                            attachedCount++;
                        }
                    }
                }

                if (Prefs.DevMode && attachedCount > 0)
                {
                    Log.Message($"[PawnControl] Attached skill trackers to {attachedCount} pawns during startup.");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[PawnControl] Error attaching skill trackers during startup: {ex}");
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

            // Always attach to tool users (like animals with manipulator limbs)
            //if (pawn.RaceProps.ToolUser)
            //{
            //    return true;
            //}

            // Check for any managed extension that indicates this pawn should work
            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null)
            {
                return false;
            }

            if (Utility_TagManager.WorkEnabled(pawn.def, ManagedTags.AllowAllWork))
            {
                if (Prefs.DevMode)
                {
                    Log.Message($"[PawnControl] Injected all skills to {pawn.LabelShort}.");
                }
                return true;
            }
            else if(Utility_TagManager.WorkEnabled(pawn.def, ManagedTags.BlockAllWork))
            {
                if (Prefs.DevMode)
                {
                    Log.Message($"[PawnControl] Blocked all skills to {pawn.LabelShort}.");
                }
                return false;
            }
            else
            {
                if (Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_Construction.ToString()))
                {
                    if (Prefs.DevMode)
                    {
                        Log.Message($"[PawnControl] Injected {SkillDefOf.Construction.label} to {pawn.LabelShort}.");
                    }
                    return true;
                }
                if (Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_Growing.ToString()) || Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_PlantCutting.ToString()))
                {
                    if (Prefs.DevMode)
                    {
                        Log.Message($"[PawnControl] Injected {SkillDefOf.Plants.label} to {pawn.LabelShort}.");
                    }
                    return true;
                }
                if (Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_Research.ToString()) || Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_DarkStudy.ToString()))
                {
                    if (Prefs.DevMode) 
                    {
                        Log.Message($"[PawnControl] Injected {SkillDefOf.Intellectual.label} to {pawn.LabelShort}.");
                    }
                    return true;
                }
                if (Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_Mining.ToString()))
                {
                    if (Prefs.DevMode)
                    {
                        Log.Message($"[PawnControl] Injected {SkillDefOf.Mining.label} to {pawn.LabelShort}.");
                    }
                    return true;
                }
                if (Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_Hunting.ToString()))
                {
                    if (Prefs.DevMode)
                    {
                        Log.Message($"[PawnControl] Injected {SkillDefOf.Shooting.label} to {pawn.LabelShort}.");
                    }
                    return true;
                }
                //if (Utility_TagManager.WorkEnabled(pawn.def, ManagedTags.AllowWorkPrefix + SkillDefOf.Melee.defName))
                //{
                //    if (Prefs.DevMode)
                //    {
                //        Log.Message($"[PawnControl] Injected {SkillDefOf.Melee.label} to {pawn.LabelShort}.");
                //    }
                //    return true;
                //}
                if (Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_Warden.ToString()) || Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_Childcare.ToString()))
                {
                    if (Prefs.DevMode)
                    {
                        Log.Message($"[PawnControl] Injected {SkillDefOf.Social.label} to {pawn.LabelShort}.");
                    }
                    return true;
                }
                if (Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_Hunting.ToString()) || Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_Handling.ToString()))
                {
                    if (Prefs.DevMode)
                    {
                        Log.Message($"[PawnControl] Injected {SkillDefOf.Animals.label} to {pawn.LabelShort}.");
                    }
                    return true;
                }
                if (Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_Cooking.ToString()))
                {
                    if (Prefs.DevMode)
                    {
                        Log.Message($"[PawnControl] Injected {SkillDefOf.Cooking.label} to {pawn.LabelShort}.");
                    }
                    return true;
                }
                if (Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_Doctor.ToString()))
                {
                    if (Prefs.DevMode)
                    {
                        Log.Message($"[PawnControl] Injected {SkillDefOf.Medicine.label} to {pawn.LabelShort}.");
                    }
                    return true;
                }
                if (Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_Art.ToString()))
                {
                    if (Prefs.DevMode)
                    {
                        Log.Message($"[PawnControl] Injected {SkillDefOf.Artistic.label} to {pawn.LabelShort}.");
                    }
                    return true;
                }
                if (Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_Crafting.ToString()) || Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_Smithing.ToString()) || Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_Tailoring.ToString()))
                {
                    if (Prefs.DevMode)
                    {
                        Log.Message($"[PawnControl] Injected {SkillDefOf.Crafting.label} to {pawn.LabelShort}.");
                    }
                    return true;
                }
            }

            // Default: don't attach skills to standard non-humanlike pawns
            return false;
        }
    }
}
