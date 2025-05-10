using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Verse.Noise;
using static emitbreaker.PawnControl.HarmonyPatches;
using static System.Net.Mime.MediaTypeNames;

namespace emitbreaker.PawnControl
{
    public class GameComponent_LateInjection : GameComponent
    {
        private bool drafterAlreadyInjected = false;
        private bool cleanupDone = false;

        public GameComponent_LateInjection(Game game) 
        {
        }

        public override void GameComponentTick()
        {
            if (drafterAlreadyInjected && Utility_IdentityManager.identityFlagsPreloaded && cleanupDone)
            {
                return;
            }

            if (Find.TickManager.TicksGame < 200)
            {
                return;
            }

            if (!Utility_IdentityManager.identityFlagsPreloaded)
            {

            }
            if (!drafterAlreadyInjected)
            {
                InjectDraftersSafely();
            }
        }

        /// <summary>
        /// Called when game is being saved.
        /// </summary>
        public override void GameComponentOnGUI()
        {
            base.GameComponentOnGUI();

            // This is a good spot to ensure cache is clean before saving
            JobGiver_WorkNonHumanlike.ResetCache();
        }

        /// <summary>
        /// Called when a game is loaded.
        /// </summary>
        public override void FinalizeInit()
        {
            base.FinalizeInit();

            // Process any pending removals first
            ProcessPendingRemovals();

            // Restore runtime mod extensions from this save file
            RestoreRuntimeModExtensions();

            // Ensure all modded pawns have their stat injections applied
            Utility_StatManager.CheckStatHediffDefExists();

            // Clear debug patch caches
            HarmonyPatches.Patch_Debuggers.Patch_ThinkNode_PrioritySorter_DebugJobs.ResetCache();
            HarmonyPatches.Patch_Debuggers.Patch_Pawn_SpawnSetup_DumpThinkTree.ResetCache();

            // Reset caches to ensure no stale data is present
            ResetAllCache();

            // Explicitly invalidate the colonist-like cache for all maps
            if (Find.Maps != null)
            {
                foreach (Map map in Find.Maps)
                {
                    Utility_CacheManager.InvalidateColonistLikeCache(map);
                }
            }
        }

        private void ProcessPendingRemovals()
        {
            try
            {
                int removed = 0;
                List<ThingDef> defsToProcess = new List<ThingDef>();

                // Find races with mod extensions marked for removal from the cache
                foreach (var cacheEntry in Utility_CacheManager._modExtensionCache.ToList())
                {
                    ThingDef def = cacheEntry.Key;
                    NonHumanlikePawnControlExtension modExtension = cacheEntry.Value;

                    // Skip null defs or extensions
                    if (def == null || modExtension == null) continue;

                    // Check if the extension is marked for removal
                    if (modExtension.toBeRemoved)
                    {
                        // Add to the processing list
                        defsToProcess.Add(def);
                    }
                }

                // Process each def with pending removals
                foreach (ThingDef def in defsToProcess)
                {
                    if (def?.modExtensions == null) continue;

                    // Clean up trackers before removing the extension
                    Utility_DrafterManager.CleanupTrackersForRace(def);

                    // Find the extension to get original think trees before removal
                    NonHumanlikePawnControlExtension extToRemove = null;
                    for (int i = def.modExtensions.Count - 1; i >= 0; i--)
                    {
                        if (def.modExtensions[i] is NonHumanlikePawnControlExtension ext && ext.toBeRemoved)
                        {
                            extToRemove = ext;
                            break;
                        }
                    }

                    // Store original think tree info before removal
                    string originalMainTree = extToRemove?.originalMainWorkThinkTreeDefName;
                    string originalConstTree = extToRemove?.originalConstantThinkTreeDefName;

                    // Remove the extensions marked for removal
                    for (int i = def.modExtensions.Count - 1; i >= 0; i--)
                    {
                        if (def.modExtensions[i] is NonHumanlikePawnControlExtension ext && ext.toBeRemoved)
                        {
                            def.modExtensions.RemoveAt(i);
                            removed++;
                        }
                    }

                    // Clear the cached extension for this def
                    Utility_CacheManager.ClearModExtensionCachePerInstance(def);

                    // Clear race-specific tag caches
                    Utility_TagManager.ClearCacheForRace(def);

                    // Use existing method to restore think trees for all pawns of this race
                    RestoreThinkTreesForAllPawns(def, originalMainTree, originalConstTree);
                }

                if (removed > 0)
                {
                    Utility_DebugManager.LogNormal($"Removed {removed} mod extensions that were marked for removal");
                }
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error processing mod extension removals: {ex}");
            }
        }

