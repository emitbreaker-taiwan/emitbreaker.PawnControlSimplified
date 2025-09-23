using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Verse;

namespace emitbreaker.PawnControl
{
    public static class Utility_WorkSettingsManager
    {
        #region Thread-Safe Collections and Cached Reflection

        // Thread-safe replacement tracking
        private static readonly ConcurrentHashSet<Pawn> _workgiverReplacementDone = new ConcurrentHashSet<Pawn>();

        // Cached reflection info - 리플렉션 성능 대폭 향상
        private static readonly ConcurrentDictionary<Type, ReflectionCache> _reflectionCache = new ConcurrentDictionary<Type, ReflectionCache>();

        // Cached WorkGiverDef list - 매번 DefDatabase 순회 방지
        private static List<WorkGiverDef> _cachedWorkGiverDefs;
        private static readonly object _workGiverDefsLock = new object();

        /// <summary>
        /// 리플렉션 정보를 캐시하는 클래스
        /// </summary>
        private class ReflectionCache
        {
            public MethodInfo CacheWorkGiversMethod { get; set; }
            public FieldInfo NormalWorkGiversField { get; set; }
            public FieldInfo EmergencyWorkGiversField { get; set; }

            public ReflectionCache(Type type)
            {
                try
                {
                    CacheWorkGiversMethod = AccessTools.Method(type, "CacheWorkGiversInOrder");
                    NormalWorkGiversField = AccessTools.Field(type, "workGiversInOrderNormal");
                    EmergencyWorkGiversField = AccessTools.Field(type, "workGiversInOrderEmergency");
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ReflectionCache GetReflectionCache(Type type)
        {
            return _reflectionCache.GetOrAdd(type, t => new ReflectionCache(t));
        }

        /// <summary>
        /// WorkGiverDef 리스트를 캐시에서 가져오거나 생성
        /// </summary>
        private static List<WorkGiverDef> GetCachedWorkGiverDefs()
        {
            if (_cachedWorkGiverDefs == null)
            {
                lock (_workGiverDefsLock)
                {
                    if (_cachedWorkGiverDefs == null)
                    {
                        _cachedWorkGiverDefs = new List<WorkGiverDef>(DefDatabase<WorkGiverDef>.AllDefsListForReading);
                    }
                }
            }
            return _cachedWorkGiverDefs;
        }

        #endregion

        #region Main Initialization Methods - 최적화된 버전

        /// <summary>
        /// Fully initializes a pawn's work settings, think tree, and workgivers for modded behavior.
        /// 최적화된 버전 - 불필요한 중복 체크 제거, 성능 향상
        /// </summary>
        public static void InitializePawnWorksetting(Pawn pawn, bool forceLock = true, string subtreeDefName = null)
        {
            if (!Utility_Common.PawnChecker(pawn))
                return;

            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null)
                return;

            if (!Utility_ThinkTreeManager.HasAllowOrBlockWorkTag(pawn))
                return;

            // 조건부 처리로 성능 향상
            EnsureWorkSettingsInitializedFast(pawn);
            EnsureWorkGiversPopulatedFast(pawn);

            Utility_DebugManager.LogNormal($"Completed FullInitializePawn for {pawn.LabelShortCap} (forceLock={forceLock})");

            // 개발 모드에서만 검증 수행
            if (Prefs.DevMode && modExtension.debugMode && pawn?.thinker?.MainThinkNodeRoot != null)
            {
                Utility_ThinkTreeManager.ValidateThinkTree(pawn);
            }
        }

        /// <summary>
        /// 모든 적격 Pawn들을 효율적으로 초기화 - 최적화된 버전
        /// </summary>
        public static void FullInitializeAllEligiblePawns(Map map, bool forceLock = true, string subtreeDefName = null)
        {
            if (map?.mapPawns == null)
                return;

            var pawns = map.mapPawns.AllPawnsSpawned;
            int processedCount = 0;

            // 직접 for 루프로 성능 향상
            for (int i = 0; i < pawns.Count; i++)
            {
                var pawn = pawns[i];
                if (pawn?.def?.race == null)
                    continue;

                if (!Utility_ThinkTreeManager.HasAllowOrBlockWorkTag(pawn))
                    continue;

                var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                if (modExtension == null)
                    continue;

                // Static ThinkTree가 이미 할당된 경우 건너뛰기
                if (!string.IsNullOrEmpty(modExtension.mainWorkThinkTreeDefName))
                {
                    if (Utility_DebugManager.ShouldLogDetailed())
                        Utility_DebugManager.LogNormal($"Skipping InitializePawnWorksetting for {pawn.LabelShortCap} (static ThinkTree '{modExtension.mainWorkThinkTreeDefName}' already assigned).");
                    continue;
                }

                try
                {
                    InitializePawnWorksetting(pawn, forceLock, subtreeDefName);
                    processedCount++;
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogWarning($"InitializePawnWorksetting failed for {pawn?.LabelShortCap ?? "unknown pawn"}: {ex.Message}");
                }
            }

            Utility_DebugManager.LogNormal($"Completed FullInitializeAllEligiblePawns on map {map.Index} - processed {processedCount} pawns (forceLock={forceLock}).");
        }

        #endregion

        #region Fast Initialization Methods - 성능 최적화

        /// <summary>
        /// WorkSettings 초기화 - 최적화된 고속 버전
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EnsureWorkSettingsInitializedFast(Pawn pawn)
        {
            // 기본 가드
            if (pawn == null || pawn.def?.race == null) return;

            var modExt = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExt == null) return; // 우리 대상 아님

            // 스킬이 필요하면 먼저 부착 (Passion/tooltip 등 의존성 방지)
            if (Utility_SkillManager.ShouldHaveSkills(pawn))
                Utility_SkillManager.ForceAttachSkillTrackerIfMissing(pawn);

            // 없으면 생성, 있으면 재사용
            if (pawn.workSettings == null)
            {
                Utility_DebugManager.LogWarning($"WorkSettings missing for {pawn.LabelShort}. Creating and initializing.");
                pawn.workSettings = new Pawn_WorkSettings(pawn);
            }

            // 초기화 보장 (이미 있더라도 EverWork/Initialized 깨져 있을 수 있음)
            if (!pawn.workSettings.EverWork || !pawn.workSettings.Initialized)
                pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
        }

