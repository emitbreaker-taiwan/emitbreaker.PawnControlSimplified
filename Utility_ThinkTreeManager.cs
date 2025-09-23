using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// 최적화된 ThinkTree 매니저 - 리플렉션 캐싱과 스레드 안전성 개선
    /// </summary>
    public static class Utility_ThinkTreeManager
    {
        #region Reflection Caching - 🟢 모든 리플렉션 호출을 캐시

        /// <summary>Cached reflection access to avoid repeated AccessTools calls</summary>
        private static readonly MethodInfo _resolveMethod = AccessTools.Method(typeof(ThinkNode), "ResolveSubnodes");
        private static readonly FieldInfo _thinkTreeField = AccessTools.Field(typeof(Pawn_MindState), "thinkTree");
        private static readonly FieldInfo _thinkTreeConstantField = AccessTools.Field(typeof(Pawn_MindState), "thinkTreeConstant");
        private static readonly FieldInfo _thinkerField = AccessTools.Field(typeof(Pawn_MindState), "thinker");
        private static readonly FieldInfo _thinkRootField = AccessTools.Field(typeof(Pawn_Thinker), "thinkRoot");
        private static readonly FieldInfo _nextThinkField = AccessTools.Field(typeof(Pawn_Thinker), "nextThink");

        // WorkSettings reflection cache
        private static readonly MethodInfo _cacheWorkGiversMethod = AccessTools.Method(typeof(Pawn_WorkSettings), "CacheWorkGiversInOrder");
        private static readonly FieldInfo _workGiversNormalField = AccessTools.Field(typeof(Pawn_WorkSettings), "workGiversInOrderNormal");
        private static readonly FieldInfo _workGiversEmergencyField = AccessTools.Field(typeof(Pawn_WorkSettings), "workGiversInOrderEmergency");

        #endregion

        #region Thread-Safe Caches - 🟢 ConcurrentDictionary로 변경

        /// <summary>Thread-safe work tag caches</summary>
        private static readonly ConcurrentDictionary<Pawn, bool> _allowWorkTagCache = new ConcurrentDictionary<Pawn, bool>();
        private static readonly ConcurrentDictionary<Pawn, bool> _blockWorkTagCache = new ConcurrentDictionary<Pawn, bool>();
        private static readonly ConcurrentDictionary<Pawn, bool> _combinedWorkTagCache = new ConcurrentDictionary<Pawn, bool>();

        /// <summary>ThinkTree definition cache</summary>
        private static readonly ConcurrentDictionary<string, ThinkTreeDef> _thinkTreeDefCache = new ConcurrentDictionary<string, ThinkTreeDef>();

        #endregion

        #region Public API Methods

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ThinkTreeDef ThinkTreeDefNamed(string defName)
        {
            if (string.IsNullOrEmpty(defName))
                return null;

            // 🟢 Use cached lookup instead of repeated DefDatabase calls
            return _thinkTreeDefCache.GetOrAdd(defName,
                name => DefDatabase<ThinkTreeDef>.GetNamedSilentFail(name));
        }

        /// <summary>
        /// 🟢 최적화된 work tag 검사 - 한번의 계산으로 모든 캐시 업데이트
        /// </summary>
        public static bool HasAllowOrBlockWorkTag(Pawn pawn)
        {
            if (!Utility_Common.RaceDefChecker(pawn.def))
                return false;

            // Fast path: check combined cache first
            if (_combinedWorkTagCache.TryGetValue(pawn, out bool combinedResult))
                return combinedResult;

            // Quick reject if no extension
            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null)
            {
                // Cache all three results as false
                _allowWorkTagCache.TryAdd(pawn, false);
                _blockWorkTagCache.TryAdd(pawn, false);
                _combinedWorkTagCache.TryAdd(pawn, false);
                return false;
            }

            // 🟢 Calculate once and cache all results atomically
            bool allowResult = Utility_TagManager.HasTag(pawn.def, ManagedTags.AllowAllWork) ||
                              Utility_TagManager.HasAnyTagWithPrefix(pawn.def, ManagedTags.AllowWorkPrefix);

            bool blockResult = Utility_TagManager.HasTag(pawn.def, ManagedTags.BlockAllWork) ||
                               Utility_TagManager.HasAnyTagWithPrefix(pawn.def, ManagedTags.BlockWorkPrefix);

            bool combined = allowResult || blockResult;

            // Store in all caches
            _allowWorkTagCache.TryAdd(pawn, allowResult);
            _blockWorkTagCache.TryAdd(pawn, blockResult);
            _combinedWorkTagCache.TryAdd(pawn, combined);

            return combined;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasAllowWorkTag(Pawn pawn)
        {
            if (!Utility_Common.RaceDefChecker(pawn.def))
                return false;

            // Try cache first
            if (_allowWorkTagCache.TryGetValue(pawn, out bool hasTag))
                return hasTag;

            // Trigger full calculation which will cache all results
            HasAllowOrBlockWorkTag(pawn);

            // Return cached result (guaranteed to exist now)
            return _allowWorkTagCache.TryGetValue(pawn, out hasTag) && hasTag;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasBlockWorkTag(Pawn pawn)
        {
            if (!Utility_Common.RaceDefChecker(pawn.def))
                return false;

            // Try cache first
            if (_blockWorkTagCache.TryGetValue(pawn, out bool hasTag))
                return hasTag;

            // Trigger full calculation which will cache all results
            HasAllowOrBlockWorkTag(pawn);

            // Return cached result (guaranteed to exist now)
            return _blockWorkTagCache.TryGetValue(pawn, out hasTag) && hasTag;
        }

        /// <summary>
        /// 최적화된 ApplyThinkTreeToRaceDefs - 종족별 ThinkTree 적용
        /// </summary>
        public static void ApplyThinkTreeToRaceDefs()
        {
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def?.race == null)
                    continue;

                var modExtension = def.GetModExtension<NonHumanlikePawnControlExtension>();
                if (modExtension == null)
                    continue;

                // 태그 체크 최적화
                bool hasTag = Utility_TagManager.HasTag(def, ManagedTags.AllowAllWork) ||
                             Utility_TagManager.HasAnyTagWithPrefix(def, ManagedTags.AllowWorkPrefix) ||
                             Utility_TagManager.HasTag(def, ManagedTags.BlockAllWork) ||
                             Utility_TagManager.HasAnyTagWithPrefix(def, ManagedTags.BlockWorkPrefix);

                if (!hasTag)
                    continue;

                // Main ThinkTree 교체
                if (!string.IsNullOrEmpty(modExtension.mainWorkThinkTreeDefName))
                {
                    var thinkTree = ThinkTreeDefNamed(modExtension.mainWorkThinkTreeDefName);
                    if (thinkTree != null)
                    {
                        def.race.thinkTreeMain = thinkTree;
                        if (Utility_DebugManager.ShouldLogDetailed())
                        {
                            Utility_DebugManager.LogNormal($"Replaced MainThinkTree '{modExtension.mainWorkThinkTreeDefName}' on race def {def.defName}");
                        }
                    }
                }

                // Constant ThinkTree 교체
                if (!string.IsNullOrEmpty(modExtension.constantThinkTreeDefName))
                {
                    var constantTree = ThinkTreeDefNamed(modExtension.constantThinkTreeDefName);
                    if (constantTree != null)
                    {
                        def.race.thinkTreeConstant = constantTree;
                        if (Utility_DebugManager.ShouldLogDetailed())
                        {
                            Utility_DebugManager.LogNormal($"Replaced ConstantThinkTree '{modExtension.constantThinkTreeDefName}' on race def {def.defName}");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 최적화된 ResetThinkTreeToVanilla - 바닐라 ThinkTree로 복원
        /// </summary>
        public static void ResetThinkTreeToVanilla(Pawn pawn, string originalMainTree = null, string originalConstTree = null)
        {
            if (pawn?.mindState == null) return;

            try
            {
                // 바닐라 기본값으로 복원
                ThinkTreeDef mainThinkTree = null;
                ThinkTreeDef constantThinkTree = null;

                if (!string.IsNullOrEmpty(originalMainTree))
                {
                    mainThinkTree = ThinkTreeDefNamed(originalMainTree);
                }
                else
                {
                    // 기본 바닐라 ThinkTree 선택
                    mainThinkTree = pawn.RaceProps.Humanlike ? ThinkTreeDefNamed("Humanlike")
                                  : pawn.RaceProps.Animal ? ThinkTreeDefNamed("Animal")
                                  : pawn.RaceProps.IsMechanoid ? ThinkTreeDefNamed("Mechanoid")
                                  : null;
                }

                if (!string.IsNullOrEmpty(originalConstTree))
                {
                    constantThinkTree = ThinkTreeDefNamed(originalConstTree);
                }
                else
                {
                    // 기본 바닐라 상수 ThinkTree 선택
                    constantThinkTree = pawn.RaceProps.Humanlike ? ThinkTreeDefNamed("HumanlikeConstant")
                                      : pawn.RaceProps.Animal ? ThinkTreeDefNamed("AnimalConstant")
                                      : null;
                }

                // ThinkTree 리셋 적용
                ResetPawnThinkTree(pawn, mainThinkTree, constantThinkTree);

                if (Utility_DebugManager.ShouldLogDetailed())
                {
                    Utility_DebugManager.LogNormal($"Reset think tree to vanilla for {pawn.LabelShort}");
                }
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error resetting think tree to vanilla for {pawn?.LabelShort}: {ex}");
            }
        }

        #endregion

        #region Optimized Reflection Operations - 🟢 캐시된 FieldInfo 사용

        /// <summary>
        /// 🟢 최적화된 ThinkTree 리셋 - 캐시된 리플렉션 사용
        /// </summary>
        public static void ResetPawnThinkTree(Pawn pawn, ThinkTreeDef mainThinkTree = null, ThinkTreeDef constantThinkTree = null)
        {
            if (pawn?.mindState == null) return;

            try
            {
                // 🟢 Reset main think tree using cached FieldInfo
                if (mainThinkTree != null && _thinkTreeField != null)
                {
                    _thinkTreeField.SetValue(pawn.mindState, mainThinkTree);
                }

                // 🟢 Reset constant think tree using cached FieldInfo
                if (constantThinkTree != null && _thinkTreeConstantField != null)
                {
                    _thinkTreeConstantField.SetValue(pawn.mindState, constantThinkTree);
                }

                // 🟢 Force think node rebuild using cached reflection
                ForceThinkNodeRebuild(pawn);
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error resetting think tree for {pawn?.LabelShort}: {ex.Message}");
            }
        }

        /// <summary>
        /// 🟢 최적화된 think node 강제 재구축
        /// </summary>
        private static void ForceThinkNodeRebuild(Pawn pawn)
        {
            if (pawn?.mindState == null || _thinkerField == null) return;

            try
            {
                // Get thinker using cached FieldInfo
                object thinkerObj = _thinkerField.GetValue(pawn.mindState);
                if (thinkerObj == null) return;

                // Reset thinkRoot using cached FieldInfo
                if (_thinkRootField != null)
                {
                    _thinkRootField.SetValue(thinkerObj, null);
                }

                // Reset nextThink if field exists
                if (_nextThinkField != null)
                {
                    _nextThinkField.SetValue(thinkerObj, 0);
                }
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error during think node rebuild for {pawn?.LabelShort}: {ex.Message}");
            }
        }

        /// <summary>
        /// 🟢 최적화된 WorkSettings 초기화
        /// </summary>
        public static void OptimizedEnsureWorkSettingsInitialized(Pawn pawn)
        {
            if (pawn?.workSettings == null) return;

            try
            {
                // Force cache rebuild using cached MethodInfo
                if (_cacheWorkGiversMethod != null)
                {
                    _cacheWorkGiversMethod.Invoke(pawn.workSettings, null);
                }

                // Ensure lists are populated using cached FieldInfo
                var normalList = _workGiversNormalField?.GetValue(pawn.workSettings);
                var emergencyList = _workGiversEmergencyField?.GetValue(pawn.workSettings);

                if (normalList == null && _workGiversNormalField != null)
                {
                    var newNormalList = new List<WorkGiver>();
                    var newEmergencyList = new List<WorkGiver>();

                    // Populate with available work givers
                    foreach (WorkGiverDef def in DefDatabase<WorkGiverDef>.AllDefsListForReading)
                    {
                        if (pawn.workSettings.GetPriority(def.workType) > 0)
                        {
                            newNormalList.Add(def.Worker);
                            if (def.emergency)
                                newEmergencyList.Add(def.Worker);
                        }
                    }

                    _workGiversNormalField.SetValue(pawn.workSettings, newNormalList);
                    if (_workGiversEmergencyField != null)
                        _workGiversEmergencyField.SetValue(pawn.workSettings, newEmergencyList);
                }
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error initializing work settings for {pawn?.LabelShort}: {ex.Message}");
            }
        }

        #endregion

        #region Utility Methods

        public static void ResolveSubnodesAndRecur(ThinkNode node)
        {
            if (node == null) return;

            // 🟢 Use cached MethodInfo instead of repeated reflection
            _resolveMethod?.Invoke(node, null);

            if (node.subNodes != null)
            {
                foreach (var sub in node.subNodes)
                {
                    ResolveSubnodesAndRecur(sub);
                }
            }
        }

        /// <summary>
        /// 🟢 Thread-safe cache cleanup
        /// </summary>
        public static void ResetCache()
        {
            _allowWorkTagCache.Clear();
            _blockWorkTagCache.Clear();
            _combinedWorkTagCache.Clear();
            _thinkTreeDefCache.Clear();
        }

        /// <summary>
        /// 🟢 Memory-conscious cache maintenance
        /// </summary>
        public static void PerformCacheMaintenance()
        {
            const int MAX_CACHE_SIZE = 1000;

            try
            {
                // Clean up work tag caches if they get too large
                if (_allowWorkTagCache.Count > MAX_CACHE_SIZE)
                {
                    CleanupWorkTagCache(_allowWorkTagCache, "AllowWorkTag");
                }

                if (_blockWorkTagCache.Count > MAX_CACHE_SIZE)
                {
                    CleanupWorkTagCache(_blockWorkTagCache, "BlockWorkTag");
                }

                if (_combinedWorkTagCache.Count > MAX_CACHE_SIZE)
                {
                    CleanupWorkTagCache(_combinedWorkTagCache, "CombinedWorkTag");
                }
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error during cache maintenance: {ex.Message}");
            }
        }

        private static void CleanupWorkTagCache(ConcurrentDictionary<Pawn, bool> cache, string cacheName)
        {
            var keysToRemove = new List<Pawn>();

            foreach (var kvp in cache)
            {
                // Remove entries for destroyed or invalid pawns
                if (kvp.Key?.Destroyed == true || kvp.Key?.def == null)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                cache.TryRemove(key, out _);
            }

            if (keysToRemove.Count > 0 && Utility_DebugManager.ShouldLogDetailed())
            {
                Utility_DebugManager.LogNormal($"[ThinkTreeManager] Cleaned up {keysToRemove.Count} stale entries from {cacheName} cache");
            }
        }

        private static ThinkNode_PrioritySorter FindFirstPrioritySorter(ThinkNode root)
        {
            if (root == null)
                return null;

            if (root is ThinkNode_Tagger tagger)
            {
                if (tagger.subNodes != null)
                {
                    foreach (var subNode in tagger.subNodes)
                    {
                        var found = FindFirstPrioritySorter(subNode);
                        if (found != null)
                            return found;
                    }
                }
            }
            else if (root is ThinkNode_PrioritySorter prioritySorter)
            {
                return prioritySorter;
            }

            // Recursively search all subnodes
            if (root.subNodes != null)
            {
                foreach (var subNode in root.subNodes)
                {
                    var found = FindFirstPrioritySorter(subNode);
                    if (found != null)
                        return found;
                }
            }

            return null;
        }


        /// <summary>
        /// 🟢 최적화된 ThinkTree 검증 메서드
        /// </summary>
        public static void ValidateThinkTree(Pawn pawn)
        {
            if (pawn?.thinker?.MainThinkNodeRoot == null)
                return;

            try
            {
                bool hasWorkGiver = false;
                bool hasConditionalColonist = false;

                // 스택 기반 탐색으로 재귀 오버헤드 제거
                var nodeStack = new Stack<ThinkNode>();
                nodeStack.Push(pawn.thinker.MainThinkNodeRoot);

                while (nodeStack.Count > 0)
                {
                    var node = nodeStack.Pop();

                    // 타입 체크 최적화
                    switch (node)
                    {
                        case JobGiver_Work _:
                            hasWorkGiver = true;
                            break;
                        case ThinkNode_ConditionalColonist _:
                            hasConditionalColonist = true;
                            break;
                    }

                    // 자식 노드 스택에 추가
                    if (node.subNodes?.Count > 0)
                    {
                        for (int i = node.subNodes.Count - 1; i >= 0; i--)
                        {
                            nodeStack.Push(node.subNodes[i]);
                        }
                    }

                    // 조기 종료 최적화
                    if (hasWorkGiver && hasConditionalColonist)
                        break;
                }

                if (Utility_DebugManager.ShouldLogDetailed())
                {
                    Utility_DebugManager.LogNormal($"ThinkTree validation for {pawn.LabelCap}:");
                    Utility_DebugManager.LogNormal($"- Has JobGiver_Work: {hasWorkGiver}");
                    Utility_DebugManager.LogNormal($"- Has ConditionalColonist: {hasConditionalColonist}");
                }
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"ThinkTree validation error: {ex}");
            }
        }

        #endregion

        #region Compatibility Methods - replace Utility_CacheManager calls

        /// <summary>
        /// 🟢 Utility_CacheManager의 Dictionary 사용을 이 클래스의 ConcurrentDictionary로 대체
        /// </summary>
        [Obsolete("Use HasAllowWorkTag instead for better performance")]
        public static void UpdateLegacyCacheCompat(Pawn pawn)
        {
            // Trigger our optimized calculation
            HasAllowOrBlockWorkTag(pawn);

            // Update legacy caches for backward compatibility if needed
            if (Utility_CacheManager.AllowWorkTag.ContainsKey(pawn))
            {
                Utility_CacheManager.AllowWorkTag[pawn] = _allowWorkTagCache.TryGetValue(pawn, out bool allow) && allow;
                Utility_CacheManager.BlockWorkTag[pawn] = _blockWorkTagCache.TryGetValue(pawn, out bool block) && block;
                Utility_CacheManager.CombinedWorkTag[pawn] = _combinedWorkTagCache.TryGetValue(pawn, out bool combined) && combined;
            }
        }

        #endregion
    }
}