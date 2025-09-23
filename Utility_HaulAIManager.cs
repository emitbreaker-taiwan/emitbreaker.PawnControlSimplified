using RimWorld;
using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// 🟢 최적화된 운반 AI 매니저 - 200개체+ 대응 최적화
    /// Bypasses HaulAIUtility's humanlike & manipulation checks so animals and mechanoids can haul.
    /// </summary>
    public static class Utility_HaulAIManager
    {
        #region Thread-Safe Caches - 🟢 핵심 성능 최적화

        /// <summary>운반 가능성 캐시 - (pawnID, thingID, forced) → bool</summary>
        private static readonly ConcurrentDictionary<ValueTuple<int, int, bool>, CachedHaulResult> _haulabilityCache =
            new ConcurrentDictionary<ValueTuple<int, int, bool>, CachedHaulResult>();

        /// <summary>접근성 캐시 - (pawnID, positionHash) → bool</summary>
        private static readonly ConcurrentDictionary<ValueTuple<int, int>, CachedReachResult> _reachabilityCache =
            new ConcurrentDictionary<ValueTuple<int, int>, CachedReachResult>();

        /// <summary>저장소 진단 결과 캐시 - thingDefName → StorageDiagnostic</summary>
        private static readonly ConcurrentDictionary<string, StorageDiagnostic> _storageDiagnosticsCache =
            new ConcurrentDictionary<string, StorageDiagnostic>();

        #endregion

        #region Cache Data Structures

        private struct CachedHaulResult
        {
            public bool CanHaul;
            public int CachedTick;
            public string FailureReason; // 디버깅용

            public CachedHaulResult(bool canHaul, int cachedTick, string failureReason = null)
            {
                CanHaul = canHaul;
                CachedTick = cachedTick;
                FailureReason = failureReason;
            }
        }

        private struct CachedReachResult
        {
            public bool CanReach;
            public int CachedTick;

            public CachedReachResult(bool canReach, int cachedTick)
            {
                CanReach = canReach;
                CachedTick = cachedTick;
            }
        }

        private struct StorageDiagnostic
        {
            public int AcceptingStorageCount;
            public bool BetterStorageExists;
            public int CachedTick;

            public StorageDiagnostic(int acceptingCount, bool betterExists, int cachedTick)
            {
                AcceptingStorageCount = acceptingCount;
                BetterStorageExists = betterExists;
                CachedTick = cachedTick;
            }
        }

        #endregion

        #region Cache Management - 🟢 메모리 효율성 보장

        private const int MAX_CACHE_SIZE = 1000;
        private const int CACHE_VALIDITY_TICKS = 120; // 2초 (120 ticks)

        private static int _lastCacheCleanupTick = 0;

        /// <summary>주기적 캐시 정리</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void MaintainCaches()
        {
            int currentTick = Find.TickManager.TicksGame;

            if (currentTick - _lastCacheCleanupTick < 600) return; // 10초마다 정리
            _lastCacheCleanupTick = currentTick;

            // 크기 기반 정리
            if (_haulabilityCache.Count > MAX_CACHE_SIZE)
                _haulabilityCache.Clear();

            if (_reachabilityCache.Count > MAX_CACHE_SIZE)
                _reachabilityCache.Clear();

            if (_storageDiagnosticsCache.Count > MAX_CACHE_SIZE / 2)
                _storageDiagnosticsCache.Clear();
        }

        /// <summary>캐시 유효성 검사</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsCacheValid(int cachedTick)
        {
            return Find.TickManager.TicksGame - cachedTick < CACHE_VALIDITY_TICKS;
        }

        #endregion

        #region Optimized Core Methods

        /// <summary>
        /// 🟢 최적화된 운반 가능성 검사 - 캐시 기반 고성능 처리
        /// Like HaulAIUtility.PawnCanAutomaticallyHaulFast, but skips the Manipulation-capacity test.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool PawnCanAutomaticallyHaulFast(Pawn pawn, Thing t, bool forced)
        {
            // 🟢 빠른 경로: 기본 검증
            if (pawn?.Map == null || t?.Map == null || t.Map != pawn.Map)
                return false;

            // 🟢 캐시 키 생성
            var cacheKey = ValueTuple.Create(pawn.thingIDNumber, t.thingIDNumber, forced);

            // 🟢 캐시 확인
            if (_haulabilityCache.TryGetValue(cacheKey, out var cachedResult) && IsCacheValid(cachedResult.CachedTick))
            {
                // 캐시된 실패 이유 로깅 (디버그 모드에서만)
                if (!cachedResult.CanHaul && Utility_DebugManager.ShouldLogDetailed() && !string.IsNullOrEmpty(cachedResult.FailureReason))
                    Utility_DebugManager.LogNormal($"Cached result for {pawn.LabelShort} → {t.Label}: {cachedResult.FailureReason}");

                return cachedResult.CanHaul;
            }

            // 🟢 캐시 미스시 실제 계산 수행
            var result = CalculateHaulabilityInternal(pawn, t, forced);

            // 🟢 결과 캐시 저장
            var cacheEntry = new CachedHaulResult(result.CanHaul, Find.TickManager.TicksGame, result.FailureReason);
            _haulabilityCache.TryAdd(cacheKey, cacheEntry);

            return result.CanHaul;
        }

        /// <summary>🟢 내부 운반 가능성 계산 로직</summary>
        private static (bool CanHaul, string FailureReason) CalculateHaulabilityInternal(Pawn pawn, Thing t, bool forced)
        {
            // 🟢 UnfinishedThing 체크 최적화
            if (t is UnfinishedThing ut)
            {
                if (!ValidateUnfinishedThing(ut))
                    return (false, "Unfinished thing validation failed");
            }

            // 🟢 접근성 검사 - 캐시된 결과 사용
            if (!CanReachCached(pawn, t))
                return (false, $"Cannot reach {t.Position}");

            // 🟢 예약 가능성 검사
            if (!pawn.CanReserve(t, ignoreOtherReservations: forced))
            {
                string reservationInfo = GetReservationInfo(pawn, t);
                return (false, $"Cannot reserve{reservationInfo}");
            }

            // 🟢 사회적 적절성 검사 (음식)
            if (IsFoodAndNotSociallyProper(pawn, t))
                return (false, "Food not socially proper");

            // 🟢 화재 검사
            if (t.IsBurning())
                return (false, "Thing is burning");

            return (true, null);
        }

        /// <summary>🟢 캐시된 접근성 검사</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool CanReachCached(Pawn pawn, Thing t)
        {
            var reachKey = ValueTuple.Create(pawn.thingIDNumber, t.Position.GetHashCode());

            if (_reachabilityCache.TryGetValue(reachKey, out var cached) && IsCacheValid(cached.CachedTick))
                return cached.CanReach;

            bool canReach = pawn.CanReach(t, PathEndMode.ClosestTouch, pawn.NormalMaxDanger());
            var cacheEntry = new CachedReachResult(canReach, Find.TickManager.TicksGame);
            _reachabilityCache.TryAdd(reachKey, cacheEntry);

            return canReach;
        }

        /// <summary>🟢 UnfinishedThing 검증 최적화</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ValidateUnfinishedThing(UnfinishedThing ut)
        {
            var bill = ut.BoundBill;
            if (bill?.billStack?.billGiver is Building giver)
            {
                return giver.Spawned && giver.OccupiedRect().ExpandedBy(1).Contains(ut.Position);
            }
            return true; // 바운드 빌이 없으면 통과
        }

        /// <summary>🟢 예약 정보 취득 최적화</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string GetReservationInfo(Pawn pawn, Thing t)
        {
            if (Utility_DebugManager.ShouldLog() && pawn.Map?.reservationManager != null)
            {
                var reserver = pawn.Map.reservationManager.FirstRespectedReserver(t, pawn);
                return reserver != null ? $" (reserved by {reserver.LabelShort})" : "";
            }
            return "";
        }

        /// <summary>🟢 음식 사회적 적절성 검사 최적화</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsFoodAndNotSociallyProper(Pawn pawn, Thing t)
        {
            return t.def.IsNutritionGivingIngestible &&
                   t.def.ingestible.HumanEdible &&
                   !t.IsSociallyProper(pawn, false, true);
        }

        /// <summary>
        /// 🟢 최적화된 운반 작업 생성
        /// Passes through to vanilla HaulToStorageJob (no additional guards).
        /// </summary>
        public static Job HaulToStorageJob(Pawn pawn, Thing t)
        {
            // 🟢 조건부 로깅으로 성능 향상
            bool shouldLog = Utility_DebugManager.ShouldLog();

            if (shouldLog)
                Utility_DebugManager.LogNormal($"Creating haul job for {pawn.LabelShort} to haul {t.Label} ({t.def.defName})");

            Job job = HaulAIUtility.HaulToStorageJob(pawn, t);

            // 🟢 성공/실패 로깅 최적화
            if (shouldLog)
            {
                LogJobCreationResult(pawn, t, job);
            }

            return job;
        }

        /// <summary>🟢 작업 생성 결과 로깅 분리</summary>
        private static void LogJobCreationResult(Pawn pawn, Thing t, Job job)
        {
            if (job != null)
            {
                var storeCell = job.targetB.Cell;
                Utility_DebugManager.LogNormal($"Created haul job: {job.def.defName} for {t.Label} to {storeCell}");

                // 🟢 저장소 정보 로깅 최적화
                if (storeCell.IsValid && pawn.Map != null)
                {
                    var slotGroup = pawn.Map.haulDestinationManager.SlotGroupAt(storeCell);
                    if (slotGroup?.parent != null)
                        Utility_DebugManager.LogNormal($"Storage destination: {slotGroup.parent}");
                }
            }
            else
            {
                Utility_DebugManager.LogWarning($"Failed to create haul job for {pawn.LabelShort} to haul {t.Label}");
                LogJobFailureContext(pawn, t);
            }
        }

        /// <summary>🟢 작업 실패 컨텍스트 로깅</summary>
        private static void LogJobFailureContext(Pawn pawn, Thing t)
        {
            bool betterStoreExists = StoreUtility.TryFindBestBetterStoreCellFor(
                t, pawn, pawn.Map, StoreUtility.CurrentStoragePriorityOf(t), pawn.Faction, out IntVec3 _);

            Utility_DebugManager.LogNormal(betterStoreExists
                ? "Better storage location exists but job creation failed"
                : "No better storage location found");
        }

        /// <summary>
        /// 🟢 최적화된 저장소 진단 - 캐시 기반 처리
        /// Diagnostic method to check storage options for a thing
        /// </summary>
        public static void DiagnoseStorageOptions(Pawn pawn, Thing t)
        {
            if (!Utility_DebugManager.ShouldLog()) return;

            // 🟢 캐시된 진단 결과 사용
            var diagnostic = GetCachedStorageDiagnostic(pawn, t);

            Utility_DebugManager.LogNormal($"=== Storage diagnosis for {t.Label} ===");
            Utility_DebugManager.LogNormal($"Current cell: {t.Position}");
            Utility_DebugManager.LogNormal($"Current storage priority: {StoreUtility.CurrentStoragePriorityOf(t)}");
            Utility_DebugManager.LogNormal($"Is forbidden: {t.IsForbidden(pawn)}");
            Utility_DebugManager.LogNormal($"Better storage found: {diagnostic.BetterStorageExists}");
            Utility_DebugManager.LogNormal($"Total accepting storage options: {diagnostic.AcceptingStorageCount}");
            Utility_DebugManager.LogNormal("=== End storage diagnosis ===");
        }

        /// <summary>🟢 캐시된 저장소 진단 결과 취득</summary>
        private static StorageDiagnostic GetCachedStorageDiagnostic(Pawn pawn, Thing t)
        {
            var cacheKey = t.def.defName;

            if (_storageDiagnosticsCache.TryGetValue(cacheKey, out var cached) && IsCacheValid(cached.CachedTick))
                return cached;

            // 캐시 미스시 계산 수행
            var diagnostic = CalculateStorageDiagnostic(pawn, t);
            _storageDiagnosticsCache.TryAdd(cacheKey, diagnostic);

            return diagnostic;
        }

        /// <summary>🟢 저장소 진단 계산</summary>
        private static StorageDiagnostic CalculateStorageDiagnostic(Pawn pawn, Thing t)
        {
            bool betterStoreExists = StoreUtility.TryFindBestBetterStoreCellFor(
                t, pawn, pawn.Map, StoreUtility.CurrentStoragePriorityOf(t), pawn.Faction, out IntVec3 _);

            int acceptingCount = 0;
            var allGroups = pawn.Map.haulDestinationManager.AllGroupsListForReading;

            // 🟢 최적화된 저장소 카운팅 - 조기 종료 적용
            for (int i = 0; i < allGroups.Count; i++)
            {
                if (allGroups[i].parent.Accepts(t))
                {
                    acceptingCount++;
                    // 🟢 디버그 로깅시에만 상세 정보 출력 (최대 5개까지만)
                    if (acceptingCount <= 5 && Utility_DebugManager.ShouldLog())
                    {
                        Utility_DebugManager.LogNormal($"Accepting storage: {allGroups[i].parent} (priority {allGroups[i].Settings.Priority})");
                    }
                }
            }

            return new StorageDiagnostic(acceptingCount, betterStoreExists, Find.TickManager.TicksGame);
        }

        #endregion

        #region Public Utility Methods

        /// <summary>캐시 무효화 - 맵 변경이나 저장소 설정 변경시 호출</summary>
        public static void InvalidateAllCaches()
        {
            _haulabilityCache.Clear();
            _reachabilityCache.Clear();
            _storageDiagnosticsCache.Clear();
        }

        /// <summary>특정 아이템의 캐시 무효화</summary>
        public static void InvalidateThingCaches(Thing t)
        {
            if (t?.thingIDNumber == null) return;

            // 해당 아이템과 관련된 캐시만 선택적으로 제거
            var keysToRemove = new System.Collections.Generic.List<ValueTuple<int, int, bool>>();

            foreach (var kvp in _haulabilityCache)
            {
                if (kvp.Key.Item2 == t.thingIDNumber)
                    keysToRemove.Add(kvp.Key);
            }

            for (int i = 0; i < keysToRemove.Count; i++)
            {
                _haulabilityCache.TryRemove(keysToRemove[i], out _);
            }
        }

        /// <summary>성능 통계 조회 (디버깅용)</summary>
        public static string GetCacheStats()
        {
            return $"HaulAI Caches - Haulability: {_haulabilityCache.Count}, Reachability: {_reachabilityCache.Count}, Storage: {_storageDiagnosticsCache.Count}";
        }

        #endregion
    }
}