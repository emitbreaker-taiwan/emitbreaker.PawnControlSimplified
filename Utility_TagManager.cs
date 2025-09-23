using RimWorld;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Verse;

namespace emitbreaker.PawnControl
{
    public static class Utility_TagManager
    {
        #region Static Cache for Work Type Mappings - 성능 최적화

        // Work type string to flag mapping 캐시 - 초기화 시 한 번만 생성
        private static readonly ConcurrentDictionary<string, PawnTagFlags> _allowWorkFlagCache = new ConcurrentDictionary<string, PawnTagFlags>();
        private static readonly ConcurrentDictionary<string, PawnTagFlags> _blockWorkFlagCache = new ConcurrentDictionary<string, PawnTagFlags>();

        // 재사용 가능한 임시 컬렉션 - 메모리 할당 최소화
        [ThreadStatic]
        private static List<ValueTuple<Pawn, string>> _tempTupleList;
        private static List<ValueTuple<Pawn, string>> TempTupleList => _tempTupleList ?? (_tempTupleList = new List<ValueTuple<Pawn, string>>());

        static Utility_TagManager()
        {
            InitializeWorkTypeFlagMappings();
        }

        /// <summary>
        /// Work type 플래그 매핑을 미리 초기화 - 런타임 성능 향상
        /// </summary>
        private static void InitializeWorkTypeFlagMappings()
        {
            // Allow work flags
            _allowWorkFlagCache["Firefighter"] = PawnTagFlags.AllowWork_Firefighter;
            _allowWorkFlagCache["Patient"] = PawnTagFlags.AllowWork_Patient;
            _allowWorkFlagCache["Doctor"] = PawnTagFlags.AllowWork_Doctor;
            _allowWorkFlagCache["PatientBedRest"] = PawnTagFlags.AllowWork_PatientBedRest;
            _allowWorkFlagCache["BasicWorker"] = PawnTagFlags.AllowWork_BasicWorker;
            _allowWorkFlagCache["Warden"] = PawnTagFlags.AllowWork_Warden;
            _allowWorkFlagCache["Handling"] = PawnTagFlags.AllowWork_Handling;
            _allowWorkFlagCache["Cooking"] = PawnTagFlags.AllowWork_Cooking;
            _allowWorkFlagCache["Hunting"] = PawnTagFlags.AllowWork_Hunting;
            _allowWorkFlagCache["Construction"] = PawnTagFlags.AllowWork_Construction;
            _allowWorkFlagCache["Growing"] = PawnTagFlags.AllowWork_Growing;
            _allowWorkFlagCache["Mining"] = PawnTagFlags.AllowWork_Mining;
            _allowWorkFlagCache["PlantCutting"] = PawnTagFlags.AllowWork_PlantCutting;
            _allowWorkFlagCache["Smithing"] = PawnTagFlags.AllowWork_Smithing;
            _allowWorkFlagCache["Tailoring"] = PawnTagFlags.AllowWork_Tailoring;
            _allowWorkFlagCache["Art"] = PawnTagFlags.AllowWork_Art;
            _allowWorkFlagCache["Crafting"] = PawnTagFlags.AllowWork_Crafting;
            _allowWorkFlagCache["Hauling"] = PawnTagFlags.AllowWork_Hauling;
            _allowWorkFlagCache["Cleaning"] = PawnTagFlags.AllowWork_Cleaning;
            _allowWorkFlagCache["Research"] = PawnTagFlags.AllowWork_Research;
            _allowWorkFlagCache["Childcare"] = PawnTagFlags.AllowWork_Childcare;
            _allowWorkFlagCache["DarkStudy"] = PawnTagFlags.AllowWork_DarkStudy;

            // Block work flags
            _blockWorkFlagCache["Firefighter"] = PawnTagFlags.BlockWork_Firefighter;
            _blockWorkFlagCache["Patient"] = PawnTagFlags.BlockWork_Patient;
            _blockWorkFlagCache["Doctor"] = PawnTagFlags.BlockWork_Doctor;
            _blockWorkFlagCache["PatientBedRest"] = PawnTagFlags.BlockWork_PatientBedRest;
            _blockWorkFlagCache["BasicWorker"] = PawnTagFlags.BlockWork_BasicWorker;
            _blockWorkFlagCache["Warden"] = PawnTagFlags.BlockWork_Warden;
            _blockWorkFlagCache["Handling"] = PawnTagFlags.BlockWork_Handling;
            _blockWorkFlagCache["Cooking"] = PawnTagFlags.BlockWork_Cooking;
            _blockWorkFlagCache["Hunting"] = PawnTagFlags.BlockWork_Hunting;
            _blockWorkFlagCache["Construction"] = PawnTagFlags.BlockWork_Construction;
            _blockWorkFlagCache["Growing"] = PawnTagFlags.BlockWork_Growing;
            _blockWorkFlagCache["Mining"] = PawnTagFlags.BlockWork_Mining;
            _blockWorkFlagCache["PlantCutting"] = PawnTagFlags.BlockWork_PlantCutting;
            _blockWorkFlagCache["Smithing"] = PawnTagFlags.BlockWork_Smithing;
            _blockWorkFlagCache["Tailoring"] = PawnTagFlags.BlockWork_Tailoring;
            _blockWorkFlagCache["Art"] = PawnTagFlags.BlockWork_Art;
            _blockWorkFlagCache["Crafting"] = PawnTagFlags.BlockWork_Crafting;
            _blockWorkFlagCache["Hauling"] = PawnTagFlags.BlockWork_Hauling;
            _blockWorkFlagCache["Cleaning"] = PawnTagFlags.BlockWork_Cleaning;
            _blockWorkFlagCache["Research"] = PawnTagFlags.BlockWork_Research;
            _blockWorkFlagCache["Childcare"] = PawnTagFlags.BlockWork_Childcare;
            _blockWorkFlagCache["DarkStudy"] = PawnTagFlags.BlockWork_DarkStudy;
        }