        private void RestoreThinkTreesForAllPawns(ThingDef raceDef, string originalMainTree, string originalConstTree)
        {
            if (raceDef == null || Find.Maps == null) return;

            int updated = 0;

            // Process all maps
            foreach (Map map in Find.Maps)
            {
                if (map?.mapPawns?.AllPawnsSpawned == null) continue;

                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (pawn?.def != raceDef || pawn.Dead || pawn.Destroyed) continue;

                    // Use the existing utility method to restore think trees
                    Utility_ThinkTreeManager.ResetThinkTreeToVanilla(pawn, originalMainTree, originalConstTree);
                    updated++;
                }
            }

            // Also process world pawns
            if (Find.World?.worldPawns?.AllPawnsAliveOrDead != null)
            {
                List<Pawn> worldPawns = Find.World.worldPawns.AllPawnsAliveOrDead
                    .Where(p => p != null && !p.Dead && p.def == raceDef)
                    .ToList();

                foreach (Pawn pawn in worldPawns)
                {
                    Utility_ThinkTreeManager.ResetThinkTreeToVanilla(pawn, originalMainTree, originalConstTree);
                    updated++;
                }
            }

            if (updated > 0)
            {
                Utility_DebugManager.LogNormal($"Reset think trees for {updated} pawns of race {raceDef.defName}");
            }
        }

        private void ResetAllCache()
        {            
            // Clear work status caches on initialization
            Utility_TagManager.ResetCache();
            Utility_ThinkTreeManager.ResetCache();

            Utility_CacheManager._bioTabVisibilityCache.Clear();

            // Reset global system caches first
            Utility_JobGiverTickManager.ResetAll();
            Utility_TargetPrefilteringManager.ResetAllCaches();
            Utility_PathfindingManager.ResetAllCaches();
            Utility_GlobalStateManager.ResetAllData();


            // JobGiver_PawnControl base class caches
            JobGiver_PawnControl.ResetAllCaches();

            // General job giver caches
            JobGiver_WorkNonHumanlike.ResetCache();

            // Plant cutting job givers
            //JobGiver_PlantCutting_PlantsCut_PawnControl.ResetCache();
            JobGiver_PlantCutting_ExtractTree_PawnControl.ResetCache();

            // Growing job givers
            JobGiver_Growing_GrowerHarvest_PawnControl.ResetCache();
            JobGiver_Growing_GrowerSow_PawnControl.ResetCache();
            JobGiver_Growing_Replant_PawnControl.ResetCache();

            // Fire Fighting job givers
            JobGiver_Firefighter_FightFires_PawnControl.ResetCache();

            // Doctor job givers
            JobGiver_Common_FeedPatient_PawnControl.ResetCache();

            // Construction job givers
            JobGiver_Construction_Deconstruct_PawnControl.ResetCache();
            JobGiver_Construction_Uninstall_PawnControl.ResetCache();
            JobGiver_Construction_FixBrokenDownBuilding_PawnControl.ResetCache();
            JobGiver_Construction_ConstructDeliverResourcesToBlueprints_PawnControl.ResetCache();
            JobGiver_Construction_ConstructDeliverResourcesToFrames_PawnControl.ResetCache();
            JobGiver_Construction_BuildRoof_PawnControl.ResetCache();
            JobGiver_Construction_RemoveRoof_PawnControl.ResetCache();
            JobGiver_Construction_ConstructFinishFrames_PawnControl.ResetCache();
            JobGiver_Construction_Repair_PawnControl.ResetCache();
            JobGiver_Construction_SmoothFloor_PawnControl.ResetCache();
            JobGiver_Construction_RemoveFloor_PawnControl.ResetCache();
            JobGiver_Construction_SmoothWall_PawnControl.ResetCache();

            // Cleaning job givers
            JobGiver_Cleaning_CleanFilth_PawnControl.ResetCache();
            JobGiver_Cleaning_ClearSnow_PawnControl.ResetCache();

            // Basic worker job givers
            JobGiver_BasicWorker_Flick_PawnControl.ResetCache();
            JobGiver_BasicWorker_Open_PawnControl.ResetCache();
            JobGiver_BasicWorker_ExtractSkull_PawnControl.ResetCache();

            // Warden job givers
            JobGiver_Warden_DoExecution_PawnControl.ResetCache();
            JobGiver_Warden_ExecuteGuilty_PawnControl.ResetCache();
            JobGiver_Warden_ReleasePrisoner_PawnControl.ResetCache();
            JobGiver_Warden_TakeToBed_PawnControl.ResetCache();
            JobGiver_Warden_Feed_PawnControl.ResetCache();
            JobGiver_Warden_DeliverFood_PawnControl.ResetCache();
            JobGiver_Warden_Chat_PawnControl.ResetCache();

            // Handling job givers
            JobGiver_Handling_Tame_PawnControl.ResetCache();
            JobGiver_Handling_Train_PawnControl.ResetCache();
            JobGiver_Handling_TakeRoamingAnimalsToPen_PawnControl.ResetCache();
            JobGiver_Handling_RebalanceAnimalsInPens_PawnControl.ResetCache();
            JobGiver_Handling_Slaughter_PawnControl.ResetCache();
            JobGiver_Handling_ReleaseAnimalToWild_PawnControl.ResetCache();
            JobGiver_Handling_Milk_PawnControl.ResetCache();
            JobGiver_Handling_Shear_PawnControl.ResetCache();

            // Hauling job givers
            JobGiver_Hauling_EmptyEggBox_PawnControl.ResetCache();
            JobGiver_Hauling_Merge_PawnControl.ResetCache();
            JobGiver_Hauling_ConstructDeliverResourcesToBlueprints_PawnControl.ResetCache();
            JobGiver_Hauling_ConstructDeliverResourcesToFrames_PawnControl.ResetCache();
            JobGiver_Hauling_HaulGeneral_PawnControl.ResetCache();
            JobGiver_Hauling_FillFermentingBarrel_PawnControl.ResetCache();
            JobGiver_Hauling_TakeBeerOutOfBarrel_PawnControl.ResetCache();
            JobGiver_Hauling_HaulCampfire_PawnControl.ResetCache();
            JobGiver_Hauling_Cremate_PawnControl.ResetCache();
            JobGiver_Hauling_HaulCorpses_PawnControl.ResetCache();
            JobGiver_Hauling_Strip_PawnControl.ResetCache();
            JobGiver_Hauling_HaulToPortal_PawnControl.ResetCache();
            JobGiver_Hauling_LoadTransporters_PawnControl.ResetCache();
            JobGiver_Hauling_GatherItemsForCaravan_PawnControl.ResetCache();
            JobGiver_Hauling_UnloadCarriers_PawnControl.ResetCache();
            JobGiver_Hauling_Refuel_PawnControl.ResetCache();
            JobGiver_Hauling_Refuel_Turret_PawnControl.ResetCache();

            Utility_DebugManager.LogNormal("Cleared job cache on game load");
        }