        /// <summary>
        /// WorkGivers 채우기 - 최적화된 고속 버전
        /// </summary>
        private static void EnsureWorkGiversPopulatedFast(Pawn pawn)
        {
            if (pawn?.workSettings == null)
                return;

            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null)
                return;

            // 빠른 조건 체크
            var normalList = pawn.workSettings.WorkGiversInOrderNormal;
            var emergencyList = pawn.workSettings.WorkGiversInOrderEmergency;

            bool needsRebuild = (normalList?.Count ?? 0) == 0 || (emergencyList?.Count ?? 0) == 0;

            if (!needsRebuild)
                return;

            try
            {
                // 먼저 일반적인 초기화 시도
                if (!pawn.workSettings.Initialized)
                {
                    Utility_DebugManager.LogWarning($"WorkSettings not initialized for {pawn.LabelCap}. Initializing...");
                    pawn.workSettings.EnableAndInitialize();
                }

                // 여전히 비어있으면 캐시된 리플렉션으로 강제 재구축
                normalList = pawn.workSettings.WorkGiversInOrderNormal;
                emergencyList = pawn.workSettings.WorkGiversInOrderEmergency;

                if ((normalList?.Count ?? 0) == 0 || (emergencyList?.Count ?? 0) == 0)
                {
                    Utility_DebugManager.LogWarning($"WorkGiver lists still empty after initialization for {pawn.LabelCap}. Forcing rebuild...");
                    RebuildWorkGiversUsingReflection(pawn);
                }
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Exception in EnsureWorkGiversPopulatedFast: {ex}");
            }
        }

        /// <summary>
        /// 캐시된 리플렉션을 사용한 WorkGiver 재구축 - 성능 최적화
        /// </summary>
        private static void RebuildWorkGiversUsingReflection(Pawn pawn)
        {
            var workSettingsType = typeof(Pawn_WorkSettings);
            var reflectionCache = GetReflectionCache(workSettingsType);

            // 캐시된 리플렉션 메서드 호출
            if (reflectionCache.CacheWorkGiversMethod != null)
            {
                reflectionCache.CacheWorkGiversMethod.Invoke(pawn.workSettings, null);
                Utility_DebugManager.LogNormal($"Forced WorkGiver cache rebuild for {pawn.LabelCap}");

                // 여전히 비어있으면 수동으로 생성
                var normalList = pawn.workSettings.WorkGiversInOrderNormal;
                if ((normalList?.Count ?? 0) == 0)
                {
                    CreateWorkGiverListsManually(pawn, reflectionCache);
                }
            }
        }

