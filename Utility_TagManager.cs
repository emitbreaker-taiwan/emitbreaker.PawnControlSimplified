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
            return tagSet.Contains(tag);
        }

        public static HashSet<string> GetTags(ThingDef def)
        {
            if (!Utility_CacheManager.tagCache.TryGetValue(def, out var set))
            {
                set = BuildTags(def);
                Utility_CacheManager.tagCache[def] = set;
            }
            return set;
        }

        private static HashSet<string> BuildTags(ThingDef def)
        {
            HashSet<string> set = new HashSet<string>();

            var modExtension = Utility_CacheManager.GetModExtension(def);

            if (modExtension?.tags == null && modExtension?.pawnTagDefs == null)
            {
                return set;
            }
                        
            List<string> effectiveTags = modExtension.tags;

            if (modExtension.pawnTagDefs != null)
            {
                foreach (var tagDef in modExtension.pawnTagDefs)
                {
                    if (tagDef != null)
                    {
                        effectiveTags.Add(tagDef.defName);
                    }
                }
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
    }
}