        private void InjectDraftersSafely()
        {
            foreach (var map in Find.Maps)
            {
                Utility_DrafterManager.InjectDraftersIntoMapPawns(map);
                
                // Now give every non-human pawn its equipment/apparel/inventory
                foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                {
                    var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                    if (modExtension == null || pawn.RaceProps.Humanlike)
                    {
                        continue;
                    }

                    if (pawn.drafter == null)
                    {
                        continue;
                    }

                    Utility_DrafterManager.EnsureAllTrackers(pawn); 
                }
            }

            drafterAlreadyInjected = true; // Set after all maps processed
        }

        // Add this field to GameComponent_LateInjection
        private List<RuntimeModExtensionRecord> runtimeModExtensions = new List<RuntimeModExtensionRecord>();

        // Add this method to save runtime extensions before saving
        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Values.Look(ref drafterAlreadyInjected, "drafterAlreadyInjected", false);
            Scribe_Values.Look(ref cleanupDone, "cleanupDone", false);

            // Save/load the runtime mod extensions with this save file
            Scribe_Collections.Look(ref runtimeModExtensions, "runtimeModExtensions", LookMode.Deep);

            // If loading a save, restore the mod extensions
            if (Scribe.mode == LoadSaveMode.LoadingVars && runtimeModExtensions != null)
            {
                // We'll restore the extensions in FinalizeInit to ensure all defs are loaded
            }
        }

        // Add this method to save a runtime mod extension to this save file
        public void SaveRuntimeModExtension(ThingDef def, NonHumanlikePawnControlExtension extension)
        {
            if (def == null || extension == null) return;

            // Initialize the list if needed
            if (runtimeModExtensions == null)
                runtimeModExtensions = new List<RuntimeModExtensionRecord>();

            // Remove any existing record for this def
            runtimeModExtensions.RemoveAll(r => r.targetDefName == def.defName);

            // Add the new record
            runtimeModExtensions.Add(new RuntimeModExtensionRecord(def.defName, extension));

            Utility_DebugManager.LogNormal($"Saved runtime mod extension for {def.defName} in current save file");
        }

        // Add this method to remove a runtime mod extension from this save file
        public void RemoveRuntimeModExtension(ThingDef def)
        {
            if (def == null || runtimeModExtensions == null) return;

            int count = runtimeModExtensions.RemoveAll(r => r.targetDefName == def.defName);

            if (count > 0)
            {
                Utility_DebugManager.LogNormal($"Removed runtime mod extension for {def.defName} from current save file");
            }
        }

        // Add this method to restore runtime mod extensions from the save file
        private void RestoreRuntimeModExtensions()
        {
            if (runtimeModExtensions == null || runtimeModExtensions.Count == 0) return;

            int restored = 0;

            foreach (var record in runtimeModExtensions)
            {
                try
                {
                    ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(record.targetDefName);
                    if (def == null)
                    {
                        Utility_DebugManager.LogWarning($"Could not find ThingDef {record.targetDefName} to restore mod extension");
                        continue;
                    }

                    // Ensure the def has a modExtensions list
                    if (def.modExtensions == null)
                        def.modExtensions = new List<DefModExtension>();
                    else
                    {
                        // Remove any existing extension of the same type
                        def.modExtensions.RemoveAll(ext => ext is NonHumanlikePawnControlExtension);
                    }

                    // Add the saved extension
                    def.modExtensions.Add(record.extension);

                    // Mark as loaded from save
                    record.extension.fromXML = false;

                    // Rebuild caches
                    record.extension.CacheSimulatedSkillLevels();
                    record.extension.CacheSkillPassions();

                    // Update the cache for this def
                    Utility_CacheManager.ClearModExtensionCachePerInstance(def);
                    Utility_CacheManager.PreloadModExtensionForRace(def);

                    // Apply to existing pawns
                    ApplyExtensionToExistingPawns(def, record.extension);

                    restored++;
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"Error restoring mod extension for {record.targetDefName}: {ex}");
                }
            }

            Utility_DebugManager.LogNormal($"Restored {restored} runtime mod extensions from save file");
        }

        // Implement this method to apply extensions to existing pawns
        private void ApplyExtensionToExistingPawns(ThingDef raceDef, NonHumanlikePawnControlExtension modExtension)
        {
            if (raceDef == null || modExtension == null) return;
            if (Find.Maps == null) return;

            int updatedCount = 0;

            // First, apply race-level changes
            modExtension.originalMainWorkThinkTreeDefName = raceDef.race.thinkTreeMain?.defName;
            modExtension.originalConstantThinkTreeDefName = raceDef.race.thinkTreeConstant?.defName;

            // Apply static think tree changes at race level if specified
            if (!string.IsNullOrEmpty(modExtension.mainWorkThinkTreeDefName))
            {
                var thinkTree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(modExtension.mainWorkThinkTreeDefName);
                if (thinkTree != null)
                {
                    raceDef.race.thinkTreeMain = thinkTree;
                }
            }

            // Apply constant think tree if specified
            if (!string.IsNullOrEmpty(modExtension.constantThinkTreeDefName))
            {
                var constantTree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(modExtension.constantThinkTreeDefName);
                if (constantTree != null)
                {
                    raceDef.race.thinkTreeConstant = constantTree;
                }
            }

            // Process all maps
            foreach (Map map in Find.Maps)
            {
                if (map?.mapPawns?.AllPawnsSpawned == null) continue;

                // Find all spawned pawns of the given race
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (pawn?.def != raceDef || pawn.Dead || pawn.Destroyed) continue;

                    try
                    {
                        // Apply drafters if needed
                        if (modExtension.forceDraftable && pawn.drafter == null)
                        {
                            Utility_DrafterManager.EnsureDrafter(pawn, modExtension);
                        }

                        // Ensure other trackers exist
                        if (modExtension.forceEquipWeapon || modExtension.forceWearApparel)
                        {
                            Utility_DrafterManager.EnsureAllTrackers(pawn);
                        }

                        // Apply think trees
                        // CRITICAL FIX: Apply the think trees directly to the pawn
                        if (pawn.mindState != null)
                        {
                            // Apply main think tree
                            if (!string.IsNullOrEmpty(modExtension.mainWorkThinkTreeDefName))
                            {
                                var thinkTree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(modExtension.mainWorkThinkTreeDefName);
                                if (thinkTree != null)
                                {
                                    // Use reflection to set the think tree
                                    Type mindStateType = pawn.mindState.GetType();
                                    FieldInfo thinkTreeField = AccessTools.Field(mindStateType, "thinkTree");
                                    if (thinkTreeField != null)
                                    {
                                        thinkTreeField.SetValue(pawn.mindState, thinkTree);
                                        Utility_DebugManager.LogNormal($"Set main think tree {thinkTree.defName} for {pawn.LabelShort}");
                                    }
                                }
                            }

                            // Apply constant think tree if needed
                            if (!string.IsNullOrEmpty(modExtension.constantThinkTreeDefName))
                            {
                                var constantTree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(modExtension.constantThinkTreeDefName);
                                if (constantTree != null)
                                {
                                    // Use reflection to set the constant think tree
                                    Type mindStateType = pawn.mindState.GetType();
                                    FieldInfo constThinkTreeField = AccessTools.Field(mindStateType, "thinkTreeConstant");
                                    if (constThinkTreeField != null)
                                    {
                                        constThinkTreeField.SetValue(pawn.mindState, constantTree);
                                        Utility_DebugManager.LogNormal($"Set constant think tree {constantTree.defName} for {pawn.LabelShort}");
                                    }
                                }
                            }

                            // Force think node rebuild
                            if (pawn.thinker != null)
                            {
                                Type thinkerType = pawn.thinker.GetType();
                                FieldInfo thinkRootField = AccessTools.Field(thinkerType, "thinkRoot");
                                if (thinkRootField != null)
                                {
                                    // Null out the think root to force a rebuild
                                    thinkRootField.SetValue(pawn.thinker, null);
                                }
                            }
                        }

                        updatedCount++;
                    }
                    catch (Exception ex)
                    {
                        Utility_DebugManager.LogError($"Error updating pawn {pawn.LabelShort}: {ex}");
                    }
                }
            }

            Utility_DebugManager.LogNormal($"Updated {updatedCount} existing pawns of race {raceDef.defName}");
        }
    }
}
//using HarmonyLib;
//using RimWorld;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Reflection;
//using System.Text;
//using System.Threading.Tasks;
//using Verse;
//using Verse.Noise;
//using static emitbreaker.PawnControl.HarmonyPatches;
//using static System.Net.Mime.MediaTypeNames;

