using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Windows;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;

namespace emitbreaker.PawnControl
{
    public class HarmonyPatches
    {
        public static class Patch_Iteration1_IdentityInjection
        {
            /// <summary>
            /// 최적화된 Animal Race 패치 - 스레드 안전성과 메모리 효율성 개선
            /// </summary>
            [HarmonyPatch(typeof(RaceProperties), nameof(RaceProperties.Animal), MethodType.Getter)]
            public static class Patch_RaceProperties_Animal
            {
                // 🟢 Thread-safe caching with ConcurrentDictionary
                private static readonly ConcurrentDictionary<RaceProperties, bool> _overrideCache =
                    new ConcurrentDictionary<RaceProperties, bool>();

                // 🟢 Race-based caching for better performance
                private static readonly ConcurrentDictionary<ThingDef, bool> _raceOverrideCache =
                    new ConcurrentDictionary<ThingDef, bool>();

                // 🟢 Throttled logging to prevent spam
                private static long _lastLogTick = 0;
                private const int LOG_INTERVAL_TICKS = 30 * 60; // 30 seconds
                private const int MAX_CACHE_SIZE = 2000; // Prevent unlimited growth

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static bool Prefix(RaceProperties __instance, ref bool __result)
                {
                    // 🟢 Fast path: check cache first
                    if (_overrideCache.TryGetValue(__instance, out bool cachedResult))
                    {
                        __result = cachedResult;
                        return false; // Skip original getter
                    }

                    // Cache miss, let vanilla compute first
                    return true;
                }

                public static void Postfix(RaceProperties __instance, ref bool __result)
                {
                    // 🟢 Early exit for vanilla animals - no need to cache true results repeatedly
                    if (__result)
                    {
                        // Only cache if not already cached to avoid redundant operations
                        _overrideCache.TryAdd(__instance, true);
                        return;
                    }

                    // 🟢 Check race-based cache first for better performance
                    var pawnKind = __instance.AnyPawnKind;
                    if (pawnKind?.race is ThingDef raceDef)
                    {
                        // Try race-based cache first
                        if (_raceOverrideCache.TryGetValue(raceDef, out bool raceResult))
                        {
                            __result = raceResult;
                            _overrideCache.TryAdd(__instance, raceResult);
                            return;
                        }

                        // Compute and cache result
                        var ext = Utility_CacheManager.GetModExtension(raceDef);
                        bool isForced = ext?.forceIdentity == ForcedIdentityType.ForceAnimal;

                        // 🟢 Cache at both levels for optimal performance
                        _raceOverrideCache.TryAdd(raceDef, isForced);
                        _overrideCache.TryAdd(__instance, isForced);

                        // 🟢 Throttled debug logging
                        if (isForced && ext.debugMode && ShouldLog())
                        {
                            Utility_DebugManager.LogNormal($"[PawnControl] ForcedAnimal: {raceDef.defName}");
                        }

                        __result = isForced;
                    }
                    else
                    {
                        // No race found, cache false to avoid repeated computation
                        _overrideCache.TryAdd(__instance, false);
                    }

                    // 🟢 Periodic cache cleanup to prevent memory leaks
                    if (_overrideCache.Count > MAX_CACHE_SIZE)
                    {
                        PerformCacheCleanup();
                    }
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private static bool ShouldLog()
                {
                    long currentTick = Find.TickManager.TicksGame;
                    if (currentTick - _lastLogTick > LOG_INTERVAL_TICKS)
                    {
                        _lastLogTick = currentTick;
                        return true;
                    }
                    return false;
                }

                /// <summary>
                /// 🟢 Memory management: Clean up old cache entries
                /// </summary>
                private static void PerformCacheCleanup()
                {
                    try
                    {
                        // Keep only the most recent 75% of entries
                        int targetSize = (int)(MAX_CACHE_SIZE * 0.75f);
                        int toRemove = _overrideCache.Count - targetSize;

                        if (toRemove > 0)
                        {
                            var keysToRemove = new List<RaceProperties>();
                            int removed = 0;

                            foreach (var kvp in _overrideCache)
                            {
                                if (removed >= toRemove) break;

                                // Remove entries that are likely stale (no longer referenced)
                                if (kvp.Key?.AnyPawnKind?.race == null)
                                {
                                    keysToRemove.Add(kvp.Key);
                                    removed++;
                                }
                            }

                            foreach (var key in keysToRemove)
                            {
                                _overrideCache.TryRemove(key, out _);
                            }

                            if (Utility_DebugManager.ShouldLogDetailed())
                            {
                                Utility_DebugManager.LogNormal($"[PawnControl] Cache cleanup: removed {removed} stale entries");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Utility_DebugManager.LogError($"Error during cache cleanup: {ex.Message}");
                    }
                }

                /// <summary>
                /// 🟢 Public method to clear caches when needed (e.g., on game state changes)
                /// </summary>
                public static void ClearCache()
                {
                    _overrideCache.Clear();
                    _raceOverrideCache.Clear();
                }
            }

            /// <summary>
            /// 🟢 Similar optimizations can be applied to Humanlike and Mechanoid patches
            /// </summary>
            [HarmonyPatch(typeof(RaceProperties), nameof(RaceProperties.Humanlike), MethodType.Getter)]
            public static class Patch_RaceProperties_Humanlike
            {
                private static readonly ConcurrentDictionary<RaceProperties, bool> _overrideCache =
                    new ConcurrentDictionary<RaceProperties, bool>();
                private static readonly ConcurrentDictionary<ThingDef, bool> _raceOverrideCache =
                    new ConcurrentDictionary<ThingDef, bool>();

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static bool Prefix(RaceProperties __instance, ref bool __result)
                {
                    if (_overrideCache.TryGetValue(__instance, out bool cachedResult))
                    {
                        __result = cachedResult;
                        return false;
                    }
                    return true;
                }

                public static void Postfix(RaceProperties __instance, ref bool __result)
                {
                    if (__result)
                    {
                        _overrideCache.TryAdd(__instance, true);
                        return;
                    }

                    var pawnKind = __instance.AnyPawnKind;
                    if (pawnKind?.race is ThingDef raceDef)
                    {
                        if (_raceOverrideCache.TryGetValue(raceDef, out bool raceResult))
                        {
                            __result = raceResult;
                            _overrideCache.TryAdd(__instance, raceResult);
                            return;
                        }

                        var ext = Utility_CacheManager.GetModExtension(raceDef);
                        bool isForced = ext?.forceIdentity == ForcedIdentityType.ForceHumanlike;

                        _raceOverrideCache.TryAdd(raceDef, isForced);
                        _overrideCache.TryAdd(__instance, isForced);
                        __result = isForced;
                    }
                    else
                    {
                        _overrideCache.TryAdd(__instance, false);
                    }
                }

                public static void ClearCache()
                {
                    _overrideCache.Clear();
                    _raceOverrideCache.Clear();
                }
            }

            [HarmonyPatch(typeof(RaceProperties), nameof(RaceProperties.IsMechanoid), MethodType.Getter)]
            public static class Patch_RaceProperties_IsMechanoid
            {
                private static readonly ConcurrentDictionary<RaceProperties, bool> _overrideCache =
                    new ConcurrentDictionary<RaceProperties, bool>();
                private static readonly ConcurrentDictionary<ThingDef, bool> _raceOverrideCache =
                    new ConcurrentDictionary<ThingDef, bool>();

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static bool Prefix(RaceProperties __instance, ref bool __result)
                {
                    if (_overrideCache.TryGetValue(__instance, out bool cachedResult))
                    {
                        __result = cachedResult;
                        return false;
                    }
                    return true;
                }

                public static void Postfix(RaceProperties __instance, ref bool __result)
                {
                    if (__result)
                    {
                        _overrideCache.TryAdd(__instance, true);
                        return;
                    }

                    var pawnKind = __instance.AnyPawnKind;
                    if (pawnKind?.race is ThingDef raceDef)
                    {
                        if (_raceOverrideCache.TryGetValue(raceDef, out bool raceResult))
                        {
                            __result = raceResult;
                            _overrideCache.TryAdd(__instance, raceResult);
                            return;
                        }

                        var ext = Utility_CacheManager.GetModExtension(raceDef);
                        bool isForced = ext?.forceIdentity == ForcedIdentityType.ForceMechanoid;

                        _raceOverrideCache.TryAdd(raceDef, isForced);
                        _overrideCache.TryAdd(__instance, isForced);
                        __result = isForced;
                    }
                    else
                    {
                        _overrideCache.TryAdd(__instance, false);
                    }
                }

                public static void ClearCache()
                {
                    _overrideCache.Clear();
                    _raceOverrideCache.Clear();
                }
            }
        }

        public static class Patch_Iteration2_WorkTabInjection
        {
            #region Cached Reflection - 🟢 모든 리플렉션 호출을 static readonly로 캐시

            // PropertyInfo 캐시
            private static readonly PropertyInfo _selThingProperty = AccessTools.Property(typeof(ITab), "SelThing");

            // MethodInfo 캐시
            private static readonly MethodInfo _drawWorkBoxBackgroundMethod = AccessTools.Method(typeof(WidgetsWork), "DrawWorkBoxBackground");

            // FieldInfo 캐시 - WorkSettings 관련
            private static readonly FieldInfo _pawnFieldInWorkSettings = AccessTools.Field(typeof(Pawn_WorkSettings), "pawn");

            #endregion

            #region Work Tab Pawn List

            /// <summary>
            /// 🟢 최적화된 Work Tab Pawn 주입 - 캐시 및 성능 개선
            /// </summary>
            [HarmonyPatch(typeof(MainTabWindow_Work), "Pawns", MethodType.Getter)]
            public static class Patch_MainTabWindow_Work_Pawns
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static bool Prefix(ref IEnumerable<Pawn> __result)
                {
                    using (new Utility_IdentityManager_ScopedFlagContext(FlagScopeTarget.IsColonist))
                    {
                        var taggedPawns = Utility_CacheManager.GetColonistLikePawns(Find.CurrentMap);

                        // 🟢 Fast path: 순수 바닐라 휴먼 체크
                        bool isPureVanillaHumanlike = taggedPawns.All(p =>
                            p.RaceProps.Humanlike && Utility_CacheManager.GetModExtension(p.def) == null);

                        if (isPureVanillaHumanlike)
                        {
                            return true; // 바닐라 로직 사용
                        }

                        // 🟢 배치 처리로 guest tracker 초기화
                        var pawnsNeedingGuest = taggedPawns.Where(p => p?.guest == null).ToList();
                        foreach (var pawn in pawnsNeedingGuest)
                        {
                            pawn.guest = new Pawn_GuestTracker(pawn);
                        }

                        __result = taggedPawns;

                        // 🟢 배치 처리로 work settings 초기화
                        var modExtensionPawns = taggedPawns
                            .Where(p => p != null && Utility_CacheManager.GetModExtension(p.def) != null)
                            .ToList();

                        foreach (var pawn in modExtensionPawns)
                        {
                            Utility_WorkSettingsManager.EnsureWorkSettingsInitialized(pawn);
                        }

                        if (Utility_DebugManager.ShouldLogDetailed())
                        {
                            Utility_DebugManager.LogNormal($"Work tab initialized {modExtensionPawns.Count} modded pawns");
                        }

                        return false; // 바닐라 로직 스킵
                    }
                }
            }

            #endregion

            #region Work Type Capability

            /// <summary>
            /// 🟢 최적화된 WorkType 비활성화 처리
            /// </summary>
            [HarmonyPatch(typeof(Pawn), nameof(Pawn.WorkTypeIsDisabled))]
            public static class Patch_Pawn_WorkTypeIsDisabled
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                [HarmonyPrefix]
                public static bool Prefix(Pawn __instance, WorkTypeDef w, ref bool __result)
                {
                    var modExtension = Utility_CacheManager.GetModExtension(__instance.def);
                    if (modExtension == null)
                    {
                        return true; // 바닐라 로직 사용
                    }

                    // 🟢 최적화된 태그 시스템 사용
                    bool allowed = Utility_TagManager.IsWorkEnabled(__instance, w.defName.ToString());
                    __result = !allowed;
                    return false;
                }
            }

