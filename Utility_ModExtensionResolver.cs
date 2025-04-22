using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Centralized logic for resolving modExtension source (physical vs. virtual).
    /// Physical XML-defined modExtension always takes precedence.
    /// Also provides tag mutation helpers for both physical and virtual extensions.
    /// </summary>
    public static class Utility_ModExtensionResolver
    {
        // Use the singleton instance from VirtualTagStorageService.
        private static IVirtualTagStorage Virtual => VirtualTagStorageService.Instance;

        /// <summary>
        /// Returns true if the ThingDef has a physical (XML-defined) modExtension.
        /// </summary>
        public static bool HasPhysicalModExtension(ThingDef def)
        {
            var modExtension = Utility_NonHumanlikePawnControl.GetExtension(def);

            if (modExtension is NonHumanlikePawnControlExtension physicalModExtension)
            {
                return true;
            }

            return false;
        }

        public static bool HasVirtualModExtension(ThingDef def)
        {
            var modExtension = Utility_NonHumanlikePawnControl.GetExtension(def);

            if (modExtension is VirtualNonHumanlikePawnControlExtension virtualModExtension)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns effective tags for a ThingDef: physical if exists, otherwise virtual.
        /// </summary>
        public static List<string> GetEffectiveTags(ThingDef def)
        {
            List<string> rawTags;

            var modExtension = Utility_NonHumanlikePawnControl.GetExtension(def);

            if (modExtension is NonHumanlikePawnControlExtension physicalModExtension)
            {
                rawTags = physicalModExtension.tags;
            }
            else if (modExtension is VirtualNonHumanlikePawnControlExtension virtualModExtension)
            {
                rawTags = virtualModExtension.tags;
            }
            else
            {
                rawTags = new List<string>();
            }

            // Normalize and prioritize tags: Enum > Def > String
            List<string> resolvedTags = rawTags?
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(Utility_CacheManager.ResolveTagPriority)
                .Where(rt => !string.IsNullOrEmpty(rt))
                .Distinct()
                .ToList();

            return resolvedTags ?? new List<string>();
        }

        /// <summary>
        /// Removes all virtual tags for races that now have physical extensions.
        /// </summary>
        public static void DisableVirtualOverrides(List<ThingDef> allDefs)
        {
            if (allDefs == null) return;

            foreach (var def in allDefs)
            {
                if (def != null && HasPhysicalModExtension(def))
                {
                    List<string> tags = Virtual.Get(def);
                    if (tags != null)
                    {
                        foreach (var tag in tags.ToArray())
                        {
                            Virtual.Remove(def, tag);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Returns true if the ThingDef contains the given tag.
        /// </summary>
        public static bool HasTag(ThingDef def, string tag)
        {
            return GetEffectiveTags(def)?.Contains(tag) ?? false;
        }

        /// <summary>
        /// Adds a tag to the appropriate extension based on source priority.
        /// </summary>
        /// 
        public static void AddTag(ThingDef def, string tag)
        {
            if (def == null || string.IsNullOrWhiteSpace(tag))
            {
                return;
            }

            if (HasPhysicalModExtension(def))
            {
                Messages.Message("PawnControl_CannotEditPhysical".Translate(def.label), MessageTypeDefOf.RejectInput);
                return;
            }

            string resolved = Utility_CacheManager.ResolveTagPriority(tag);
            List<string> tags = GetVirtualTags(def);
            if (!tags.Contains(resolved))
                tags.Add(resolved);

            Utility_CacheManager.InvalidateTagCachesFor(def);
        }

        /// <summary>
        /// Removes a tag from the appropriate extension based on source priority.
        /// </summary>
        public static void RemoveTag(ThingDef def, string tag)
        {
            if (def == null || string.IsNullOrWhiteSpace(tag)) return;

            if (HasPhysicalModExtension(def))
            {
                Messages.Message("PawnControl_CannotEditPhysical".Translate(def.label), MessageTypeDefOf.RejectInput);
                return;
            }

            string resolved = Utility_CacheManager.ResolveTagPriority(tag);
            List<string> tags = GetVirtualTags(def);
            if (tags.Contains(resolved))
                tags.Remove(resolved);

            Utility_CacheManager.InvalidateTagCachesFor(def);
        }

        /// <summary>
        /// Sets tag state to true (add) or false (remove) depending on argument.
        /// </summary>
        public static void SetTagState(ThingDef def, string tag, bool enabled)
        {
            if (def == null || string.IsNullOrWhiteSpace(tag)) return;

            if (HasPhysicalModExtension(def))
            {
                Messages.Message("PawnControl_CannotEditPhysical".Translate(def.label), MessageTypeDefOf.RejectInput);
                return;
            }

            if (enabled)
                AddTag(def, tag);
            else
                RemoveTag(def, tag);
        }

        /// <summary>
        /// Gets the tag list from virtual storage, or creates a new one.
        /// Always returns a mutable reference.
        /// </summary>
        private static List<string> GetVirtualTags(ThingDef def)
        {
            var modExtension = VirtualTagStorage.Instance.GetOrCreate(def);
            return modExtension.tags ?? (modExtension.tags = new List<string>());
        }
    }
}