        #endregion

        #region Tag Flags Cache

        /// <summary>
        /// Gets the cached PawnTagFlags for a ThingDef, or builds them if not cached
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PawnTagFlags GetTagFlags(ThingDef def)
        {
            if (!Utility_Common.RaceDefChecker(def))
                return PawnTagFlags.None;

            return Utility_CacheManager.TagFlags.GetOrAdd(def, BuildTagFlags);
        }

        /// <summary>
        /// Builds PawnTagFlags for a ThingDef from its string-based tags
        /// </summary>
        private static PawnTagFlags BuildTagFlags(ThingDef def)
        {
            if (!Utility_Common.RaceDefChecker(def))
                return PawnTagFlags.None;

            PawnTagFlags flags = PawnTagFlags.None;
            var modExtension = Utility_CacheManager.GetModExtension(def);

            if (modExtension?.tags == null)
                return flags;

            // Convert string tags to flags - 최적화된 버전
            for (int i = 0; i < modExtension.tags.Count; i++)
            {
                string tag = modExtension.tags[i];
                if (string.IsNullOrEmpty(tag)) continue;

                flags |= ConvertTagToFlagOptimized(tag);
            }

            // Apply extension properties directly
            if (modExtension.forceDraftable) flags |= PawnTagFlags.ForceDraftable;
            if (modExtension.forceEquipWeapon) flags |= PawnTagFlags.ForceEquipWeapon;
            if (modExtension.forceWearApparel) flags |= PawnTagFlags.ForceWearApparel;

            return flags;
        }

