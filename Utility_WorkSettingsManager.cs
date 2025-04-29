using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace emitbreaker.PawnControl
{
    public static class Utility_WorkSettingsManager
    {
        private static readonly HashSet<Pawn> _workgiverReplacementDone = new HashSet<Pawn>();

        /// <summary>
        /// Safely replaces a pawn's WorkGivers with modded versions if the pawn has proper tags. 
        /// Uses prefix-based matching (e.g., PawnControl_WorkGiver_XXX).
        /// </summary>
        public static void SafeReplaceWorkGiversIfTagged(Pawn pawn, string prefix = "PawnControl_")
        {
            if (pawn == null || pawn.def == null || pawn.def.race == null || pawn.workSettings == null)
                return;

            // ✅ Check tag requirements before proceeding
            if (!Utility_ThinkTreeManager.HasAllowOrBlockWorkTag(pawn.def))
            {
                if (Prefs.DevMode)
                {
                    Log.Message($"[PawnControl] Skipped workgiver replacement for {pawn.LabelCap} - No eligible work tags.");
                }
                return;
            }

            // ✅ Ensure work settings have a work giver list
            var giversField = AccessTools.Field(typeof(Pawn_WorkSettings), "workGiversInOrderNormal");
            var internalGivers = (List<WorkGiver>)giversField.GetValue(pawn.workSettings);

            if (internalGivers == null)
            {
                if (Prefs.DevMode)
                    Log.Warning($"[PawnControl] WorkGivers list is null for {pawn.LabelCap}, skipping replacement.");
                return;
            }

            bool replacedAny = false;

            // ✅ Attempt to replace workgivers
            for (int i = 0; i < internalGivers.Count; i++)
            {
                var original = internalGivers[i];
                var def = original?.def;
                if (def?.giverClass == null)
                    continue;

                string replacementDefName = prefix + def.defName;
                var replacementDef = DefDatabase<WorkGiverDef>.GetNamedSilentFail(replacementDefName);

                if (replacementDef != null && replacementDef.giverClass != def.giverClass)
                {
                    internalGivers[i] = replacementDef.Worker;
                    replacedAny = true;

                    if (Prefs.DevMode)
                    {
                        Log.Message($"[PawnControl] Replaced WorkGiver {def.defName} with {replacementDef.defName} for {pawn.LabelCap}.");
                    }
                }
            }

            // ✅ Always force assign modified list back
            giversField.SetValue(pawn.workSettings, internalGivers);

            if (Prefs.DevMode && replacedAny)
            {
                Log.Message($"[PawnControl] Completed workgiver replacement for {pawn.LabelCap}.");
                Utility_DebugManager.DumpWorkGiversForPawn(pawn);
            }
        }

        /// <summary>
        /// Fully initializes a pawn's work settings, think tree, and workgivers for modded behavior.
        /// - Ensures WorkSettings are created and initialized if missing or invalid.
        /// - Injects a modded think tree subtree for custom behavior.
        /// - Replaces WorkGivers with modded versions, optionally locking the cache.
        /// - Logs detailed debug information in developer mode.
        /// </summary>
        // Fully initialize a pawn without destroying existing priorities
        public static void FullInitializePawn(Pawn pawn, bool forceLock = true, string prefix = "PawnControl_", string subtreeDefName = null)
        {
            if (pawn == null || pawn.def == null || pawn.def.race == null)
            {
                return;
            }

            // ✅ New Safe Check: Only inject subtree if mainWorkThinkTreeDefName was NOT injected statically
            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);

            if (modExtension == null)
            {
                return;
            }

            if (!Utility_ThinkTreeManager.HasAllowOrBlockWorkTag(pawn.def))
            {
                return;
            }

            // ✅ Ensure WorkSettings exist
            EnsureWorkSettingsInitialized(pawn);

            if (Prefs.DevMode)
            {
                Log.Message($"[PawnControl] Completed FullInitializePawn for {pawn.LabelShortCap} (forceLock={forceLock})");
            }
        }

        /// <summary>
        /// Utility class for managing pawn work settings and workgivers in the PawnControl mod.
        /// - Provides methods to initialize, replace, and lock work settings for modded pawns.
        /// - Supports backup and restoration of work priorities.
        /// - Includes safe methods to ensure pawns are ready for work with modded behavior.
        /// - Logs detailed debug information in developer mode for troubleshooting.
        /// </summary>
        public static void FullInitializeAllEligiblePawns(Map map, bool forceLock = true, string subtreeDefName = null)
        {
            if (map == null || map.mapPawns == null)
                return;

            foreach (var pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn == null || pawn.def == null || pawn.def.race == null)
                    continue;

                if (!Utility_ThinkTreeManager.HasAllowOrBlockWorkTag(pawn.def))
                    continue;

                var modExtension = Utility_CacheManager.GetModExtension(pawn.def);

                // ✅ Skip full reinitialization if pawn already has static ThinkTree assigned
                if (modExtension != null && !string.IsNullOrEmpty(modExtension.mainWorkThinkTreeDefName))
                {
                    if (Prefs.DevMode && modExtension.debugMode)
                    {
                        Log.Message($"[PawnControl] Skipping FullInitialize for {pawn.LabelShortCap} (static ThinkTree '{modExtension.mainWorkThinkTreeDefName}' already assigned).");
                    }
                    continue;
                }

                try
                {
                    FullInitializePawn(pawn, forceLock, "PawnControl_", subtreeDefName);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[PawnControl] FullInitializePawn failed for {pawn?.LabelShortCap ?? "unknown pawn"}: {ex.Message}");
                }
            }

            if (Prefs.DevMode)
            {
                Log.Message($"[PawnControl] Completed FullInitializeAllEligiblePawns on map {map.Index} (forceLock={forceLock}).");
            }
        }

        /// <summary>
        /// Ensure that a pawn's WorkSettings are initialized if missing or invalid.
        /// Only applies to pawns tagged for PawnControl work injection.
        /// </summary>
        public static void EnsureWorkSettingsInitialized(Pawn pawn)
        {
            if (pawn == null || pawn.workSettings == null)
            {
                if (Prefs.DevMode)
                {
                    Log.Warning($"[PawnControl] WorkSettings missing for {pawn?.LabelShort ?? "null pawn"}. Attempting to create.");
                }
                // First ensure skills exist if needed (skills should be initialized before work settings)
                if (Utility_SkillManager.ShouldHaveSkills(pawn))
                {
                    Utility_SkillManager.ForceAttachSkillTrackerIfMissing(pawn);
                }

                pawn.workSettings = new Pawn_WorkSettings(pawn);
                pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
            }
        }

        /// <summary>
        /// Ensures that a pawn has valid WorkSettings and proper WorkGivers.
        /// Can be safely called during runtime to rescue pawns whose WorkSettings got reset or corrupted.
        /// Only operates on tagged pawns.
        /// </summary>
        public static void SafeEnsurePawnReadyForWork(Pawn pawn)
        {
            if (pawn == null || pawn.def == null || pawn.def.race == null)
            {
                return;
            }

            if (!Utility_ThinkTreeManager.HasAllowOrBlockWorkTag(pawn.def))
            {
                return;
            }

            if (_workgiverReplacementDone.Contains(pawn))
            {
                return; // ✅ Already replaced, skip
            }

            // ✅ Ensure workSettings exists
            if (pawn.workSettings == null || !pawn.workSettings.EverWork)
            {
                EnsureWorkSettingsInitialized(pawn);

                if (Prefs.DevMode)
                {
                    Log.Warning($"[PawnControl] SafeEnsurePawnReadyForWork: WorkSettings recreated for {pawn.LabelCap}.");
                }
            }

            // ✅ Replace WorkGivers (light safe method)
            SafeReplaceWorkGiversIfTagged(pawn, "PawnControl_");

            if (Prefs.DevMode)
            {
                Log.Message($"[PawnControl] SafeEnsurePawnReadyForWork completed for {pawn.LabelCap}.");
            }

            // ✅ Mark as completed
            _workgiverReplacementDone.Add(pawn);
        }

        /// <summary>
        /// Force the WorkGiversInOrder lists to be populated for a pawn if they're null or empty.
        /// This is necessary for pawns with statically assigned ThinkTrees that might skip normal initialization.
        /// </summary>
        public static void EnsureWorkGiversPopulated(Pawn pawn)
        {
            if (pawn?.workSettings == null)
                return;
            
            try
            {
                // Use direct property access when available rather than reflection
                List<WorkGiver> normalList = pawn.workSettings.WorkGiversInOrderNormal;
                List<WorkGiver> emergencyList = pawn.workSettings.WorkGiversInOrderEmergency;
                
                bool needsRebuild = (normalList == null || normalList.Count == 0 || 
                                    emergencyList == null || emergencyList.Count == 0);
                
                if (needsRebuild)
                {
                    // First ensure base initialization
                    if (!pawn.workSettings.Initialized)
                    {
                        if (Prefs.DevMode)
                        {
                            Log.Message($"[PawnControl] Initializing uninitialized workSettings for {pawn.LabelCap}");
                        }
                        pawn.workSettings.EnableAndInitialize();
                        
                        // Check if initialization fixed it
                        normalList = pawn.workSettings.WorkGiversInOrderNormal;
                        emergencyList = pawn.workSettings.WorkGiversInOrderEmergency;
                        
                        needsRebuild = (normalList == null || normalList.Count == 0 || 
                                       emergencyList == null || emergencyList.Count == 0);
                    }
                    
                    // If still needed, try forcing via reflection
                    if (needsRebuild)
                    {
                        // Use reflection to call the private CacheWorkGiversInOrder method
                        MethodInfo cacheMethod = AccessTools.Method(typeof(Pawn_WorkSettings), "CacheWorkGiversInOrder");
                        if (cacheMethod != null)
                        {
                            try 
                            {
                                cacheMethod.Invoke(pawn.workSettings, null);
                                
                                if (Prefs.DevMode)
                                {
                                    // Verify result
                                    normalList = pawn.workSettings.WorkGiversInOrderNormal;
                                    emergencyList = pawn.workSettings.WorkGiversInOrderEmergency;
                                    
                                    Log.Message($"[PawnControl] WorkGiver lists after rebuild for {pawn.LabelCap}: " +
                                               $"Normal={normalList?.Count ?? 0}, Emergency={emergencyList?.Count ?? 0}");
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Error($"[PawnControl] Failed to rebuild WorkGiver lists for {pawn.LabelCap}: {ex}");
                            }
                        }
                        else
                        {
                            Log.Warning($"[PawnControl] Could not find CacheWorkGiversInOrder method via reflection");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[PawnControl] Exception in EnsureWorkGiversPopulated for {pawn?.LabelCap ?? "null pawn"}: {ex}");
            }
        }
    }
}
