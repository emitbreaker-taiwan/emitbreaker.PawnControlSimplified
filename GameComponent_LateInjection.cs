using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Concurrent;
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
        #region Fields

        private bool drafterAlreadyInjected = false;
        private bool cleanupDone = false;
        private int lastCacheMaintenanceTick = 0;
        private const int cacheMaintenanceInterval = 2000; // Every 2000 ticks (~33 seconds)

        // Runtime mod extensions storage - Thread-safe
        private readonly ConcurrentDictionary<string, NonHumanlikePawnControlExtension> runtimeModExtensions = new ConcurrentDictionary<string, NonHumanlikePawnControlExtension>();

        // Cached reflection info - 리플렉션 캐싱으로 성능 향상
        private static readonly ConcurrentDictionary<Type, ReflectionCache> _reflectionCache = new ConcurrentDictionary<Type, ReflectionCache>();

        // 재사용 가능한 컬렉션들 - 메모리 할당 최소화
        [ThreadStatic]
        private static HashSet<ThingDef> _tempUsedRaces;
        private static HashSet<ThingDef> TempUsedRaces => _tempUsedRaces ?? (_tempUsedRaces = new HashSet<ThingDef>());

        [ThreadStatic]
        private static List<ThingDef> _tempUnusedDefs;
        private static List<ThingDef> TempUnusedDefs => _tempUnusedDefs ?? (_tempUnusedDefs = new List<ThingDef>());

        [ThreadStatic]
        private static List<Pawn> _tempPawnList;
        private static List<Pawn> TempPawnList => _tempPawnList ?? (_tempPawnList = new List<Pawn>());

        #endregion

        #region Reflection Cache

        /// <summary>
        /// 리플렉션 정보를 캐시하는 클래스
        /// </summary>
        private class ReflectionCache
        {
            public FieldInfo ThinkTreeField { get; set; }
            public FieldInfo ConstantThinkTreeField { get; set; }
            public FieldInfo ThinkRootField { get; set; }

            public ReflectionCache(Type type)
            {
                try
                {
                    if (type.Name.Contains("MindState"))
                    {
                        ThinkTreeField = AccessTools.Field(type, "thinkTree");
                        ConstantThinkTreeField = AccessTools.Field(type, "thinkTreeConstant");
                    }
                    else if (type.Name.Contains("Pawn_ThinkerTracker") || type.Name.Contains("Thinker"))
                    {
                        ThinkRootField = AccessTools.Field(type, "thinkRoot");
                    }
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"Failed to cache reflection info for {type}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 타입에 대한 리플렉션 캐시를 가져옵니다
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static ReflectionCache GetReflectionCache(Type type)
        {
            return _reflectionCache.GetOrAdd(type, t => new ReflectionCache(t));
        }

        #endregion

        #region Constructor

        public GameComponent_LateInjection(Game game)
        {
        }

        #endregion

        #region Game Lifecycle Methods

        public override void StartedNewGame()
        {
            base.StartedNewGame();

            // Clear all caches when starting a new game
            Utility_CacheManager.Clear();
            ResetAllCaches(resetMode: "NewGame");

            // Clean up any runtime mod extensions to start fresh
            Utility_CacheManager.CleanupRuntimeModExtensions();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();

            // Clear all caches when loading a saved game
            Utility_CacheManager.Clear();
            ResetAllCaches(resetMode: "LoadedGame");

            // Repopulate BioTabVisibility cache
            RefreshBioTabVisibilityCache();
        }

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

            // Reset all caches to ensure no stale data is present
            ResetAllCaches(resetMode: "FinalizeInit");

            // Explicitly invalidate the colonist-like cache for all maps
            if (Find.Maps != null)
            {
                foreach (Map map in Find.Maps)
                {
                    Utility_CacheManager.InvalidateColonistCache(map);
                }
            }
        }

        public override void GameComponentTick()
        {
            if (drafterAlreadyInjected && Utility_IdentityManager.identityFlagsPreloaded && cleanupDone)
            {
                // Perform periodic cache maintenance to prevent memory leaks
                PerformPeriodicCacheMaintenance();
                return;
            }

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

        #endregion

        #region Cache Management

        /// <summary>
        /// BioTabVisibility 캐시를 효율적으로 새로고침 - LINQ 제거
        /// </summary>
        private void RefreshBioTabVisibilityCache()
        {
            // Clear only the BioTabVisibility cache
            Utility_CacheManager.BioTabVisibility.Clear();

            // Scan all maps - LINQ 대신 직접 루프 사용
            if (Find.Maps != null)
            {
                for (int i = 0; i < Find.Maps.Count; i++)
                {
                    var map = Find.Maps[i];
                    ScanPawnsForBioTabVisibility(map.mapPawns.AllPawnsSpawned);
                }
            }

            // Scan all world pawns
            if (Find.World?.worldPawns?.AllPawnsAliveOrDead != null)
            {
                ScanPawnsForBioTabVisibility(Find.World.worldPawns.AllPawnsAliveOrDead);
            }

            if (Prefs.DevMode)
                Utility_DebugManager.LogNormal($"BioTabVisibility cache rebuilt: {Utility_CacheManager.BioTabVisibility.Count} entries");
        }

        /// <summary>
        /// Pawn 리스트를 스캔하여 BioTabVisibility 캐시 업데이트 - 성능 최적화
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void ScanPawnsForBioTabVisibility(IReadOnlyList<Pawn> pawns)
        {
            for (int i = 0; i < pawns.Count; i++)
            {
                var pawn = pawns[i];
                if (pawn?.def == null) continue;

                // Pick up either XML or runtime-injected extension
                var ext = Utility_CacheManager.GetModExtension(pawn.def) ?? GetRuntimeModExtensionForRace(pawn.def);
                if (ext != null && !pawn.RaceProps.Humanlike)
                {
                    Utility_CacheManager.BioTabVisibility.TryAdd(pawn.thingIDNumber, true);
                    if (ext.debugMode)
                        Utility_DebugManager.LogNormal($"[CachePrep] will hide Bio for {pawn.LabelShort}");
                }
            }
        }

        /// <summary>
        /// 주기적 캐시 유지보수 - 메모리 효율성 향상
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
                // Clean up unused race extension caches
                CleanupUnusedRaceExtensionCaches();
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error during cache maintenance: {ex}");
            }
        }

        /// <summary>
        /// 사용되지 않는 Race Extension 캐시 정리 - LINQ 제거 및 성능 향상
        /// </summary>
        private void CleanupUnusedRaceExtensionCaches()
        {
            // This is mostly a memory optimization
            // Only perform if we have a lot of cached extensions
            if (Utility_CacheManager._modExtensionCache.Count <= 50)
                return;

            var usedRaces = TempUsedRaces;
            usedRaces.Clear();

            // Get all currently spawned pawn races - LINQ 대신 직접 루프
            if (Find.Maps != null)
            {
                for (int i = 0; i < Find.Maps.Count; i++)
                {
                    var map = Find.Maps[i];
                    var allPawns = map.mapPawns.AllPawnsSpawned;

                    for (int j = 0; j < allPawns.Count; j++)
                    {
                        var pawn = allPawns[j];
                        if (pawn?.def != null)
                            usedRaces.Add(pawn.def);
                    }
                }
            }

            // Also check world pawns
            if (Find.World?.worldPawns?.AllPawnsAliveOrDead != null)
            {
                var worldPawns = Find.World.worldPawns.AllPawnsAliveOrDead;
                for (int i = 0; i < worldPawns.Count; i++)
                {
                    var pawn = worldPawns[i];
                    if (pawn?.def != null)
                        usedRaces.Add(pawn.def);
                }
            }

            // Find extension cache entries for races not currently in use
            var unusedDefs = TempUnusedDefs;
            unusedDefs.Clear();

            foreach (var kvp in Utility_CacheManager._modExtensionCache)
            {
                if (!usedRaces.Contains(kvp.Key))
                    unusedDefs.Add(kvp.Key);
            }

            // Only clean up if we have a significant number of unused entries
            if (unusedDefs.Count > 20)
            {
                int removed = 0;
                int toRemove = unusedDefs.Count / 2; // Remove half

                // Remove a portion of the unused entries
                for (int i = 0; i < toRemove && i < unusedDefs.Count; i++)
                {
                    if (Utility_CacheManager._modExtensionCache.TryRemove(unusedDefs[i], out _))
                        removed++;
                }

                if (Prefs.DevMode)
                {
                    Utility_DebugManager.LogNormal($"Cleaned up {removed} unused mod extension caches");
                }
            }

            // Clear temp collections
            usedRaces.Clear();
            unusedDefs.Clear();
        }

        /// <summary>
        /// 모든 캐시 리셋
        /// </summary>
        private void ResetAllCaches(string resetMode)
        {
            // Core reset using the unified cache
            Utility_CacheManager.Clear();

            // Reset other specialized subsystems
            Utility_DrafterManager.ResetTrackerTracking();
            Utility_ThinkTreeManager.ResetCache();

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

        #endregion

        #region Runtime Extension Management

        /// <summary>
        /// Runtime Extension 복구 - 성능 최적화
        /// </summary>
        private void RestoreRuntimeModExtensions()
        {
            if (runtimeModExtensions.IsEmpty)
                return;

            int restored = 0;

            // ConcurrentDictionary<string, NonHumanlikePawnControlExtension>를 순회
            foreach (var kvp in runtimeModExtensions)
            {
                try
                {
                    string defName = kvp.Key;
                    NonHumanlikePawnControlExtension extension = kvp.Value;

                    ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                    if (def == null)
                    {
                        Utility_DebugManager.LogWarning($"Could not find ThingDef {defName} to restore mod extension");
                        continue;
                    }

                    if (def.modExtensions == null)
                        def.modExtensions = new List<DefModExtension>();
                    else
                    {
                        def.modExtensions.RemoveAll(ext => ext is NonHumanlikePawnControlExtension);
                    }

                    def.modExtensions.Add(extension); // 바로 extension 사용
                    extension.fromXML = false;

                    extension.CacheSimulatedSkillLevels();
                    extension.CacheSkillPassions();

                    Utility_CacheManager.ClearModExtension(def);
                    Utility_CacheManager.PreloadModExtensionForRace(def);

                    ApplyExtensionToExistingPawns(def, extension); // 바로 extension 사용

                    restored++;
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"Error restoring mod extension for {kvp.Key}: {ex}");
                }
            }

            Utility_DebugManager.LogNormal($"Restored {restored} runtime mod extensions from save file");
        }

        /// <summary>
        /// Runtime mod extension 저장
        /// </summary>
        public void SaveRuntimeModExtension(ThingDef def, NonHumanlikePawnControlExtension extension)
        {
            if (def == null || extension == null) return;
            // AddOrUpdate를 사용하면 추가/수정이 한 번에 처리됩니다.
            runtimeModExtensions[def.defName] = extension;
            Utility_DebugManager.LogNormal($"Saved runtime mod extension for {def.defName}");
        }

        /// <summary>
        /// Runtime mod extension 제거
        /// </summary>
        public void RemoveRuntimeModExtension(ThingDef def)
        {
            if (def == null) return;
            // TryRemove로 안전하게 제거
            if (runtimeModExtensions.TryRemove(def.defName, out _))
            {
                Utility_DebugManager.LogNormal($"Removed runtime mod extension for {def.defName}");
            }
        }

        /// <summary>
        /// Runtime mod extension 조회 - 성능 최적화
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private NonHumanlikePawnControlExtension GetRuntimeModExtensionForRace(ThingDef def)
        {
            if (runtimeModExtensions.TryGetValue(def.defName, out var extension))
            {
                return extension;
            }
            return null;
        }

        #endregion

        #region Pawn Processing

        /// <summary>
        /// Drafter 안전 주입
        /// </summary>
        private void InjectDraftersSafely()
        {
            foreach (var map in Find.Maps)
            {
                Utility_DrafterManager.InjectDraftersIntoMapPawns(map);

                // Give every non-human pawn its equipment/apparel/inventory
                var allPawns = map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < allPawns.Count; i++)
                {
                    var pawn = allPawns[i];
                    var modExtension = Utility_CacheManager.GetModExtension(pawn.def);

                    if (modExtension == null || pawn.RaceProps.Humanlike)
                        continue;

                    if (pawn.drafter == null)
                        continue;

                    Utility_DrafterManager.EnsureAllTrackers(pawn);
                }
            }

            drafterAlreadyInjected = true;
        }

        /// <summary>
        /// 기존 Pawn들에게 Extension 적용 - 리플렉션 캐싱으로 성능 향상
        /// </summary>
        private void ApplyExtensionToExistingPawns(ThingDef raceDef, NonHumanlikePawnControlExtension modExtension)
        {
            if (raceDef == null || modExtension == null || Find.Maps == null)
                return;

            int updatedCount = 0;

            // First, apply race-level changes
            modExtension.originalMainWorkThinkTreeDefName = raceDef.race.thinkTreeMain?.defName;
            modExtension.originalConstantThinkTreeDefName = raceDef.race.thinkTreeConstant?.defName;

            // Apply static think tree changes at race level if specified
            ApplyRaceLevelThinkTrees(raceDef, modExtension);

            // Process all maps
            for (int mapIndex = 0; mapIndex < Find.Maps.Count; mapIndex++)
            {
                var map = Find.Maps[mapIndex];
                if (map?.mapPawns?.AllPawnsSpawned == null) continue;

                var allPawns = map.mapPawns.AllPawnsSpawned;
                for (int pawnIndex = 0; pawnIndex < allPawns.Count; pawnIndex++)
                {
                    var pawn = allPawns[pawnIndex];
                    if (pawn?.def != raceDef || pawn.Dead || pawn.Destroyed) continue;

                    try
                    {
                        ApplyExtensionToPawn(pawn, modExtension);
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

        /// <summary>
        /// Race 수준의 Think Tree 적용
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static void ApplyRaceLevelThinkTrees(ThingDef raceDef, NonHumanlikePawnControlExtension modExtension)
        {
            if (!string.IsNullOrEmpty(modExtension.mainWorkThinkTreeDefName))
            {
                var thinkTree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(modExtension.mainWorkThinkTreeDefName);
                if (thinkTree != null)
                {
                    raceDef.race.thinkTreeMain = thinkTree;
                }
            }

            if (!string.IsNullOrEmpty(modExtension.constantThinkTreeDefName))
            {
                var constantTree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(modExtension.constantThinkTreeDefName);
                if (constantTree != null)
                {
                    raceDef.race.thinkTreeConstant = constantTree;
                }
            }
        }

        /// <summary>
        /// 개별 Pawn에게 Extension 적용 - 캐시된 리플렉션 사용
        /// </summary>
        private void ApplyExtensionToPawn(Pawn pawn, NonHumanlikePawnControlExtension modExtension)
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

            // Apply think trees using cached reflection
            if (pawn.mindState != null)
            {
                ApplyThinkTreesToMindState(pawn, modExtension);
            }
        }

        /// <summary>
        /// MindState에 Think Tree 적용 - 캐시된 리플렉션으로 성능 향상
        /// </summary>
        private void ApplyThinkTreesToMindState(Pawn pawn, NonHumanlikePawnControlExtension modExtension)
        {
            var mindStateType = pawn.mindState.GetType();
            var reflectionCache = GetReflectionCache(mindStateType);

            // Apply main think tree
            if (!string.IsNullOrEmpty(modExtension.mainWorkThinkTreeDefName))
            {
                var thinkTree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(modExtension.mainWorkThinkTreeDefName);
                if (thinkTree != null && reflectionCache.ThinkTreeField != null)
                {
                    reflectionCache.ThinkTreeField.SetValue(pawn.mindState, thinkTree);
                    Utility_DebugManager.LogNormal($"Set main think tree {thinkTree.defName} for {pawn.LabelShort}");
                }
            }

            // Apply constant think tree if needed
            if (!string.IsNullOrEmpty(modExtension.constantThinkTreeDefName))
            {
                var constantTree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(modExtension.constantThinkTreeDefName);
                if (constantTree != null && reflectionCache.ConstantThinkTreeField != null)
                {
                    reflectionCache.ConstantThinkTreeField.SetValue(pawn.mindState, constantTree);
                    Utility_DebugManager.LogNormal($"Set constant think tree {constantTree.defName} for {pawn.LabelShort}");
                }
            }

            // Force think node rebuild using cached reflection
            if (pawn.thinker != null)
            {
                var thinkerType = pawn.thinker.GetType();
                var thinkerCache = GetReflectionCache(thinkerType);

                if (thinkerCache.ThinkRootField != null)
                {
                    // Null out the think root to force a rebuild
                    thinkerCache.ThinkRootField.SetValue(pawn.thinker, null);
                }
            }
        }

        #endregion

        #region Pending Removals Processing

        /// <summary>
        /// 제거 대기 중인 Extension 처리 - LINQ 제거
        /// </summary>
        private void ProcessPendingRemovals()
        {
            try
            {
                var defsToRemove = TempUnusedDefs; // ThingDef 전용 임시 리스트 사용
                defsToRemove.Clear();
                int removedCount = 0;

                // 1. 제거할 Def 목록 수집
                foreach (var kvp in Utility_CacheManager._modExtensionCache)
                {
                    if (kvp.Value?.toBeRemoved == true)
                    {
                        defsToRemove.Add(kvp.Key);
                    }
                }

                if (defsToRemove.Count == 0) return;

                // 2. 수집된 Def들에 대해 제거 작업 수행
                for (int i = 0; i < defsToRemove.Count; i++)
                {
                    var def = defsToRemove[i];
                    var modExtension = Utility_CacheManager.GetModExtension(def);

                    if (modExtension != null)
                    {
                        // 모든 폰의 ThinkTree를 원래대로 복원
                        RestoreThinkTreesForAllPawns(def, modExtension.originalMainWorkThinkTreeDefName, modExtension.originalConstantThinkTreeDefName);

                        // Def의 modExtensions 리스트에서 직접 제거
                        if (def.modExtensions != null)
                        {
                            def.modExtensions.Remove(modExtension);
                        }

                        // 런타임 저장 목록에서도 제거
                        RemoveRuntimeModExtension(def); // 이 메서드도 수정 필요 (아래 문제 2 참조)

                        // 캐시에서 제거
                        Utility_CacheManager.ClearModExtension(def);
                        removedCount++;
                    }
                }

                if (removedCount > 0)
                {
                    Utility_DebugManager.LogNormal($"Removed {removedCount} mod extensions marked for removal.");
                }
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error processing mod extension removals: {ex}");
            }
            finally
            {
                TempUnusedDefs.Clear(); // 작업 후 리스트 비우기
            }
        }

        /// <summary>
        /// 모든 Pawn들의 Think Tree 복원
        /// </summary>
        private void RestoreThinkTreesForAllPawns(ThingDef raceDef, string originalMainTree, string originalConstTree)
        {
            if (raceDef == null || Find.Maps == null) return;

            int updated = 0;

            // Process all maps
            foreach (Map map in Find.Maps)
            {
                if (map?.mapPawns?.AllPawnsSpawned == null) continue;

                var allPawns = map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < allPawns.Count; i++)
                {
                    var pawn = allPawns[i];
                    if (pawn?.def != raceDef || pawn.Dead || pawn.Destroyed) continue;

                    // Use the existing utility method to restore think trees
                    Utility_ThinkTreeManager.ResetThinkTreeToVanilla(pawn, originalMainTree, originalConstTree);
                    updated++;
                }
            }

            // Also process world pawns - LINQ 최소화
            if (Find.World?.worldPawns?.AllPawnsAliveOrDead != null)
            {
                var worldPawns = Find.World.worldPawns.AllPawnsAliveOrDead;
                for (int i = 0; i < worldPawns.Count; i++)
                {
                    var pawn = worldPawns[i];
                    if (pawn != null && !pawn.Dead && pawn.def == raceDef)
                    {
                        Utility_ThinkTreeManager.ResetThinkTreeToVanilla(pawn, originalMainTree, originalConstTree);
                        updated++;
                    }
                }
            }

            if (updated > 0)
            {
                Utility_DebugManager.LogNormal($"Reset think trees for {updated} pawns of race {raceDef.defName}");
            }
        }

        #endregion

        #region Save/Load

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
            var tempList = runtimeModExtensions.ToList();
            Scribe_Collections.Look(ref tempList, "runtimeModExtensions", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                runtimeModExtensions.Clear();
                if (tempList != null)
                {
                    foreach (var kvp in tempList)
                    {
                        runtimeModExtensions.TryAdd(kvp.Key, kvp.Value);
                    }
                }
            }
        }

        #endregion

        #region Static Cache Cleanup

        /// <summary>
        /// 정적 캐시들을 정리합니다 (게임 종료시 호출)
        /// </summary>
        public static void CleanupStaticCaches()
        {
            _reflectionCache.Clear();

            // ThreadStatic 필드들은 자동으로 정리되지만 명시적으로 null 처리
            _tempUsedRaces = null;
            _tempUnusedDefs = null;
            _tempPawnList = null;
        }

        #endregion
    }
}