        /// <summary>
        /// 최적화된 태그-플래그 변환 - switch 문 사용으로 성능 향상
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static PawnTagFlags ConvertTagToFlagOptimized(string tag)
        {
            // 기본 태그들을 switch로 빠르게 처리
            switch (tag)
            {
                case ManagedTags.ForceAnimal: return PawnTagFlags.ForceAnimal;
                case ManagedTags.ForceMechanoid: return PawnTagFlags.ForceMechanoid;
                case ManagedTags.ForceHumanlike: return PawnTagFlags.ForceHumanlike;
                case ManagedTags.ForceDraftable: return PawnTagFlags.ForceDraftable;
                case ManagedTags.ForceEquipWeapon: return PawnTagFlags.ForceEquipWeapon;
                case ManagedTags.ForceWearApparel: return PawnTagFlags.ForceWearApparel;
                case ManagedTags.AllowAllWork: return PawnTagFlags.AllowAllWork;
                case ManagedTags.BlockAllWork: return PawnTagFlags.BlockAllWork;
                case ManagedTags.ForceVFECombatTree: return PawnTagFlags.ForceVFECombatTree;
                case ManagedTags.VFECore_Combat: return PawnTagFlags.VFECore_Combat;
                case ManagedTags.Mech_DefendBeacon: return PawnTagFlags.Mech_DefendBeacon;
                case ManagedTags.Mech_EscortCommander: return PawnTagFlags.Mech_EscortCommander;
                case ManagedTags.VFESkipWorkJobGiver: return PawnTagFlags.VFESkipWorkJobGiver;
                case ManagedTags.DisableVFEAIJobs: return PawnTagFlags.DisableVFEAIJobs;
            }

            // Enum 태그들 처리
            var enumTag = PawnEnumTags.ForceWork.ToStringSafe();
            if (tag == enumTag) return PawnTagFlags.ForceWork;

            enumTag = PawnEnumTags.ForceTrainerTab.ToStringSafe();
            if (tag == enumTag) return PawnTagFlags.ForceTrainerTab;

            enumTag = PawnEnumTags.AutoDraftInjection.ToStringSafe();
            if (tag == enumTag) return PawnTagFlags.AutoDraftInjection;

            // Prefix 기반 태그 처리
            if (tag.StartsWith(ManagedTags.AllowWorkPrefix))
            {
                string workType = tag.Substring(ManagedTags.AllowWorkPrefix.Length);
                return GetAllowWorkFlag(workType);
            }

            if (tag.StartsWith(ManagedTags.BlockWorkPrefix))
            {
                string workType = tag.Substring(ManagedTags.BlockWorkPrefix.Length);
                return GetBlockWorkFlag(workType);
            }

            return PawnTagFlags.None;
        }

        /// <summary>
        /// Allow work flag을 캐시에서 가져오기 - 성능 최적화
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static PawnTagFlags GetAllowWorkFlag(string workType)
        {
            if (string.IsNullOrEmpty(workType))
                return PawnTagFlags.None;

            return _allowWorkFlagCache.GetValueOrDefault(workType, PawnTagFlags.None);
        }

        /// <summary>
        /// Block work flag을 캐시에서 가져오기 - 성능 최적화
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static PawnTagFlags GetBlockWorkFlag(string workType)
        {
            if (string.IsNullOrEmpty(workType))
                return PawnTagFlags.None;

            return _blockWorkFlagCache.GetValueOrDefault(workType, PawnTagFlags.None);
        }

        /// <summary>
        /// Work type flag 가져오기 - 통합된 인터페이스
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static PawnTagFlags GetWorkTypeFlag(string workTypeName, bool isBlock = false)
        {
            return isBlock ? GetBlockWorkFlag(workTypeName) : GetAllowWorkFlag(workTypeName);
        }

        #endregion

        #region Legacy API (String-based) - 최적화된 버전

        public static bool HasTagSet(ThingDef def, NonHumanlikePawnControlExtension modExtension = null)
        {
            if (!Utility_Common.RaceDefChecker(def))
                return false;

            if (modExtension == null)
                modExtension = Utility_CacheManager.GetModExtension(def);

            if (modExtension == null)
                return false;

            // Flag 기반 체크가 더 빠름
            PawnTagFlags flags = GetTagFlags(def);
            if (flags != PawnTagFlags.None)
                return true;

            return modExtension.tags?.Count > 0;
        }

        public static bool HasTag(Pawn pawn, string tag)
        {
            return Utility_Common.PawnChecker(pawn) && !string.IsNullOrEmpty(tag) && HasTag(pawn.def, tag);
        }

        public static bool HasTag(ThingDef def, string tag)
        {
            if (!Utility_Common.RaceDefChecker(def) || string.IsNullOrEmpty(tag))
                return false;

            // Flag 기반 체크 우선 - 성능 향상
            PawnTagFlags tagFlag = ConvertTagToFlagOptimized(tag);
            if (tagFlag != PawnTagFlags.None)
                return HasFlag(def, tagFlag);

            // String 기반 fallback
            if (!HasTagSet(def))
                return false;

            var tagSet = GetTags(def);
            bool hasTag = tagSet.Contains(tag);

            if (Prefs.DevMode)
                Utility_DebugManager.LogNormal($"Checking tag '{tag}' for def={def.defName}: {hasTag}");

            return hasTag;
        }

