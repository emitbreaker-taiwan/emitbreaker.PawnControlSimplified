using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace emitbreaker.PawnControl
{
    public static class Utility_WorkSettingsManager
    {
        private static readonly HashSet<Pawn> _workgiverReplacementDone = new HashSet<Pawn>();

        /// <summary>
        /// Fully initializes a pawn's work settings, think tree, and workgivers for modded behavior.
        /// - Ensures WorkSettings are created and initialized if missing or invalid.
        /// - Injects a modded think tree subtree for custom behavior.
        /// - Replaces WorkGivers with modded versions, optionally locking the cache.
        /// - Logs detailed debug information in developer mode.
        /// </summary>
        public static void InitializePawnWorksetting(Pawn pawn, bool forceLock = true, string subtreeDefName = null)
        {
            if (!Utility_Common.PawnChecker(pawn))
            {
                return;
            }

            // ✅ New Safe Check: Only inject subtree if mainWorkThinkTreeDefName was NOT injected statically
            var modExtension = Utility_UnifiedCache.GetModExtension(pawn.def);

            if (modExtension == null)
            {
                return;
            }

            if (!Utility_ThinkTreeManager.HasAllowOrBlockWorkTag(pawn))
            {
                return;
            }

            // ✅ Ensure WorkSettings exist
            EnsureWorkSettingsInitialized(pawn);
            EnsureWorkGiversPopulated(pawn);

            Utility_DebugManager.LogNormal($"Completed FullInitializePawn for {pawn.LabelShortCap} (forceLock={forceLock})");

            // Add at the end of the method
            if (Prefs.DevMode && modExtension.debugMode && pawn?.thinker?.MainThinkNodeRoot != null)
            {
                Utility_ThinkTreeManager.ValidateThinkTree(pawn);
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
            {
                return;
            }

            foreach (var pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn == null || pawn.def == null || pawn.def.race == null)
                    continue;

                if (!Utility_ThinkTreeManager.HasAllowOrBlockWorkTag(pawn))
                    continue;

                var modExtension = Utility_UnifiedCache.GetModExtension(pawn.def);

                if (modExtension == null)
                    continue; // No mod extension found, nothing to do

                // ✅ Skip full reinitialization if pawn already has static ThinkTree assigned
                if (modExtension != null && !string.IsNullOrEmpty(modExtension.mainWorkThinkTreeDefName))
                {
                    if (Utility_DebugManager.ShouldLogDetailed())
                        Utility_DebugManager.LogNormal($"Skipping InitializePawnWorksetting for {pawn.LabelShortCap} (static ThinkTree '{modExtension.mainWorkThinkTreeDefName}' already assigned).");
                    continue;
                }

                try
                {
                    InitializePawnWorksetting(pawn, forceLock, subtreeDefName);
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogWarning($"InitializePawnWorksetting failed for {pawn?.LabelShortCap ?? "unknown pawn"}: {ex.Message}");
                }
            }

            Utility_DebugManager.LogNormal($"Completed FullInitializeAllEligiblePawns on map {map.Index} (forceLock={forceLock}).");
        }

        /// <summary>
        /// Ensure that a pawn's WorkSettings are initialized if missing or invalid.
        /// Only applies to pawns tagged for PawnControl work injection.
        /// </summary>
        public static void EnsureWorkSettingsInitialized(Pawn pawn)
        {
            if (pawn == null || pawn.workSettings == null)
            {
                var modExtension = Utility_UnifiedCache.GetModExtension(pawn?.def);
                if (modExtension == null)
                {
                    return; // No mod extension found, nothing to do
                }

                Utility_DebugManager.LogWarning($"WorkSettings missing for {pawn?.LabelShort ?? "null pawn"}. Attempting to create.");

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

            var modExtension = Utility_UnifiedCache.GetModExtension(pawn.def);
            if (modExtension == null)
            {
                return; // No mod extension found, nothing to do
            }

            if (!Utility_ThinkTreeManager.HasAllowOrBlockWorkTag(pawn))
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

                Utility_DebugManager.LogWarning($"SafeEnsurePawnReadyForWork: WorkSettings recreated for {pawn.LabelCap}.");
            }
            
            Utility_DebugManager.LogNormal($"SafeEnsurePawnReadyForWork completed for {pawn.LabelCap}.");

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
            {
                return;
            }
            var modExtension = Utility_UnifiedCache.GetModExtension(pawn.def);
            if (modExtension == null)
            {
                return; // No mod extension found, nothing to do
            }

            try
            {
                // Check if work lists are empty
                List<WorkGiver> normalList = pawn.workSettings.WorkGiversInOrderNormal;
                List<WorkGiver> emergencyList = pawn.workSettings.WorkGiversInOrderEmergency;
                
                bool needsRebuild = (normalList == null || normalList.Count == 0 || 
                                   emergencyList == null || emergencyList.Count == 0);
                
                if (needsRebuild)
                {
                    // First try regular initialization
                    if (!pawn.workSettings.Initialized)
                    {
                        Utility_DebugManager.LogWarning($"WorkSettings not initialized for {pawn.LabelCap}. Initializing...");
                        pawn.workSettings.EnableAndInitialize();
                    }
                    
                    // If still empty, force rebuild via reflection
                    normalList = pawn.workSettings.WorkGiversInOrderNormal;
                    emergencyList = pawn.workSettings.WorkGiversInOrderEmergency;
                    
                    if (normalList == null || normalList.Count == 0 || 
                        emergencyList == null || emergencyList.Count == 0)
                    {
                        Utility_DebugManager.LogWarning($"WorkGiver lists still empty after initialization for {pawn.LabelCap}. Forcing rebuild...");
                        // Force rebuild via reflection
                        MethodInfo cacheMethod = AccessTools.Method(typeof(Pawn_WorkSettings), "CacheWorkGiversInOrder");
                        if (cacheMethod != null)
                        {
                            cacheMethod.Invoke(pawn.workSettings, null);
                            Utility_DebugManager.LogNormal($"Forced WorkGiver cache rebuild for {pawn.LabelCap}");

                            // Add all WorkGivers manually if still empty
                            normalList = pawn.workSettings.WorkGiversInOrderNormal;
                            if (normalList == null || normalList.Count == 0)
                            {
                                Utility_DebugManager.LogWarning($"Creating new WorkGiver lists for {pawn.LabelCap}");

                                // Create new lists if needed
                                var normalField = AccessTools.Field(typeof(Pawn_WorkSettings), "workGiversInOrderNormal");
                                var emergencyField = AccessTools.Field(typeof(Pawn_WorkSettings), "workGiversInOrderEmergency");
                                
                                if (normalField != null && emergencyField != null)
                                {
                                    var newNormalList = new List<WorkGiver>();
                                    var newEmergencyList = new List<WorkGiver>();
                                    
                                    // Populate with default work givers
                                    foreach (WorkGiverDef def in DefDatabase<WorkGiverDef>.AllDefsListForReading)
                                    {
                                        if (pawn.workSettings.GetPriority(def.workType) > 0)
                                        {
                                            newNormalList.Add(def.Worker);
                                            if (def.emergency)
                                                newEmergencyList.Add(def.Worker);
                                        }
                                    }
                                    
                                    normalField.SetValue(pawn.workSettings, newNormalList);
                                    emergencyField.SetValue(pawn.workSettings, newEmergencyList);
                                    Utility_DebugManager.LogNormal($"Created new WorkGiver lists with {newNormalList.Count} normal and {newEmergencyList.Count} emergency givers");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Exception in EnsureWorkGiversPopulated: {ex}");
            }
        }
    }
}