using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// 🟢 최적화된 드래프터 매니저 - 200개체+ 대응 최적화
    /// Handles the injection of draft controllers into non-humanlike pawns
    /// </summary>
    public static class Utility_DrafterManager
    {
        #region Cached Reflection Fields - 🟢 리플렉션 호출 최적화

        private static readonly FieldInfo EquipmentField = AccessTools.Field(typeof(Pawn), "equipment");
        private static readonly FieldInfo ApparelField = AccessTools.Field(typeof(Pawn), "apparel");
        private static readonly FieldInfo InventoryField = AccessTools.Field(typeof(Pawn), "inventory");
        private static readonly FieldInfo DrafterField = AccessTools.Field(typeof(Pawn), "drafter");
        private static readonly FieldInfo DrafterPawnField = AccessTools.Field(typeof(Pawn_DraftController), "pawn");

        #endregion

        #region Thread-Safe Caches - 🟢 고성능 캐시 시스템

        /// <summary>드래프터 주입 필요성 캐시 - pawnID → bool</summary>
        private static readonly ConcurrentDictionary<int, bool> _shouldInjectDrafterCache =
            new ConcurrentDictionary<int, bool>();

        /// <summary>트래커 상태 캐시 - pawnID → TrackerFlags</summary>
        private static readonly ConcurrentDictionary<int, TrackerFlags> _trackerStateCache =
            new ConcurrentDictionary<int, TrackerFlags>();

        /// <summary>트래커 주입된 폰 추적 - Thread-safe</summary>
        private static readonly ConcurrentDictionary<int, bool> _pawnsWithInjectedTrackers =
            new ConcurrentDictionary<int, bool>();

        /// <summary>시즈 듀티 캐시 - raceDefName → DutyDef</summary>
        private static readonly ConcurrentDictionary<string, DutyDef> _siegeDutyCache =
            new ConcurrentDictionary<string, DutyDef>();

        // 🟢 재사용 가능한 컬렉션들 - 메모리 할당 최소화
        [ThreadStatic]
        private static List<Pawn> _tempPawnList;
        private static List<Pawn> TempPawnList => _tempPawnList ?? (_tempPawnList = new List<Pawn>());

        [ThreadStatic]
        private static List<ThingWithComps> _tempEquipment;
        private static List<ThingWithComps> TempEquipment => _tempEquipment ?? (_tempEquipment = new List<ThingWithComps>());

        [ThreadStatic]
        private static List<Apparel> _tempApparel;
        private static List<Apparel> TempApparel => _tempApparel ?? (_tempApparel = new List<Apparel>());

        [ThreadStatic]
        private static List<Thing> _tempInventory;
        private static List<Thing> TempInventory => _tempInventory ?? (_tempInventory = new List<Thing>());

        #endregion

        #region Data Structures

        [Flags]
        private enum TrackerFlags : byte
        {
            None = 0,
            HasEquipment = 1,
            HasApparel = 2,
            HasInventory = 4,
            HasDrafter = 8,
            All = HasEquipment | HasApparel | HasInventory | HasDrafter
        }

        private struct ItemDropResult
        {
            public int ItemsDropped;
            public bool Success;

            public ItemDropResult(int itemsDropped, bool success)
            {
                ItemsDropped = itemsDropped;
                Success = success;
            }
        }

        #endregion

        #region Cache Management - 🟢 메모리 효율성 보장

        private const int MAX_CACHE_SIZE = 1000;
        private const int CACHE_VALIDITY_TICKS = 300; // 5초

        private static int _lastCacheMaintenanceTick = 0;

        /// <summary>주기적 캐시 정리</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void MaintainCaches()
        {
            int currentTick = Find.TickManager.TicksGame;

            if (currentTick - _lastCacheMaintenanceTick < 1200) return; // 20초마다
            _lastCacheMaintenanceTick = currentTick;

            if (_shouldInjectDrafterCache.Count > MAX_CACHE_SIZE)
                _shouldInjectDrafterCache.Clear();

            if (_trackerStateCache.Count > MAX_CACHE_SIZE)
                _trackerStateCache.Clear();

            if (_siegeDutyCache.Count > 50) // 시즈 듀티는 적게 캐시
                _siegeDutyCache.Clear();
        }

        /// <summary>특정 폰의 캐시 무효화</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void InvalidatePawnCaches(Pawn pawn)
        {
            if (pawn?.thingIDNumber == null) return;

            var pawnId = pawn.thingIDNumber;
            _shouldInjectDrafterCache.TryRemove(pawnId, out _);
            _trackerStateCache.TryRemove(pawnId, out _);
        }

        #endregion

        #region Optimized Core Methods

        /// <summary>
        /// 🟢 최적화된 트래커 보장 - 캐시 기반 상태 관리
        /// Injects any missing trackers onto the pawn so Gear/Equip/Apparel UI never NREs.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EnsureAllTrackers(Pawn pawn)
        {
            if (!Utility_Common.RaceDefChecker(pawn.def)) return;

            var pawnId = pawn.thingIDNumber;

            // 🟢 캐시된 트래커 상태 확인
            if (!_trackerStateCache.TryGetValue(pawnId, out var cachedState))
            {
                cachedState = GetCurrentTrackerState(pawn);
                _trackerStateCache.TryAdd(pawnId, cachedState);
            }

            // 🟢 누락된 트래커만 선택적으로 주입
            var injectedFlags = InjectMissingTrackers(pawn, cachedState);

            if (injectedFlags != TrackerFlags.None)
            {
                _pawnsWithInjectedTrackers.TryAdd(pawnId, true);

                // 🟢 캐시 업데이트
                var newState = cachedState | injectedFlags;
                _trackerStateCache.TryAdd(pawnId, newState);
            }
        }

        /// <summary>🟢 현재 트래커 상태 취득</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static TrackerFlags GetCurrentTrackerState(Pawn pawn)
        {
            var flags = TrackerFlags.None;

            if (EquipmentField.GetValue(pawn) != null) flags |= TrackerFlags.HasEquipment;
            if (ApparelField.GetValue(pawn) != null) flags |= TrackerFlags.HasApparel;
            if (InventoryField.GetValue(pawn) != null) flags |= TrackerFlags.HasInventory;
            if (pawn.drafter != null) flags |= TrackerFlags.HasDrafter;

            return flags;
        }

        /// <summary>🟢 누락된 트래커 주입</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static TrackerFlags InjectMissingTrackers(Pawn pawn, TrackerFlags currentState)
        {
            var injected = TrackerFlags.None;

            // 🟢 Equipment 트래커
            if (!currentState.HasFlag(TrackerFlags.HasEquipment))
            {
                var eq = new Pawn_EquipmentTracker(pawn);
                EquipmentField.SetValue(pawn, eq);
                eq.Notify_PawnSpawned();
                injected |= TrackerFlags.HasEquipment;
            }

            // 🟢 Apparel 트래커
            if (!currentState.HasFlag(TrackerFlags.HasApparel))
            {
                var ap = new Pawn_ApparelTracker(pawn);
                ApparelField.SetValue(pawn, ap);
                injected |= TrackerFlags.HasApparel;
            }

            // 🟢 Inventory 트래커
            if (!currentState.HasFlag(TrackerFlags.HasInventory))
            {
                var inv = new Pawn_InventoryTracker(pawn);
                InventoryField.SetValue(pawn, inv);
                injected |= TrackerFlags.HasInventory;
            }

            return injected;
        }

        /// <summary>
        /// 🟢 최적화된 드래프터 주입 필요성 판단 - 캐시 기반
        /// Determines if a pawn should have a draft controller injected
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ShouldInjectDrafter(Pawn pawn, NonHumanlikePawnControlExtension modExtension)
        {
            if (!Utility_Common.RaceDefChecker(pawn.def) || pawn.drafter != null)
                return false;

            if (modExtension == null) return false;

            // 🟢 캐시 확인
            var pawnId = pawn.thingIDNumber;
            if (_shouldInjectDrafterCache.TryGetValue(pawnId, out bool cached))
                return cached;

            // 🟢 계산 수행
            bool result = Utility_TagManager.ForceDraftable(pawn);

            // 🟢 결과 캐시
            _shouldInjectDrafterCache.TryAdd(pawnId, result);

            return result;
        }

        /// <summary>
        /// 🟢 최적화된 드래프터 보장
        /// Ensures that a pawn's draft controller is properly set up and connected
        /// </summary>
        public static void EnsureDrafter(Pawn pawn, NonHumanlikePawnControlExtension modExtension, bool isSpawnSetup = false)
        {
            // 🟢 빠른 검증
            if (!Utility_Common.RaceDefChecker(pawn.def) || pawn.Dead || pawn.Destroyed)
                return;

            if (!isSpawnSetup && !pawn.Spawned)
                return;

            if (modExtension == null) return;

            // 🟢 캐시된 결과로 드래프터 필요성 확인
            if (!ShouldInjectDrafter(pawn, modExtension))
                return;

            // 🟢 기존 드래프터 복구
            if (pawn.drafter != null)
            {
                RepairExistingDrafter(pawn);
                return;
            }

            // 🟢 새 드래프터 생성
            CreateNewDrafter(pawn);
        }

        /// <summary>🟢 기존 드래프터 복구</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void RepairExistingDrafter(Pawn pawn)
        {
            DrafterPawnField?.SetValue(pawn.drafter, pawn);

            if (Utility_DebugManager.ShouldLogDetailed())
                Utility_DebugManager.LogNormal($"Updated existing drafter for {pawn.LabelShort}");
        }

        /// <summary>🟢 새 드래프터 생성</summary>
        private static void CreateNewDrafter(Pawn pawn)
        {
            try
            {
                pawn.drafter = new Pawn_DraftController(pawn);
                pawn.drafter.ExposeData();

                Utility_DebugManager.LogNormal($"Injected drafter for {pawn.LabelShort} ({pawn.def.defName})");
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error injecting drafter for {pawn.LabelShort}: {ex}");
            }
        }

        /// <summary>
        /// 🟢 최적화된 맵 폰 드래프터 주입 - 배치 처리
        /// Injects draft controllers into all eligible pawns on a map
        /// </summary>
        public static void InjectDraftersIntoMapPawns(Map map)
        {
            if (map?.mapPawns?.AllPawnsSpawned == null) return;

            int injected = 0;
            var spawned = map.mapPawns.AllPawnsSpawned;

            // 🟢 배치 처리로 성능 최적화
            for (int i = 0; i < spawned.Count; i++)
            {
                var pawn = spawned[i];

                if (!IsValidPawnForDrafter(pawn)) continue;

                var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                if (modExtension == null) continue;

                if (ShouldInjectDrafter(pawn, modExtension))
                {
                    EnsureDrafter(pawn, modExtension);
                    injected++;
                }
            }

            if (injected > 0 && Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Injected drafters into {injected} non-humanlike pawns on map {map.uniqueID}");
            }
        }

        /// <summary>🟢 드래프터 유효 폰 검증</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsValidPawnForDrafter(Pawn pawn)
        {
            return pawn?.def != null &&
                   pawn.RaceProps != null &&
                   !pawn.Dead &&
                   !pawn.Destroyed &&
                   !pawn.RaceProps.Humanlike &&
                   pawn.drafter == null;
        }

        /// <summary>
        /// 🟢 최적화된 시즈 듀티 해결 - 캐시 기반
        /// Determines the appropriate duty for a pawn in a siege scenario
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static DutyDef ResolveSiegeDuty(Pawn p)
        {
            var cacheKey = p.def.defName;

            if (_siegeDutyCache.TryGetValue(cacheKey, out var cachedDuty))
                return cachedDuty;

            var result = CalculateSiegeDuty(p);
            _siegeDutyCache.TryAdd(cacheKey, result);

            return result;
        }

        /// <summary>🟢 시즈 듀티 계산</summary>
        private static DutyDef CalculateSiegeDuty(Pawn p)
        {
            if (Utility_TagManager.HasTag(p.def, "Siege_HoldFire"))
            {
                var holdFire = Utility_CacheManager.GetDuty("HoldFire");
                if (holdFire != null) return holdFire;
            }

            if (Utility_TagManager.HasTag(p.def, "Siege_ManTurret"))
            {
                var manTurrets = Utility_CacheManager.GetDuty("ManTurrets");
                if (manTurrets != null) return manTurrets;
            }

            return DutyDefOf.Defend;
        }

        /// <summary>
        /// 🟢 최적화된 종족별 트래커 정리 - 대량 처리 최적화
        /// Cleans up trackers for all pawns of the specified race, safely handling items
        /// </summary>
        public static void CleanupTrackersForRace(ThingDef raceDef)
        {
            if (raceDef?.race == null || Find.Maps == null) return;

            // 🟢 인간형 종족 건너뛰기
            if (raceDef.race.Humanlike)
            {
                Utility_DebugManager.LogNormal($"Skipping cleanup for humanlike race: {raceDef.defName}");
                return;
            }

            var stats = new CleanupStats();
            var pawnsToClean = TempPawnList;
            pawnsToClean.Clear();

            // 🟢 모든 대상 폰 수집 (맵 + 월드)
            CollectPawnsForCleanup(raceDef, pawnsToClean);

            // 🟢 배치 정리 처리
            ProcessPawnCleanupBatch(pawnsToClean, ref stats);

            // 🟢 결과 로깅
            if (stats.PawnsProcessed > 0)
            {
                Utility_DebugManager.LogNormal($"Cleaned up trackers for race: {raceDef.defName}, processed {stats.PawnsProcessed} pawns, dropped {stats.TotalItemsDropped} items");
            }
        }

        private struct CleanupStats
        {
            public int PawnsProcessed;
            public int TotalItemsDropped;
        }

        /// <summary>🟢 정리 대상 폰 수집</summary>
        private static void CollectPawnsForCleanup(ThingDef raceDef, List<Pawn> targetList)
        {
            // 맵 폰들 수집
            if (Find.Maps != null)
            {
                for (int i = 0; i < Find.Maps.Count; i++)
                {
                    var map = Find.Maps[i];
                    if (map?.mapPawns?.AllPawnsSpawned == null) continue;

                    var spawned = map.mapPawns.AllPawnsSpawned;
                    for (int j = 0; j < spawned.Count; j++)
                    {
                        var pawn = spawned[j];
                        if (IsValidForCleanup(pawn, raceDef))
                            targetList.Add(pawn);
                    }
                }
            }

            // 월드 폰들 수집
            if (Find.World?.worldPawns != null)
            {
                var worldPawns = Find.World.worldPawns.AllPawnsAliveOrDead;
                for (int i = 0; i < worldPawns.Count; i++)
                {
                    var pawn = worldPawns[i];
                    if (IsValidForCleanup(pawn, raceDef))
                        targetList.Add(pawn);
                }
            }
        }

        /// <summary>🟢 정리 유효성 검사</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsValidForCleanup(Pawn pawn, ThingDef raceDef)
        {
            return pawn != null &&
                   pawn.def == raceDef &&
                   !pawn.Dead &&
                   !pawn.Destroyed;
        }

        /// <summary>🟢 배치 정리 처리</summary>
        private static void ProcessPawnCleanupBatch(List<Pawn> pawns, ref CleanupStats stats)
        {
            for (int i = 0; i < pawns.Count; i++)
            {
                var pawn = pawns[i];
                try
                {
                    // 1. 아이템 드롭
                    var dropResult = ForceDropAllItemsOptimized(pawn);
                    stats.TotalItemsDropped += dropResult.ItemsDropped;

                    // 2. Think Tree 리셋
                    Utility_ThinkTreeManager.ResetThinkTreeToVanilla(pawn);

                    // 3. 트래커 정리
                    SafeCleanupTrackersOptimized(pawn);

                    stats.PawnsProcessed++;
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"Error cleaning up trackers for {pawn?.LabelShort ?? "unknown"}: {ex}");
                }
            }
        }

        /// <summary>
        /// 🟢 최적화된 아이템 강제 드롭 - 재사용 가능한 컬렉션 활용
        /// Forcibly drops all carried items from a pawn onto the ground, regardless of tracker state
        /// </summary>
        public static int ForceDropAllItems(Pawn pawn)
        {
            return ForceDropAllItemsOptimized(pawn).ItemsDropped;
        }

        /// <summary>🟢 내부 최적화된 아이템 드롭</summary>
        private static ItemDropResult ForceDropAllItemsOptimized(Pawn pawn)
        {
            if (pawn?.Map == null || pawn.Dead || pawn.Destroyed)
                return new ItemDropResult(0, false);

            int itemsDropped = 0;
            var dropMap = pawn.Map;
            var dropPos = pawn.Position;

            try
            {
                // 🟢 Equipment 드롭
                itemsDropped += DropEquipmentOptimized(pawn, dropMap, dropPos);

                // 🟢 Apparel 드롭
                itemsDropped += DropApparelOptimized(pawn, dropMap, dropPos);

                // 🟢 Inventory 드롭
                itemsDropped += DropInventoryOptimized(pawn, dropMap, dropPos);

                return new ItemDropResult(itemsDropped, true);
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error in ForceDropAllItems for {pawn.LabelShort}: {ex}");
                return new ItemDropResult(itemsDropped, false);
            }
        }

        /// <summary>🟢 장비 드롭 최적화</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int DropEquipmentOptimized(Pawn pawn, Map dropMap, IntVec3 dropPos)
        {
            if (pawn.equipment == null) return 0;

            var equipment = TempEquipment;
            equipment.Clear();

            // 🟢 리스트 복사 최소화
            var original = pawn.equipment.AllEquipmentListForReading;
            if (original != null)
            {
                for (int i = 0; i < original.Count; i++)
                    equipment.Add(original[i]);
            }

            int dropped = 0;
            for (int i = 0; i < equipment.Count; i++)
            {
                var eq = equipment[i];
                if (eq == null) continue;

                try
                {
                    pawn.equipment.Remove(eq);
                    if (!eq.Destroyed && GenDrop.TryDropSpawn(eq, dropPos, dropMap, ThingPlaceMode.Near, out _))
                        dropped++;
                }
                catch { } // 개별 아이템 오류 무시
            }

            return dropped;
        }

        /// <summary>🟢 의복 드롭 최적화</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int DropApparelOptimized(Pawn pawn, Map dropMap, IntVec3 dropPos)
        {
            if (pawn.apparel == null) return 0;

            var apparel = TempApparel;
            apparel.Clear();

            var original = pawn.apparel.WornApparel;
            if (original != null)
            {
                for (int i = 0; i < original.Count; i++)
                    apparel.Add(original[i]);
            }

            int dropped = 0;
            for (int i = 0; i < apparel.Count; i++)
            {
                var ap = apparel[i];
                if (ap == null) continue;

                try
                {
                    pawn.apparel.Remove(ap);
                    if (!ap.Destroyed && GenDrop.TryDropSpawn(ap, dropPos, dropMap, ThingPlaceMode.Near, out _))
                        dropped++;
                }
                catch { } // 개별 아이템 오류 무시
            }

            return dropped;
        }

        /// <summary>🟢 인벤토리 드롭 최적화</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int DropInventoryOptimized(Pawn pawn, Map dropMap, IntVec3 dropPos)
        {
            if (pawn.inventory?.innerContainer == null) return 0;

            var inventory = TempInventory;
            inventory.Clear();
            inventory.AddRange(pawn.inventory.innerContainer);

            int dropped = 0;
            for (int i = 0; i < inventory.Count; i++)
            {
                var item = inventory[i];
                if (item == null) continue;

                try
                {
                    pawn.inventory.innerContainer.Remove(item);
                    if (!item.Destroyed && GenDrop.TryDropSpawn(item, dropPos, dropMap, ThingPlaceMode.Near, out _))
                        dropped++;
                }
                catch { } // 개별 아이템 오류 무시
            }

            return dropped;
        }

        /// <summary>
        /// 🟢 최적화된 트래커 안전 정리
        /// Safely cleans up trackers for a specific pawn, ensuring all inventory is forcibly dropped
        /// </summary>
        public static void SafeCleanupTrackers(Pawn pawn)
        {
            SafeCleanupTrackersOptimized(pawn);
        }

        /// <summary>🟢 내부 최적화된 트래커 정리</summary>
        private static void SafeCleanupTrackersOptimized(Pawn pawn)
        {
            if (pawn == null) return;

            try
            {
                var pawnId = pawn.thingIDNumber;
                _pawnsWithInjectedTrackers.TryAdd(pawnId, true);

                // 🟢 트래커별 정리 (순서 중요: 아이템 드롭 → 필드 null)
                CleanupEquipmentTracker(pawn);
                CleanupApparelTracker(pawn);
                CleanupInventoryTracker(pawn);
                CleanupDrafterTracker(pawn);

                // 🟢 캐시 무효화
                InvalidatePawnCaches(pawn);
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error in SafeCleanupTrackers for {pawn.LabelShort}: {ex}");
            }
        }

        /// <summary>🟢 개별 트래커 정리 메소드들</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void CleanupEquipmentTracker(Pawn pawn)
        {
            try { EquipmentField.SetValue(pawn, null); }
            catch { }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void CleanupApparelTracker(Pawn pawn)
        {
            try { ApparelField.SetValue(pawn, null); }
            catch { }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void CleanupInventoryTracker(Pawn pawn)
        {
            try { InventoryField.SetValue(pawn, null); }
            catch { }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void CleanupDrafterTracker(Pawn pawn)
        {
            try
            {
                if (pawn.drafter?.Drafted == true)
                    pawn.drafter.Drafted = false;
                DrafterField?.SetValue(pawn, null);
            }
            catch { }
        }

        #endregion

        #region Public Utility Methods - 🟢 기존 API 호환성 유지

        /// <summary>트래커 주입 여부 확인</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool WasTrackerInjected(Pawn pawn)
        {
            return pawn != null && _pawnsWithInjectedTrackers.ContainsKey(pawn.thingIDNumber);
        }

        /// <summary>모든 트래커 제거 및 아이템 드롭</summary>
        public static void PurgeAllTrackersAndDropItems(Pawn pawn)
        {
            if (pawn?.Dead == true || pawn?.Destroyed == true) return;

            try
            {
                var dropResult = ForceDropAllItemsOptimized(pawn);
                Utility_ThinkTreeManager.ResetThinkTreeToVanilla(pawn);
                SafeCleanupTrackersOptimized(pawn);

                Utility_DebugManager.LogNormal($"Purged all trackers and dropped {dropResult.ItemsDropped} items from {pawn.LabelShort}");
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error in PurgeAllTrackersAndDropItems for {pawn.LabelShort}: {ex}");
            }
        }

        /// <summary>트래커 추적 데이터 리셋</summary>
        public static void ResetTrackerTracking()
        {
            _pawnsWithInjectedTrackers.Clear();
            _shouldInjectDrafterCache.Clear();
            _trackerStateCache.Clear();
            _siegeDutyCache.Clear();
        }

        /// <summary>캐시 통계 조회 (디버깅용)</summary>
        public static string GetCacheStats()
        {
            return $"DrafterManager Caches - ShouldInject: {_shouldInjectDrafterCache.Count}, TrackerState: {_trackerStateCache.Count}, InjectedTrackers: {_pawnsWithInjectedTrackers.Count}, SiegeDuty: {_siegeDutyCache.Count}";
        }

        /// <summary>모든 캐시 무효화</summary>
        public static void InvalidateAllCaches()
        {
            _shouldInjectDrafterCache.Clear();
            _trackerStateCache.Clear();
            _siegeDutyCache.Clear();
        }

        /// <summary>특정 맵의 모든 폰 캐시 무효화</summary>
        public static void InvalidateMapCaches(Map map)
        {
            if (map?.mapPawns?.AllPawnsSpawned == null) return;

            var spawned = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < spawned.Count; i++)
            {
                InvalidatePawnCaches(spawned[i]);
            }
        }

        /// <summary>메모리 사용량 최적화를 위한 캐시 압축</summary>
        public static void CompactCaches()
        {
            // 사용되지 않는 캐시 엔트리 제거
            var activeIds = new System.Collections.Generic.HashSet<int>();

            // 현재 활성 폰들의 ID 수집
            if (Find.Maps != null)
            {
                for (int i = 0; i < Find.Maps.Count; i++)
                {
                    var map = Find.Maps[i];
                    if (map?.mapPawns?.AllPawnsSpawned == null) continue;

                    var spawned = map.mapPawns.AllPawnsSpawned;
                    for (int j = 0; j < spawned.Count; j++)
                    {
                        var pawn = spawned[j];
                        if (pawn != null) activeIds.Add(pawn.thingIDNumber);
                    }
                }
            }

            // 비활성 엔트리 제거
            var keysToRemove = new System.Collections.Generic.List<int>();

            foreach (var kvp in _shouldInjectDrafterCache)
            {
                if (!activeIds.Contains(kvp.Key))
                    keysToRemove.Add(kvp.Key);
            }

            for (int i = 0; i < keysToRemove.Count; i++)
            {
                _shouldInjectDrafterCache.TryRemove(keysToRemove[i], out _);
                _trackerStateCache.TryRemove(keysToRemove[i], out _);
                _pawnsWithInjectedTrackers.TryRemove(keysToRemove[i], out _);
            }

            if (keysToRemove.Count > 0)
            {
                Utility_DebugManager.LogNormal($"Compacted DrafterManager caches: removed {keysToRemove.Count} inactive entries");
            }
        }

        #endregion
    }
}