//namespace emitbreaker.PawnControl
//{
//    public class GameComponent_LateInjection : GameComponent
//    {
//        private bool drafterAlreadyInjected = false;
//        private bool cleanupDone = false;

//        public GameComponent_LateInjection(Game game)
//        {
//        }

//        public override void GameComponentTick()
//        {
//            if (drafterAlreadyInjected && Utility_IdentityManager.identityFlagsPreloaded && cleanupDone)
//            {
//                return;
//            }

//            if (Find.TickManager.TicksGame < 200)
//            {
//                return;
//            }

//            if (!Utility_IdentityManager.identityFlagsPreloaded)
//            {

//            }
//            if (!drafterAlreadyInjected)
//            {
//                InjectDraftersSafely();
//            }
//        }

//        /// <summary>
//        /// Called when game is being saved.
//        /// </summary>
//        public override void GameComponentOnGUI()
//        {
//            base.GameComponentOnGUI();

//            // This is a good spot to ensure cache is clean before saving
//            JobGiver_WorkNonHumanlike.ResetCache();
//        }

//        /// <summary>
//        /// Called when a game is loaded.
//        /// </summary>
//        public override void FinalizeInit()
//        {
//            base.FinalizeInit();

//            // Process any pending removals first
//            ProcessPendingRemovals();

