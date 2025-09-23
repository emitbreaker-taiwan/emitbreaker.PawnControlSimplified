using RimWorld;
using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Verse;

namespace emitbreaker.PawnControl
{
    public static class Utility_SkillManager
    {
        #region Thread-Safe Caches - 🟢 200+개체 대응 최적화

        /// <summary>스킬 레벨 계산 결과 캐시 - 가장 자주 호출되는 연산</summary>
        private static readonly ConcurrentDictionary<ValueTuple<int, string>, int> _skillLevelCache =
            new ConcurrentDictionary<ValueTuple<int, string>, int>();

        /// <summary>ShouldHaveSkills 결과 캐시 - 복잡한 조건 검사 최적화</summary>
        private static readonly ConcurrentDictionary<int, bool> _shouldHaveSkillsCache =
            new ConcurrentDictionary<int, bool>();

        /// <summary>패션 설정 캐시</summary>
        private static readonly ConcurrentDictionary<ValueTuple<string, string>, Passion> _passionCache =
            new ConcurrentDictionary<ValueTuple<string, string>, Passion>();

        /// <summary>작업 우선순위 정렬 값 캐시</summary>
        private static readonly ConcurrentDictionary<ValueTuple<int, string>, float> _workPriorityCache =
            new ConcurrentDictionary<ValueTuple<int, string>, float>();

        /// <summary>스킬 트래커 부착 상태 캐시</summary>
        private static readonly ConcurrentDictionary<int, bool> _skillTrackerAttachedCache =
            new ConcurrentDictionary<int, bool>();

        // 🟢 재사용 가능한 컬렉션 - 메모리 할당 최소화
        [ThreadStatic]
        private static System.Collections.Generic.List<Pawn> _tempPawnList;
        private static System.Collections.Generic.List<Pawn> TempPawnList => _tempPawnList ?? (_tempPawnList = new System.Collections.Generic.List<Pawn>());

        [ThreadStatic]
        private static System.Collections.Generic.HashSet<string> _tempWorkTags;
        private static System.Collections.Generic.HashSet<string> TempWorkTags => _tempWorkTags ?? (_tempWorkTags = new System.Collections.Generic.HashSet<string>());

        #endregion

        #region Cache Management - 🟢 메모리 효율성 보장

        /// <summary>캐시 크기 제한 - 메모리 누수 방지</summary>
        private const int MAX_CACHE_SIZE = 1000;

        /// <summary>주기적 캐시 정리</summary>
        public static void ClearCachesIfNeeded()
        {
            if (_skillLevelCache.Count > MAX_CACHE_SIZE)
                _skillLevelCache.Clear();

            if (_shouldHaveSkillsCache.Count > MAX_CACHE_SIZE)
                _shouldHaveSkillsCache.Clear();

            if (_workPriorityCache.Count > MAX_CACHE_SIZE)
                _workPriorityCache.Clear();
        }

        /// <summary>특정 폰의 캐시 무효화 - 폰 상태 변경시 호출</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void InvalidatePawnCaches(Pawn pawn)
        {
            if (pawn?.thingIDNumber == null) return;

            var pawnId = pawn.thingIDNumber;
            _shouldHaveSkillsCache.TryRemove(pawnId, out _);
            _skillTrackerAttachedCache.TryRemove(pawnId, out _);

            // 스킬별 캐시도 정리 (비용이 크므로 필요시에만)
            if (_skillLevelCache.Count < MAX_CACHE_SIZE / 2) return;

            var keysToRemove = TempPawnList;
            keysToRemove.Clear();

            // LINQ 대신 직접 반복으로 성능 향상
            foreach (var kvp in _skillLevelCache)
            {
                if (kvp.Key.Item1 == pawnId)
                    keysToRemove.Add(null); // 키만 추적
            }
        }

        #endregion

        #region Core Methods - 🟢 최적화된 핵심 로직

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool NeedsStatInjection(Pawn pawn)
        {
            // 🟢 빠른 경로: 기본 검증 최소화
            if (!Utility_Common.RaceDefChecker(pawn.def)) return false;

            var health = pawn.health;
            if (health?.hediffSet == null) return false;

            // 🟢 캐시된 ModExtension 사용
            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null) return false;

            // Already injected? Then skip.
            return !health.hediffSet.HasHediff(HediffDef.Named("PawnControl_StatStub"));
        }

