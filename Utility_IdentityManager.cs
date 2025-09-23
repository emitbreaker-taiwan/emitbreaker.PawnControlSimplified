using RimWorld;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Verse;

namespace emitbreaker.PawnControl
{
    public static class Utility_IdentityManager
    {
        public static bool identityFlagsPreloaded = false;
        public static bool IsIdentityFlagsPreloaded => identityFlagsPreloaded;

        #region Optimized Helper Methods

        /// <summary>
        /// ConcurrentDictionary의 true 값 개수를 효율적으로 계산
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CountTrueValues<TKey>(ConcurrentDictionary<TKey, bool> dict)
        {
            int count = 0;
            foreach (var kvp in dict)
            {
                if (kvp.Value) count++;
            }
            return count;
        }

        /// <summary>
        /// 태그 prefix 체크 - 최적화된 버전 (LINQ 제거)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool AnyTagStartsWith(ThingDef def, string prefix)
        {
            // Flag 기반 체크 우선 - 성능 향상
            if (prefix == ManagedTags.AllowWorkPrefix)
                return Utility_TagManager.HasAnyTagWithPrefix(def, prefix);

            if (prefix == ManagedTags.BlockWorkPrefix)
                return Utility_TagManager.HasAnyTagWithPrefix(def, prefix);

            // String 기반 fallback - LINQ 제거
            var tags = Utility_TagManager.GetTags(def);
            if (tags.Count == 0) return false;

            foreach (string tag in tags)
            {
                if (tag?.StartsWith(prefix) == true)
                    return true;
            }
            return false;
        }

        #endregion

        #region Main Cache Building

        public static void BuildIdentityFlagCache(bool reload = false)
        {
            int animalCount = 0;
            int humanlikeCount = 0;
            int mechanoidCount = 0;
            int animalCountNew = 0;
            int humanlikeCountNew = 0;
            int mechanoidCountNew = 0;

            if (reload)
            {
                animalCount = CountTrueValues(Utility_CacheManager.IsAnimal);
                humanlikeCount = CountTrueValues(Utility_CacheManager.IsHumanlike);
                mechanoidCount = CountTrueValues(Utility_CacheManager.IsMechanoid);
            }

            // Clear caches
            Utility_CacheManager.IsAnimal.Clear();
            Utility_CacheManager.IsHumanlike.Clear();
            Utility_CacheManager.IsMechanoid.Clear();

            var defs = DefDatabase<ThingDef>.AllDefsListForReading;

            // 직접 for 루프로 성능 향상
            for (int i = 0; i < defs.Count; i++)
            {
                var def = defs[i];
                if (def?.race == null)
                    continue;

                var modExtension = Utility_CacheManager.GetModExtension(def);
                if (modExtension == null)
                    continue;

                // 조건 체크를 한 번만 수행하고 결과를 캐시에 저장
                bool forceAnimal = ForcedAnimalCondition(def, modExtension);
                bool forceHumanlike = ForcedHumanlikeCondition(def, modExtension);
                bool forceMechanoid = ForcedMechanoidCondition(def, modExtension);

                // ConcurrentDictionary에 안전하게 추가
                Utility_CacheManager.IsAnimal.TryAdd(def, forceAnimal);
                Utility_CacheManager.IsHumanlike.TryAdd(def, forceHumanlike);
                Utility_CacheManager.IsMechanoid.TryAdd(def, forceMechanoid);
            }

            identityFlagsPreloaded = true;

            if (reload)
            {
                animalCountNew = CountTrueValues(Utility_CacheManager.IsAnimal);
                humanlikeCountNew = CountTrueValues(Utility_CacheManager.IsHumanlike);
                mechanoidCountNew = CountTrueValues(Utility_CacheManager.IsMechanoid);

                if ((animalCount != animalCountNew) || (humanlikeCount != humanlikeCountNew) || (mechanoidCount != mechanoidCountNew))
                {
                    Utility_DebugManager.LogNormal($"Identity flag summary: Animal={Utility_CacheManager.IsAnimal.Count}, Humanlike={Utility_CacheManager.IsHumanlike.Count}, Mechanoid={Utility_CacheManager.IsMechanoid.Count}");
                }
            }
        }

        #endregion

        #region Quick Check Methods - Harmony Patches 호환

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsForcedAnimal(ThingDef def) =>
            def != null && Utility_CacheManager.IsAnimal.TryGetValue(def, out var result) && result;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsForcedHumanlike(ThingDef def) =>
            def != null && Utility_CacheManager.IsHumanlike.TryGetValue(def, out var result) && result;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsForcedMechanoid(ThingDef def) =>
            def != null && Utility_CacheManager.IsMechanoid.TryGetValue(def, out var result) && result;

        #endregion

        #region Condition Checkers - 최적화된 버전

