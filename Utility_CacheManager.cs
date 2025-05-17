using RimWorld;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    public static class Utility_CacheManager
    {
        #region Caches

        // ModExtension caching
        public static readonly Dictionary<ThingDef, NonHumanlikePawnControlExtension> _modExtensionCache = new Dictionary<ThingDef, NonHumanlikePawnControlExtension>();

        // Tag caching
        public static readonly Dictionary<ThingDef, HashSet<string>> Tags = new Dictionary<ThingDef, HashSet<string>>();
        public static readonly Dictionary<ThingDef, PawnTagFlags> TagFlags = new Dictionary<ThingDef, PawnTagFlags>();

        // Thread-safe animal caching
        public static readonly ConcurrentDictionary<ThingDef, bool> ForcedAnimals = new ConcurrentDictionary<ThingDef, bool>();

        // Colony pawns caching
        private static readonly Dictionary<Map, List<Pawn>> _colonistLikePawns = new Dictionary<Map, List<Pawn>>();
        public static readonly Dictionary<Map, int> FrameIndices = new Dictionary<Map, int>();

        // Duty caching
        private static readonly Dictionary<string, DutyDef> _dutyDefs = new Dictionary<string, DutyDef>();

        // Apparel restriction caching
        private static readonly Dictionary<Pawn, bool> _apparelRestrictions = new Dictionary<Pawn, bool>();

        // Work-related caches
        public static readonly Dictionary<ValueTuple<Pawn, string>, bool> WorkEnabled = new Dictionary<ValueTuple<Pawn, string>, bool>();
        public static readonly Dictionary<ValueTuple<Pawn, WorkTypeDef>, bool> WorkTypeEnabled = new Dictionary<ValueTuple<Pawn, WorkTypeDef>, bool>();
        public static readonly Dictionary<ValueTuple<Pawn, string>, bool> WorkDisabled = new Dictionary<ValueTuple<Pawn, string>, bool>();
        public static readonly Dictionary<ValueTuple<Pawn, string>, bool> ForceDraftable = new Dictionary<ValueTuple<Pawn, string>, bool>();
        public static readonly Dictionary<ValueTuple<Pawn, string>, bool> ForceEquipWeapon = new Dictionary<ValueTuple<Pawn, string>, bool>();
        public static readonly Dictionary<ValueTuple<Pawn, string>, bool> ForceWearApparel = new Dictionary<ValueTuple<Pawn, string>, bool>();

        // Work tag caching
        public static readonly Dictionary<Pawn, bool> AllowWorkTag = new Dictionary<Pawn, bool>();
        public static readonly Dictionary<Pawn, bool> BlockWorkTag = new Dictionary<Pawn, bool>();
        public static readonly Dictionary<Pawn, bool> CombinedWorkTag = new Dictionary<Pawn, bool>();

        // Race identity caches
        public static readonly Dictionary<ThingDef, bool> IsAnimal = new Dictionary<ThingDef, bool>();
        public static readonly Dictionary<ThingDef, bool> IsHumanlike = new Dictionary<ThingDef, bool>();
        public static readonly Dictionary<ThingDef, bool> IsMechanoid = new Dictionary<ThingDef, bool>();
        public static readonly Dictionary<FlagScopeTarget, bool> FlagOverrides = new Dictionary<FlagScopeTarget, bool>();

        // UI caching
        public static readonly Dictionary<int, bool> BioTabVisibility =  new Dictionary<int, bool>();

        #endregion

        #region ModExtension Caching

        /// <summary>
        /// Gets a cached ModExtension for a ThingDef or creates one if not present
        /// </summary>
        public static NonHumanlikePawnControlExtension GetModExtension(ThingDef def)
        {
            if (def == null)
                return null;

            // Check cache first
            if (_modExtensionCache.TryGetValue(def, out var cached))
                return cached;

            // Safely try to get the extension
            NonHumanlikePawnControlExtension modExtension = null;
            try
            {
                if (def.modExtensions != null)
                    modExtension = def.GetModExtension<NonHumanlikePawnControlExtension>();
            }
            catch (Exception ex)
            {
                if (Prefs.DevMode)
                    Log.Warning($"[PawnControl] GetModExtension failed for def {def.defName}: {ex.Message}");
            }

            // Cache for future use (even if null)
            _modExtensionCache[def] = modExtension;
            return modExtension;
        }

        /// <summary>
        /// Updates the cached ModExtension for a ThingDef
        /// </summary>
        public static void UpdateModExtension(ThingDef def, NonHumanlikePawnControlExtension extension)
        {
            if (def == null)
                return;

            _modExtensionCache[def] = extension;
        }

        /// <summary>
        /// Preloads all ModExtensions from the DefDatabase
        /// </summary>
        public static void PreloadModExtensions()
        {
            int loadedCount = 0;

            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def?.race == null)
                    continue;

                var modExtension = def.GetModExtension<NonHumanlikePawnControlExtension>();
                if (modExtension != null)
                {
                    _modExtensionCache[def] = modExtension;
                    modExtension.CacheSkillPassions();
                    loadedCount++;
                }
            }

            Utility_DebugManager.LogNormal($"Non Humanlike Pawn Controller refreshed: {loadedCount} extensions loaded.");
        }

        /// <summary>
        /// Preloads the ModExtension for a specific race
        /// </summary>
        public static void PreloadModExtensionForRace(ThingDef def)
        {
            if (def == null)
                return;

            // Look for our extension type in the mod extensions
            if (def.modExtensions != null)
            {
                foreach (var ext in def.modExtensions)
                {
                    if (ext is NonHumanlikePawnControlExtension controlExt)
                    {
                        _modExtensionCache[def] = controlExt;
                        return;
                    }
                }
            }

            // No extension found, ensure null is cached
            _modExtensionCache[def] = null;
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

            _modExtensionCache.Remove(def);

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

            // Clean all ThingDefs that have our extensions
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
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
            _modExtensionCache.Clear();
            Tags.Clear();
            ForcedAnimals.Clear();
            WorkEnabled.Clear();
            WorkTypeEnabled.Clear();
            WorkDisabled.Clear();
            ForceDraftable.Clear();
            ForceEquipWeapon.Clear();
            ForceWearApparel.Clear();
            IsAnimal.Clear();
            IsHumanlike.Clear();
            IsMechanoid.Clear();

            Log.Message($"[PawnControl] Removed {removedCount} runtime mod extensions when starting new game");
        }

        #endregion

        #region Duty & Colonial Pawn Caching

        /// <summary>
        /// Gets a cached DutyDef by name
        /// </summary>
        public static DutyDef GetDuty(string defName)
        {
            if (string.IsNullOrEmpty(defName))
                return null;

            if (!_dutyDefs.TryGetValue(defName, out var result))
            {
                result = DefDatabase<DutyDef>.GetNamedSilentFail(defName);
                _dutyDefs[defName] = result;
            }

            return result;
        }

        /// <summary>
        /// Returns a list of all colonist-like pawns in the given map and cache them
        /// </summary>
        public static IEnumerable<Pawn> GetColonistLikePawns(Map map)
        {
            if (map == null)
                return Enumerable.Empty<Pawn>();

            int frame = Time.frameCount;
            if (_colonistLikePawns.TryGetValue(map, out var cached) &&
                FrameIndices.TryGetValue(map, out var cachedFrame) &&
                frame - cachedFrame <= 5) // 5-frame TTL
            {
                return cached;
            }

            var result = map.mapPawns.AllPawns.Where(pawn =>
            {
                if (pawn == null || pawn.def == null || pawn.def.race == null)
                    return false;

                if (pawn.Faction != Faction.OfPlayer)
                    return false;

                // Require at least one valid work type to be enabled
                var modExtension = GetModExtension(pawn.def);

                // Fallback: vanilla logic
                if (modExtension == null)
                {
                    foreach (WorkTypeDef workType in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                    {
                        if (pawn.workSettings?.WorkIsActive(workType) == true)
                            return true;
                    }
                    return false;
                }

                // If extension exists, check for at least one tag-allowed work type
                foreach (var workTypeDef in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                {
                    if (IsWorkTypeEnabledForPawn(pawn, workTypeDef))
                        return true;
                }

                return false;
            }).ToList();

            _colonistLikePawns[map] = result;
            FrameIndices[map] = frame;
            return result;
        }

        /// <summary>
        /// Invalidates colonist-like pawns cache for a map
        /// </summary>
        public static void InvalidateColonistCache(Map map)
        {
            if (map == null)
                return;

            _colonistLikePawns.Remove(map);
            FrameIndices.Remove(map);
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

            // Only non-humanlike pawns with valid mod extensions AND allow tags can enable work
            var modExtension = GetModExtension(pawn.def);

            if (modExtension == null)
                return pawn.workSettings?.WorkIsActive(workType) == true; // Vanilla fallback

            if (modExtension.tags == null)
                return false;

            return Utility_TagManager.HasTag(pawn.def, ManagedTags.AllowAllWork) ||
                   Utility_TagManager.HasTag(pawn.def, ManagedTags.AllowWorkPrefix + workType.defName);
        }

        /// <summary>
        /// Checks if a pawn has apparel restrictions
        /// </summary>
        public static bool IsApparelRestricted(Pawn pawn)
        {
            if (pawn == null || pawn.def == null)
                return false;

            if (!_apparelRestrictions.TryGetValue(pawn, out var result))
            {
                var modExtension = GetModExtension(pawn.def);
                result = modExtension != null && modExtension.restrictApparelByBodyType;
                _apparelRestrictions[pawn] = result;
            }

            return result;
        }

        #endregion

        #region Reset Methods

        /// <summary>
        /// Clears all caches
        /// </summary>
        public static void Clear()
        {
            // Clear generic caches
            //foreach (var priorityLevel in _genericCaches.Keys)
            //    _genericCaches[priorityLevel].Clear();

            //_dependencies.Clear();
            //_keyToPriority.Clear();

            // Clear specialized caches
            _modExtensionCache.Clear();
            Tags.Clear();
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
            //JobCache.Clear();
            AllowWorkTag.Clear();
            BlockWorkTag.Clear();
            CombinedWorkTag.Clear();
            BioTabVisibility.Clear();
            TagFlags.Clear();

            if (Prefs.DevMode)
                Utility_DebugManager.LogNormal("All caches cleared.");
        }

        #endregion
    }
}
