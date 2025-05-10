using System;
using System.Collections.Generic;
using Verse;

namespace emitbreaker.PawnControl
{
    public static class Utility_TagManager
    {
        public static bool HasTagSet(ThingDef def, NonHumanlikePawnControlExtension modExtension = null)
        {
            if (!Utility_Common.RaceDefChecker(def))
            {
                return false;
            }

            modExtension = Utility_CacheManager.GetModExtension(def);

            if (modExtension == null)
            {
                return false;
            }

            return modExtension.tags != null && modExtension.tags.Count > 0;
        }

        public static bool HasTag(ThingDef def, string tag)
        {
            if (def == null || string.IsNullOrEmpty(tag))
            {
                return false; // Invalid input
            }

            if (!HasTagSet(def))
            {
                return false;
            }

            // Use GetTag to retrieve the cached or built tag set
            var tagSet = GetTags(def);

            // Check if the tag exists in the set
            bool hasTag = tagSet.Contains(tag);
            if (Prefs.DevMode)
            {
                Utility_DebugManager.TagManager_HasTag_HasTag(def, tag, hasTag);
            }
            return hasTag;
        }

        public static bool WorkDisabled(ThingDef def, string tag)
        {
            if (def == null || string.IsNullOrEmpty(tag))
            {
                return false; // Invalid input
            }

            var key = new ValueTuple<ThingDef, string>(def, "disabled_" + tag);

            // Try to get from cache first
            if (Utility_CacheManager._workDisabledCache.TryGetValue(key, out bool result))
            {
                return result;
            }

            // Compute result
            result = (!HasTag(def, ManagedTags.AllowAllWork) && HasTag(def, ManagedTags.BlockAllWork))
                    || (!HasTag(def, ManagedTags.BlockAllWork) &&
                        HasTag(def, (ManagedTags.BlockWorkPrefix + tag)) &&
                        !HasTag(def, (ManagedTags.AllowWorkPrefix + tag)));

            // Store in cache
            Utility_CacheManager._workDisabledCache[key] = result;

            return result;
        }

        public static bool WorkEnabled(ThingDef def, string tag)
        {
            if (def == null || string.IsNullOrEmpty(tag))
            {
                return false; // Invalid input
            }

            var key = new ValueTuple<ThingDef, string>(def, tag);

            // Try to get from cache first
            if (Utility_CacheManager._workEnabledCache.TryGetValue(key, out bool result))
            {
                return result;
            }

            // Compute result
            result = HasTag(def, ManagedTags.AllowAllWork) ||
                     HasTag(def, (ManagedTags.AllowWorkPrefix + tag));

            // Store in cache
            Utility_CacheManager._workEnabledCache[key] = result;

            return result;
        }
        
        public static bool ForceDraftable(ThingDef def, string tag = ManagedTags.ForceDraftable)
        {
            if (def == null)
            {
                return false; // Invalid input
            }

            var key = new ValueTuple<ThingDef, string>(def, tag);
            // Try to get from cache first
            if (Utility_CacheManager._forceDraftableCache.TryGetValue(key, out bool result))
            {
                return result;
            }

            var modExtension = Utility_CacheManager.GetModExtension(def);
            if (modExtension == null)
            {
                return false; // No mod extension found
            }

            // Only check forceDraftable field, not tags
            result = HasTag(def, tag) || modExtension.forceDraftable;

            // Store in cache
            Utility_CacheManager._forceDraftableCache[key] = result;

            return result;
        }
        
        public static bool ForceEquipWeapon(ThingDef def, string tag = ManagedTags.ForceEquipWeapon)
        {
            if (def == null)
            {
                return false; // Invalid input
            }

            var key = new ValueTuple<ThingDef, string>(def, tag);
            // Try to get from cache first
            if (Utility_CacheManager._forceEquipWeaponCache.TryGetValue(key, out bool result))
            {
                return result;
            }

            var modExtension = Utility_CacheManager.GetModExtension(def);
            if (modExtension == null)
            {
                return false; // No mod extension found
            }

            if (!ForceDraftable(def))
            {
                Utility_DebugManager.LogError($"{def.defName} is not forceDraftable, cannot force equip weapon.");
                return false;
            }

            // Only check forceDraftable field, not tags
            result = HasTag(def, tag) || modExtension.forceEquipWeapon;

            // Store in cache
            Utility_CacheManager._forceEquipWeaponCache[key] = result;

            return result;
        }

        public static bool ForceWearApparel(ThingDef def, string tag = ManagedTags.ForceWearApparel)
        {
            if (def == null)
            {
                return false; // Invalid input
            }

            var key = new ValueTuple<ThingDef, string>(def, tag);
            // Try to get from cache first
            if (Utility_CacheManager._forceWearApparelCache.TryGetValue(key, out bool result))
            {
                return result;
            }

            var modExtension = Utility_CacheManager.GetModExtension(def);
            if (modExtension == null)
            {
                return false; // No mod extension found
            }

            if (!ForceDraftable(def))
            {
                Utility_DebugManager.LogError($"{def.defName} is not forceDraftable, cannot force wear apparel.");
                return false;
            }

            // Only check forceDraftable field, not tags
            result = HasTag(def, tag) || modExtension.forceWearApparel;

            // Store in cache
            Utility_CacheManager._forceWearApparelCache[key] = result;

            return result;
        }

