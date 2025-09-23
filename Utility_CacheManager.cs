using RimWorld;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    public static class Utility_CacheManager
    {
        #region Caches

        // ModExtension caching - Thread-safe
        public static readonly ConcurrentDictionary<ThingDef, NonHumanlikePawnControlExtension> _modExtensionCache = new ConcurrentDictionary<ThingDef, NonHumanlikePawnControlExtension>();

        // Tag caching - Thread-safe
        public static readonly ConcurrentDictionary<ThingDef, HashSet<string>> Tags = new ConcurrentDictionary<ThingDef, HashSet<string>>();
        public static readonly ConcurrentDictionary<ThingDef, PawnTagFlags> TagFlags = new ConcurrentDictionary<ThingDef, PawnTagFlags>();

        // Thread-safe animal caching
        public static readonly ConcurrentDictionary<ThingDef, bool> ForcedAnimals = new ConcurrentDictionary<ThingDef, bool>();

        // Colony pawns caching - Thread-safe
        private static readonly ConcurrentDictionary<Map, List<Pawn>> _colonistLikePawns = new ConcurrentDictionary<Map, List<Pawn>>();
        public static readonly ConcurrentDictionary<Map, int> FrameIndices = new ConcurrentDictionary<Map, int>();

        // Duty caching - Thread-safe
        private static readonly ConcurrentDictionary<string, DutyDef> _dutyDefs = new ConcurrentDictionary<string, DutyDef>();

        // Apparel restriction caching - Thread-safe
        private static readonly ConcurrentDictionary<Pawn, bool> _apparelRestrictions = new ConcurrentDictionary<Pawn, bool>();

        // Work-related caches - Thread-safe
        public static readonly ConcurrentDictionary<ValueTuple<Pawn, string>, bool> WorkEnabled = new ConcurrentDictionary<ValueTuple<Pawn, string>, bool>();
        public static readonly ConcurrentDictionary<ValueTuple<Pawn, WorkTypeDef>, bool> WorkTypeEnabled = new ConcurrentDictionary<ValueTuple<Pawn, WorkTypeDef>, bool>();
        public static readonly ConcurrentDictionary<ValueTuple<Pawn, string>, bool> WorkDisabled = new ConcurrentDictionary<ValueTuple<Pawn, string>, bool>();
        public static readonly ConcurrentDictionary<ValueTuple<Pawn, string>, bool> ForceDraftable = new ConcurrentDictionary<ValueTuple<Pawn, string>, bool>();
        public static readonly ConcurrentDictionary<ValueTuple<Pawn, string>, bool> ForceEquipWeapon = new ConcurrentDictionary<ValueTuple<Pawn, string>, bool>();
        public static readonly ConcurrentDictionary<ValueTuple<Pawn, string>, bool> ForceWearApparel = new ConcurrentDictionary<ValueTuple<Pawn, string>, bool>();

        // Work tag caching - Thread-safe
        public static readonly ConcurrentDictionary<Pawn, bool> AllowWorkTag = new ConcurrentDictionary<Pawn, bool>();
        public static readonly ConcurrentDictionary<Pawn, bool> BlockWorkTag = new ConcurrentDictionary<Pawn, bool>();
        public static readonly ConcurrentDictionary<Pawn, bool> CombinedWorkTag = new ConcurrentDictionary<Pawn, bool>();

        // Race identity caches - Thread-safe
        public static readonly ConcurrentDictionary<ThingDef, bool> IsAnimal = new ConcurrentDictionary<ThingDef, bool>();
        public static readonly ConcurrentDictionary<ThingDef, bool> IsHumanlike = new ConcurrentDictionary<ThingDef, bool>();
        public static readonly ConcurrentDictionary<ThingDef, bool> IsMechanoid = new ConcurrentDictionary<ThingDef, bool>();
        public static readonly ConcurrentDictionary<FlagScopeTarget, bool> FlagOverrides = new ConcurrentDictionary<FlagScopeTarget, bool>();

        // UI caching - Thread-safe
        public static readonly ConcurrentDictionary<int, bool> BioTabVisibility = new ConcurrentDictionary<int, bool>();

        // 재사용 가능한 리스트 - 메모리 할당 최소화
        [ThreadStatic]
        private static List<ValueTuple<Pawn, string>> _tempTupleList;
        private static List<ValueTuple<Pawn, string>> TempTupleList => _tempTupleList ?? (_tempTupleList = new List<ValueTuple<Pawn, string>>());

        [ThreadStatic]
        private static List<ValueTuple<Pawn, WorkTypeDef>> _tempWorkTypeTupleList;
        private static List<ValueTuple<Pawn, WorkTypeDef>> TempWorkTypeTupleList => _tempWorkTypeTupleList ?? (_tempWorkTypeTupleList = new List<ValueTuple<Pawn, WorkTypeDef>>());

        #endregion

        #region ModExtension Caching

        /// <summary>
        /// Gets a cached ModExtension for a ThingDef or creates one if not present
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static NonHumanlikePawnControlExtension GetModExtension(ThingDef def)
        {
            if (def == null)
                return null;

            return _modExtensionCache.GetOrAdd(def, defKey =>
            {
                NonHumanlikePawnControlExtension modExtension = null;
                try
                {
                    if (defKey.modExtensions != null)
                        modExtension = defKey.GetModExtension<NonHumanlikePawnControlExtension>();
                }
                catch (Exception ex)
                {
                    if (Prefs.DevMode)
                        Log.Warning($"[PawnControl] GetModExtension failed for def {defKey.defName}: {ex.Message}");
                }
                return modExtension;
            });
        }

        /// <summary>
        /// Updates the cached ModExtension for a ThingDef
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static void UpdateModExtension(ThingDef def, NonHumanlikePawnControlExtension extension)
        {
            if (def == null)
                return;

            _modExtensionCache.AddOrUpdate(def, extension, (key, oldValue) => extension);
        }

        /// <summary>
        /// Preloads all ModExtensions from the DefDatabase
        /// </summary>
        public static void PreloadModExtensions()
        {
            int loadedCount = 0;
            var defs = DefDatabase<ThingDef>.AllDefsListForReading;

            // 병렬 처리로 성능 향상
            Parallel.ForEach(defs, def =>
            {
                if (def?.race == null)
                    return;

                var modExtension = def.GetModExtension<NonHumanlikePawnControlExtension>();
                if (modExtension != null)
                {
                    _modExtensionCache.TryAdd(def, modExtension);
                    modExtension.CacheSkillPassions();
                    Interlocked.Increment(ref loadedCount);
                }
            });

            Utility_DebugManager.LogNormal($"Non Humanlike Pawn Controller refreshed: {loadedCount} extensions loaded.");
        }

        /// <summary>
        /// Preloads the ModExtension for a specific race
        /// </summary>
        public static void PreloadModExtensionForRace(ThingDef def)
        {
            if (def == null)
                return;

            _modExtensionCache.GetOrAdd(def, defKey =>
            {
                // Look for our extension type in the mod extensions
                if (defKey.modExtensions != null)
                {
                    foreach (var ext in defKey.modExtensions)
                    {
                        if (ext is NonHumanlikePawnControlExtension controlExt)
                        {
                            return controlExt;
                        }
                    }
                }
                return null; // No extension found
            });
        }

        /// <summary>
        /// Clears all cached ModExtensions
        /// </summary>
        public static void ClearAllModExtensions()
        {
            _modExtensionCache.Clear();
            Utility_DebugManager.LogNormal("Cleared all mod extensions in cache.");
        }

        /// <summary>
        /// Clears the cached ModExtension for a specific ThingDef
        /// </summary>
        public static void ClearModExtension(ThingDef def)
        {
            if (def == null)
                return;

            _modExtensionCache.TryRemove(def, out _);

            if (Prefs.DevMode)
                Utility_DebugManager.LogNormal($"Cleared mod extension cache for {def.defName}.");
        }

        /// <summary>
        /// Cleans up all runtime ModExtensions when starting a new game
        /// </summary>
        public static void CleanupRuntimeModExtensions()
        {
            Log.Message("[PawnControl] Cleaning up runtime mod extensions for new game");

            int removedCount = 0;
            var defs = DefDatabase<ThingDef>.AllDefsListForReading;

            // Clean all ThingDefs that have our extensions
            foreach (ThingDef def in defs)
            {
                if (def?.modExtensions == null)
                    continue;

                // Look for and remove our extensions (non-XML ones)
                for (int i = def.modExtensions.Count - 1; i >= 0; i--)
                {
                    if (def.modExtensions[i] is NonHumanlikePawnControlExtension ext && !ext.fromXML)
                    {
                        def.modExtensions.RemoveAt(i);
                        removedCount++;
                    }
                }
            }

            // Clear all runtime caches
            Clear();

            Log.Message($"[PawnControl] Removed {removedCount} runtime mod extensions when starting new game");
        }

        #endregion

        #region Duty & Colonial Pawn Caching

        /// <summary>
        /// Gets a cached DutyDef by name
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static DutyDef GetDuty(string defName)
        {
            if (string.IsNullOrEmpty(defName))
                return null;

            return _dutyDefs.GetOrAdd(defName, name => DefDatabase<DutyDef>.GetNamedSilentFail(name));
        }

        /// <summary>
        /// Returns a list of all colonist-like pawns in the given map and cache them
        /// </summary>
        public static IEnumerable<Pawn> GetColonistLikePawns(Map map)
        {
            if (map == null)
                return Enumerable.Empty<Pawn>();

            int frame = Time.frameCount;

            // TTL 체크를 GetOrAdd 내부에서 처리
            return _colonistLikePawns.GetOrAdd(map, mapKey =>
            {
                FrameIndices.AddOrUpdate(mapKey, frame, (key, oldFrame) => frame);
                return CalculateColonistLikePawns(mapKey);
            });
        }

        /// <summary>
        /// 실제 계산 로직을 별도 메서드로 분리 - LINQ 제거로 성능 향상
        /// </summary>
        private static List<Pawn> CalculateColonistLikePawns(Map map)
        {
            var result = new List<Pawn>();
            var allPawns = map.mapPawns.AllPawns;

            for (int i = 0; i < allPawns.Count; i++)
            {
                var pawn = allPawns[i];

                if (pawn == null || pawn.def == null || pawn.def.race == null)
                    continue;

                if (pawn.Faction != Faction.OfPlayer)
                    continue;

                // Require at least one valid work type to be enabled
                var modExtension = GetModExtension(pawn.def);

                // Fallback: vanilla logic
                if (modExtension == null)
                {
                    var workTypeDefs = DefDatabase<WorkTypeDef>.AllDefsListForReading;
                    bool hasValidWork = false;

                    for (int j = 0; j < workTypeDefs.Count; j++)
                    {
                        if (pawn.workSettings?.WorkIsActive(workTypeDefs[j]) == true)
                        {
                            hasValidWork = true;
                            break;
                        }
                    }

                    if (hasValidWork)
                        result.Add(pawn);
                }
                else
                {
                    // If extension exists, check for at least one tag-allowed work type
                    var workTypeDefs = DefDatabase<WorkTypeDef>.AllDefsListForReading;
                    bool hasValidWork = false;

                    for (int j = 0; j < workTypeDefs.Count; j++)
                    {
                        if (IsWorkTypeEnabledForPawn(pawn, workTypeDefs[j]))
                        {
                            hasValidWork = true;
                            break;
                        }
                    }

                    if (hasValidWork)
                        result.Add(pawn);
                }
            }

            return result;
        }

        /// <summary>
        /// Invalidates colonist-like pawns cache for a map
        /// </summary>
        public static void InvalidateColonistCache(Map map)
        {
            if (map == null)
                return;

            _colonistLikePawns.TryRemove(map, out _);
            FrameIndices.TryRemove(map, out _);
        }

        #endregion

        #region Work-Related Caching

        /// <summary>
        /// Checks whether a pawn is allowed to perform a work type
        /// </summary>
        public static bool IsWorkTypeEnabledForPawn(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn == null || workType == null)
                return false;

            var key = new ValueTuple<Pawn, WorkTypeDef>(pawn, workType);

            return WorkTypeEnabled.GetOrAdd(key, tuple =>
            {
                var (targetPawn, targetWorkType) = tuple;

                // Only non-humanlike pawns with valid mod extensions AND allow tags can enable work
                var modExtension = GetModExtension(targetPawn.def);

                if (modExtension == null)
                    return targetPawn.workSettings?.WorkIsActive(targetWorkType) == true; // Vanilla fallback

                if (modExtension.tags == null)
                    return false;

                return Utility_TagManager.HasTag(targetPawn.def, ManagedTags.AllowAllWork) ||
                       Utility_TagManager.HasTag(targetPawn.def, ManagedTags.AllowWorkPrefix + targetWorkType.defName);
            });
        }

        /// <summary>
        /// Checks if a pawn has apparel restrictions
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static bool IsApparelRestricted(Pawn pawn)
        {
            if (pawn == null || pawn.def == null)
                return false;

            return _apparelRestrictions.GetOrAdd(pawn, targetPawn =>
            {
                var modExtension = GetModExtension(targetPawn.def);
                return modExtension != null && modExtension.restrictApparelByBodyType;
            });
        }

        #endregion

        #region Efficient Cache Cleanup Methods

        /// <summary>
        /// 효율적인 Pawn별 캐시 정리 - 메모리 할당 최소화
        /// </summary>
        public static void ClearCacheForPawn(Pawn pawn)
        {
            if (pawn == null)
                return;

            // 기본 캐시들
            AllowWorkTag.TryRemove(pawn, out _);
            BlockWorkTag.TryRemove(pawn, out _);
            CombinedWorkTag.TryRemove(pawn, out _);
            _apparelRestrictions.TryRemove(pawn, out _);

            // ValueTuple 캐시들 - 효율적으로 정리
            ClearTupleCache(WorkEnabled, pawn);
            ClearTupleCache(WorkDisabled, pawn);
            ClearTupleCache(ForceDraftable, pawn);
            ClearTupleCache(ForceEquipWeapon, pawn);
            ClearTupleCache(ForceWearApparel, pawn);
            ClearWorkTypeTupleCache(WorkTypeEnabled, pawn);

            if (Prefs.DevMode)
                Utility_DebugManager.LogNormal($"Cleared all cache entries for pawn {pawn.NameShortColored}");
        }

        /// <summary>
        /// 효율적인 Race별 캐시 정리
        /// </summary>
        public static void ClearCacheForRace(ThingDef def)
        {
            if (def == null)
                return;

            // 기본 캐시들
            Tags.TryRemove(def, out _);
            TagFlags.TryRemove(def, out _);
            _modExtensionCache.TryRemove(def, out _);
            ForcedAnimals.TryRemove(def, out _);
            IsAnimal.TryRemove(def, out _);
            IsHumanlike.TryRemove(def, out _);
            IsMechanoid.TryRemove(def, out _);

            // ValueTuple 캐시들을 Race별로 정리
            ClearTuplesCacheByRace(def);

            if (Prefs.DevMode)
                Utility_DebugManager.LogNormal($"Cache entries for race {def.defName} successfully cleared");
        }

        /// <summary>
        /// ValueTuple<Pawn, string> 캐시를 효율적으로 정리
        /// </summary>
        private static void ClearTupleCache(ConcurrentDictionary<ValueTuple<Pawn, string>, bool> cache, Pawn pawn)
        {
            var tempList = TempTupleList;
            tempList.Clear();

            // 제거할 키들을 먼저 수집
            foreach (var kvp in cache)
            {
                if (kvp.Key.Item1 == pawn)
                    tempList.Add(kvp.Key);
            }

            // 수집된 키들을 제거
            for (int i = 0; i < tempList.Count; i++)
            {
                cache.TryRemove(tempList[i], out _);
            }

            tempList.Clear();
        }

        /// <summary>
        /// ValueTuple<Pawn, WorkTypeDef> 캐시를 효율적으로 정리
        /// </summary>
        private static void ClearWorkTypeTupleCache(ConcurrentDictionary<ValueTuple<Pawn, WorkTypeDef>, bool> cache, Pawn pawn)
        {
            var tempList = TempWorkTypeTupleList;
            tempList.Clear();

            // 제거할 키들을 먼저 수집
            foreach (var kvp in cache)
            {
                if (kvp.Key.Item1 == pawn)
                    tempList.Add(kvp.Key);
            }

            // 수집된 키들을 제거
            for (int i = 0; i < tempList.Count; i++)
            {
                cache.TryRemove(tempList[i], out _);
            }

            tempList.Clear();
        }

        /// <summary>
        /// Race별 ValueTuple 캐시들을 효율적으로 정리
        /// </summary>
        private static void ClearTuplesCacheByRace(ThingDef def)
        {
            var tempList = TempTupleList;
            var tempWorkTypeList = TempWorkTypeTupleList;

            // WorkEnabled 캐시 정리
            tempList.Clear();
            foreach (var kvp in WorkEnabled)
            {
                if (kvp.Key.Item1?.def == def)
                    tempList.Add(kvp.Key);
            }
            for (int i = 0; i < tempList.Count; i++)
                WorkEnabled.TryRemove(tempList[i], out _);

            // WorkDisabled 캐시 정리
            tempList.Clear();
            foreach (var kvp in WorkDisabled)
            {
                if (kvp.Key.Item1?.def == def)
                    tempList.Add(kvp.Key);
            }
            for (int i = 0; i < tempList.Count; i++)
                WorkDisabled.TryRemove(tempList[i], out _);

            // ForceDraftable 캐시 정리
            tempList.Clear();
            foreach (var kvp in ForceDraftable)
            {
                if (kvp.Key.Item1?.def == def)
                    tempList.Add(kvp.Key);
            }
            for (int i = 0; i < tempList.Count; i++)
                ForceDraftable.TryRemove(tempList[i], out _);

            // ForceEquipWeapon 캐시 정리
            tempList.Clear();
            foreach (var kvp in ForceEquipWeapon)
            {
                if (kvp.Key.Item1?.def == def)
                    tempList.Add(kvp.Key);
            }
            for (int i = 0; i < tempList.Count; i++)
                ForceEquipWeapon.TryRemove(tempList[i], out _);

            // ForceWearApparel 캐시 정리
            tempList.Clear();
            foreach (var kvp in ForceWearApparel)
            {
                if (kvp.Key.Item1?.def == def)
                    tempList.Add(kvp.Key);
            }
            for (int i = 0; i < tempList.Count; i++)
                ForceWearApparel.TryRemove(tempList[i], out _);

            // WorkTypeEnabled 캐시 정리
            tempWorkTypeList.Clear();
            foreach (var kvp in WorkTypeEnabled)
            {
                if (kvp.Key.Item1?.def == def)
                    tempWorkTypeList.Add(kvp.Key);
            }
            for (int i = 0; i < tempWorkTypeList.Count; i++)
                WorkTypeEnabled.TryRemove(tempWorkTypeList[i], out _);

            tempList.Clear();
            tempWorkTypeList.Clear();
        }

        #endregion

        #region Reset Methods

        /// <summary>
        /// Clears all caches
        /// </summary>
        public static void Clear()
        {
            // Clear specialized caches
            _modExtensionCache.Clear();
            Tags.Clear();
            TagFlags.Clear();
            ForcedAnimals.Clear();
            _colonistLikePawns.Clear();
            FrameIndices.Clear();
            _dutyDefs.Clear();
            _apparelRestrictions.Clear();
            WorkEnabled.Clear();
            WorkTypeEnabled.Clear();
            WorkDisabled.Clear();
            ForceDraftable.Clear();
            ForceEquipWeapon.Clear();
            ForceWearApparel.Clear();
            IsAnimal.Clear();
            IsHumanlike.Clear();
            IsMechanoid.Clear();
            FlagOverrides.Clear();
            AllowWorkTag.Clear();
            BlockWorkTag.Clear();
            CombinedWorkTag.Clear();
            BioTabVisibility.Clear();

            if (Prefs.DevMode)
                Utility_DebugManager.LogNormal("All caches cleared.");
        }

        #endregion
    }
}