            /// <summary>
            /// 🟢 최적화된 Work 능력 부족 검사
            /// </summary>
            [HarmonyPatch(typeof(PawnColumnWorker_WorkPriority), "IsIncapableOfWholeWorkType")]
            public static class Patch_IsIncapableOfWholeWorkType_ByTag
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static bool Prefix(Pawn p, WorkTypeDef work, ref bool __result)
                {
                    if (p == null || work == null) return true;

                    // 🟢 빠른 identity 체크
                    if (!Utility_IdentityManager.MatchesIdentityFlags(p, PawnIdentityFlags.IsColonist))
                        return true;

                    var modExtension = Utility_CacheManager.GetModExtension(p.def);
                    if (modExtension != null)
                    {
                        // 🟢 string concatenation 최적화
                        if (Utility_TagManager.IsWorkEnabled(p, ManagedTags.AllowWorkPrefix + work.defName))
                        {
                            __result = false;
                            return false;
                        }
                    }

                    return true;
                }
            }

            #endregion

            #region Work Tab UI - 최적화된 UI 렌더링

            /// <summary>
            /// 🟢 최적화된 툴팁 제공
            /// </summary>
            [HarmonyPatch(typeof(WidgetsWork), nameof(WidgetsWork.TipForPawnWorker))]
            public static class Patch_WidgetsWork_TipForPawnWorker
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static bool Prefix(Pawn p, WorkTypeDef wDef, bool incapableBecauseOfCapacities, ref string __result)
                {
                    if (!p.RaceProps.Humanlike && p.skills == null)
                    {
                        // 🟢 String interpolation 최적화
                        __result = $"Simulated {wDef.label} skill: {Utility_SkillManager.SetInjectedSkillLevel(p)}";
                        return false;
                    }
                    return true;
                }
            }

            /// <summary>
            /// 🟢 최적화된 Work Priority 셀 처리
            /// </summary>
            [HarmonyPatch(typeof(PawnColumnWorker_WorkPriority))]
            public static class Patch_PawnColumnWorker_WorkPriority_SafeAccess
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                [HarmonyPrefix]
                [HarmonyPatch(nameof(PawnColumnWorker_WorkPriority.DoCell))]
                public static bool DoCell_Prefix(PawnColumnWorker_WorkPriority __instance, Rect rect, Pawn pawn, PawnTable table)
                {
                    // 🟢 조기 반환 최적화
                    if (pawn?.def?.race == null || !pawn.Spawned || pawn.Dead)
                        return true;

                    if (!Utility_IdentityManager.MatchesIdentityFlags(pawn, PawnIdentityFlags.IsColonist))
                        return true;

                    Utility_WorkSettingsManager.EnsureWorkSettingsInitialized(pawn);
                    return true;
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                [HarmonyPrefix]
                [HarmonyPatch(nameof(PawnColumnWorker_WorkPriority.Compare))]
                public static bool Compare_Prefix(PawnColumnWorker_WorkPriority __instance, Pawn a, Pawn b, ref int __result)
                {
                    // 🟢 빠른 스킬 체크
                    if (a.skills != null && b.skills != null)
                        return true;

                    var modExtA = Utility_CacheManager.GetModExtension(a.def);
                    var modExtB = Utility_CacheManager.GetModExtension(b.def);

                    if (modExtA == null && modExtB == null)
                        return true;

                    // 🟢 최적화된 정렬 값 계산
                    float valA = Utility_SkillManager.GetWorkPrioritySortingValue(a, __instance.def.workType);
                    float valB = Utility_SkillManager.GetWorkPrioritySortingValue(b, __instance.def.workType);
                    __result = valA.CompareTo(valB);
                    return false;
                }
            }

            /// <summary>
            /// 🟢 최적화된 Work Box 배경 그리기 - 텍스처 캐싱 개선
            /// </summary>
            [HarmonyPatch(typeof(WidgetsWork), "DrawWorkBoxBackground")]
            public static class Patch_WidgetsWork_DrawWorkBoxBackground
            {
                // 🟢 텍스처 캐시 (자주 사용되는 텍스처들)
                private static readonly Dictionary<string, Texture2D> _textureCache = new Dictionary<string, Texture2D>();

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private static Texture2D GetCachedTexture(string textureName)
                {
                    if (!_textureCache.TryGetValue(textureName, out var texture))
                    {
                        texture = ContentFinder<Texture2D>.Get(textureName);
                        _textureCache[textureName] = texture;
                    }
                    return texture;
                }

                public static bool Prefix(Rect rect, Pawn p, WorkTypeDef workDef)
                {
                    if (p == null || workDef == null)
                        return true;

                    // 🟢 스킬 값 캐시된 계산 사용
                    float skillAvg = Utility_SkillManager.SafeAverageOfRelevantSkillsFor(p, workDef);

                    // 🟢 룩업 테이블 방식으로 텍스처 선택 최적화
                    Texture2D baseTex, blendTex;
                    float blendFactor;

                    if (skillAvg < 4f)
                    {
                        baseTex = WidgetsWork.WorkBoxBGTex_Awful;
                        blendTex = WidgetsWork.WorkBoxBGTex_Bad;
                        blendFactor = skillAvg * 0.25f; // / 4f 최적화
                    }
                    else if (skillAvg <= 14f)
                    {
                        baseTex = WidgetsWork.WorkBoxBGTex_Bad;
                        blendTex = WidgetsWork.WorkBoxBGTex_Mid;
                        blendFactor = (skillAvg - 4f) * 0.1f; // / 10f 최적화
                    }
                    else
                    {
                        baseTex = WidgetsWork.WorkBoxBGTex_Mid;
                        blendTex = WidgetsWork.WorkBoxBGTex_Excellent;
                        blendFactor = Math.Min((skillAvg - 14f) / 6f, 1f); // 상한 제한
                    }

                    // 🟢 GPU 호출 최적화 - 색상 변경 최소화
                    GUI.DrawTexture(rect, baseTex);
                    if (blendFactor > 0f)
                    {
                        var oldColor = GUI.color;
                        GUI.color = new Color(1f, 1f, 1f, blendFactor);
                        GUI.DrawTexture(rect, blendTex);
                        GUI.color = oldColor;
                    }

                    // 🟢 조건부 렌더링 최적화
                    if (p.RaceProps?.Humanlike == true && p.Ideo?.IsWorkTypeConsideredDangerous(workDef) == true)
                    {
                        GUI.DrawTexture(rect, WidgetsWork.WorkBoxOverlay_PreceptWarning);
                    }

                    // 🟢 스킬 경고 최적화
                    if (workDef.relevantSkills?.Count > 0 && skillAvg <= 2f &&
                        p.workSettings?.WorkIsActive(workDef) == true)
                    {
                        GUI.DrawTexture(rect.ContractedBy(2f), WidgetsWork.WorkBoxOverlay_Warning);
                    }

                    // 🟢 Passion 렌더링 최적화
                    DrawPassionIcon(rect, p, workDef);

                    return false;
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private static void DrawPassionIcon(Rect rect, Pawn p, WorkTypeDef workDef)
                {
                    Passion passion = p.skills?.MaxPassionOfRelevantSkillsFor(workDef) ??
                                    Utility_SkillManager.SetInjectedPassion(p.def,
                                        workDef.relevantSkills?.FirstOrDefault());

                    if (passion > Passion.None)
                    {
                        var oldColor = GUI.color;
                        GUI.color = new Color(1f, 1f, 1f, 0.4f);

                        var passionRect = new Rect(rect.center.x, rect.center.y,
                                                  rect.width * 0.5f, rect.height * 0.5f);

                        GUI.DrawTexture(passionRect, passion == Passion.Minor ?
                            WidgetsWork.PassionWorkboxMinorIcon :
                            WidgetsWork.PassionWorkboxMajorIcon);

                        GUI.color = oldColor;
                    }
                }
            }

            /// <summary>
            /// 🟢 최적화된 Work Box 그리기 - 마우스 이벤트 및 우선순위 처리
            /// </summary>
            [HarmonyPatch(typeof(WidgetsWork), nameof(WidgetsWork.DrawWorkBoxFor))]
            public static class Patch_WidgetsWork_DrawWorkBoxFor
            {
                // 🟢 자주 사용되는 Rect 캐싱
                private static readonly Dictionary<(float x, float y), Rect> _rectCache =
                    new Dictionary<(float x, float y), Rect>();

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private static Rect GetCachedRect(float x, float y)
                {
                    var key = (x, y);
                    if (!_rectCache.TryGetValue(key, out var rect))
                    {
                        rect = new Rect(x, y, 25f, 25f);
                        _rectCache[key] = rect;
                    }
                    return rect;
                }