        /// <summary>
        /// 수동으로 WorkGiver 리스트 생성 - 캐시 활용으로 성능 향상
        /// </summary>
        private static void CreateWorkGiverListsManually(Pawn pawn, ReflectionCache reflectionCache)
        {
            if (reflectionCache.NormalWorkGiversField == null || reflectionCache.EmergencyWorkGiversField == null)
                return;

            Utility_DebugManager.LogWarning($"Creating new WorkGiver lists for {pawn.LabelCap}");

            var newNormalList = new List<WorkGiver>();
            var newEmergencyList = new List<WorkGiver>();

            // 캐시된 WorkGiverDef 리스트 사용
            var cachedDefs = GetCachedWorkGiverDefs();

            for (int i = 0; i < cachedDefs.Count; i++)
            {
                var def = cachedDefs[i];
                if (pawn.workSettings.GetPriority(def.workType) > 0)
                {
                    newNormalList.Add(def.Worker);
                    if (def.emergency)
                        newEmergencyList.Add(def.Worker);
                }
            }

            reflectionCache.NormalWorkGiversField.SetValue(pawn.workSettings, newNormalList);
            reflectionCache.EmergencyWorkGiversField.SetValue(pawn.workSettings, newEmergencyList);

            Utility_DebugManager.LogNormal($"Created new WorkGiver lists with {newNormalList.Count} normal and {newEmergencyList.Count} emergency givers");
        }

        #endregion

        #region Public Interface Methods - 최적화된 버전

        /// <summary>
        /// WorkSettings 초기화 보장 - 공개 인터페이스
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EnsureWorkSettingsInitialized(Pawn pawn)
        {
            EnsureWorkSettingsInitializedFast(pawn);
        }

        /// <summary>
        /// 안전한 Pawn 작업 준비 - 스레드 안전성 보장
        /// </summary>
        public static void SafeEnsurePawnReadyForWork(Pawn pawn)
        {
            if (pawn?.def?.race == null)
                return;

            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null)
                return;

            if (!Utility_ThinkTreeManager.HasAllowOrBlockWorkTag(pawn))
                return;

            // Thread-safe 체크
            if (_workgiverReplacementDone.Contains(pawn))
                return;

            // WorkSettings가 없거나 초기화되지 않은 경우
            if (pawn.workSettings == null || !pawn.workSettings.EverWork)
            {
                EnsureWorkSettingsInitializedFast(pawn);
                Utility_DebugManager.LogWarning($"SafeEnsurePawnReadyForWork: WorkSettings recreated for {pawn.LabelCap}.");
            }

            Utility_DebugManager.LogNormal($"SafeEnsurePawnReadyForWork completed for {pawn.LabelCap}.");

            // Thread-safe 추가
            _workgiverReplacementDone.Add(pawn);
        }

        /// <summary>
        /// WorkGivers 채우기 보장 - 공개 인터페이스
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EnsureWorkGiversPopulated(Pawn pawn)
        {
            EnsureWorkGiversPopulatedFast(pawn);
        }

        #endregion

        #region Cache Management

        /// <summary>
        /// 캐시된 데이터들을 정리합니다
        /// </summary>
        public static void ClearCaches()
        {
            _workgiverReplacementDone.Clear();
            _cachedWorkGiverDefs = null;

            Utility_DebugManager.LogNormal("Work settings manager caches cleared.");
        }

        /// <summary>
        /// 정적 캐시들을 정리합니다 (게임 종료시 호출)
        /// </summary>
        public static void CleanupStaticCaches()
        {
            _reflectionCache.Clear();
            _workgiverReplacementDone.Clear();
            _cachedWorkGiverDefs = null;
        }

        public static void RemovePawnFromCache(Pawn pawn)
        {
            if (pawn == null) return;

            _workgiverReplacementDone.Remove(pawn);
        }

        #endregion
    }

    #region Thread-Safe HashSet Implementation

    /// <summary>
    /// Thread-safe HashSet 구현 - ConcurrentHashSet가 없는 경우 대안
    /// </summary>
    public class ConcurrentHashSet<T>
    {
        private readonly HashSet<T> _hashSet = new HashSet<T>();
        private readonly object _lock = new object();

        public bool Add(T item)
        {
            lock (_lock)
            {
                return _hashSet.Add(item);
            }
        }

        public bool Contains(T item)
        {
            lock (_lock)
            {
                return _hashSet.Contains(item);
            }
        }

        public bool Remove(T item)
        {
            lock (_lock)
            {
                return _hashSet.Remove(item);
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _hashSet.Clear();
            }
        }

        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _hashSet.Count;
                }
            }
        }
    }

    #endregion
}