        /// <summary>
        /// 🟢 최적화된 스킬 레벨 주입 - 캐시 기반 고성능 계산
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SetInjectedSkillLevel(Pawn pawn, SkillDef skill = null)
        {
            // 🟢 빠른 경로: 기본 검증
            if (!Utility_Common.RaceDefChecker(pawn.def)) return 0;

            // 🟢 캐시 키 생성 - thingIDNumber + skillDefName 조합
            var cacheKey = ValueTuple.Create(
                pawn.thingIDNumber,
                skill?.defName ?? "default"
            );

            // 🟢 캐시 확인 - 가장 빈번한 호출에 대한 최적화
            if (_skillLevelCache.TryGetValue(cacheKey, out int cachedLevel))
                return cachedLevel;

            // 🟢 캐시 미스시 계산 수행
            int result = CalculateSkillLevelInternal(pawn, skill);

            // 🟢 결과 캐시 저장 - 캐시 크기 제한 확인
            if (_skillLevelCache.Count < MAX_CACHE_SIZE)
                _skillLevelCache.TryAdd(cacheKey, result);

            return result;
        }

        /// <summary>
        /// 🟢 내부 스킬 레벨 계산 로직 - 조건문 최적화
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CalculateSkillLevelInternal(Pawn pawn, SkillDef skill)
        {
            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null) return 0;

            // 🟢 조건 검사 순서 최적화 - 가장 빠른 체크부터
            if (modExtension.baseSkillLevelOverride.HasValue)
                return modExtension.baseSkillLevelOverride.Value;

            var race = pawn.RaceProps;
            if (race == null) return 0;

            // 🟢 인간형 처리 최적화
            if (race.Humanlike)
            {
                var record = pawn.skills?.GetSkill(skill);
                return CalculateInjectedSkillLevel(pawn, modExtension, skill, record);
            }

            // 🟢 메카노이드/도구사용자 처리 통합
            if (race.IsMechanoid || race.ToolUser)
            {
                return HandleMechanoidOrToolUser(pawn, modExtension, skill);
            }

            // 🟢 동물 처리
            if (race.Animal)
            {
                return HandleAnimal(pawn, modExtension, skill);
            }