                public static bool Prefix(float x, float y, Pawn p, WorkTypeDef wType, bool incapableBecauseOfCapacities)
                {
                    // 🟢 조기 반환 최적화
                    if (p?.def?.race == null)
                        return true;

                    var modExtension = Utility_CacheManager.GetModExtension(p.def);
                    if (modExtension == null)
                        return true;

                    if (!Utility_ThinkTreeManager.HasAllowWorkTag(p))
                        return true;

                    Utility_WorkSettingsManager.SafeEnsurePawnReadyForWork(p);

                    // 🟢 비활성화된 작업 타입 처리
                    if (p.WorkTypeIsDisabled(wType))
                    {
                        if (p.IsWorkTypeDisabledByAge(wType, out var minAgeRequired))
                        {
                            var rect = GetCachedRect(x, y);
                            HandleAgeDisabledWorkType(rect, p, wType, minAgeRequired);
                        }
                        return false;
                    }

                    var workRect = GetCachedRect(x, y);

                    // 🟢 색상 변경 최적화
                    if (incapableBecauseOfCapacities)
                    {
                        var oldColor = GUI.color;
                        GUI.color = new Color(1f, 0.3f, 0.3f);

                        // 🟢 캐시된 리플렉션 메서드 사용
                        _drawWorkBoxBackgroundMethod?.Invoke(null, new object[] { workRect, p, wType });

                        GUI.color = oldColor;
                    }
                    else
                    {
                        _drawWorkBoxBackgroundMethod?.Invoke(null, new object[] { workRect, p, wType });
                    }

                    // 🟢 우선순위 모드별 처리 분기
                    if (Find.PlaySettings.useWorkPriorities)
                    {
                        return HandlePriorityMode(workRect, p, wType);
                    }
                    else
                    {
                        return HandleCheckboxMode(workRect, p, wType);
                    }
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private static void HandleAgeDisabledWorkType(Rect rect, Pawn p, WorkTypeDef wType, int minAgeRequired)
                {
                    if (Event.current.type == EventType.MouseDown && Mouse.IsOver(rect))
                    {
                        Messages.Message("MessageWorkTypeDisabledAge".Translate(p, p.ageTracker.AgeBiologicalYears, wType.labelShort, minAgeRequired),
                            p, MessageTypeDefOf.RejectInput, historical: false);
                        SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    }
                    GUI.DrawTexture(rect, ContentFinder<Texture2D>.Get("UI/Widgets/WorkBoxBG_AgeDisabled"));
                }

                private static bool HandlePriorityMode(Rect rect, Pawn p, WorkTypeDef wType)
                {
                    int priority = p.workSettings.GetPriority(wType);

                    // 🟢 우선순위 표시 최적화
                    if (priority > 0)
                    {
                        Text.Anchor = TextAnchor.MiddleCenter;
                        var oldColor = GUI.color;
                        GUI.color = WidgetsWork.ColorOfPriority(priority);
                        Widgets.Label(rect.ContractedBy(-3f), priority.ToStringCached());
                        GUI.color = oldColor;
                        Text.Anchor = TextAnchor.UpperLeft;
                    }

                    // 🟢 마우스 이벤트 처리 최적화
                    if (Event.current.type == EventType.MouseDown && Mouse.IsOver(rect))
                    {
                        HandlePriorityClick(p, wType, priority, Event.current.button);
                        Event.current.Use();

                        // 🟢 지식 시스템 알림
                        PlayerKnowledgeDatabase.KnowledgeDemonstrated(ConceptDefOf.WorkTab, KnowledgeAmount.SpecificInteraction);
                        PlayerKnowledgeDatabase.KnowledgeDemonstrated(ConceptDefOf.ManualWorkPriorities, KnowledgeAmount.SmallInteraction);
                    }

                    return false;
                }

                private static bool HandleCheckboxMode(Rect rect, Pawn p, WorkTypeDef wType)
                {
                    // 🟢 체크박스 표시
                    if (p.workSettings.GetPriority(wType) > 0)
                    {
                        GUI.DrawTexture(rect, WidgetsWork.WorkBoxCheckTex);
                    }

                    if (!Widgets.ButtonInvisible(rect))
                        return false;

                    // 🟢 토글 처리
                    ToggleWork(p, wType);
                    PlayerKnowledgeDatabase.KnowledgeDemonstrated(ConceptDefOf.WorkTab, KnowledgeAmount.SpecificInteraction);
                    return false;
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private static void HandlePriorityClick(Pawn p, WorkTypeDef wType, int currentPriority, int mouseButton)
                {
                    int newPriority = mouseButton == 0
                        ? (currentPriority - 1 + 5) % 5  // 좌클릭: 감소
                        : (currentPriority + 1) % 5;     // 우클릭: 증가

                    p.workSettings.SetPriority(wType, newPriority);
                    SoundDefOf.DragSlider.PlayOneShotOnCamera();

                    // 🟢 피드백 처리
                    if (newPriority > 0)
                    {
                        HandleWorkActivationFeedback(p, wType);
                    }
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private static void ToggleWork(Pawn p, WorkTypeDef wType)
                {
                    if (p.workSettings.GetPriority(wType) > 0)
                    {
                        p.workSettings.SetPriority(wType, 0);
                        SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
                    }
                    else
                    {
                        p.workSettings.SetPriority(wType, 3);
                        SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                        HandleWorkActivationFeedback(p, wType);
                    }
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private static void HandleWorkActivationFeedback(Pawn p, WorkTypeDef wType)
                {
                    // 🟢 스킬 경고
                    if (wType.relevantSkills?.Any() == true)
                    {
                        float avgSkill = Utility_SkillManager.SafeAverageOfRelevantSkillsFor(p, wType);
                        if (avgSkill <= 2f)
                        {
                            SoundDefOf.Crunch.PlayOneShotOnCamera();
                        }
                    }

                    // 🟢 이데올로기 경고
                    if (p.Ideo?.IsWorkTypeConsideredDangerous(wType) == true)
                    {
                        Messages.Message("PawnControl_MessageIdeoOpposedWorkTypeSelected"
                            .Translate(p, wType.gerundLabel), p, MessageTypeDefOf.CautionInput, historical: false);
                        SoundDefOf.DislikedWorkTypeActivated.PlayOneShotOnCamera();
                    }
                }
            }

            #endregion

            #region Map Initialization

            [HarmonyPatch(typeof(Map), nameof(Map.FinalizeInit))]
            public static class Patch_Map_FinalizeInit_FullInitialize
            {
                [HarmonyPostfix]
                public static void Postfix(Map __instance)
                {
                    if (__instance == null) return;

                    // 🟢 Identity flags 미리 로드
                    if (!Utility_IdentityManager.IsIdentityFlagsPreloaded)
                    {
                        Utility_IdentityManager.BuildIdentityFlagCache(true);
                    }

                    if (Utility_IdentityManager.IsIdentityFlagsPreloaded)
                    {
                        Utility_WorkSettingsManager.FullInitializeAllEligiblePawns(__instance, forceLock: true);
                        if (Utility_DebugManager.ShouldLogDetailed())
                        {
                            Utility_DebugManager.LogNormal($"FullInitializeAllEligiblePawns completed for map '{__instance.uniqueID}'");
                        }
                    }
                    else
                    {
                        Utility_DebugManager.LogWarning($"Identity flags not preloaded for map '{__instance.uniqueID}'. Initialization skipped.");
                    }
                }
            }

            #endregion

            #region Bio Tab Management - 🟢 캐시된 PropertyInfo 사용

            [HarmonyPatch(typeof(ITab_Pawn_Character), nameof(ITab_Pawn_Character.IsVisible), MethodType.Getter)]
            public static class Patch_ITab_Pawn_Character_IsVisible
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                [HarmonyPrefix]
                public static bool Prefix(ITab_Pawn_Character __instance, ref bool __result)
                {
                    try
                    {
                        if (_selThingProperty == null)
                            return true;

                        // 🟢 캐시된 PropertyInfo 사용
                        var thing = _selThingProperty.GetValue(__instance) as Thing;
                        if (thing == null) return true;

                        var pawn = thing as Pawn ?? (thing as Corpse)?.InnerPawn;
                        if (pawn?.def == null) return true;

                        var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                        if (modExtension == null) return true;

                        int pawnId = pawn.thingIDNumber;

                        // 🟢 캐시 우선 확인
                        if (Utility_CacheManager.BioTabVisibility.TryGetValue(pawnId, out bool shouldHide))
                        {
                            if (shouldHide)
                            {
                                __result = false;
                                return false;
                            }
                            return true;
                        }

                        // 🟢 가시성 결정 및 캐시
                        bool hideBioTab = !pawn.RaceProps.Humanlike;
                        Utility_CacheManager.BioTabVisibility[pawnId] = hideBioTab;

                        if (hideBioTab)
                        {
                            if (Utility_DebugManager.ShouldLogDetailed())
                                Utility_DebugManager.LogNormal($"Hidden Bio tab for: {pawn.LabelShort}");

                            __result = false;
                            return false;
                        }

                        return true;
                    }
                    catch (Exception ex)
                    {
                        Utility_DebugManager.LogError($"Error in Bio tab visibility check: {ex}");
                        return true;
                    }
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                [HarmonyPostfix]
                public static void Postfix(ITab_Pawn_Character __instance, ref bool __result)
                {
                    if (!__result) return;

                    try
                    {
                        var selThing = _selThingProperty?.GetValue(__instance) as Thing;
                        if (selThing == null) return;

                        var pawn = selThing as Pawn ?? (selThing as Corpse)?.InnerPawn;
                        if (pawn?.def == null) return;

                        var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                        if (modExtension == null) return;

                        int pawnId = pawn.thingIDNumber;
                        if (Utility_CacheManager.BioTabVisibility.TryGetValue(pawnId, out bool shouldHide) && shouldHide)
                        {
                            __result = false;
                            return;
                        }

                        bool hideBioTab = !pawn.RaceProps.Humanlike;
                        Utility_CacheManager.BioTabVisibility[pawnId] = hideBioTab;

                        if (hideBioTab)
                        {
                            __result = false;
                            if (modExtension.debugMode && Utility_DebugManager.ShouldLogDetailed())
                                Utility_DebugManager.LogNormal($"Postfix: Hidden Bio tab for {pawn.LabelShort}");
                        }
                    }
                    catch
                    {
                        // Postfix에서는 예외 무시
                    }
                }
            }

            #endregion

            #region Pawn Generation - 최적화된 생성 처리

            [HarmonyPatch(typeof(PawnGenerator), "GeneratePawn", new Type[] { typeof(PawnGenerationRequest) })]
            public static class Patch_PawnGenerator_GeneratePawn
            {
                [HarmonyPostfix]
                public static void Postfix(Pawn __result)
                {
                    // 🟢 조기 반환 최적화
                    if (__result?.def == null || __result.RaceProps.Humanlike)
                        return;

                    var modExtension = Utility_CacheManager.GetModExtension(__result.def);
                    if (modExtension == null) return;

                    // 🟢 배치 초기화 - 한번에 모든 필요한 컴포넌트 설정
                    InitializeModdedPawn(__result);
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private static void InitializeModdedPawn(Pawn pawn)
                {
                    var stubDef = HediffDef.Named("PawnControl_StatStub");
                    bool hasStub = pawn.health.hediffSet.HasHediff(stubDef);
                    bool alreadyInjected = Utility_StatManager.HasAlreadyInjected(pawn);

                    if (!hasStub && !alreadyInjected)
                    {
                        Utility_SkillManager.ForceAttachSkillTrackerIfMissing(pawn);
                        Utility_StatManager.InjectConsolidatedStatHediff(pawn);
                        Utility_WorkSettingsManager.EnsureWorkSettingsInitialized(pawn);
                    }
                }
            }

            [HarmonyPatch(typeof(Pawn), "SpawnSetup", new[] { typeof(Map), typeof(bool) })]
            public static class Patch_Pawn_SpawnSetup
            {
                [HarmonyPostfix]
                public static void Postfix(Pawn __instance, Map map, bool respawningAfterLoad)
                {
                    // 🟢 조기 반환 최적화
                    if (__instance?.def == null || __instance.Dead || __instance.Destroyed || __instance.RaceProps.Humanlike)
                        return;

                    var modExtension = Utility_CacheManager.GetModExtension(__instance.def);
                    if (modExtension == null || !Utility_ThinkTreeManager.HasAllowOrBlockWorkTag(__instance))
                        return;

                    // 🟢 MindState 활성화 최적화
                    if (__instance.mindState != null && __instance.workSettings?.EverWork == true)
                    {
                        __instance.mindState.Active = true;
                        if (__instance.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                        {
                            Utility_DebugManager.LogNormal($"Activated {__instance.LabelShort}'s mind during SpawnSetup");
                        }
                    }

                    // 🟢 지연된 초기화 - 비동기 처리
                    LongEventHandler.ExecuteWhenFinished(() =>
                    {
                        if (__instance.health?.hediffSet == null || !__instance.Spawned)
                            return;

                        var stubDef = HediffDef.Named("PawnControl_StatStub");
                        if (__instance.health.hediffSet.HasHediff(stubDef))
                            return;

                        // 🟢 배치 초기화
                        Utility_SkillManager.ForceAttachSkillTrackerIfMissing(__instance);
                        Utility_StatManager.InjectConsolidatedStatHediff(__instance);
                        Utility_WorkSettingsManager.EnsureWorkSettingsInitialized(__instance);

                        if (Utility_DebugManager.ShouldLogDetailed())
                        {
                            Utility_DebugManager.DumpThinkerStatus(__instance);
                            Utility_DebugManager.DiagnoseWorkGiversForPawn(__instance);
                        }
                    });
                }
            }

            [HarmonyPatch(typeof(Pawn), nameof(Pawn.SetFaction), new[] { typeof(Faction), typeof(Pawn) })]
            public static class Patch_Pawn_SetFaction_InvalidateColonistLikeCache
            {
                [HarmonyPostfix]
                public static void Postfix(Pawn __instance, Faction newFaction)
                {
                    if (__instance?.Map == null) return;

                    // 플레이어 진영으로 바뀐 케이스만 처리
                    if (newFaction == Faction.OfPlayer)
                    {
                        // 1) 캐시 무효화
                        Utility_CacheManager.ClearCacheForPawn(__instance);

                        // 2) WorkSettings 보장
                        Utility_WorkSettingsManager.EnsureWorkSettingsInitialized(__instance);
                        Utility_WorkSettingsManager.EnsureWorkGiversPopulated(__instance);
                    }
                }
            }

            [HarmonyPatch(typeof(MapPawns), nameof(MapPawns.RegisterPawn))]
            public static class Patch_MapPawns_RegisterPawn_ReinitWork
            {
                [HarmonyPostfix]
                public static void Postfix(Pawn p)
                {
                    if (p?.Map == null) return;
                    var ext = Utility_CacheManager.GetModExtension(p.def);
                    if (p.Faction == Faction.OfPlayer && ext != null && Utility_ThinkTreeManager.HasAllowOrBlockWorkTag(p))
                    {
                        Utility_CacheManager.ClearCacheForPawn(p);
                        Utility_WorkSettingsManager.EnsureWorkSettingsInitialized(p);
                        Utility_WorkSettingsManager.EnsureWorkGiversPopulated(p);
                    }
                }
            }

            #endregion

            #region Safe-Guard for new game

            [HarmonyPatch(typeof(Game), "InitNewGame")]
            public static class Patch_Game_InitNewGame_CleanupModExtensions
            {
                [HarmonyPrefix]
                public static void Prefix()
                {
                    Utility_CacheManager.CleanupRuntimeModExtensions();
                }
            }

            #endregion

            #region Job Management - 최적화된 작업 처리

            [HarmonyPatch(typeof(GenConstruct), nameof(GenConstruct.HandleBlockingThingJob))]
            public static class Patch_GenConstruct_HandleBlockingThingJob_PlantCutOverride
            {
                [HarmonyPostfix]
                public static void Postfix(ref Job __result, Thing constructible, Pawn worker, bool forced = false)
                {
                    // 🟢 조기 반환 최적화
                    if (__result != null || constructible == null || worker == null || worker.RaceProps.Humanlike)
                        return;

                    if (!Utility_TagManager.IsWorkEnabled(worker, "CutPlant"))
                        return;

                    // 🟢 블로킹 오브젝트 검사
                    Thing blocker = GenConstruct.FirstBlockingThing(constructible, worker);
                    if (blocker?.def.category != ThingCategory.Plant)
                        return;

                    // 🟢 도달 가능성 검사 최적화
                    if (!worker.CanReserveAndReach(blocker, PathEndMode.ClosestTouch,
                        worker.NormalMaxDanger(), 1, -1, null, forced))
                        return;

                    __result = JobMaker.MakeJob(JobDefOf.CutPlant, blocker);

                    if (Utility_DebugManager.ShouldLogDetailed())
                    {
                        Utility_DebugManager.LogNormal($"Generated CutPlant job for {worker.LabelShort} on {blocker.Label}");
                    }
                }
            }

            [HarmonyPatch(typeof(Toils_Haul), "JumpToCarryToNextContainerIfPossible")]
            public static class Patch_Toils_Haul_JumpToCarryToNextContainerIfPossible
            {
                [HarmonyPrefix]
                public static bool Prefix(ref Toil __result, Toil carryToContainerToil, TargetIndex primaryTargetInd)
                {
                    __result = Utility_JobManager.GeneratePatchedToil(carryToContainerToil, primaryTargetInd);
                    return false;
                }
            }

            [HarmonyPatch(typeof(GenConstruct), nameof(GenConstruct.CanConstruct),
                new Type[] { typeof(Thing), typeof(Pawn), typeof(bool), typeof(bool), typeof(JobDef) })]
            public static class Patch_GenConstruct_CanConstruct
            {
                [HarmonyPrefix]
                public static bool Prefix(Thing t, Pawn p, bool checkSkills, bool forced,
                    JobDef jobForReservation, ref bool __result)
                {
                    // 🟢 휴먼라이크 또는 비모드 폰 조기 반환
                    if (p.RaceProps.Humanlike || Utility_CacheManager.GetModExtension(p.def) == null)
                        return true;

                    // 🟢 블로킹 체크
                    if (GenConstruct.FirstBlockingThing(t, p) != null)
                    {
                        __result = false;
                        return false;
                    }

                    // 🟢 예약 로직 최적화
                    if (jobForReservation != null)
                    {
                        if (!p.Spawned ||
                            !p.Map.reservationManager.OnlyReservationsForJobDef(t, jobForReservation) ||
                            !p.CanReach(t, PathEndMode.Touch, forced ? Danger.Deadly : p.NormalMaxDanger()))
                        {
                            __result = false;
                            return false;
                        }
                    }
                    else if (!p.CanReserveAndReach(t, PathEndMode.Touch,
                        forced ? Danger.Deadly : p.NormalMaxDanger(), 1, -1, null, forced))
                    {
                        __result = false;
                        return false;
                    }

                    // 🟢 화재 체크
                    if (t.IsBurning())
                    {
                        __result = false;
                        return false;
                    }

                    // 🟢 블루프린트 특별 체크
                    if ((t.def.IsBlueprint || t.def.IsFrame) && t.def.entityDefToBuild is ThingDef thingDef)
                    {
                        if (thingDef.building?.isAttachment == true)
                        {
                            Thing wallAttachedTo = GenConstruct.GetWallAttachedTo(t);
                            if (wallAttachedTo == null || wallAttachedTo.def.IsBlueprint || wallAttachedTo.def.IsFrame)
                            {
                                __result = false;
                                return false;
                            }
                        }
                    }

                    __result = true;
                    return false;
                }
            }

            [HarmonyPatch(typeof(Frame), nameof(Frame.CompleteConstruction))]
            public static class Patch_Frame_CompleteConstruction
            {
                [HarmonyPrefix]
                public static bool Prefix(Frame __instance, Pawn worker)
                {
                    // 🟢 조기 반환 최적화
                    if (__instance == null || worker == null || worker.RaceProps.Humanlike)
                        return true;

                    var modExtension = Utility_CacheManager.GetModExtension(worker.def);
                    if (modExtension == null || !Utility_TagManager.HasTagSet(worker.def))
                        return true;

                    try
                    {
                        return HandleModdedConstruction(__instance, worker);
                    }
                    catch (Exception ex)
                    {
                        Utility_DebugManager.LogError($"Error in modded construction: {ex}");
                        return true; // 바닐라 폴백
                    }
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private static bool HandleModdedConstruction(Frame frame, Pawn worker)
                {
                    Thing frameInstance = frame.holdingOwner?.Take(frame);
                    if (frameInstance == null) return true;

                    frame.resourceContainer.ClearAndDestroyContents();
                    Map map = worker.Map;
                    frame.Destroy(DestroyMode.Vanish);

                    // 🟢 사운드 재생 최적화
                    if (frame.def.entityDefToBuild is ThingDef thingDef &&
                        thingDef.category == ThingCategory.Building &&
                        frame.def.GetStatValueAbstract(StatDefOf.WorkToBuild) > 150f)
                    {
                        SoundDefOf.Building_Complete.PlayOneShot(new TargetInfo(frameInstance.Position, map));
                    }

                    // 🟢 건설 완료 처리
                    if (frame.def.entityDefToBuild is ThingDef entityDef)
                    {
                        CompleteThingConstruction(frame, frameInstance, entityDef, worker, map);
                    }
                    else
                    {
                        CompleteTerrainConstruction(frame, frameInstance.Position, map);
                    }

                    worker.records.Increment(RecordDefOf.ThingsConstructed);
                    return false;
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private static void CompleteThingConstruction(Frame frame, Thing frameInstance,
                    ThingDef entityDef, Pawn worker, Map map)
                {
                    Thing thing = ThingMaker.MakeThing(entityDef, frameInstance.Stuff);
                    thing.SetFactionDirect(frameInstance.Faction);

                    // 🟢 품질 설정 최적화
                    var compQuality = thing.TryGetComp<CompQuality>();
                    if (compQuality != null)
                    {
                        compQuality.SetQuality(QualityCategory.Normal, ArtGenerationContext.Colony);
                    }

                    var compArt = thing.TryGetComp<CompArt>();
                    if (compArt != null)
                    {
                        compArt.InitializeArt(ArtGenerationContext.Colony);
                        compArt.JustCreatedBy(worker);
                    }

                    // 🟢 체력 계산 최적화
                    thing.HitPoints = Mathf.CeilToInt(
                        (float)frame.HitPoints / frameInstance.MaxHitPoints * thing.MaxHitPoints);

                    GenSpawn.Spawn(thing, frameInstance.Position, map, frameInstance.Rotation, WipeMode.Vanish);
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private static void CompleteTerrainConstruction(Frame frame, IntVec3 position, Map map)
                {
                    map.terrainGrid.SetTerrain(position, (TerrainDef)frame.def.entityDefToBuild);
                    FilthMaker.RemoveAllFilth(position, map);
                }
            }

            #endregion

            #region Work Settings Tracking - 🟢 캐시된 FieldInfo 사용

            [HarmonyPatch(typeof(Pawn_WorkSettings), nameof(Pawn_WorkSettings.SetPriority))]
            public static class Patch_PawnWorkSettings_SetPriority_TrackNonHumanlike
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                static void Postfix(Pawn_WorkSettings __instance, WorkTypeDef w, int priority)
                {
                    if (__instance == null || w == null) return;

                    // 🟢 캐시된 FieldInfo 사용
                    var pawn = _pawnFieldInWorkSettings.GetValue(__instance) as Pawn;
                    if (pawn?.RaceProps.Humanlike != false) return;

                    var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                    if (modExtension == null || !Utility_TagManager.HasTagSet(pawn.def)) return;

                    if (Utility_DebugManager.ShouldLogDetailed())
                    {
                        Utility_DebugManager.LogNormal($"{pawn.LabelShort} set '{w.defName}' → {priority}");
                    }

                    // 🟢 배치 캐시 정리 및 재초기화
                    Utility_TagManager.ClearCacheForRace(pawn.def);
                    __instance.EnableAndInitializeIfNotAlreadyInitialized();
                }
            }

            #endregion
        }

        public static class Patch_Iteration3_DrafterInjection
        {
            #region Cached Reflection - 모든 리플렉션 호출을 static readonly로 캐시

            // Pawn 트래커 필드 캐시 (이미 있음 - 좋음!)
            public static readonly FieldInfo EquipmentField = AccessTools.Field(typeof(Pawn), "equipment");
            public static readonly FieldInfo ApparelField = AccessTools.Field(typeof(Pawn), "apparel");
            public static readonly FieldInfo InventoryField = AccessTools.Field(typeof(Pawn), "inventory");
            private static readonly FieldInfo _draftedField = AccessTools.Property(typeof(FloatMenuOptionProvider), "Drafted")?.GetGetMethod()?.IsVirtual == true
                ? null : AccessTools.Field(typeof(FloatMenuOptionProvider), "Drafted");
            private static readonly FieldInfo _undraftedField = AccessTools.Property(typeof(FloatMenuOptionProvider), "Undrafted")?.GetGetMethod()?.IsVirtual == true
                ? null : AccessTools.Field(typeof(FloatMenuOptionProvider), "Undrafted");
            private static readonly FieldInfo _mechanoidCanDoField = AccessTools.Property(typeof(FloatMenuOptionProvider), "MechanoidCanDo")?.GetGetMethod()?.IsVirtual == true
                ? null : AccessTools.Field(typeof(FloatMenuOptionProvider), "MechanoidCanDo");

            // 추가 리플렉션 캐시
            private static readonly FieldInfo _pawnFieldInJobTracker = AccessTools.Field(typeof(Pawn_JobTracker), "pawn");
            private static readonly FieldInfo _pawnFieldInPathFollower = AccessTools.Field(typeof(Pawn_PathFollower), "pawn");
            private static readonly FieldInfo _thinkRootField = AccessTools.Field(typeof(Pawn_Thinker), "thinkRoot");
            private static readonly Func<FloatMenuOptionProvider, bool> _getDrafted =
                AccessTools.MethodDelegate<Func<FloatMenuOptionProvider, bool>>(AccessTools.PropertyGetter(typeof(FloatMenuOptionProvider), "Drafted"));
            private static readonly Func<FloatMenuOptionProvider, bool> _getUndrafted =
                AccessTools.MethodDelegate<Func<FloatMenuOptionProvider, bool>>(AccessTools.PropertyGetter(typeof(FloatMenuOptionProvider), "Undrafted"));
            private static readonly Func<FloatMenuOptionProvider, bool> _getMechanoidCanDo =
                AccessTools.MethodDelegate<Func<FloatMenuOptionProvider, bool>>(AccessTools.PropertyGetter(typeof(FloatMenuOptionProvider), "MechanoidCanDo"));
            private static readonly Func<FloatMenuOptionProvider, bool> _getRequiresManipulation =
                AccessTools.MethodDelegate<Func<FloatMenuOptionProvider, bool>>(AccessTools.PropertyGetter(typeof(FloatMenuOptionProvider), "RequiresManipulation"));

            // MethodInfo 캐시
            private static readonly MethodInfo _drafterGetGizmosMI = AccessTools.Method(typeof(Pawn_DraftController), "GetGizmos");
            private static readonly Func<Pawn_DraftController, IEnumerable<Gizmo>> _drafterGetGizmos =
                AccessTools.MethodDelegate<Func<Pawn_DraftController, IEnumerable<Gizmo>>>(_drafterGetGizmosMI);

            // 자주 사용되는 JobDef 캐시
            private static readonly ConcurrentDictionary<string, JobDef> _jobDefCache = new ConcurrentDictionary<string, JobDef>();

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static JobDef GetCachedJobDef(string defName)
            {
                return _jobDefCache.GetOrAdd(defName, name => DefDatabase<JobDef>.GetNamedSilentFail(name));
            }

            #endregion

            #region Pawn Spawn Setup - 최적화된 초기화

            [HarmonyPatch(typeof(Pawn), "SpawnSetup", new Type[] { typeof(Map), typeof(bool) })]
            public static class Patch_Pawn_SpawnSetup_DraftInjector
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                [HarmonyPostfix]
                public static void Postfix(Pawn __instance, Map map)
                {
                    // 조기 반환 최적화
                    if (__instance?.def?.race == null || __instance.Dead || __instance.RaceProps.Humanlike)
                        return;

                    var modExtension = Utility_CacheManager.GetModExtension(__instance.def);
                    if (modExtension == null || !Utility_TagManager.ForceDraftable(__instance))
                        return;

                    // 배치 처리로 트래커 초기화
                    InitializeTrackersBatch(__instance, modExtension);
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private static void InitializeTrackersBatch(Pawn pawn, NonHumanlikePawnControlExtension modExtension)
                {
                    // Drafter 초기화
                    Utility_DrafterManager.EnsureDrafter(pawn, modExtension, isSpawnSetup: true);

                    // 트래커들을 배치로 확인 및 생성
                    bool equipmentMissing = EquipmentField.GetValue(pawn) == null;
                    bool apparelMissing = ApparelField.GetValue(pawn) == null;
                    bool inventoryMissing = InventoryField.GetValue(pawn) == null;

                    // 한번에 모든 트래커 생성
                    if (equipmentMissing)
                    {
                        var eqTracker = new Pawn_EquipmentTracker(pawn);
                        EquipmentField.SetValue(pawn, eqTracker);
                        eqTracker.Notify_PawnSpawned();
                    }

                    if (apparelMissing)
                    {
                        ApparelField.SetValue(pawn, new Pawn_ApparelTracker(pawn));
                    }

                    if (inventoryMissing)
                    {
                        InventoryField.SetValue(pawn, new Pawn_InventoryTracker(pawn));
                    }
                }
            }

            #endregion

            #region Gizmo Management - 최적화된 UI 처리

            [HarmonyPatch]
            public static class Patch_Pawn_GetGizmos_AddDrafterViaReflection_Cached
            {
                public static MethodBase TargetMethod() =>
                    AccessTools.Method(typeof(Pawn), "GetGizmos", Type.EmptyTypes);

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
                {
                    // 바닐라 기즈모들 먼저 반환
                    foreach (var g in __result)
                        yield return g;

                    // 빠른 체크
                    if (__instance?.def == null || __instance.Faction != Faction.OfPlayer)
                        yield break;

                    var modExtension = Utility_CacheManager.GetModExtension(__instance.def);
                    if (modExtension == null)
                        yield break;

                    var drafter = __instance.drafter;
                    if (drafter != null)
                    {
                        // 캐시된 delegate 사용
                        foreach (var g in _drafterGetGizmos(drafter))
                            yield return g;
                    }
                }
            }

            #endregion

            #region Draft State Management - 최적화된 드래프트 처리

            [HarmonyPatch(typeof(Pawn_DraftController), nameof(Pawn_DraftController.Drafted), MethodType.Setter)]
            public static class Patch_PawnDraftController_DraftedSetter_ForceInterrupt
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static void Postfix(Pawn_DraftController __instance, bool value)
                {
                    var pawn = __instance?.pawn;
                    if (pawn?.def == null) return;

                    // 드래프트될 때만 처리 (언드래프트는 스킵)
                    if (!value) return;

                    var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                    if (modExtension == null || !Utility_TagManager.ForceDraftable(pawn))
                        return;

                    // 배치 처리로 상태 정리
                    HandleDraftActivation(pawn);
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private static void HandleDraftActivation(Pawn pawn)
                {
                    // 현재 작업 상태 확인
                    bool isWandering = IsWanderingJob(pawn.jobs?.curJob);

                    // 모든 작업 즉시 중단
                    pawn.jobs.StopAll(false);
                    pawn.ClearAllReservations(true);
                    pawn.jobs.ClearQueuedJobs();

                    // MindState 정리
                    if (pawn.mindState != null)
                    {
                        pawn.mindState.maxDistToSquadFlag = -1f;
                        pawn.mindState.duty = null;
                    }

                    // 이동 중단 처리
                    if (isWandering)
                    {
                        StopMovement(pawn);
                    }

                    // ThinkTree 재구축
                    ForceRebuildThinkTree(pawn);

                    if (Utility_DebugManager.ShouldLogDetailed())
                    {
                        Utility_DebugManager.LogNormal($"Drafted pawn {pawn.LabelShort} - jobs stopped");
                    }
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private static bool IsWanderingJob(Job job)
                {
                    return job?.def?.defName == "Wander";
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private static void StopMovement(Pawn pawn)
                {
                    pawn.pather?.StopDead();

                    var currentStance = pawn.stances?.curStance;
                    if (currentStance is Stance_Busy || currentStance is Stance_Mobile)
                    {
                        pawn.stances.CancelBusyStanceHard();
                    }
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private static void ForceRebuildThinkTree(Pawn pawn)
                {
                    if (pawn?.thinker == null || _thinkRootField == null) return;

                    try
                    {
                        _thinkRootField.SetValue(pawn.thinker, null);
                    }
                    catch (Exception ex)
                    {
                        Utility_DebugManager.LogError($"Error rebuilding think tree: {ex}");
                    }
                }
            }

            #endregion

            #region Job Management - 최적화된 작업 처리

            [HarmonyPatch(typeof(Pawn_JobTracker), "TryFindAndStartJob")]
            public static class Patch_Pawn_JobTracker_TryFindAndStartJob_DraftedCheck
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static bool Prefix(Pawn_JobTracker __instance)
                {
                    // 캐시된 필드 사용
                    if (!(_pawnFieldInJobTracker.GetValue(__instance) is Pawn pawn))
                        return true;

                    // 바닐라 휴먼 스킵
                    if (pawn.RaceProps.Humanlike && Utility_CacheManager.GetModExtension(pawn.def) == null)
                        return true;

                    // 드래프트된 폰은 일반 작업 금지
                    if (pawn.drafter?.Drafted == true)
                    {
                        if (Utility_DebugManager.ShouldLogDetailed())
                            Utility_DebugManager.LogNormal($"Blocked job start for drafted pawn {pawn.LabelShort}");

                        return false;
                    }

                    return true;
                }
            }

            [HarmonyPatch(typeof(Pawn_JobTracker), "EndCurrentJob")]
            public static class Patch_Pawn_JobTracker_EndCurrentJob_ForceImmediate
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static void Prefix(Pawn_JobTracker __instance, ref JobCondition condition, ref bool startNewJob)
                {
                    if (!(_pawnFieldInJobTracker.GetValue(__instance) is Pawn pawn))
                        return;

                    // 바닐라 휴먼 스킵
                    if (pawn.RaceProps.Humanlike && Utility_CacheManager.GetModExtension(pawn.def) == null)
                        return;

                    // 드래프트된 폰의 작업 즉시 종료
                    if (pawn.drafter?.Drafted == true && __instance.curJob != null && condition == JobCondition.InterruptForced)
                    {
                        condition = JobCondition.InterruptForced;
                        startNewJob = false;

                        if (Utility_DebugManager.ShouldLogDetailed())
                        {
                            Utility_DebugManager.LogNormal($"Force-ended job {__instance.curJob.def.defName} for drafted pawn {pawn.LabelShort}");
                        }
                    }
                }
            }

            #endregion

            #region Movement Control - 최적화된 이동 제어

            [HarmonyPatch(typeof(Pawn_PathFollower), "PatherTick")]
            public static class Patch_Pawn_PathFollower_PatherTick_StopIfDrafted
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static bool Prefix(Pawn_PathFollower __instance)
                {
                    // 캐시된 필드 사용
                    if (!(_pawnFieldInPathFollower.GetValue(__instance) is Pawn pawn))
                        return true;

                    var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                    if (modExtension == null)
                        return true;

                    // 드래프트된 폰의 이동 중단
                    if (pawn.drafter?.Drafted == true)
                    {
                        __instance.StopDead();
                        return false;
                    }

                    return true;
                }
            }

            #endregion

            #region Think Tree Job Control - 최적화된 AI 제어

            [HarmonyPatch(typeof(ThinkNode_JobGiver), nameof(ThinkNode_JobGiver.TryIssueJobPackage))]
            public static class Patch_ThinkNode_JobGiver_TryIssueJobPackage_StopOnDrafted
            {
                // 캐시된 결과 저장
                private static readonly ConcurrentDictionary<(Pawn, ThinkNode_JobGiver), bool> _constantTreeCache =
                    new ConcurrentDictionary<(Pawn, ThinkNode_JobGiver), bool>();

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public static bool Prefix(ThinkNode_JobGiver __instance, Pawn pawn, JobIssueParams jobParams, ref ThinkResult __result)
                {
                    // 조기 반환 최적화
                    if (pawn?.def == null || pawn.Dead || pawn.Destroyed)
                        return true;

                    var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                    if (modExtension == null || !Utility_TagManager.ForceDraftable(pawn))
                        return true;

                    var drafter = pawn.drafter;
                    if (drafter?.Drafted != true)
                        return true;

                    // 캐시된 상수 트리 체크
                    var cacheKey = (pawn, __instance);
                    if (_constantTreeCache.TryGetValue(cacheKey, out bool isInConstantTree))
                    {
                        if (isInConstantTree)
                            return true;
                    }
                    else
                    {
                        // 상수 트리 체크 및 캐시 저장
                        var thinker = pawn.thinker;
                        isInConstantTree = thinker?.ConstantThinkNodeRoot?.ThisAndChildrenRecursive?.Contains(__instance) == true;
                        _constantTreeCache.TryAdd(cacheKey, isInConstantTree);

                        if (isInConstantTree)
                            return true;
                    }

                    // 드래프트된 폰은 일반 작업 금지
                    __result = ThinkResult.NoJob;
                    return false;
                }

                // 메모리 정리를 위한 캐시 클리어 메서드
                public static void ClearCache()
                {
                    _constantTreeCache.Clear();
                }
            }

            #endregion

            #region Float Menu Integration - 최적화된 메뉴 처리

            [HarmonyPatch(typeof(FloatMenuMakerMap), "ShouldGenerateFloatMenuForPawn")]
            public static class Test_ShouldGenerate
            {
                [HarmonyPrefix]
                public static void Prefix(Pawn pawn)
                {
                    Log.Message($"[TEST] ShouldGenerateFloatMenuForPawn called for: {pawn?.LabelShort}");
                }
            }

            [HarmonyPatch(typeof(FloatMenuMakerMap))]
            [HarmonyPatch("GetProviderOptions")]
            public static class Test_GetProviderOptions_Alt
            {
                [HarmonyPrefix]
                public static void Prefix()
                {
                    Log.Message("[TEST] GetProviderOptions (alternative signature) called");
                }
            }

            #endregion

            #region Cache Management - 메모리 관리

            /// <summary>
            /// 캐시 정리 메서드 - GameComponent에서 주기적으로 호출
            /// </summary>
            public static void PerformCacheMaintenance()
            {
                const int MAX_CACHE_SIZE = 1000;

                try
                {
                    if (_jobDefCache.Count > MAX_CACHE_SIZE)
                    {
                        var keysToRemove = _jobDefCache.Keys.Take(MAX_CACHE_SIZE / 4).ToList();
                        foreach (var key in keysToRemove)
                        {
                            _jobDefCache.TryRemove(key, out _);
                        }
                    }

                    // ThinkNode 캐시 정리 - 죽은 폰 참조 제거
                    Patch_ThinkNode_JobGiver_TryIssueJobPackage_StopOnDrafted.ClearCache();

                    if (Utility_DebugManager.ShouldLogDetailed())
                    {
                        Utility_DebugManager.LogNormal($"[Iteration3] Cache maintenance completed");
                    }
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"Error during Iteration3 cache maintenance: {ex}");
                }
            }

            #endregion

        }

        public static class Patch_Iteration4_DoBills
        {
            [HarmonyPatch(typeof(Bill), nameof(Bill.PawnAllowedToStartAnew))]
            public static class Patch_Bill_PawnAllowedToStartAnew
            {
                [HarmonyPrefix]
                public static bool Prefix(Pawn p, ref bool __result)
                {
                    if (p == null) return true; // Let vanilla handle null pawn

                    // For non-humanlike modded pawns, allow them to start bills
                    if (p.RaceProps.Humanlike) return true; // Let vanilla handle humanlike pawns

                    var modExtension = Utility_CacheManager.GetModExtension(p.def);
                    if (modExtension == null) return true; // No mod extension, skip

                    if (!Utility_TagManager.HasTagSet(p.def)) return true; // No tag set, skip

                    if (Utility_TagManager.IsWorkEnabled(p, PawnEnumTags.AllowAllWork.ToString()))
                        __result = true;
                    if (Utility_TagManager.IsWorkEnabled(p, PawnEnumTags.AllowWork_Hauling.ToString()))
                        __result = true;
                    if (Utility_TagManager.IsWorkEnabled(p, PawnEnumTags.AllowWork_Crafting.ToString()))
                        __result = true;
                    if (Utility_TagManager.IsWorkEnabled(p, PawnEnumTags.AllowWork_Smithing.ToString()))
                        __result = true;
                    if (Utility_TagManager.IsWorkEnabled(p, PawnEnumTags.AllowWork_Tailoring.ToString()))
                        __result = true;
                    if (Utility_TagManager.IsWorkEnabled(p, PawnEnumTags.AllowWork_Art.ToString()))
                        __result = true;
                    if (Utility_TagManager.IsWorkEnabled(p, PawnEnumTags.AllowWork_Cooking.ToString()))
                        __result = true;
                    if (Utility_TagManager.IsWorkEnabled(p, PawnEnumTags.AllowWork_Doctor.ToString()))
                        __result = true;

                    return false; // Skip original method
                }
            }
        }

        public static class Patch_Iteration5_FoodSelection
        {
            ///// <summary>
            ///// Enhances food selection for modded non-humanlike pawns to use appropriate food sources
            ///// </summary>
            //[HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.TryFindBestFoodSourceFor))]
            //public static class Patch_FoodUtility_TryFindBestFoodSourceFor
            //{
            //    [HarmonyPrefix]
            //    public static bool Prefix(
            //        Pawn getter,
            //        Pawn eater,
            //        bool desperate,
            //        ref bool __result,
            //        out Thing foodSource,
            //        out ThingDef foodDef,
            //        bool canRefillDispenser = true,
            //        bool canUseInventory = true,
            //        bool allowForbidden = false,
            //        bool allowCorpse = true,
            //        bool allowSociallyImproper = false,
            //        bool allowHarvest = false)
            //    {
            //        foodSource = null;
            //        foodDef = null;

            //        // Skip for humanlike pawns or null getter/eater
            //        if (getter == null || eater == null || getter.RaceProps.Humanlike)
            //            return true;  // Use vanilla logic

            //        // Only handle our modded non-humanlike pawns
            //        var modExtension = Utility_CacheManager.GetModExtension(getter.def);
            //        if (modExtension == null)
            //            return true;  // Use vanilla logic

            //        try
            //        {
            //            // Check inventory first if allowed
            //            Thing inventoryFood = null;
            //            if (canUseInventory && getter.inventory?.innerContainer != null)
            //            {
            //                // Search for edible food in inventory
            //                foreach (Thing item in getter.inventory.innerContainer)
            //                {
            //                    if (item?.def?.IsIngestible == true &&
            //                        FoodUtility.WillEat(eater, item))
            //                    {
            //                        inventoryFood = item;
            //                        break;
            //                    }
            //                }
            //            }

            //            // Try to find food on map if needed
            //            ThingDef mapFoodDef = null;
            //            Thing mapFood = FoodUtility.BestFoodSourceOnMap(
            //                getter, eater, desperate, out mapFoodDef, // Changed 'ref' to 'out'
            //                FoodPreferability.MealLavish, // Allow up to lavish meals
            //                getter == eater,  // Whether getter is the eater
            //                !eater.IsTeetotaler(),  // Allow drugs if not teetotaler
            //                allowCorpse,
            //                true,  // Allow dispenser
            //                canRefillDispenser,
            //                allowForbidden,
            //                allowSociallyImproper,
            //                allowHarvest,
            //                false,  // No camp food
            //                false,  // No inventory food (handled separately)
            //                false); // No moved campfire food

            //            // Compare inventory and map food if both exist
            //            if (inventoryFood != null && mapFood != null)
            //            {
            //                float inventoryScore = FoodUtility.FoodOptimality(eater, inventoryFood, inventoryFood.def, 0f);
            //                float mapScore = FoodUtility.FoodOptimality(eater, mapFood, mapFoodDef,
            //                    (getter.Position - mapFood.Position).LengthHorizontalSquared);

            //                // Prefer inventory food unless map food is significantly better
            //                if (mapScore > inventoryScore + 30f)
            //                {
            //                    foodSource = mapFood;
            //                    foodDef = mapFoodDef;
            //                    __result = true;
            //                    return false;
            //                }
            //                else
            //                {
            //                    foodSource = inventoryFood;
            //                    foodDef = inventoryFood.def;
            //                    __result = true;
            //                    return false;
            //                }
            //            }
            //            else if (inventoryFood != null)
            //            {
            //                // Only inventory food
            //                foodSource = inventoryFood;
            //                foodDef = inventoryFood.def;
            //                __result = true;
            //                return false;
            //            }
            //            else if (mapFood != null)
            //            {
            //                // Only map food
            //                foodSource = mapFood;
            //                foodDef = mapFoodDef;
            //                __result = true;
            //                return false;
            //            }

            //            // No food found
            //            foodSource = null;
            //            foodDef = null;
            //            __result = false;
            //            return false;
            //        }
            //        catch (Exception ex)
            //        {
            //            Utility_DebugManager.LogError($"Error in food selection patch: {ex.Message}");
            //            return true; // Fall back to vanilla on error
            //        }
            //    }
            //}

            ///// <summary>
            ///// Adds custom food preference for modded pawns to allow proper food selection
            ///// </summary>
            //[HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.WillEat))]
            //public static class Patch_FoodUtility_WillEat
            //{
            //    [HarmonyPrefix]
            //    public static bool Prefix(Pawn p, Thing food, ref bool __result)
            //    {
            //        // Skip for null or humanlike pawns
            //        if (p == null || food == null || p.RaceProps.Humanlike)
            //            return true;

            //        // Only handle our modded non-humanlike pawns
            //        var modExtension = Utility_CacheManager.GetModExtension(p.def);
            //        if (modExtension == null)
            //            return true;

            //        try
            //        {
            //            // Basic ingestibility check
            //            if (food.def?.ingestible == null)
            //            {
            //                __result = false;
            //                return false;
            //            }

            //            // Allow raw food with custom preferences based on tags
            //            if (food.def.ingestible != null &&
            //                (food.def.ingestible.foodType & (FoodTypeFlags.Meat | FoodTypeFlags.VegetableOrFruit | FoodTypeFlags.Plant)) != 0)
            //            {
            //                // Raw food preference based on tags
            //                if (Utility_TagManager.HasTag(p.def, "AllowFood_Raw") ||
            //                    Utility_TagManager.HasTag(p.def, "AllowAllFood"))
            //                {
            //                    // Check food category preference based on tags
            //                    if (food.def.ingestible.foodType == FoodTypeFlags.Meat)
            //                    {
            //                        if (Utility_TagManager.HasTag(p.def, "Diet_Carnivore") ||
            //                            Utility_TagManager.HasTag(p.def, "Diet_Omnivore"))
            //                        {
            //                            __result = true;
            //                            return false;
            //                        }
            //                    }
            //                    else if (food.def.ingestible.foodType == FoodTypeFlags.VegetableOrFruit)
            //                    {
            //                        if (Utility_TagManager.HasTag(p.def, "Diet_Herbivore") ||
            //                            Utility_TagManager.HasTag(p.def, "Diet_Omnivore"))
            //                        {
            //                            __result = true;
            //                            return false;
            //                        }
            //                    }
            //                    else
            //                    {
            //                        // For other food types, allow if omnivore
            //                        if (Utility_TagManager.HasTag(p.def, "Diet_Omnivore"))
            //                        {
            //                            __result = true;
            //                            return false;
            //                        }
            //                    }
            //                }
            //            }

            //            // Allow meals based on tags
            //            if (food.def.IsIngestible && food.def.ingestible.foodType.HasFlag(FoodTypeFlags.Meal))
            //            {
            //                if (Utility_TagManager.HasTag(p.def, "AllowFood_Meals") ||
            //                    Utility_TagManager.HasTag(p.def, "AllowAllFood"))
            //                {
            //                    __result = true;
            //                    return false;
            //                }
            //            }

            //            // Block toxic or dangerously drugged food 
            //            if (food.def.IsIngestible && food.def.ingestible.drugCategory != DrugCategory.None)
            //            {
            //                // Only allow drugs with specific tag
            //                if (!Utility_TagManager.HasTag(p.def, "AllowFood_Drugs"))
            //                {
            //                    __result = false;
            //                    return false;
            //                }
            //            }

            //            // Default to vanilla for any other cases
            //            return true;
            //        }
            //        catch (Exception ex)
            //        {
            //            Utility_DebugManager.LogError($"Error in WillEat patch: {ex.Message}");
            //            return true;
            //        }
            //    }
            //}

            ///// <summary>
            ///// Enhances food optimization for modded pawns
            ///// </summary>
            //[HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.FoodOptimality))]
            //public static class Patch_FoodUtility_FoodOptimality
            //{
            //    [HarmonyPostfix]
            //    public static void Postfix(Pawn eater, Thing foodSource, ThingDef foodDef, float dist, ref float __result)
            //    {
            //        if (eater == null || foodSource == null || foodDef == null)
            //            return;

            //        // Only handle our modded non-humanlike pawns
            //        if (eater.RaceProps.Humanlike)
            //            return;

            //        var modExtension = Utility_CacheManager.GetModExtension(eater.def);
            //        if (modExtension == null)
            //            return;

            //        try
            //        {
            //            // Apply custom food preference adjustments based on tags

            //            // Prefer raw meat for carnivores
            //            if (Utility_TagManager.HasTag(eater.def, "Diet_Carnivore") &&
            //                foodDef.ingestible != null && foodDef.ingestible.foodType.HasFlag(FoodTypeFlags.Meat))
            //            {
            //                __result += 20f;
            //            }

            //            // Prefer raw plants for herbivores
            //            if (Utility_TagManager.HasTag(eater.def, "Diet_Herbivore") &&
            //                foodDef.ingestible != null && foodDef.ingestible.foodType == FoodTypeFlags.VegetableOrFruit)
            //            {
            //                __result += 20f;
            //            }

            //            // Prefer meals for omnivores
            //            if (Utility_TagManager.HasTag(eater.def, "Diet_Omnivore") &&
            //                foodDef.IsIngestible && foodDef.ingestible.foodType.HasFlag(FoodTypeFlags.Meal))
            //            {
            //                __result += 10f;
            //            }

            //            // Penalize non-preferred food types
            //            if (Utility_TagManager.HasTag(eater.def, "Diet_Carnivore") &&
            //                foodDef.ingestible.foodType == FoodTypeFlags.VegetableOrFruit)
            //            {
            //                __result -= 40f;
            //            }

            //            if (Utility_TagManager.HasTag(eater.def, "Diet_Herbivore") &&
            //                foodDef.ingestible.foodType == FoodTypeFlags.Meat)
            //            {
            //                __result -= 40f;
            //            }

            //            // Log if in debug mode
            //            if (modExtension.debugMode && Prefs.DevMode)
            //            {
            //                Utility_DebugManager.LogNormal($"Food optimality for {eater.LabelShort}: {foodSource.Label} = {__result}");
            //            }
            //        }
            //        catch (Exception ex)
            //        {
            //            Utility_DebugManager.LogError($"Error in FoodOptimality patch: {ex.Message}");
            //        }
            //    }
            //}

            ///// <summary>
            ///// Allows modded pawns to eat without traits affecting their food preferences
            ///// </summary>
            //[HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.ThoughtsFromIngesting))]
            //public static class Patch_FoodUtility_ThoughtsFromIngesting
            //{
            //    [HarmonyPrefix]
            //    public static bool Prefix(Pawn ingester, Thing foodSource, ThingDef foodDef, ref List<FoodUtility.ThoughtFromIngesting> __result)
            //    {
            //        if (ingester == null || foodSource == null || foodDef == null)
            //            return true;

            //        // Only handle our modded non-humanlike pawns
            //        if (ingester.RaceProps.Humanlike)
            //            return true;

            //        var modExtension = Utility_CacheManager.GetModExtension(ingester.def);
            //        if (modExtension == null)
            //            return true;

            //        // Animals don't get thoughts from food
            //        __result = new List<FoodUtility.ThoughtFromIngesting>();
            //        return false;
            //    }
            //}
        }

        public static class Patch_SafePurge
        {
            [HarmonyPatch(typeof(ITab_Pawn_Gear), "ShouldShowInventory")]
            public static class Patch_ITab_Pawn_Gear_ShouldShowInventory
            {
                [HarmonyPrefix]
                public static bool Prefix(Pawn p, ref bool __result)
                {
                    // If pawn is null, let original method handle it
                    if (p == null)
                        return true;

                    // Check if this is a pawn that previously had trackers injected
                    // This assumes you have some way to check if the pawn had trackers injected
                    // You can use a static dictionary to track this if needed
                    if (Utility_DrafterManager.WasTrackerInjected(p))
                    {
                        // Simply return false to hide inventory without accessing trackers
                        __result = false;
                        return false; // Skip original method
                    }

                    return true; // Run original method for other pawns
                }
            }
        }

        public static class Patch_Debuggers
        {
            #region Cached Components - 리플렉션 및 반복 연산 캐시

            // HediffDef 캐시
            private static readonly Lazy<HediffDef> _statStubDef =
                new Lazy<HediffDef>(() => HediffDef.Named("PawnControl_StatStub"));

            // StringBuilder 풀 - 메모리 할당 최적화
            private static readonly ObjectPool<StringBuilder> _stringBuilderPool =
                new ObjectPool<StringBuilder>(() => new StringBuilder(1024), sb => sb.Clear());

            // SkillDef 캐시 - 반복 DefDatabase 호출 방지
            private static readonly Lazy<SkillDef[]> _allSkillDefs =
                new Lazy<SkillDef[]>(() => DefDatabase<SkillDef>.AllDefsListForReading.ToArray());

            #endregion

            #region Stat Hediff Debug - 최적화된 상태 추적

            /// <summary>
            /// 최적화된 Stat Hediff 디버그 패치 - 캐시된 HediffDef 사용
            /// </summary>
            [HarmonyPatch(typeof(Utility_StatManager), nameof(Utility_StatManager.InjectConsolidatedStatHediff))]
            public static class Patch_Utility_StatManager_InjectConsolidatedStatHediff
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                [HarmonyPrefix]
                public static bool Prefix(Pawn pawn)
                {
                    // 조기 반환 최적화
                    if (pawn?.def == null) return true;

                    var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                    if (modExtension == null || !Utility_DebugManager.ShouldLog() || !modExtension.debugMode)
                        return true;

                    if (Utility_DebugManager.ShouldLogDetailed())
                    {
                        Utility_DebugManager.LogNormal($"DEBUG: InjectConsolidatedStatHediff CALLED for {pawn.LabelShort} (Humanlike: {pawn.RaceProps.Humanlike})");
                    }

                    return true;
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                [HarmonyPostfix]
                public static void Postfix(Pawn pawn)
                {
                    if (pawn?.def == null) return;

                    var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                    if (modExtension == null || !Utility_DebugManager.ShouldLog() || !modExtension.debugMode)
                        return;

                    if (Utility_DebugManager.ShouldLogDetailed())
                    {
                        Utility_DebugManager.LogNormal($"DEBUG: InjectConsolidatedStatHediff COMPLETED for {pawn.LabelShort}");

                        // 캐시된 HediffDef 사용으로 최적화
                        ValidateStatHediffPresence(pawn);
                    }
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private static void ValidateStatHediffPresence(Pawn pawn)
                {
                    var hediffs = pawn.health?.hediffSet?.hediffs;
                    if (hediffs == null) return;

                    var stubDef = _statStubDef.Value;
                    bool hasStatHediff = false;

                    // 최적화된 hediff 검색
                    for (int i = 0; i < hediffs.Count; i++)
                    {
                        if (hediffs[i]?.def == stubDef)
                        {
                            hasStatHediff = true;
                            break;
                        }
                    }

                    if (hasStatHediff)
                    {
                        Utility_DebugManager.LogNormal("DEBUG: Found PawnControl_StatStub hediff on pawn");
                    }
                    else
                    {
                        Utility_DebugManager.LogWarning("DEBUG: After injection, NO PawnControl_StatStub hediff found on pawn!");
                    }
                }
            }

            #endregion

            #region ThinkNode Debug - 최적화된 AI 디버깅

            /// <summary>
            /// 최적화된 ThinkNode_PrioritySorter 디버그 패치
            /// </summary>
            [HarmonyPatch(typeof(ThinkNode_PrioritySorter), nameof(ThinkNode_PrioritySorter.TryIssueJobPackage))]
            public static class Patch_ThinkNode_PrioritySorter_DebugJobs
            {
                // 스레드 안전 캐시
                private static readonly ConcurrentHashSet<int> _processedPawns = new ConcurrentHashSet<int>();

                public static void ResetCache() => _processedPawns.Clear();

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                [HarmonyPrefix]
                public static void Prefix(ThinkNode_PrioritySorter __instance, Pawn pawn)
                {
                    // 조기 반환 최적화
                    if (pawn?.def == null || __instance == null) return;

                    // 이미 처리된 pawn 스킵
                    if (!_processedPawns.Add(pawn.thingIDNumber)) return;

                    var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                    if (modExtension == null || !Utility_DebugManager.ShouldLog() || !modExtension.debugMode)
                        return;

                    if (!Utility_ThinkTreeManager.HasAllowWorkTag(pawn))
                        return;

                    if (Utility_DebugManager.ShouldLogDetailed())
                    {
                        Utility_DebugManager.LogNormal($"{pawn.LabelShort} entering PrioritySorter (one-time debug log)");

                        if (__instance.subNodes?.Count > 0)
                        {
                            Utility_DebugManager.SafeExpandAndDump(
                                __instance,
                                expandBeforeDump: true,
                                label: $"{pawn.LabelShort}_PrioritySorterDebug",
                                showFullDump: true
                            );
                        }
                    }
                }
            }

            #endregion

            #region Spawn Debug - 최적화된 스폰 디버깅

            /// <summary>
            /// 최적화된 Pawn 스폰 ThinkTree 덤프 패치
            /// </summary>
            [HarmonyPatch(typeof(Pawn), "SpawnSetup", new Type[] { typeof(Map), typeof(bool) })]
            public static class Patch_Pawn_SpawnSetup_DumpThinkTree
            {
                // 스레드 안전 캐시
                private static readonly ConcurrentHashSet<int> _processedPawns = new ConcurrentHashSet<int>();

                public static void ResetCache() => _processedPawns.Clear();

                [HarmonyPostfix]
                public static void Postfix(Pawn __instance, Map map, bool respawningAfterLoad)
                {
                    // 조기 반환 최적화
                    if (__instance?.def == null) return;

                    // 이미 처리된 pawn 스킵
                    if (!_processedPawns.Add(__instance.thingIDNumber)) return;

                    var modExtension = Utility_CacheManager.GetModExtension(__instance.def);
                    if (modExtension == null || !Utility_DebugManager.ShouldLog() || !modExtension.debugMode)
                        return;

                    if (!Utility_ThinkTreeManager.HasAllowOrBlockWorkTag(__instance))
                        return;

                    // 비동기 처리로 메인 스레드 블로킹 방지
                    LongEventHandler.ExecuteWhenFinished(() =>
                    {
                        try
                        {
                            if (__instance?.thinker?.MainThinkNodeRoot != null && Utility_DebugManager.ShouldLogDetailed())
                            {
                                Utility_DebugManager.LogNormal($"Dumping ThinkTree for newly spawned pawn {__instance.LabelShort} (one-time debug dump)");
                                Utility_DebugManager.DumpPawnThinkTreeDetailed(__instance);
                            }
                        }
                        catch (Exception ex)
                        {
                            Utility_DebugManager.LogError($"Error during ThinkTree dump for {__instance?.LabelShort}: {ex}");
                        }
                    });
                }
            }

            #endregion

            #region Debug Gizmos - 최적화된 디버그 UI

            /// <summary>
            /// 최적화된 Stat 디버그 기즈모 패치
            /// </summary>
            [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
            public static class Patch_Pawn_GetGizmos_StatDebug
            {
                [HarmonyPostfix]
                public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
                {
                    // 바닐라 기즈모 먼저 반환
                    foreach (var gizmo in __result)
                        yield return gizmo;

                    // 조기 반환 최적화
                    if (__instance?.RaceProps == null || !Utility_DebugManager.ShouldLog())
                        yield break;

                    var modExtension = Utility_CacheManager.GetModExtension(__instance.def);
                    if (modExtension?.debugMode != true)
                        yield break;

                    yield return CreateStatLogGizmo(__instance);
                    yield return CreateStatValidationGizmo(__instance);
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private static Command_Action CreateStatLogGizmo(Pawn pawn)
                {
                    return new Command_Action
                    {
                        defaultLabel = "PawnControl_LogStat",
                        defaultDesc = "Log race + stat mutation info.",
                        action = () => Utility_DebugManager.LogRaceAndPawnStats(pawn)
                    };
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private static Command_Action CreateStatValidationGizmo(Pawn pawn)
                {
                    return new Command_Action
                    {
                        defaultLabel = "PawnControl_ValidateStat",
                        defaultDesc = "Validate current stat values against race base.",
                        action = () => Utility_DebugManager.StatMutationValidator.Validate(pawn)
                    };
                }
            }

            /// <summary>
            /// 최적화된 ThinkTree 디버그 기즈모 패치
            /// </summary>
            [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
            public static class Patch_Pawn_GetGizmos_DebugThinkTree
            {
                [HarmonyPostfix]
                public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
                {
                    foreach (var gizmo in __result)
                        yield return gizmo;

                    // 조기 반환 최적화
                    if (__instance?.def == null || !Utility_DebugManager.ShouldLog())
                        yield break;

                    var modExtension = Utility_CacheManager.GetModExtension(__instance.def);
                    if (modExtension == null || !Utility_ThinkTreeManager.HasAllowOrBlockWorkTag(__instance))
                        yield break;

                    yield return new Command_Action
                    {
                        defaultLabel = "Debug: Validate ThinkTree",
                        defaultDesc = "Validate this pawn's ThinkTree configuration",
                        action = () => Utility_ThinkTreeManager.ValidateThinkTree(__instance)
                    };
                }
            }

            /// <summary>
            /// 최적화된 스킬 검증 기즈모 패치 - 메모리 할당 최적화
            /// </summary>
            [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
            public static class Patch_Pawn_GetGizmos_VerifySkills
            {
                [HarmonyPostfix]
                public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
                {
                    foreach (var g in __result)
                        yield return g;

                    // 조기 반환 최적화
                    if (!Prefs.DevMode || !Utility_DebugManager.ShouldLog())
                        yield break;

                    var modExtension = Utility_CacheManager.GetModExtension(__instance.def);
                    if (modExtension == null)
                        yield break;

                    yield return CreateSkillVerificationGizmo(__instance, modExtension);
                }

                private static Command_Action CreateSkillVerificationGizmo(Pawn pawn, NonHumanlikePawnControlExtension modExtension)
                {
                    return new Command_Action
                    {
                        defaultLabel = "Debug: Verify Skills",
                        defaultDesc = "Shows detailed information about injected skills",
                        action = () => ShowSkillVerificationDialog(pawn, modExtension)
                    };
                }

                private static void ShowSkillVerificationDialog(Pawn pawn, NonHumanlikePawnControlExtension modExtension)
                {
                    // StringBuilder 풀 사용으로 메모리 할당 최적화
                    var sb = _stringBuilderPool.Get();

                    try
                    {
                        BuildSkillVerificationText(sb, pawn, modExtension);
                        Find.WindowStack.Add(new Dialog_MessageBox(sb.ToString()));
                    }
                    finally
                    {
                        _stringBuilderPool.Return(sb);
                    }
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private static void BuildSkillVerificationText(StringBuilder sb, Pawn pawn, NonHumanlikePawnControlExtension modExtension)
                {
                    sb.AppendLine($"Skill verification for {pawn.LabelCap}:");
                    sb.AppendLine($"Race: {pawn.def.defName}");
                    sb.AppendLine($"Has mod extension: {modExtension != null}");

                    if (modExtension == null) return;

                    sb.AppendLine($"Extension from XML: {modExtension.fromXML}");

                    // 주입된 스킬 정보
                    AppendInjectedSkillsInfo(sb, modExtension);

                    // 캐시된 딕셔너리 정보
                    AppendCachedDictionaryInfo(sb, modExtension);

                    // 실제 스킬 값들
                    AppendEffectiveSkillValues(sb, pawn);
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private static void AppendInjectedSkillsInfo(StringBuilder sb, NonHumanlikePawnControlExtension modExtension)
                {
                    sb.AppendLine($"\nInjected Skills (count: {modExtension.injectedSkills?.Count ?? 0}):");
                    if (modExtension.injectedSkills?.Count > 0)
                    {
                        foreach (var skill in modExtension.injectedSkills)
                        {
                            sb.AppendLine($" - {skill.skill}: {skill.level}");
                        }
                    }
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private static void AppendCachedDictionaryInfo(StringBuilder sb, NonHumanlikePawnControlExtension modExtension)
                {
                    sb.AppendLine($"\nSimulatedSkillDict populated: {modExtension._simulatedSkillDict != null}");
                    sb.AppendLine($"SimulatedSkillDict count: {modExtension._simulatedSkillDict?.Count ?? 0}");

                    if (modExtension._simulatedSkillDict?.Count > 0)
                    {
                        foreach (var pair in modExtension._simulatedSkillDict)
                        {
                            sb.AppendLine($" - {pair.Key.defName}: {pair.Value}");
                        }
                    }
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private static void AppendEffectiveSkillValues(StringBuilder sb, Pawn pawn)
                {
                    sb.AppendLine("\nEffective skill values:");
                    var skillDefs = _allSkillDefs.Value;

                    for (int i = 0; i < skillDefs.Length; i++)
                    {
                        var skill = skillDefs[i];
                        int level = Utility_SkillManager.SetInjectedSkillLevel(pawn, skill);
                        sb.AppendLine($" - {skill.defName}: {level}");
                    }
                }
            }

            #endregion

            #region Utility Classes - 성능 최적화 도구

            /// <summary>
            /// 스레드 안전 HashSet 구현
            /// </summary>
            private class ConcurrentHashSet<T>
            {
                private readonly HashSet<T> _set = new HashSet<T>();
                private readonly object _lock = new object();

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public bool Add(T item)
                {
                    lock (_lock)
                    {
                        return _set.Add(item);
                    }
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public void Clear()
                {
                    lock (_lock)
                    {
                        _set.Clear();
                    }
                }

                public int Count
                {
                    get
                    {
                        lock (_lock)
                        {
                            return _set.Count;
                        }
                    }
                }
            }

            /// <summary>
            /// 간단한 오브젝트 풀 구현 - 메모리 할당 최적화
            /// </summary>
            private class ObjectPool<T> where T : class
            {
                private readonly ConcurrentQueue<T> _objects = new ConcurrentQueue<T>();
                private readonly Func<T> _objectGenerator;
                private readonly Action<T> _resetAction;

                public ObjectPool(Func<T> objectGenerator, Action<T> resetAction = null)
                {
                    _objectGenerator = objectGenerator ?? throw new ArgumentNullException(nameof(objectGenerator));
                    _resetAction = resetAction;
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public T Get()
                {
                    return _objects.TryDequeue(out T item) ? item : _objectGenerator();
                }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public void Return(T item)
                {
                    _resetAction?.Invoke(item);
                    _objects.Enqueue(item);
                }
            }

            #endregion
        }

    }
}