        public static bool WorkTypeSettingEnabled(Pawn pawn, WorkTypeDef workTypeDef)
        {
            if (!Utility_Common.RaceDefChecker(pawn.def) || workTypeDef == null)
                return false;

            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null)
                return false;

            if (!IsWorkEnabled(pawn, workTypeDef))
                return false;

            // Work settings 초기화
            if (pawn.workSettings == null)
            {
                Utility_DebugManager.LogWarning($"[{pawn.LabelShort}] has no work setting.");
                pawn.workSettings = new Pawn_WorkSettings(pawn);
            }

            if (!pawn.workSettings.EverWork)
                pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();

            bool active = pawn.workSettings.WorkIsActive(workTypeDef);
            int priority = pawn.workSettings.GetPriority(workTypeDef);

            if (priority > 0)
                active = true;

            if (!active && Prefs.DevMode)
                Utility_DebugManager.LogWarning($"[{pawn.LabelShort}] {workTypeDef.defName} is disabled in UI");

            return active;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool WorkTypeSettingEnabled(Pawn pawn, string workTypeName)
        {
            var workTypeDef = Utility_WorkTypeManager.Named(workTypeName);
            return Utility_Common.RaceDefChecker(pawn.def) && !string.IsNullOrEmpty(workTypeName) &&
                   WorkTypeSettingEnabled(pawn, workTypeDef);
        }

        public static bool ForceDraftable(Pawn pawn, string tag = ManagedTags.ForceDraftable)
        {
            if (!Utility_Common.RaceDefChecker(pawn.def) || string.IsNullOrEmpty(tag))
                return false;

            var key = new ValueTuple<Pawn, string>(pawn, tag);

            return Utility_CacheManager.ForceDraftable.GetOrAdd(key, k =>
            {
                var modExtension = Utility_CacheManager.GetModExtension(k.Item1.def);
                if (modExtension == null)
                    return false;

                if (tag == ManagedTags.ForceDraftable)
                {
                    PawnTagFlags flags = GetTagFlags(k.Item1.def);
                    return (flags & PawnTagFlags.ForceDraftable) != 0;
                }

                return HasTag(k.Item1, tag) || modExtension.forceDraftable;
            });
        }

        public static bool ForceEquipWeapon(Pawn pawn, string tag = ManagedTags.ForceEquipWeapon)
        {
            if (!Utility_Common.RaceDefChecker(pawn.def) || string.IsNullOrEmpty(tag))
                return false;

            var key = new ValueTuple<Pawn, string>(pawn, tag);

            return Utility_CacheManager.ForceEquipWeapon.GetOrAdd(key, k =>
            {
                var modExtension = Utility_CacheManager.GetModExtension(k.Item1.def);
                if (modExtension == null)
                    return false;

                if (!ForceDraftable(k.Item1))
                {
                    Utility_DebugManager.LogError($"{k.Item1.def.defName} is not forceDraftable, cannot force equip weapon.");
                    return false;
                }

                if (tag == ManagedTags.ForceEquipWeapon)
                {
                    PawnTagFlags flags = GetTagFlags(k.Item1.def);
                    return (flags & PawnTagFlags.ForceEquipWeapon) != 0;
                }

                return HasTag(k.Item1, tag) || modExtension.forceEquipWeapon;
            });
        }

