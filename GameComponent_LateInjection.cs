using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
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
        private int lastCacheMaintenanceTick = 0;
        private const int cacheMaintenanceInterval = 2000; // Every 2000 ticks (~33 seconds)

        public GameComponent_LateInjection(Game game)
        {
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();

            // Clear all caches when starting a new game
            Utility_MapCacheManager.ClearAllCaches();
            Utility_UnifiedCache.Clear(); // Use the unified cache
            ResetAllCaches(resetMode: "NewGame");

            // Clean up any runtime mod extensions to start fresh
            Utility_UnifiedCache.CleanupRuntimeModExtensions(); // Use the unified cache method
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            Utility_JobGiverRegistry.RegisterAllJobGivers();

            // Initial calculation of tick groups based on colony size
            Utility_JobGiverSchedulingManager.RecalculateTickGroups();

            // Clear all caches when loading a saved game
            Utility_MapCacheManager.ClearAllCaches();
            Utility_UnifiedCache.Clear(); // Use the unified cache
            ResetAllCaches(resetMode: "LoadedGame");
        }

        public override void GameComponentTick()
        {
            if (drafterAlreadyInjected && Utility_IdentityManager.identityFlagsPreloaded && cleanupDone)
            {
                // Perform periodic cache maintenance to prevent memory leaks
                PerformPeriodicCacheMaintenance();
                return;
            }

            // Recalculate periodically - the manager handles frequency internally
            Utility_JobGiverSchedulingManager.RecalculateTickGroups();

            if (Find.TickManager.TicksGame < 200)
            {
                return;
            }

            if (!Utility_IdentityManager.identityFlagsPreloaded)
            {
                // Identity flags will be loaded elsewhere
            }

            if (!drafterAlreadyInjected)
            {
                InjectDraftersSafely();
            }
        }

        /// <summary>
        /// Performs periodic maintenance on caches to ensure they don't grow too large
        /// </summary>
        private void PerformPeriodicCacheMaintenance()
        {
            int currentTick = Find.TickManager.TicksGame;

            // Only run maintenance on a fixed interval to avoid performance impact
            if (currentTick - lastCacheMaintenanceTick < cacheMaintenanceInterval)
                return;

            lastCacheMaintenanceTick = currentTick;

            try
            {
                // Clean up any invalid entries in the reachability cache
                CleanupInvalidReachabilityCacheEntries();

                // The unified cache system handles its own maintenance
                // but we can still clean up specific things that need game logic

                // Clean job caches for non-existent things
                CleanupStaleJobCaches();

                // Prune extension caches for races no longer in use
                CleanupUnusedRaceExtensionCaches();
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error during cache maintenance: {ex}");
            }
        }

        /// <summary>
        /// Clean up any invalid cached reachability results
        /// </summary>
        private void CleanupInvalidReachabilityCacheEntries()
        {
            // We don't have direct access to the internal dictionary in the generic class
            // Instead we'll provide a utility method that each job giver can call

            // Signal to all maps that they should check for cleanup
            if (Find.Maps != null)
            {
                foreach (var map in Find.Maps)
                {
                    // Invalidate any reachability results for despawned things
                    Utility_TargetPrefilteringManager.CleanupInvalidTargetsForMap(map);
                }
            }
        }

        /// <summary>
        /// Clean up any stale job caches for things that no longer exist
        /// </summary>
        private void CleanupStaleJobCaches()
        {
            // Clean cached jobs for things that are no longer valid
            var invalidEntries = Utility_UnifiedCache.JobCache
                .Where(kvp => kvp.Key == null || !kvp.Key.Spawned || kvp.Key.Destroyed)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in invalidEntries)
            {
                Utility_UnifiedCache.JobCache.Remove(key);
            }

            if (invalidEntries.Count > 0 && Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Cleaned up {invalidEntries.Count} stale job cache entries");
            }
        }

        /// <summary>
        /// Clean up extension caches for races that are no longer in use
        /// </summary>
        private void CleanupUnusedRaceExtensionCaches()
        {
            // This is mostly a memory optimization
            // Only perform if we have a lot of cached extensions
            if (Utility_UnifiedCache.ModExtensions.Count > 50)
            {
                // Find races that aren't currently spawned on any map
                var usedRaces = new HashSet<ThingDef>();

                // Get all currently spawned pawn races
                if (Find.Maps != null)
                {
                    foreach (var map in Find.Maps)
                    {
                        foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                        {
                            if (pawn?.def != null)
                                usedRaces.Add(pawn.def);
                        }
                    }
                }

                // Also check world pawns
                if (Find.World?.worldPawns?.AllPawnsAliveOrDead != null)
                {
                    foreach (var pawn in Find.World.worldPawns.AllPawnsAliveOrDead)
                    {
                        if (pawn?.def != null)
                            usedRaces.Add(pawn.def);
                    }
                }

                // Find extension cache entries for races not currently in use
                var unusedExtensionDefs = Utility_UnifiedCache.ModExtensions.Keys
                    .Where(def => !usedRaces.Contains(def))
                    .ToList();

                // Only clean up if we have a significant number of unused entries
                if (unusedExtensionDefs.Count > 20)
                {
                    int removed = 0;

                    // Remove a portion of the unused entries (not all, in case they're needed soon)
                    foreach (var def in unusedExtensionDefs.Take(unusedExtensionDefs.Count / 2))
                    {
                        Utility_UnifiedCache.ModExtensions.Remove(def);
                        removed++;
                    }

                    if (Prefs.DevMode)
                    {
                        Utility_DebugManager.LogNormal($"Cleaned up {removed} unused mod extension caches");
                    }
                }
            }
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

            Utility_JobGiverManager.ResetRegistry();

            // Ensure all modded pawns have their stat injections applied
            Utility_StatManager.CheckStatHediffDefExists();

            // Clear debug patch caches
            HarmonyPatches.Patch_Debuggers.Patch_ThinkNode_PrioritySorter_DebugJobs.ResetCache();
            HarmonyPatches.Patch_Debuggers.Patch_Pawn_SpawnSetup_DumpThinkTree.ResetCache();

            // Reset all caches to ensure no stale data is present
            ResetAllCaches(resetMode: "FinalizeInit");

            // Explicitly invalidate the colonist-like cache for all maps
            if (Find.Maps != null)
            {
                foreach (Map map in Find.Maps)
                {
                    Utility_UnifiedCache.InvalidateColonistCache(map); // Use the unified cache
                }
            }
        }

        /// <summary>
        /// Resets all caches with optional diagnostics based on reset mode
        /// </summary>
        /// <param name="resetMode">When the reset is happening (FinalizeInit, NewGame, LoadedGame)</param>
        private void ResetAllCaches(string resetMode)
        {
            // Core reset using the unified cache
            Utility_UnifiedCache.Clear();

            // Reset other specialized subsystems
            Utility_DrafterManager.ResetTrackerTracking();
            Utility_ThinkTreeManager.ResetCache();

            // Reset global system caches that aren't part of the unified system
            Utility_JobGiverTickManager.ResetAll();
            Utility_TargetPrefilteringManager.ResetAllCaches();
            Utility_PathfindingManager.ResetAllCaches();
            Utility_GlobalStateManager.ResetAllData();
            Utility_JobGiverCacheManager<Thing>.Reset();

            // Initialize work settings for all maps
            if (Find.Maps != null)
            {
                foreach (var map in Find.Maps)
                {
                    Utility_WorkSettingsManager.FullInitializeAllEligiblePawns(map, forceLock: true);
                }
            }

            Utility_DebugManager.LogNormal($"Reset all job caches for {resetMode}");
        }

        private void ProcessPendingRemovals()
        {
            try
            {
                int removed = 0;
                List<ThingDef> defsToProcess = new List<ThingDef>();

                // Find races with mod extensions marked for removal from the cache
                foreach (var cacheEntry in Utility_UnifiedCache.ModExtensions.ToList())
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
                    Utility_UnifiedCache.ClearModExtension(def);

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

        private void InjectDraftersSafely()
        {
            foreach (var map in Find.Maps)
            {
                Utility_DrafterManager.InjectDraftersIntoMapPawns(map);

                // Now give every non-human pawn its equipment/apparel/inventory
                foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                {
                    var modExtension = Utility_UnifiedCache.GetModExtension(pawn.def);
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

            // Reset tracker data after loading
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                Utility_DrafterManager.ResetTrackerTracking();
            }

            Scribe_Values.Look(ref drafterAlreadyInjected, "drafterAlreadyInjected", false);
            Scribe_Values.Look(ref cleanupDone, "cleanupDone", false);
            Scribe_Values.Look(ref lastCacheMaintenanceTick, "lastCacheMaintenanceTick", 0);

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
                    Utility_UnifiedCache.ClearModExtension(def);
                    Utility_UnifiedCache.PreloadModExtensionForRace(def);

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