//            // Ensure all modded pawns have their stat injections applied
//            Utility_StatManager.CheckStatHediffDefExists();

//            // Clear debug patch caches
//            HarmonyPatches.Patch_Debuggers.Patch_ThinkNode_PrioritySorter_DebugJobs.ResetCache();
//            HarmonyPatches.Patch_Debuggers.Patch_Pawn_SpawnSetup_DumpThinkTree.ResetCache();

//            // Reset caches to ensure no stale data is present
//            ResetAllCache();

//            // Explicitly invalidate the colonist-like cache for all maps
//            if (Find.Maps != null)
//            {
//                foreach (Map map in Find.Maps)
//                {
//                    Utility_CacheManager.InvalidateColonistLikeCache(map);
//                }
//            }
//        }

//        private void ProcessPendingRemovals()
//        {
//            try
//            {
//                int removed = 0;
//                List<ThingDef> defsToProcess = new List<ThingDef>();

//                // Find races with mod extensions marked for removal from the cache
//                foreach (var cacheEntry in Utility_CacheManager._modExtensionCache.ToList())
//                {
//                    ThingDef def = cacheEntry.Key;
//                    NonHumanlikePawnControlExtension modExtension = cacheEntry.Value;

//                    // Skip null defs or extensions
//                    if (def == null || modExtension == null) continue;

