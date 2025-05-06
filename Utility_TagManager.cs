using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
                if (Prefs.DevMode && modExtension.debugMode)
                {
                    Log.Error($"[PawnControl] {def.defName} is not forceDraftable, cannot force equip weapon.");
                }
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
                if (Prefs.DevMode && modExtension.debugMode)
                {
                    Log.Error($"[PawnControl] {def.defName} is not forceDraftable, cannot force wear apparel.");
                }
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
            if (set.Contains(ManagedTags.ForceTrainerTab)) set.Add(ManagedTags.ForceTrainerTab);
            if (set.Contains(ManagedTags.ForceWork)) set.Add(ManagedTags.ForceWork);

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