        public static HashSet<string> GetTags(ThingDef def)
        {
            if (def == null)
            {
                return new HashSet<string>();
            }

            if (!Utility_CacheManager._tagCache.TryGetValue(def, out var set))
            {
                set = BuildTags(def);
                Utility_CacheManager._tagCache[def] = set;
            }
            return set;
        }

        private static HashSet<string> BuildTags(ThingDef def)
        {
            HashSet<string> set = new HashSet<string>();

            var modExtension = Utility_CacheManager.GetModExtension(def);

            if (modExtension?.tags == null)
            {
                return set;
            }
                        
            List<string> effectiveTags = null;

            if (modExtension.tags != null)
            {
                effectiveTags = new List<string>(modExtension.tags);
            }

            if (effectiveTags == null || effectiveTags.Count == 0)
            {
                return set;
            }

            foreach (string tag in effectiveTags)
            {
                set.Add(tag);
            }

            // ✅ Ensure legacy force flags are still represented
            if (set.Contains(ManagedTags.ForceAnimal)) set.Add(ManagedTags.ForceAnimal);
            if (set.Contains(ManagedTags.ForceDraftable)) set.Add(ManagedTags.ForceDraftable);

            return set;
        }

        public static bool HasAnyTagWithPrefix(ThingDef def, string prefix)
        {
            if (string.IsNullOrEmpty(prefix) || def == null)
            {
                return false;
            }

            if (!HasTagSet(def))
            {
                return false;
            }

            var tagSet = GetTags(def);

            foreach (string t in tagSet)
            {
                if (t != null && t.StartsWith(prefix))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Clears all cache entries for a specific race without affecting other races.
        /// Use this instead of ResetCache() when working with a single race.
        /// </summary>
        public static void ClearCacheForRace(ThingDef def)
        {
            if (def == null) return;

            Utility_DebugManager.LogNormal($"Clearing cache entries for race: {def.defName}");

            // Clear race's tag cache
            Utility_CacheManager._tagCache.Remove(def);

            // Find and remove all tuple entries that reference this race
            var keysToRemove = new List<ValueTuple<ThingDef, string>>();

            // Clear work enabled cache for this race
            foreach (var key in Utility_CacheManager._workEnabledCache.Keys)
            {
                if (key.Item1 == def)
                    keysToRemove.Add(key);
            }
            foreach (var key in keysToRemove)
                Utility_CacheManager._workEnabledCache.Remove(key);

            // Reset and reuse list for work disabled cache
            keysToRemove.Clear();
            foreach (var key in Utility_CacheManager._workDisabledCache.Keys)
            {
                if (key.Item1 == def)
                    keysToRemove.Add(key);
            }
            foreach (var key in keysToRemove)
                Utility_CacheManager._workDisabledCache.Remove(key);

            // Reset and reuse list for force draftable cache
            keysToRemove.Clear();
            foreach (var key in Utility_CacheManager._forceDraftableCache.Keys)
            {
                if (key.Item1 == def)
                    keysToRemove.Add(key);
            }
            foreach (var key in keysToRemove)
                Utility_CacheManager._forceDraftableCache.Remove(key);

            // Reset and reuse list for force equip weapon cache
            keysToRemove.Clear();
            foreach (var key in Utility_CacheManager._forceEquipWeaponCache.Keys)
            {
                if (key.Item1 == def)
                    keysToRemove.Add(key);
            }
            foreach (var key in keysToRemove)
                Utility_CacheManager._forceEquipWeaponCache.Remove(key);

            // Reset and reuse list for force wear apparel cache
            keysToRemove.Clear();
            foreach (var key in Utility_CacheManager._forceWearApparelCache.Keys)
            {
                if (key.Item1 == def)
                    keysToRemove.Add(key);
            }
            foreach (var key in keysToRemove)
                Utility_CacheManager._forceWearApparelCache.Remove(key);

            // Also clear related caches in other managers if needed
            Utility_CacheManager._isAnimalCache.Remove(def);
            Utility_CacheManager._isHumanlikeCache.Remove(def);
            Utility_CacheManager._isMechanoidCache.Remove(def);

            Utility_DebugManager.LogNormal($"Cache entries for race {def.defName} successfully cleared");
        }

        // Add a method to clear both caches
        public static void ResetCache()
        {
            Utility_CacheManager._workEnabledCache.Clear();
            Utility_CacheManager._workDisabledCache.Clear(); 
            Utility_CacheManager._forceDraftableCache.Clear();
            Utility_CacheManager._forceEquipWeaponCache.Clear();
            Utility_CacheManager._forceWearApparelCache.Clear();
        }
    }
}