//                    // Check if the extension is marked for removal
//                    if (modExtension.toBeRemoved)
//                    {
//                        // Add to the processing list
//                        defsToProcess.Add(def);
//                    }
//                }

//                // Process each def with pending removals
//                foreach (ThingDef def in defsToProcess)
//                {
//                    if (def?.modExtensions == null) continue;

//                    // Clean up trackers before removing the extension
//                    Utility_DrafterManager.CleanupTrackersForRace(def);

//                    // Find the extension to get original think trees before removal
//                    NonHumanlikePawnControlExtension extToRemove = null;
//                    for (int i = def.modExtensions.Count - 1; i >= 0; i--)
//                    {
//                        if (def.modExtensions[i] is NonHumanlikePawnControlExtension ext && ext.toBeRemoved)
//                        {
//                            extToRemove = ext;
//                            break;
//                        }
//                    }

//                    // Store original think tree info before removal
//                    string originalMainTree = extToRemove?.originalMainWorkThinkTreeDefName;
//                    string originalConstTree = extToRemove?.originalConstantThinkTreeDefName;

//                    // Remove the extensions marked for removal
//                    for (int i = def.modExtensions.Count - 1; i >= 0; i--)
//                    {
//                        if (def.modExtensions[i] is NonHumanlikePawnControlExtension ext && ext.toBeRemoved)
//                        {
//                            def.modExtensions.RemoveAt(i);
//                            removed++;
//                        }
//                    }

//                    // Clear the cached extension for this def
//                    Utility_CacheManager.ClearModExtensionCachePerInstance(def);

//                    // Clear race-specific tag caches
//                    Utility_TagManager.ClearCacheForRace(def);

//                    // Use existing method to restore think trees for all pawns of this race
//                    RestoreThinkTreesForAllPawns(def, originalMainTree, originalConstTree);
//                }

//                if (removed > 0)
//                {
//                    Utility_DebugManager.LogNormal($"Removed {removed} mod extensions that were marked for removal");
//                }
//            }
//            catch (Exception ex)
//            {
//                Utility_DebugManager.LogError($"Error processing mod extension removals: {ex}");
//            }
//        }

//        private void RestoreThinkTreesForAllPawns(ThingDef raceDef, string originalMainTree, string originalConstTree)
//        {
//            if (raceDef == null || Find.Maps == null) return;

//            int updated = 0;

//            // Process all maps
//            foreach (Map map in Find.Maps)
//            {
//                if (map?.mapPawns?.AllPawnsSpawned == null) continue;

//                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
//                {
//                    if (pawn?.def != raceDef || pawn.Dead || pawn.Destroyed) continue;

//                    // Use the existing utility method to restore think trees
//                    Utility_ThinkTreeManager.ResetThinkTreeToVanilla(pawn, originalMainTree, originalConstTree);
//                    updated++;
//                }
//            }

//            // Also process world pawns
//            if (Find.World?.worldPawns?.AllPawnsAliveOrDead != null)
//            {
//                List<Pawn> worldPawns = Find.World.worldPawns.AllPawnsAliveOrDead
//                    .Where(p => p != null && !p.Dead && p.def == raceDef)
//                    .ToList();

//                foreach (Pawn pawn in worldPawns)
//                {
//                    Utility_ThinkTreeManager.ResetThinkTreeToVanilla(pawn, originalMainTree, originalConstTree);
//                    updated++;
//                }
//            }

//            if (updated > 0)
//            {
//                Utility_DebugManager.LogNormal($"Reset think trees for {updated} pawns of race {raceDef.defName}");
//            }
//        }

//        private void ResetAllCache()
//        {
//            // Clear work status caches on initialization
//            Utility_TagManager.ResetCache();
//            Utility_ThinkTreeManager.ResetCache();

//            Utility_CacheManager._bioTabVisibilityCache.Clear();

//            // General job giver caches
//            JobGiver_WorkNonHumanlike.ResetCache();

//            // Plant cutting job givers
//            JobGiver_PlantsCut_PawnControl.ResetCache();
//            JobGiver_ExtractTree_PawnControl.ResetCache();

