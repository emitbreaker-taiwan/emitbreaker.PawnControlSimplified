using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    public static class Utility_IdentityManager
    {
        public static bool identityFlagsPreloaded = false;
        public static bool IsIdentityFlagsPreloaded => identityFlagsPreloaded;

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
                animalCount = Utility_CacheManager._isAnimalCache.Count(x => x.Value);
                humanlikeCount = Utility_CacheManager._isHumanlikeCache.Count(x => x.Value);
                mechanoidCount = Utility_CacheManager._isMechanoidCache.Count(x => x.Value);
            }

            Utility_CacheManager._isAnimalCache.Clear();
            Utility_CacheManager._isHumanlikeCache.Clear();
            Utility_CacheManager._isMechanoidCache.Clear();

            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def?.race == null)
                {
                    continue;
                }

                var modExtension = Utility_CacheManager.GetModExtension(def);
                if (modExtension == null)
                {                    
                    continue;
                }

                bool forceAnimal = ForcedAnimalCondition(def, modExtension);
                bool forceHumanlike = ForcedHumanlikeCondition(def, modExtension);
                bool forceMechanoid = ForcedMechanoidCondition(def, modExtension);
                Utility_CacheManager._isAnimalCache[def] = forceAnimal;
                Utility_CacheManager._isHumanlikeCache[def] = forceHumanlike;
                Utility_CacheManager._isMechanoidCache[def] = forceMechanoid;
            }

            identityFlagsPreloaded = true;

            if (reload)
            {
                animalCountNew = Utility_CacheManager._isAnimalCache.Count(x => x.Value);
                humanlikeCountNew = Utility_CacheManager._isHumanlikeCache.Count(x => x.Value);
                mechanoidCountNew = Utility_CacheManager._isMechanoidCache.Count(x => x.Value);

                if ((animalCount != animalCountNew) || (humanlikeCount != humanlikeCountNew) || (mechanoidCount != mechanoidCountNew))
                {
                    Utility_DebugManager.LogNormal($"Identity flag summary: Animal={Utility_CacheManager._isAnimalCache.Count}, Humanlike={Utility_CacheManager._isHumanlikeCache.Count}, Mechanoid={Utility_CacheManager._isMechanoidCache.Count}");
                }
            }
        }
        
        // For Harmony Patch in early loading stage where Mod Extensions are not fully loaded yet.
        public static bool IsForcedAnimal(ThingDef def) => def != null && Utility_CacheManager._isAnimalCache.TryGetValue(def, out var result) && result;
        public static bool IsForcedHumanlike(ThingDef def) => def != null && Utility_CacheManager._isHumanlikeCache.TryGetValue(def, out var result) && result;
        public static bool IsForcedMechanoid(ThingDef def) => def != null && Utility_CacheManager._isMechanoidCache.TryGetValue(def, out var result) && result;

        // Check whether does pawn forcefully converted to other type or not.
        private static bool ForcedAnimalCondition(ThingDef def, NonHumanlikePawnControlExtension modExtension)
        {
            bool result = false;

            if (modExtension.forceIdentity == ForcedIdentityType.ForceAnimal || Utility_TagManager.HasTag(def, ManagedTags.ForceAnimal))
            {
                result = true;
            }

            return result;
        }

        private static bool ForcedHumanlikeCondition(ThingDef def, NonHumanlikePawnControlExtension modExtension)
        {
            bool result = false;

            if (modExtension.forceDraftable
                       || Utility_TagManager.HasTag(def, ManagedTags.ForceDraftable)
                       || Utility_TagManager.HasTag(def, ManagedTags.AllowAllWork)
                       || Utility_TagManager.GetTags(def).Any(t => t != null && t.StartsWith(ManagedTags.AllowWorkPrefix))
                       || Utility_TagManager.HasTag(def, ManagedTags.BlockAllWork)
                       || Utility_TagManager.GetTags(def).Any(tag => tag != null && tag.StartsWith(ManagedTags.BlockWorkPrefix)))
            {
                result = true;
            }

            return result;
        }

        private static bool ForcedMechanoidCondition(ThingDef def, NonHumanlikePawnControlExtension modExtension)
        {
            bool result = false;

            if (modExtension.forceIdentity == ForcedIdentityType.ForceMechanoid || Utility_TagManager.HasTag(def, ManagedTags.ForceMechanoid))
            {
                result = true;
            }

            return result;
        }

        // For Caching
        public static bool IsAnimal(ThingDef def)
        {
            if (def == null || def.race == null)
            {
                return false;
            }

            if (Utility_CacheManager._isAnimalCache.TryGetValue(def, out bool value))
            {
                return value;
            }

            return def.race.Animal;
        }
        
        public static bool IsHumanlike(ThingDef def)
        {
            if (def == null || def.race == null)
            {
                return false;
            }

            if (Utility_CacheManager._isHumanlikeCache.TryGetValue(def, out bool value))
            {
                return value;
            }

            return def.race.Humanlike;
        }

        public static bool IsMechanoid(ThingDef def)
        {
            if (def == null || def.race == null)
            {
                return false;
            }

            if (Utility_CacheManager._isMechanoidCache.TryGetValue(def, out bool value))
            {
                return value;
            }

            return def.race.IsMechanoid;
        }

        // Dynamic Flag Management
        public static void SetFlagOverride(FlagScopeTarget flag, bool value)
        {
            Utility_CacheManager._flagOverrides[flag] = value;
        }

        public static bool IsFlagOverridden(FlagScopeTarget flag)
        {
            return Utility_CacheManager._flagOverrides.TryGetValue(flag, out bool active) && active;
        }

        public static bool MatchesIdentityFlags(Pawn pawn, PawnIdentityFlags flags)
        {
            if (pawn == null || pawn.def == null) return false;

            bool match = false;

            if (flags.HasFlag(PawnIdentityFlags.IsColonist)) match |= IsColonist(pawn);
            if (flags.HasFlag(PawnIdentityFlags.IsPrisoner)) match |= IsPrisoner(pawn);
            if (flags.HasFlag(PawnIdentityFlags.IsPrisonerOfColony)) match |= IsPrisonerOfColony(pawn);
            if (flags.HasFlag(PawnIdentityFlags.IsSlave)) match |= IsSlave(pawn);
            if (flags.HasFlag(PawnIdentityFlags.IsSlaveOfColony)) match |= IsSlaveOfColony(pawn);
            if (flags.HasFlag(PawnIdentityFlags.IsGuest)) match |= IsGuest(pawn);

            // Cached identity traits
            if (flags.HasFlag(PawnIdentityFlags.IsHumanlike)) match |= IsHumanlike(pawn.def);
            if (flags.HasFlag(PawnIdentityFlags.IsAnimal)) match |= IsAnimal(pawn.def);
            if (flags.HasFlag(PawnIdentityFlags.IsMechanoid)) match |= IsMechanoid(pawn.def);

            // Dynamic traits
            if (flags.HasFlag(PawnIdentityFlags.IsWildMan)) match |= pawn.IsWildMan();
            if (flags.HasFlag(PawnIdentityFlags.IsQuestLodger)) match |= pawn.IsQuestLodger();
            if (flags.HasFlag(PawnIdentityFlags.IsMutant)) match |= pawn.IsMutant;

            return match;
        }

        public static PawnIdentityFlags GetEffectiveIdentityFlags(Pawn pawn)
        {
            if (pawn == null || pawn.def == null || pawn.RaceProps == null)
                return PawnIdentityFlags.None;

            PawnIdentityFlags result = PawnIdentityFlags.None;

            if (IsColonist(pawn)) result |= PawnIdentityFlags.IsColonist;
            if (IsPrisoner(pawn)) result |= PawnIdentityFlags.IsPrisoner;
            if (IsPrisonerOfColony(pawn)) result |= PawnIdentityFlags.IsPrisonerOfColony;
            if (IsSlave(pawn)) result |= PawnIdentityFlags.IsSlave;
            if (IsSlaveOfColony(pawn)) result |= PawnIdentityFlags.IsSlaveOfColony;
            if (IsGuest(pawn)) result |= PawnIdentityFlags.IsGuest;

            // Use cached identity evaluation
            if (IsHumanlike(pawn.def)) result |= PawnIdentityFlags.IsHumanlike;
            if (IsAnimal(pawn.def)) result |= PawnIdentityFlags.IsAnimal;
            if (IsMechanoid(pawn.def)) result |= PawnIdentityFlags.IsMechanoid;

            if (pawn.IsWildMan()) result |= PawnIdentityFlags.IsWildMan;
            if (pawn.IsQuestLodger()) result |= PawnIdentityFlags.IsQuestLodger;
            if (pawn.IsMutant) result |= PawnIdentityFlags.IsMutant;

            return result;
        }

        // === Scoped Identity Checks ===
        public static bool IsColonist(Pawn pawn)
        {
            if (pawn?.def == null || pawn.Faction != Faction.OfPlayer)
            {
                return false;
            }

            if (IsFlagOverridden(FlagScopeTarget.IsColonist))
            {
                var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                if (modExtension != null && (
                        Utility_TagManager.HasTag(pawn.def, ManagedTags.AllowAllWork) ||
                        Utility_TagManager.GetTags(pawn.def).Any(tag => tag != null && tag.StartsWith(ManagedTags.AllowWorkPrefix)) ||
                        Utility_TagManager.HasTag(pawn.def, ManagedTags.BlockAllWork) ||
                        Utility_TagManager.GetTags(pawn.def).Any(tag => tag != null && tag.StartsWith(ManagedTags.BlockWorkPrefix))
                    ))
                {
                    return true;
                }
            }

            return pawn.RaceProps?.Humanlike == true && pawn.Faction == Faction.OfPlayer;
        }

        public static bool IsPrisoner(Pawn pawn)
        {
            if (pawn?.def == null || pawn.Faction != Faction.OfPlayer)
            {
                return false;
            }

            if (IsFlagOverridden(FlagScopeTarget.IsPrisoner))
            {
                var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                if (modExtension != null && (
                        Utility_TagManager.HasTag(pawn.def, ManagedTags.AllowAllWork) ||
                        Utility_TagManager.GetTags(pawn.def).Any(tag => tag != null && tag.StartsWith(ManagedTags.AllowWorkPrefix)) ||
                        Utility_TagManager.HasTag(pawn.def, ManagedTags.BlockAllWork) ||
                        Utility_TagManager.GetTags(pawn.def).Any(tag => tag != null && tag.StartsWith(ManagedTags.BlockWorkPrefix))
                    ))
                {
                    return true;
                }
            }

            return pawn.IsPrisoner;
        }

        public static bool IsSlave(Pawn pawn)
        {
            if (pawn?.def == null || pawn.Faction != Faction.OfPlayer)
            {
                return false;
            }

            if (IsFlagOverridden(FlagScopeTarget.IsSlave))
            {
                var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                if (modExtension != null && (
                        Utility_TagManager.HasTag(pawn.def, ManagedTags.AllowAllWork) ||
                        Utility_TagManager.GetTags(pawn.def).Any(tag => tag != null && tag.StartsWith(ManagedTags.AllowWorkPrefix)) ||
                        Utility_TagManager.HasTag(pawn.def, ManagedTags.BlockAllWork) ||
                        Utility_TagManager.GetTags(pawn.def).Any(tag => tag != null && tag.StartsWith(ManagedTags.BlockWorkPrefix))
                    ))
                {
                    return true;
                }
            }

            return pawn.IsSlave;
        }

        public static bool IsGuest(Pawn pawn)
        {
            if (pawn?.def == null || pawn.HostFaction != Faction.OfPlayer)
            {
                return false;
            }

            if (IsFlagOverridden(FlagScopeTarget.IsGuest))
            {
                var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                if (modExtension != null && (
                        Utility_TagManager.HasTag(pawn.def, ManagedTags.AllowAllWork) ||
                        Utility_TagManager.GetTags(pawn.def).Any(tag => tag != null && tag.StartsWith(ManagedTags.AllowWorkPrefix)) ||
                        Utility_TagManager.HasTag(pawn.def, ManagedTags.BlockAllWork) ||
                        Utility_TagManager.GetTags(pawn.def).Any(tag => tag != null && tag.StartsWith(ManagedTags.BlockWorkPrefix))
                    ))
                {
                    return true;
                }
            }

            return pawn.HostFaction != null && !pawn.IsPrisoner && !pawn.IsSlave;
        }

        public static bool IsPrisonerOfColony(Pawn pawn)
        {
            if (pawn?.def == null || pawn.HostFaction != Faction.OfPlayer)
            {
                return false;
            }

            if (IsFlagOverridden(FlagScopeTarget.IsPrisonerOfColony))
            {
                var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                if (modExtension != null && (
                        Utility_TagManager.HasTag(pawn.def, ManagedTags.AllowAllWork) ||
                        Utility_TagManager.GetTags(pawn.def).Any(tag => tag != null && tag.StartsWith(ManagedTags.AllowWorkPrefix)) ||
                        Utility_TagManager.HasTag(pawn.def, ManagedTags.BlockAllWork) ||
                        Utility_TagManager.GetTags(pawn.def).Any(tag => tag != null && tag.StartsWith(ManagedTags.BlockWorkPrefix))
                    ))
                {
                    return true;
                }
            }

            return pawn.IsPrisonerOfColony;
        }

        public static bool IsSlaveOfColony(Pawn pawn)
        {
            if (pawn?.def == null || pawn.HostFaction != Faction.OfPlayer)
            {
                return false;
            }

            if (IsFlagOverridden(FlagScopeTarget.IsSlaveOfColony))
            {
                var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                if (modExtension != null && (
                        Utility_TagManager.HasTag(pawn.def, ManagedTags.AllowAllWork) ||
                        Utility_TagManager.GetTags(pawn.def).Any(tag => tag != null && tag.StartsWith(ManagedTags.AllowWorkPrefix)) ||
                        Utility_TagManager.HasTag(pawn.def, ManagedTags.BlockAllWork) ||
                        Utility_TagManager.GetTags(pawn.def).Any(tag => tag != null && tag.StartsWith(ManagedTags.BlockWorkPrefix))
                    ))
                {
                    return true;
                }
            }

            return pawn.IsSlaveOfColony;
        }
    }
}