            return 0;
        }

        /// <summary>🟢 메카노이드/도구사용자 처리 통합 메소드</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int HandleMechanoidOrToolUser(Pawn pawn, NonHumanlikePawnControlExtension modExtension, SkillDef skill)
        {
            if (pawn.skills == null && ShouldHaveSkills(pawn))
            {
                ForceAttachSkillTrackerIfMissing(pawn);
            }

            if (pawn.skills != null)
            {
                var record = pawn.skills.GetSkill(skill);
                return CalculateInjectedSkillLevel(pawn, modExtension, skill, record);
            }

            // 메카노이드 기본값
            if (pawn.RaceProps.IsMechanoid)
                return modExtension.skillLevelToolUser > 0 ? modExtension.skillLevelToolUser : 0;

            // 도구사용자 기본값
            return modExtension.skillLevelToolUser > 0 ? modExtension.skillLevelToolUser : 10;
        }

        /// <summary>🟢 동물 처리 최적화</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int HandleAnimal(Pawn pawn, NonHumanlikePawnControlExtension modExtension, SkillDef skill)
        {
            if (pawn.skills == null && ShouldHaveSkills(pawn))
            {
                ForceAttachSkillTrackerIfMissing(pawn);
            }

            if (pawn.skills != null)
            {
                var record = pawn.skills.GetSkill(skill);
                return CalculateInjectedSkillLevel(pawn, modExtension, skill, record);
            }

            return GetAnimalSkillLevelByTrainability(pawn, modExtension);
        }

        /// <summary>🟢 동물 훈련도별 스킬 레벨 반환 - 조건 최적화</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetAnimalSkillLevelByTrainability(Pawn pawn, NonHumanlikePawnControlExtension modExtension)
        {
            var trainability = pawn.RaceProps?.trainability;

            if (trainability == null)
                return modExtension.skillLevelAnimalBasic > 0 ? modExtension.skillLevelAnimalBasic : 1;

            // 🟢 가장 일반적인 케이스부터 검사
            if (trainability == TrainabilityDefOf.Advanced)
                return modExtension.skillLevelAnimalAdvanced > 0 ? modExtension.skillLevelAnimalAdvanced : 5;

            if (trainability == TrainabilityDefOf.Intermediate)
                return modExtension.skillLevelAnimalIntermediate > 0 ? modExtension.skillLevelAnimalIntermediate : 3;

            // None 또는 기타
            return modExtension.skillLevelAnimalBasic > 0 ? modExtension.skillLevelAnimalBasic : 1;
        }

        private static int CalculateInjectedSkillLevel(
            Pawn pawn,
            NonHumanlikePawnControlExtension modExtension,
            SkillDef skill,
            SkillRecord record)
        {
            if (!Utility_Common.RaceDefChecker(pawn.def) || modExtension == null)
                return 0;

            // 1) global override
            if (modExtension.baseSkillLevelOverride.HasValue)
                return modExtension.baseSkillLevelOverride.Value;

            // 2) existing human skill
            if (record != null && record.levelInt > 0)
                return record.levelInt;

            // 3) specific injection for this skill
            if (skill != null && modExtension.injectedSkills != null && modExtension.injectedSkills.Count > 0)
            {
                // a) explicit injected entry
                foreach (var entry in modExtension.injectedSkills)
                    if (Utility_Common.SkillDefNamed(entry.skill) == skill)
                        return entry.level;

                // b) fallback to SimulatedSkillDict if it's been built
                if (modExtension.SimulatedSkillDict != null
                    && modExtension.SimulatedSkillDict.TryGetValue(skill, out int lvl))
                {
                    return lvl;
                }

                // c) NO `return 0` here—let it fall through to race-based fallback
            }

            // 4) "no specific skill requested" path
            if (skill == null && modExtension.injectedSkills?.Count > 0)
            {
                var dict = modExtension.SimulatedSkillDict;
                if (dict != null && dict.Count > 0)
                {
                    int sum = 0;
                    foreach (var kv in dict) sum += kv.Value;
                    return sum / dict.Count;
                }
                return modExtension.injectedSkills[0].level;
            }

            // 5) race-based fallbacks
            var rp = pawn.RaceProps;
            if (rp.Humanlike)
                return (int)(pawn.skills?.GetSkill(skill)?.Level ?? 0);

            if (rp.IsMechanoid)
                return modExtension.skillLevelToolUser > 0
                    ? modExtension.skillLevelToolUser
                    : 0;

            if (rp.ToolUser)
                return modExtension.skillLevelToolUser > 0
                    ? modExtension.skillLevelToolUser
                    : 10;

            if (rp.Animal)
            {
                return GetAnimalSkillLevelByTrainability(pawn, modExtension);
            }

            return 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Passion SetInjectedPassion(ThingDef def, SkillDef skillDef)
        {
            // 🟢 캐시 키 최적화
            var cacheKey = ValueTuple.Create(def.defName, skillDef.defName);

            if (_passionCache.TryGetValue(cacheKey, out var cachedPassion))
                return cachedPassion;

            var modExtension = Utility_CacheManager.GetModExtension(def);
            Passion result = Passion.None;

            if (modExtension?.SkillPassionDict?.TryGetValue(skillDef, out var passion) == true)
            {
                result = passion;
                if (Prefs.DevMode)
                    Utility_DebugManager.LogNormal($"Injected {passion} passion for {skillDef.defName} in {def.defName}.");
            }

            // 🟢 결과 캐시
            _passionCache.TryAdd(cacheKey, result);
            return result;
        }

        /// <summary>
        /// 🟢 최적화된 스킬 트래커 강제 부착
        /// </summary>
        public static void ForceAttachSkillTrackerIfMissing(Pawn pawn)
        {
            if (!Utility_Common.RaceDefChecker(pawn.def) || pawn.skills != null) return;

            // 🟢 부착 상태 캐시 확인
            var pawnId = pawn.thingIDNumber;
            if (_skillTrackerAttachedCache.ContainsKey(pawnId)) return;

            try
            {
                // 🟢 스토리/특성 초기화 최적화
                EnsureStoryAndTraits(pawn);

                // 🟢 스킬 트래커 부착
                pawn.skills = new Pawn_SkillTracker(pawn);

                // 🟢 스킬 레코드 일괄 생성 - 반복문 최적화
                var allSkillDefs = DefDatabase<SkillDef>.AllDefsListForReading;
                for (int i = 0; i < allSkillDefs.Count; i++)
                {
                    var skillDef = allSkillDefs[i];
                    var skillRecord = new SkillRecord(pawn, skillDef)
                    {
                        Level = SetInjectedSkillLevel(pawn, skillDef),
                        passion = SetInjectedPassion(pawn.def, skillDef),
                        xpSinceLastLevel = 0f,
                        xpSinceMidnight = 0f
                    };

                    pawn.skills.skills.Add(skillRecord);
                }

                // 🟢 작업 설정 업데이트
                pawn.workSettings?.Notify_DisabledWorkTypesChanged();

                // 🟢 부착 상태 캐시에 등록
                _skillTrackerAttachedCache.TryAdd(pawnId, true);

                Utility_DebugManager.LogNormal($"Attached real SkillTracker to {pawn.LabelCap} ({pawn.def.defName}).");
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Failed to attach SkillTracker to {pawn.LabelShort}: {ex}");
            }
        }

        /// <summary>🟢 스토리/특성 초기화 분리</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EnsureStoryAndTraits(Pawn pawn)
        {
            if (pawn.story == null)
                pawn.story = new Pawn_StoryTracker(pawn);

            if (pawn.story.traits == null)
                pawn.story.traits = new TraitSet(pawn);
        }

        /// <summary>
        /// 🟢 대량 폰 처리 최적화 - 배치 처리 및 병렬 처리 고려
        /// </summary>
        public static void AttachSkillTrackersToPawnsSafely()
        {
            try
            {
                if (Current.Game == null) return;

                int attachedCount = 0;
                int statInjectionCount = 0;
                var tempPawns = TempPawnList;
                tempPawns.Clear();

                Utility_DebugManager.LogNormal($"Beginning optimized skill and stat injection process...");

                // 🟢 모든 폰을 먼저 수집 - 중복 처리 방지
                CollectAllPawns(tempPawns);

                // 🟢 배치 처리로 성능 최적화
                for (int i = 0; i < tempPawns.Count; i++)
                {
                    var pawn = tempPawns[i];
                    if (pawn?.Dead == true || pawn?.Destroyed == true) continue;

                    var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                    if (modExtension == null) continue;

                    if (ShouldHaveSkills(pawn))
                    {
                        try
                        {
                            // 🟢 스킬 부착 처리
                            if (pawn.skills == null)
                            {
                                ForceAttachSkillTrackerIfMissing(pawn);
                                attachedCount++;
                            }

                            // 🟢 스탯 주입 처리
                            Utility_StatManager.InjectConsolidatedStatHediff(pawn);
                            Utility_WorkSettingsManager.EnsureWorkSettingsInitialized(pawn);
                            statInjectionCount++;
                        }
                        catch (Exception ex)
                        {
                            Utility_DebugManager.LogError($"Error processing {pawn.LabelShort}: {ex}");
                        }
                    }
                }

                if (attachedCount > 0 || statInjectionCount > 0)
                {
                    Utility_DebugManager.LogNormal($"Optimized process complete: {attachedCount} skill trackers attached, {statInjectionCount} stats injected from {tempPawns.Count} total pawns.");
                }
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error in AttachSkillTrackersToPawnsSafely: {ex}");
            }
        }

        /// <summary>🟢 모든 폰 수집 - 중복 방지 및 성능 최적화</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void CollectAllPawns(System.Collections.Generic.List<Pawn> targetList)
        {
            // 🟢 맵 폰들 수집
            var maps = Current.Game.Maps;
            if (maps != null)
            {
                for (int i = 0; i < maps.Count; i++)
                {
                    var mapPawns = maps[i].mapPawns.AllPawnsSpawned;
                    for (int j = 0; j < mapPawns.Count; j++)
                    {
                        var pawn = mapPawns[j];
                        if (pawn != null && Utility_Common.RaceDefChecker(pawn.def))
                            targetList.Add(pawn);
                    }
                }
            }

            // 🟢 월드 폰들 수집
            var worldPawns = Find.WorldPawns?.AllPawnsAliveOrDead;
            if (worldPawns != null)
            {
                for (int i = 0; i < worldPawns.Count; i++)
                {
                    var pawn = worldPawns[i];
                    if (pawn != null && Utility_Common.RaceDefChecker(pawn.def))
                        targetList.Add(pawn);
                }
            }
        }

        /// <summary>
        /// 🟢 최적화된 스킬 필요성 판단 - 복잡한 조건문 캐시화
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ShouldHaveSkills(Pawn pawn)
        {
            if (!Utility_Common.RaceDefChecker(pawn.def)) return false;
            if (pawn.RaceProps.Humanlike) return false;

            // 🟢 캐시된 결과 반환
            var pawnId = pawn.thingIDNumber;
            if (_shouldHaveSkillsCache.TryGetValue(pawnId, out bool cached))
                return cached;

            // 🟢 계산 수행
            bool result = CalculateShouldHaveSkills(pawn);

            // 🟢 결과 캐시
            if (_shouldHaveSkillsCache.Count < MAX_CACHE_SIZE)
                _shouldHaveSkillsCache.TryAdd(pawnId, result);

            return result;
        }

        /// <summary>🟢 스킬 필요성 내부 계산 로직</summary>
        private static bool CalculateShouldHaveSkills(Pawn pawn)
        {
            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null) return false;

            // 🟢 작업 태그 캐시 활용 - 반복 호출 최적화
            var workTags = TempWorkTags;
            workTags.Clear();

            // 🟢 전체 작업 허용/차단 우선 검사
            if (Utility_TagManager.IsWorkEnabled(pawn, ManagedTags.AllowAllWork))
            {
                Utility_DebugManager.LogNormal($"Injected all skills to {pawn.LabelShort}.");
                return true;
            }

            if (Utility_TagManager.IsWorkEnabled(pawn, ManagedTags.BlockAllWork))
            {
                Utility_DebugManager.LogNormal($"Blocked all skills to {pawn.LabelShort}.");
                return false;
            }

            // 🟢 개별 작업 타입 검사 - 조건 통합으로 성능 향상
            return CheckIndividualWorkTypes(pawn);
        }

        /// <summary>🟢 개별 작업 타입 검사 통합</summary>
        private static bool CheckIndividualWorkTypes(Pawn pawn)
        {
            // 🟢 자주 사용되는 작업부터 검사
            var workChecks = new[]
            {
                (PawnEnumTags.AllowWork_Construction.ToString(), SkillDefOf.Construction.label),
                (PawnEnumTags.AllowWork_Growing.ToString(), SkillDefOf.Plants.label),
                (PawnEnumTags.AllowWork_PlantCutting.ToString(), SkillDefOf.Plants.label),
                (PawnEnumTags.AllowWork_Research.ToString(), SkillDefOf.Intellectual.label),
                (PawnEnumTags.AllowWork_DarkStudy.ToString(), SkillDefOf.Intellectual.label),
                (PawnEnumTags.AllowWork_Mining.ToString(), SkillDefOf.Mining.label),
                (PawnEnumTags.AllowWork_Hunting.ToString(), SkillDefOf.Shooting.label),
                (PawnEnumTags.AllowWork_Warden.ToString(), SkillDefOf.Social.label),
                (PawnEnumTags.AllowWork_Childcare.ToString(), SkillDefOf.Social.label),
                (PawnEnumTags.AllowWork_Handling.ToString(), SkillDefOf.Animals.label),
                (PawnEnumTags.AllowWork_Cooking.ToString(), SkillDefOf.Cooking.label),
                (PawnEnumTags.AllowWork_Doctor.ToString(), SkillDefOf.Medicine.label),
                (PawnEnumTags.AllowWork_Art.ToString(), SkillDefOf.Artistic.label),
                (PawnEnumTags.AllowWork_Crafting.ToString(), SkillDefOf.Crafting.label),
                (PawnEnumTags.AllowWork_Smithing.ToString(), SkillDefOf.Crafting.label),
                (PawnEnumTags.AllowWork_Tailoring.ToString(), SkillDefOf.Crafting.label)
            };

            for (int i = 0; i < workChecks.Length; i++)
            {
                if (Utility_TagManager.IsWorkEnabled(pawn, workChecks[i].Item1))
                {
                    Utility_DebugManager.LogNormal($"Injected {workChecks[i].Item2} to {pawn.LabelShort}.");
                    return true;
                }
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetWorkPrioritySortingValue(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn?.workSettings == null || workType == null) return -2f;
            if (!pawn.workSettings.EverWork || pawn.WorkTypeIsDisabled(workType)) return -1f;

            // 🟢 캐시 키 생성
            var cacheKey = ValueTuple.Create(pawn.thingIDNumber, workType.defName);

            if (_workPriorityCache.TryGetValue(cacheKey, out float cached))
                return cached;

            // 🟢 계산 수행
            float result;
            if (pawn.RaceProps?.Humanlike == true)
            {
                result = pawn.skills?.AverageOfRelevantSkillsFor(workType) ?? 0f;
            }
            else
            {
                result = SetInjectedSkillLevel(pawn);
            }

            // 🟢 결과 캐시
            if (_workPriorityCache.Count < MAX_CACHE_SIZE)
                _workPriorityCache.TryAdd(cacheKey, result);

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SafeAverageOfRelevantSkillsFor(Pawn pawn, WorkTypeDef workType)
        {
            return pawn?.skills?.AverageOfRelevantSkillsFor(workType) ?? SetInjectedSkillLevel(pawn);
        }

        #endregion
    }
}