        /// <summary>
        /// Animal 강제 조건 체크 - Flag 기반 최적화
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ForcedAnimalCondition(ThingDef def, NonHumanlikePawnControlExtension modExtension)
        {
            if (modExtension.forceIdentity == ForcedIdentityType.ForceAnimal)
                return true;

            // Flag 기반 체크로 성능 향상
            return Utility_TagManager.HasFlag(def, PawnTagFlags.ForceAnimal);
        }

        /// <summary>
        /// Humanlike 강제 조건 체크 - Flag 기반 최적화
        /// </summary>
        private static bool ForcedHumanlikeCondition(ThingDef def, NonHumanlikePawnControlExtension modExtension)
        {
            if (modExtension.forceDraftable)
                return true;

            // Flag 기반 체크로 한 번에 처리 - 성능 대폭 향상
            var flags = Utility_TagManager.GetTagFlags(def);

            // 관련 플래그들을 한 번에 체크
            var relevantFlags = PawnTagFlags.ForceDraftable |
                               PawnTagFlags.AllowAllWork |
                               PawnTagFlags.BlockAllWork;

            if ((flags & relevantFlags) != PawnTagFlags.None)
                return true;

            // Work-related flags 체크
            var workFlags = flags & (
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
                PawnTagFlags.AllowWork_Childcare | PawnTagFlags.AllowWork_DarkStudy |
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

            return workFlags != PawnTagFlags.None;
        }

        /// <summary>
        /// Mechanoid 강제 조건 체크 - Flag 기반 최적화
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ForcedMechanoidCondition(ThingDef def, NonHumanlikePawnControlExtension modExtension)
        {
            if (modExtension.forceIdentity == ForcedIdentityType.ForceMechanoid)
                return true;

            // Flag 기반 체크로 성능 향상
            return Utility_TagManager.HasFlag(def, PawnTagFlags.ForceMechanoid);
        }

        #endregion

        #region Identity Checks - 캐시 활용

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAnimal(ThingDef def)
        {
            if (def?.race == null)
                return false;

            return Utility_CacheManager.IsAnimal.TryGetValue(def, out bool value) ? value : def.race.Animal;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsHumanlike(ThingDef def)
        {
            if (def?.race == null)
                return false;

            return Utility_CacheManager.IsHumanlike.TryGetValue(def, out bool value) ? value : def.race.Humanlike;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsMechanoid(ThingDef def)
        {
            if (def?.race == null)
                return false;

            return Utility_CacheManager.IsMechanoid.TryGetValue(def, out bool value) ? value : def.race.IsMechanoid;
        }

        #endregion

        #region Dynamic Flag Management

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetFlagOverride(FlagScopeTarget flag, bool value)
        {
            Utility_CacheManager.FlagOverrides.AddOrUpdate(flag, value, (key, oldValue) => value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsFlagOverridden(FlagScopeTarget flag)
        {
            return Utility_CacheManager.FlagOverrides.TryGetValue(flag, out bool active) && active;
        }

        public static bool MatchesIdentityFlags(Pawn pawn, PawnIdentityFlags flags)
        {
            if (pawn?.def == null) return false;

            bool match = false;

            // 비트 연산으로 한 번에 체크하여 성능 향상
            if ((flags & PawnIdentityFlags.IsColonist) != 0) match |= IsColonist(pawn);
            if ((flags & PawnIdentityFlags.IsPrisoner) != 0) match |= IsPrisoner(pawn);
            if ((flags & PawnIdentityFlags.IsPrisonerOfColony) != 0) match |= IsPrisonerOfColony(pawn);
            if ((flags & PawnIdentityFlags.IsSlave) != 0) match |= IsSlave(pawn);
            if ((flags & PawnIdentityFlags.IsSlaveOfColony) != 0) match |= IsSlaveOfColony(pawn);
            if ((flags & PawnIdentityFlags.IsGuest) != 0) match |= IsGuest(pawn);

            // 캐시된 identity traits
            if ((flags & PawnIdentityFlags.IsHumanlike) != 0) match |= IsHumanlike(pawn.def);
            if ((flags & PawnIdentityFlags.IsAnimal) != 0) match |= IsAnimal(pawn.def);
            if ((flags & PawnIdentityFlags.IsMechanoid) != 0) match |= IsMechanoid(pawn.def);

            // Dynamic traits
            if ((flags & PawnIdentityFlags.IsWildMan) != 0) match |= pawn.IsWildMan();
            if ((flags & PawnIdentityFlags.IsQuestLodger) != 0) match |= pawn.IsQuestLodger();
            if ((flags & PawnIdentityFlags.IsMutant) != 0) match |= pawn.IsMutant;

            return match;
        }

        public static PawnIdentityFlags GetEffectiveIdentityFlags(Pawn pawn)
        {
            if (pawn?.def == null || pawn.RaceProps == null)
                return PawnIdentityFlags.None;

            PawnIdentityFlags result = PawnIdentityFlags.None;

            // 조건부 OR 연산으로 플래그 설정
            if (IsColonist(pawn)) result |= PawnIdentityFlags.IsColonist;
            if (IsPrisoner(pawn)) result |= PawnIdentityFlags.IsPrisoner;
            if (IsPrisonerOfColony(pawn)) result |= PawnIdentityFlags.IsPrisonerOfColony;
            if (IsSlave(pawn)) result |= PawnIdentityFlags.IsSlave;
            if (IsSlaveOfColony(pawn)) result |= PawnIdentityFlags.IsSlaveOfColony;
            if (IsGuest(pawn)) result |= PawnIdentityFlags.IsGuest;

            // 캐시된 identity 평가 사용
            if (IsHumanlike(pawn.def)) result |= PawnIdentityFlags.IsHumanlike;
            if (IsAnimal(pawn.def)) result |= PawnIdentityFlags.IsAnimal;
            if (IsMechanoid(pawn.def)) result |= PawnIdentityFlags.IsMechanoid;

            if (pawn.IsWildMan()) result |= PawnIdentityFlags.IsWildMan;
            if (pawn.IsQuestLodger()) result |= PawnIdentityFlags.IsQuestLodger;
            if (pawn.IsMutant) result |= PawnIdentityFlags.IsMutant;

            return result;
        }

        #endregion

        #region Scoped Identity Checks - 최적화된 버전

        /// <summary>
        /// 공통 작업 태그 체크 로직 - 중복 코드 제거
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool HasWorkTags(ThingDef def)
        {
            // Flag 기반 체크로 성능 향상
            var flags = Utility_TagManager.GetTagFlags(def);

            var workRelatedFlags = PawnTagFlags.AllowAllWork | PawnTagFlags.BlockAllWork;
            if ((flags & workRelatedFlags) != PawnTagFlags.None)
                return true;

            // Allow/Block work flags 체크
            var allowWorkFlags = flags & (
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

            var blockWorkFlags = flags & (
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

            return (allowWorkFlags | blockWorkFlags) != PawnTagFlags.None;
        }

        public static bool IsColonist(Pawn pawn)
        {
            if (pawn?.def == null || pawn.Faction != Faction.OfPlayer)
                return false;

            if (IsFlagOverridden(FlagScopeTarget.IsColonist))
            {
                var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                if (modExtension != null && HasWorkTags(pawn.def))
                    return true;
            }

            return pawn.RaceProps?.Humanlike == true && pawn.Faction == Faction.OfPlayer;
        }

        public static bool IsPrisoner(Pawn pawn)
        {
            if (pawn?.def == null || pawn.Faction != Faction.OfPlayer)
                return false;

            if (IsFlagOverridden(FlagScopeTarget.IsPrisoner))
            {
                var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                if (modExtension != null && HasWorkTags(pawn.def))
                    return true;
            }

            return pawn.IsPrisoner;
        }

        public static bool IsSlave(Pawn pawn)
        {
            if (pawn?.def == null || pawn.Faction != Faction.OfPlayer)
                return false;

            if (IsFlagOverridden(FlagScopeTarget.IsSlave))
            {
                var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                if (modExtension != null && HasWorkTags(pawn.def))
                    return true;
            }

            return pawn.IsSlave;
        }

        public static bool IsGuest(Pawn pawn)
        {
            if (pawn?.def == null || pawn.HostFaction != Faction.OfPlayer)
                return false;

            if (IsFlagOverridden(FlagScopeTarget.IsGuest))
            {
                var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                if (modExtension != null && HasWorkTags(pawn.def))
                    return true;
            }

            return pawn.HostFaction != null && !pawn.IsPrisoner && !pawn.IsSlave;
        }

        public static bool IsPrisonerOfColony(Pawn pawn)
        {
            if (pawn?.def == null || pawn.HostFaction != Faction.OfPlayer)
                return false;

            if (IsFlagOverridden(FlagScopeTarget.IsPrisonerOfColony))
            {
                var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                if (modExtension != null && HasWorkTags(pawn.def))
                    return true;
            }

            return pawn.IsPrisonerOfColony;
        }

        public static bool IsSlaveOfColony(Pawn pawn)
        {
            if (pawn?.def == null || pawn.HostFaction != Faction.OfPlayer)
                return false;

            if (IsFlagOverridden(FlagScopeTarget.IsSlaveOfColony))
            {
                var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                if (modExtension != null && HasWorkTags(pawn.def))
                    return true;
            }

            return pawn.IsSlaveOfColony;
        }

        #endregion
    }
}