//            // Growing job givers
//            JobGiver_GrowerHarvest_PawnControl.ResetCache();
//            JobGiver_GrowerSow_PawnControl.ResetCache();
//            JobGiver_Replant_PawnControl.ResetCache();

//            // Fire Fighting job givers
//            JobGiver_FightFires_PawnControl.ResetCache();

//            // Doctor job givers
//            JobGiver_FeedPatient_PawnControl.ResetCache();

//            // Construction job givers
//            JobGiver_Deconstruct_PawnControl.ResetCache();
//            JobGiver_Uninstall_PawnControl.ResetCache();
//            JobGiver_FixBrokenDownBuilding_PawnControl.ResetCache();
//            JobGiver_ConstructDeliverResourcesToBlueprints_Construction_PawnControl.ResetCache();
//            JobGiver_ConstructDeliverResourcesToFrames_Construction_PawnControl.ResetCache();
//            JobGiver_BuildRoof_PawnControl.ResetCache();
//            JobGiver_RemoveRoof_PawnControl.ResetCache();
//            JobGiver_ConstructFinishFrames_PawnControl.ResetCache();
//            JobGiver_Repair_PawnControl.ResetCache();
//            JobGiver_SmoothFloor_PawnControl.ResetCache();
//            JobGiver_RemoveFloor_PawnControl.ResetCache();
//            JobGiver_SmoothWall_PawnControl.ResetCache();

//            // Cleaning job givers
//            JobGiver_CleanFilth_PawnControl.ResetCache();
//            JobGiver_ClearSnow_PawnControl.ResetCache();

//            // Basic worker job givers
//            JobGiver_Flick_PawnControl.ResetCache();
//            JobGiver_Open_PawnControl.ResetCache();
//            JobGiver_ExtractSkull_PawnControl.ResetCache();

//            // Warden job givers
//            JobGiver_Warden_DoExecution_PawnControl.ResetCache();
//            JobGiver_Warden_ExecuteGuilty_PawnControl.ResetCache();
//            JobGiver_Warden_ReleasePrisoner_PawnControl.ResetCache();
//            JobGiver_Warden_TakeToBed_PawnControl.ResetCache();
//            JobGiver_Warden_Feed_PawnControl.ResetCache();
//            JobGiver_Warden_DeliverFood_PawnControl.ResetCache();
//            JobGiver_Warden_Chat_PawnControl.ResetCache();

//            // Handling job givers
//            JobGiver_Tame_PawnControl.ResetCache();
//            JobGiver_Train_PawnControl.ResetCache();
//            JobGiver_TakeRoamingToPen_PawnControl.ResetCache();
//            JobGiver_RebalanceAnimalsInPens_PawnControl.ResetCache();
//            JobGiver_Slaughter_PawnControl.ResetCache();
//            JobGiver_ReleaseAnimalToWild_PawnControl.ResetCache();
//            JobGiver_Milk_PawnControl.ResetCache();
//            JobGiver_Shear_PawnControl.ResetCache();

//            // Hauling job givers
//            JobGiver_EmptyEggBox_PawnControl.ResetCache();
//            JobGiver_Merge_PawnControl.ResetCache();
//            JobGiver_ConstructDeliverResourcesToBlueprints_Hauling_PawnControl.ResetCache();
//            JobGiver_ConstructDeliverResourcesToFrames_Hauling_PawnControl.ResetCache();
//            JobGiver_HaulGeneral_PawnControl.ResetCache();
//            JobGiver_FillFermentingBarrel_PawnControl.ResetCache();
//            JobGiver_TakeBeerOutOfBarrel_PawnControl.ResetCache();
//            JobGiver_HaulCampfire_PawnControl.ResetCache();
//            JobGiver_Cremate_PawnControl.ResetCache();
//            JobGiver_HaulCorpses_PawnControl.ResetCache();
//            JobGiver_Strip_PawnControl.ResetCache();
//            JobGiver_HaulToPortal_PawnControl.ResetCache();
//            JobGiver_LoadTransporters_PawnControl.ResetCache();
//            JobGiver_GatherItemsForCaravan_PawnControl.ResetCache();
//            JobGiver_UnloadCarriers_PawnControl.ResetCache();
//            JobGiver_Refuel_PawnControl.ResetCache();
//            JobGiver_Refuel_Turret_PawnControl.ResetCache();

//            Utility_DebugManager.LogNormal("Cleared job cache on game load");
//        }

//        private void InjectDraftersSafely()
//        {
//            foreach (var map in Find.Maps)
//            {
//                Utility_DrafterManager.InjectDraftersIntoMapPawns(map);

