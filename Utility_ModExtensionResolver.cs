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

        public static bool IsVirtual(ThingDef def)
        {
            return def.GetModExtension<NonHumanlikePawnControlExtension>() == null;
        }

        /// <summary>
        /// Returns true if the ThingDef has a physical (XML-defined) modExtension.
        /// </summary>
        public static bool HasPhysicalModExtension(ThingDef def)
        {
            var modExtension = def.GetModExtension<NonHumanlikePawnControlExtension>();
            return modExtension != null && !(modExtension is VirtualNonHumanlikePawnControlExtension);
        }

        public static bool HasVirtualModExtension(ThingDef def)
        {
            return def.GetModExtension<VirtualNonHumanlikePawnControlExtension>() != null;
        }

        /// <summary>
        /// Returns effective tags for a ThingDef: physical if exists, otherwise virtual.
        /// </summary>
        public static List<string> GetEffectiveTags(ThingDef def)
        {
            if (HasPhysicalModExtension(def))
                return def.GetModExtension<NonHumanlikePawnControlExtension>()?.tags ?? new List<string>();

            if (HasVirtualModExtension(def))
                return def.GetModExtension<VirtualNonHumanlikePawnControlExtension>()?.tags ?? new List<string>();

            return new List<string>();
        }

        /// <summary>
        /// Returns true if the ThingDef only uses virtual storage.
        /// </summary>
        public static bool IsVirtualOnly(ThingDef def)
        {
            return !HasPhysicalModExtension(def);
        }

        /// <summary>
        /// Returns true if the provided tag list is from virtual storage.
        /// </summary>
        public static bool IsVirtualSource(ThingDef def, List<string> tagList)
        {
            return IsVirtualOnly(def) && ReferenceEquals(tagList, Virtual?.Get(def));
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
        public static void AddTag(ThingDef def, string tag)
        {
            if (def == null || string.IsNullOrWhiteSpace(tag)) return;

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