        public static bool ForceWearApparel(Pawn pawn, string tag = ManagedTags.ForceWearApparel)
        {
            if (!Utility_Common.RaceDefChecker(pawn.def) || string.IsNullOrEmpty(tag))
                return false;

            var key = new ValueTuple<Pawn, string>(pawn, tag);

            return Utility_CacheManager.ForceWearApparel.GetOrAdd(key, k =>
            {
                var modExtension = Utility_CacheManager.GetModExtension(k.Item1.def);
                if (modExtension == null)
                    return false;

                if (!ForceDraftable(k.Item1))
                {
                    Utility_DebugManager.LogError($"{k.Item1.def.defName} is not forceDraftable, cannot force wear apparel.");
                    return false;
                }

                if (tag == ManagedTags.ForceWearApparel)
                {
                    PawnTagFlags flags = GetTagFlags(k.Item1.def);
                    return (flags & PawnTagFlags.ForceWearApparel) != 0;
                }

                return HasTag(k.Item1, tag) || modExtension.forceWearApparel;
            });
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static HashSet<string> GetTags(ThingDef def)
        {
            if (!Utility_Common.RaceDefChecker(def))
                return new HashSet<string>();

            return Utility_CacheManager.Tags.GetOrAdd(def, BuildTags);
        }

        private static HashSet<string> BuildTags(ThingDef def)
        {
            var set = new HashSet<string>();

            if (!Utility_Common.RaceDefChecker(def))
                return set;

            var modExtension = Utility_CacheManager.GetModExtension(def);
            if (modExtension?.tags == null)
                return set;

            // 직접 for 루프로 성능 향상
            for (int i = 0; i < modExtension.tags.Count; i++)
            {
                string tag = modExtension.tags[i];
                if (!string.IsNullOrEmpty(tag))
                    set.Add(tag);
            }

            // Legacy enum tags 추가
            var enumTags = new[] { PawnEnumTags.ForceAnimal, PawnEnumTags.ForceDraftable };
            for (int i = 0; i < enumTags.Length; i++)
            {
                string enumStr = enumTags[i].ToStringSafe();
                if (set.Contains(enumStr))
                    set.Add(enumStr);
            }

            return set;
        }

        public static bool HasAnyTagWithPrefix(ThingDef def, string prefix)
        {
            if (!Utility_Common.RaceDefChecker(def) || string.IsNullOrEmpty(prefix))
                return false;

            // Flag 기반 최적화
            if (prefix == ManagedTags.AllowWorkPrefix)
            {
                PawnTagFlags flags = GetTagFlags(def);
                PawnTagFlags allowWorkFlags = flags & (
                    PawnTagFlags.AllowWork_Firefighter | PawnTagFlags.AllowWork_Patient |
                    PawnTagFlags.AllowWork_Doctor | PawnTagFlags.AllowWork_PatientBedRest |
                    PawnTagFlags.AllowWork_BasicWorker | PawnTagFlags.AllowWork_Warden |
                    PawnTagFlags.AllowWork_Handling | PawnTagFlags.AllowWork_Cooking |
                    PawnTagFlags.AllowWork_Hunting | PawnTagFlags.AllowWork_Construction |
                    PawnTagFlags.AllowWork_Growing | PawnTagFlags.AllowWork_Mining |
                    PawnTagFlags.AllowWork_PlantCutting | PawnTagFlags.AllowWork_Smithing |
                    PawnTagFlags.AllowWork_Tailoring | PawnTagFlags.AllowWork_Art |
                    PawnTagFlags.AllowWork_Crafting | PawnTagFlags.AllowWork_Hauling |
                    PawnTagFlags.AllowWork_Cleaning | PawnTagFlags.AllowWork_Research |
                    PawnTagFlags.AllowWork_Childcare | PawnTagFlags.AllowWork_DarkStudy
                );
                return allowWorkFlags != PawnTagFlags.None;
            }

            if (prefix == ManagedTags.BlockWorkPrefix)
            {
                PawnTagFlags flags = GetTagFlags(def);
                PawnTagFlags blockWorkFlags = flags & (
                    PawnTagFlags.BlockWork_Firefighter | PawnTagFlags.BlockWork_Patient |
                    PawnTagFlags.BlockWork_Doctor | PawnTagFlags.BlockWork_PatientBedRest |
                    PawnTagFlags.BlockWork_BasicWorker | PawnTagFlags.BlockWork_Warden |
                    PawnTagFlags.BlockWork_Handling | PawnTagFlags.BlockWork_Cooking |
                    PawnTagFlags.BlockWork_Hunting | PawnTagFlags.BlockWork_Construction |
                    PawnTagFlags.BlockWork_Growing | PawnTagFlags.BlockWork_Mining |
                    PawnTagFlags.BlockWork_PlantCutting | PawnTagFlags.BlockWork_Smithing |
                    PawnTagFlags.BlockWork_Tailoring | PawnTagFlags.BlockWork_Art |
                    PawnTagFlags.BlockWork_Crafting | PawnTagFlags.BlockWork_Hauling |
                    PawnTagFlags.BlockWork_Cleaning | PawnTagFlags.BlockWork_Research |
                    PawnTagFlags.BlockWork_Childcare | PawnTagFlags.BlockWork_DarkStudy
                );
                return blockWorkFlags != PawnTagFlags.None;
            }

            // String 기반 fallback
            if (!HasTagSet(def))
                return false;

            var tagSet = GetTags(def);
            foreach (string tag in tagSet)
            {
                if (tag?.StartsWith(prefix) == true)
                    return true;
            }

            return false;
        }

        #endregion

        #region New API (Flag-based) - 최적화된 버전

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasFlag(ThingDef def, PawnTagFlags flag)
        {
            return Utility_Common.RaceDefChecker(def) && (GetTagFlags(def) & flag) == flag;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasFlag(Pawn pawn, PawnTagFlags flag)
        {
            return Utility_Common.PawnChecker(pawn) && HasFlag(pawn.def, flag);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasAnyFlag(ThingDef def, PawnTagFlags flags)
        {
            return Utility_Common.RaceDefChecker(def) && (GetTagFlags(def) & flags) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasAnyFlag(Pawn pawn, PawnTagFlags flags)
        {
            return Utility_Common.PawnChecker(pawn) && HasAnyFlag(pawn.def, flags);
        }

        public static bool IsWorkEnabled(Pawn pawn, string workTypeName)
        {
            if (!Utility_Common.PawnChecker(pawn) || string.IsNullOrEmpty(workTypeName))
                return false;

            var flags = GetTagFlags(pawn.def);

            // AllowAllWork 체크 우선
            if ((flags & PawnTagFlags.AllowAllWork) != 0)
                return true;

            // 특정 allow flag 체크
            var allowFlag = GetAllowWorkFlag(workTypeName);
            return allowFlag != PawnTagFlags.None && (flags & allowFlag) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsWorkEnabled(Pawn pawn, WorkTypeDef workTypeDef)
        {
            return IsWorkEnabled(pawn, workTypeDef.ToStringSafe());
        }

        public static bool IsWorkDisabled(Pawn pawn, string workTypeName)
        {
            if (!Utility_Common.PawnChecker(pawn) || string.IsNullOrEmpty(workTypeName))
                return false;

            var flags = GetTagFlags(pawn.def);
            bool hasAllowAllWork = (flags & PawnTagFlags.AllowAllWork) != 0;
            bool hasBlockAllWork = (flags & PawnTagFlags.BlockAllWork) != 0;

            // BlockAllWork이 모든 것을 오버라이드
            if (!hasAllowAllWork && hasBlockAllWork)
                return true;

            // 특정 block/allow flag 체크
            var blockFlag = GetBlockWorkFlag(workTypeName);
            var allowFlag = GetAllowWorkFlag(workTypeName);

            return !hasBlockAllWork &&
                   blockFlag != PawnTagFlags.None &&
                   (flags & blockFlag) != 0 &&
                   (allowFlag == PawnTagFlags.None || (flags & allowFlag) == 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsWorkDisabled(Pawn pawn, WorkTypeDef workTypeDef)
        {
            return IsWorkDisabled(pawn, workTypeDef.ToStringSafe());
        }

        #endregion

        #region Cache Management - 최적화된 버전

        /// <summary>
        /// Race별 캐시 정리 - 최적화된 버전 (불필요한 Pawn 생성 제거)
        /// </summary>
        public static void ClearCacheForRace(ThingDef def)
        {
            if (!Utility_Common.RaceDefChecker(def))
                return;

            // 기본 캐시 정리
            Utility_CacheManager.TagFlags.TryRemove(def, out _);
            Utility_CacheManager.Tags.TryRemove(def, out _);
            Utility_CacheManager.IsAnimal.TryRemove(def, out _);
            Utility_CacheManager.IsHumanlike.TryRemove(def, out _);
            Utility_CacheManager.IsMechanoid.TryRemove(def, out _);

            // ValueTuple 캐시들을 효율적으로 정리
            ClearTupleCache(Utility_CacheManager.WorkEnabled, p => p.def == def);
            ClearTupleCache(Utility_CacheManager.WorkDisabled, p => p.def == def);
            ClearTupleCache(Utility_CacheManager.ForceDraftable, p => p.def == def);
            ClearTupleCache(Utility_CacheManager.ForceEquipWeapon, p => p.def == def);
            ClearTupleCache(Utility_CacheManager.ForceWearApparel, p => p.def == def);

            Utility_DebugManager.LogNormal($"Cache entries for race {def.defName} successfully cleared");
        }

        /// <summary>
        /// Pawn별 캐시 정리 - 최적화된 버전
        /// </summary>
        public static void ClearCacheForPawn(Pawn pawn)
        {
            if (!Utility_Common.RaceDefChecker(pawn.def))
                return;

            // Race level 캐시는 건드리지 않고 pawn-specific 캐시만 정리
            ClearTupleCache(Utility_CacheManager.WorkEnabled, p => p == pawn);
            ClearTupleCache(Utility_CacheManager.WorkDisabled, p => p == pawn);
            ClearTupleCache(Utility_CacheManager.ForceDraftable, p => p == pawn);
            ClearTupleCache(Utility_CacheManager.ForceEquipWeapon, p => p == pawn);
            ClearTupleCache(Utility_CacheManager.ForceWearApparel, p => p == pawn);

            Utility_DebugManager.LogNormal($"Cache entries for pawn {pawn.LabelShort} successfully cleared");
        }

        /// <summary>
        /// ValueTuple 캐시를 효율적으로 정리 - 성능 최적화
        /// </summary>
        private static void ClearTupleCache(ConcurrentDictionary<ValueTuple<Pawn, string>, bool> cache,
            Func<Pawn, bool> predicate)
        {
            var tempList = TempTupleList;
            tempList.Clear();

            // 제거할 키들을 수집
            foreach (var kvp in cache)
            {
                if (predicate(kvp.Key.Item1))
                    tempList.Add(kvp.Key);
            }

            // 수집된 키들을 제거
            for (int i = 0; i < tempList.Count; i++)
            {
                cache.TryRemove(tempList[i], out _);
            }

            tempList.Clear();
        }

        public static void ClearAllCaches()
        {
            Utility_CacheManager.TagFlags.Clear();
            Utility_CacheManager.Tags.Clear();
            Utility_CacheManager.WorkEnabled.Clear();
            Utility_CacheManager.WorkDisabled.Clear();
            Utility_CacheManager.ForceDraftable.Clear();
            Utility_CacheManager.ForceEquipWeapon.Clear();
            Utility_CacheManager.ForceWearApparel.Clear();
            Utility_CacheManager.IsAnimal.Clear();
            Utility_CacheManager.IsHumanlike.Clear();
            Utility_CacheManager.IsMechanoid.Clear();

            Utility_DebugManager.LogNormal("All tag caches cleared");
        }

        #endregion

        #region Helper Methods - 최적화된 버전

        /// <summary>
        /// 태그 문자열을 PawnTagFlag로 변환 - 레거시 호환성
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static PawnTagFlags ConvertTagToFlag(string tag)
        {
            return ConvertTagToFlagOptimized(tag);
        }

        /// <summary>
        /// 정적 캐시들을 정리합니다 (게임 종료시 호출)
        /// </summary>
        public static void CleanupStaticCaches()
        {
            _allowWorkFlagCache.Clear();
            _blockWorkFlagCache.Clear();

            // ThreadStatic 필드들은 자동으로 정리되지만 명시적으로 null 처리
            _tempTupleList = null;
        }

        #endregion

        #region Extension Methods - 성능 향상을 위한 확장

        /// <summary>
        /// ConcurrentDictionary의 GetValueOrDefault 확장 메서드
        /// (.NET Framework에서 지원하지 않는 경우 대비)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static TValue GetValueOrDefault<TKey, TValue>(this ConcurrentDictionary<TKey, TValue> dictionary,
            TKey key, TValue defaultValue)
        {
            return dictionary.TryGetValue(key, out var value) ? value : defaultValue;
        }

        #endregion
    }
}