//                // Now give every non-human pawn its equipment/apparel/inventory
//                foreach (var pawn in map.mapPawns.AllPawnsSpawned)
//                {
//                    var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
//                    if (modExtension == null || pawn.RaceProps.Humanlike)
//                    {
//                        continue;
//                    }

//                    if (pawn.drafter == null)
//                    {
//                        continue;
//                    }

//                    Utility_DrafterManager.EnsureAllTrackers(pawn);
//                }
//            }

//            drafterAlreadyInjected = true; // Set after all maps processed
//        }

//        // Add this field to GameComponent_LateInjection
//        private List<RuntimeModExtensionRecord> runtimeModExtensions = new List<RuntimeModExtensionRecord>();

//        // Add this method to save runtime extensions before saving
//        public override void ExposeData()
//        {
//            base.ExposeData();

//            // When saving, collect all runtime mod extensions
//            if (Scribe.mode == LoadSaveMode.Saving)
//            {
//                CollectRuntimeModExtensions();
//            }

//            // Save/load the runtime extensions
//            Scribe_Collections.Look(ref runtimeModExtensions, "runtimeModExtensions", LookMode.Deep);

//            // When loading, apply the saved extensions
//            if (Scribe.mode == LoadSaveMode.PostLoadInit)
//            {
//                RestoreRuntimeModExtensions();
//            }
//        }

//        // Collect all runtime-added mod extensions for saving
//        private void CollectRuntimeModExtensions()
//        {
//            runtimeModExtensions = new List<RuntimeModExtensionRecord>();

//            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
//            {
//                if (def?.modExtensions == null) continue;

//                foreach (DefModExtension extension in def.modExtensions)
//                {
//                    if (extension is NonHumanlikePawnControlExtension modExt && !modExt.fromXML)
//                    {
//                        // Create a record for each runtime-added extension
//                        runtimeModExtensions.Add(new RuntimeModExtensionRecord(def.defName, modExt));
//                        Utility_DebugManager.LogNormal($"Saving runtime mod extension for {def.defName}");
//                    }
//                }
//            }

//            Utility_DebugManager.LogNormal($"Collected {runtimeModExtensions.Count} runtime mod extensions for saving");
//        }

//        // Restore runtime-added mod extensions when loading a game
//        private void RestoreRuntimeModExtensions()
//        {
//            if (runtimeModExtensions == null || runtimeModExtensions.Count == 0) return;

//            int restored = 0;

//            foreach (RuntimeModExtensionRecord record in runtimeModExtensions)
//            {
//                try
//                {
//                    if (string.IsNullOrEmpty(record.targetDefName)) continue;

//                    // Find the target ThingDef
//                    ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(record.targetDefName);
//                    if (def == null)
//                    {
//                        Utility_DebugManager.LogWarning($"Could not find ThingDef {record.targetDefName} to restore mod extension");
//                        continue;
//                    }

//                    // Check if extension already exists (from a previous load or XML)
//                    bool alreadyExists = false;
//                    if (def.modExtensions != null)
//                    {
//                        foreach (DefModExtension ext in def.modExtensions)
//                        {
//                            if (ext is NonHumanlikePawnControlExtension)
//                            {
//                                alreadyExists = true;
//                                break;
//                            }
//                        }
//                    }

//                    if (alreadyExists)
//                    {
//                        Utility_DebugManager.LogNormal($"Mod extension already exists for {def.defName}, skipping");
//                        continue;
//                    }

//                    // Ensure modExtensions list exists
//                    if (def.modExtensions == null)
//                        def.modExtensions = new List<DefModExtension>();

//                    // Add the saved extension
//                    def.modExtensions.Add(record.extension);

//                    // Mark as loaded from save, not XML
//                    record.extension.fromXML = false;

//                    // Refresh caches
//                    Utility_CacheManager.ClearModExtensionCachePerInstance(def);
//                    Utility_CacheManager.GetModExtension(def); // Force re-cache
//                    record.extension.CacheSimulatedSkillLevels();
//                    record.extension.CacheSkillPassions();

//                    restored++;
//                    Utility_DebugManager.LogNormal($"Restored runtime mod extension for {def.defName}");
//                }
//                catch (Exception ex)
//                {
//                    Utility_DebugManager.LogError($"Error restoring mod extension for {record.targetDefName}: {ex.Message}");
//                }
//            }

//            Utility_DebugManager.LogNormal($"Restored {restored} runtime mod extensions");
//        }
//    }
//}