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
        public static readonly Dictionary<ThingDef, NonHumanlikePawnControlExtension> _modExtensionCache = new Dictionary<ThingDef, NonHumanlikePawnControlExtension>();
        public static readonly Dictionary<ThingDef, HashSet<string>> _tagCache = new Dictionary<ThingDef, HashSet<string>>();

        public static readonly ConcurrentDictionary<ThingDef, bool> _forcedAnimalCache = new ConcurrentDictionary<ThingDef, bool>();

        // Caching ForceColonist Pawns
        private static readonly Dictionary<Map, List<Pawn>> _colonistLikePawnCache = new Dictionary<Map, List<Pawn>>();
        public static readonly Dictionary<Map, int> _frameIndexCache = new Dictionary<Map, int>();

        private static readonly Dictionary<string, DutyDef> _dutyCache = new Dictionary<string, DutyDef>();
        // Cache to store the tag-check result per ThingDef
        private static readonly Dictionary<ThingDef, bool> _apparelRestrictionCache = new Dictionary<ThingDef, bool>();

        public static readonly Dictionary<ValueTuple<ThingDef, string>, bool> _workEnabledCache = new Dictionary<ValueTuple<ThingDef, string>, bool>();
        public static readonly Dictionary<ValueTuple<ThingDef, WorkTypeDef>, bool> _workTypeEnabledCache = new Dictionary<ValueTuple<ThingDef, WorkTypeDef>, bool>();
        public static readonly Dictionary<ValueTuple<ThingDef, string>, bool> _workDisabledCache = new Dictionary<ValueTuple<ThingDef, string>, bool>();
        public static readonly Dictionary<ValueTuple<ThingDef, string>, bool> _forceDraftableCache = new Dictionary<ValueTuple<ThingDef, string>, bool>();
        public static readonly Dictionary<ValueTuple<ThingDef, string>, bool> _forceEquipWeaponCache = new Dictionary<ValueTuple<ThingDef, string>, bool>();
        public static readonly Dictionary<ValueTuple<ThingDef, string>, bool> _forceWearApparelCache = new Dictionary<ValueTuple<ThingDef, string>, bool>();

        // Cache for Utililty_IdentityManager
        public static readonly Dictionary<ThingDef, bool> _isAnimalCache = new Dictionary<ThingDef, bool>();
        public static readonly Dictionary<ThingDef, bool> _isHumanlikeCache = new Dictionary<ThingDef, bool>();
        public static readonly Dictionary<ThingDef, bool> _isMechanoidCache = new Dictionary<ThingDef, bool>();
        public static readonly Dictionary<FlagScopeTarget, bool> _flagOverrides = new Dictionary<FlagScopeTarget, bool>();

        // Add this static dictionary to cache jobs
        public static readonly Dictionary<Thing, Job> _jobCache = new Dictionary<Thing, Job>();

        /// <summary>Cache of ThingDefs with their work tag allowance status</summary>
        // Use separate caches for different tag types
        public static Dictionary<ThingDef, bool> _allowWorkTagCache = new Dictionary<ThingDef, bool>();
        public static Dictionary<ThingDef, bool> _blockWorkTagCache = new Dictionary<ThingDef, bool>();
        public static Dictionary<ThingDef, bool> _combinedWorkTagCache = new Dictionary<ThingDef, bool>();

        public static readonly Dictionary<int, bool> _bioTabVisibilityCache = new Dictionary<int, bool>();

        public static DutyDef GetDuty(string defName)
        {
            DutyDef result;
            if (!_dutyCache.TryGetValue(defName, out result))
            {
                result = DefDatabase<DutyDef>.GetNamedSilentFail(defName);
                _dutyCache[defName] = result;
            }
            return result;
        }

        /// <summary>
        /// ModExtension Cache for NonHumanlikePawnControlExtension
        /// </summary>
        public static NonHumanlikePawnControlExtension GetModExtension(ThingDef def)
        {
            if (def == null)
            {
                return null;
            }

            NonHumanlikePawnControlExtension cached;

            // === Check Cache First ===
            if (_modExtensionCache.TryGetValue(def, out cached))
            {
                return cached;
            }

            // === Safe check against incomplete def ===
            NonHumanlikePawnControlExtension modExtension = null;
            try
            {
                // Defensive: modExtensions might not be initialized yet
                if (def.modExtensions != null)
                {
                    modExtension = def.GetModExtension<NonHumanlikePawnControlExtension>();
                }
            }
            catch (Exception ex)
            {
                if (Prefs.DevMode)
                {
                    Log.Warning($"[PawnControl] GetModExtension failed for def {def.defName}: {ex.Message}");
                }
            }

            _modExtensionCache[def] = modExtension; // Safe to cache null for future checks

            return modExtension;
        }

        public static void UpdateModExtensionCache(ThingDef def, NonHumanlikePawnControlExtension extension)
        {
            // Update the cache with the modified extension
            if (_modExtensionCache.ContainsKey(def))
            {
                _modExtensionCache[def] = extension;
            }
        }

        public static void PreloadModExtensions()
        {
            int loadedCount = 0;

            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def?.race == null) continue;

                var modExtension = def.GetModExtension<NonHumanlikePawnControlExtension>();
                if (modExtension != null)
                {
                    _modExtensionCache[def] = modExtension;
                    loadedCount++;

                    // ✅ Add this line to enable skill passion injection support
                    modExtension.CacheSkillPassions();
                }
            }

            Utility_DebugManager.LogNormal($"Non Humanlike Pawn Controller refreshed: {loadedCount} extensions loaded.");
        }

        public static void PreloadModExtensionForRace(ThingDef def)
        {
            if (def == null) return;

            // Look for our extension type in the mod extensions
            if (def.modExtensions != null)
            {
                foreach (var ext in def.modExtensions)
                {
                    if (ext is NonHumanlikePawnControlExtension controlExt)
                    {
                        // Found an extension, update the cache
                        _modExtensionCache[def] = controlExt;
                        return;
                    }
                }
            }

            // No extension found, ensure null is cached
            _modExtensionCache[def] = null;
        }

        public static void ClearModExtensionCache()
        {
            _modExtensionCache.Clear();
            Utility_DebugManager.LogNormal("Cleared modExtensionCache.");
        }

        /// <summary>
        /// Clears cached <see cref="NonHumanlikePawnControlExtension"/> for a specific ThingDef.
        /// </summary>
        public static void ClearModExtensionCachePerInstance(ThingDef def)
        {
            if (def == null)
            {
                return;
            }

            _modExtensionCache.Remove(def); // UPDATED: remove only this def’s entry
            Utility_DebugManager.LogNormal($"Cleared modExtensionCache for {def.defName}."); // UPDATED: dev‐mode log
        }

        /// <summary>
        /// Returns a list of all colonist-like pawns in the given map and cache them.
        /// </summary>
        public static IEnumerable<Pawn> GetEffectiveColonistLikePawns(Map map)
        {
            if (map == null)
            {
                return Enumerable.Empty<Pawn>();
            }

            int frame = Time.frameCount;
            if (_colonistLikePawnCache.TryGetValue(map, out var cached) &&
                _frameIndexCache.TryGetValue(map, out var cachedFrame) &&
                frame - cachedFrame <= 5) // 5-frame TTL (adjustable)
            {
                return cached;
            }

            var result = map.mapPawns.AllPawns.Where(pawn =>
            {
                if (pawn == null || pawn.def == null || pawn.def.race == null)
                    return false;

                if (pawn.Faction != Faction.OfPlayer)
                    return false;

                // ✅ Require at least one valid work type to be enabled
                var modExtension = GetModExtension(pawn.def);

                // ✅ Fallback: vanilla logic
                if (modExtension == null)
                {
                    foreach (WorkTypeDef workType in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                    {
                        if (pawn.workSettings?.WorkIsActive(workType) == true)
                            return true;
                    }
                    return false;
                }

                // ✅ If extension exists, check for at least one tag-allowed work type
                foreach (var workTypeDef in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                {
                    if (IsWorkTypeEnabledForPawn(pawn, workTypeDef))
                    {
                        return true;
                    }
                }

                return false;
            }).ToList();

            _colonistLikePawnCache[map] = result;
            _frameIndexCache[map] = frame;
            return result;
        }

        public static void InvalidateColonistLikeCache(Map map)
        {
            _colonistLikePawnCache.Remove(map);
            _frameIndexCache.Remove(map);
        }

        /// <summary>
        /// Checks whether a pawn (including non-humanlike) is allowed to perform the given WorkType.
        /// Uses tag-based logic for non-humanlike pawns.
        /// </summary>
        public static bool IsWorkTypeEnabledForPawn(Pawn pawn, WorkTypeDef workType)
        {
            if (pawn == null || workType == null)
            {
                return false;
            }

            // 🧱 Only non-humanlike pawns with valid mod extensions AND allow tags can enable work
            var modExtension = GetModExtension(pawn.def);

            if (modExtension == null)
            {
                return pawn.workSettings?.WorkIsActive(workType) == true; // ✅ REAL vanilla fallback
            }

            if (modExtension.tags == null)
            {
                return false;
            }

            return Utility_TagManager.HasTag(pawn.def, ManagedTags.AllowAllWork) ||
                   Utility_TagManager.HasTag(pawn.def, ManagedTags.AllowWorkPrefix + workType.defName);
        }

        public static bool IsApparelRestricted(Pawn pawn)
        {
            if (pawn == null || pawn.def == null) return false;

            bool result;
            if (!_apparelRestrictionCache.TryGetValue(pawn.def, out result))
            {
                var modExtension = GetModExtension(pawn.def);

                if (modExtension != null)
                {
                    result = modExtension != null && modExtension.restrictApparelByBodyType;
                }

                _apparelRestrictionCache[pawn.def] = result;
            }
            return result;
        }

        // For JobGiverManager use
        /// <summary>
        /// Gets or creates a reachability dictionary for the specified map ID
        /// </summary>
        public static Dictionary<T, bool> GetOrNewReachabilityDict<T>(
            Dictionary<int, Dictionary<T, bool>> reachabilityCache,
            int mapId)
        {
            if (!reachabilityCache.ContainsKey(mapId))
                reachabilityCache[mapId] = new Dictionary<T, bool>();

            return reachabilityCache[mapId];
        }

        /// <summary>
        /// Updates a cached collection of things based on designations
        /// </summary>
        public static void UpdateDesignationBasedCache<T>(
            Map map,
            ref int lastUpdateTick,
            int updateInterval,
            Dictionary<int, List<T>> cache,
            Dictionary<int, Dictionary<T, bool>> reachabilityCache,
            DesignationDef designationDef,
            Func<Designation, T> extractFunc,
            int maxCacheEntries = 100) where T : Thing
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > lastUpdateTick + updateInterval ||
                !cache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (cache.ContainsKey(mapId))
                    cache[mapId].Clear();
                else
                    cache[mapId] = new List<T>();

                // Clear reachability cache too
                if (reachabilityCache.ContainsKey(mapId))
                    reachabilityCache[mapId].Clear();
                else
                    reachabilityCache[mapId] = new Dictionary<T, bool>();

                // Find all things designated for the specified action
                List<T> validThings = new List<T>();
                foreach (Designation designation in map.designationManager.SpawnedDesignationsOfDef(designationDef))
                {
                    T thing = extractFunc(designation);
                    if (thing != null && thing.Spawned)
                    {
                        validThings.Add(thing);
                    }
                }

                // Add items to cache
                cache[mapId].AddRange(validThings);

                // Limit cache size for memory efficiency
                if (cache[mapId].Count > maxCacheEntries)
                {
                    cache[mapId].RemoveRange(maxCacheEntries, cache[mapId].Count - maxCacheEntries);
                }

                lastUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Creates a generic cache system for storing map-specific data
        /// </summary>
        public static void UpdateGenericCache<T>(int mapId, Dictionary<int, List<T>> cache, IEnumerable<T> newItems, int maxCacheEntries = 100)
        {
            // Clear outdated cache or initialize if needed
            if (cache.ContainsKey(mapId))
                cache[mapId].Clear();
            else
                cache[mapId] = new List<T>();

            // Add items to cache
            if (newItems != null)
            {
                cache[mapId].AddRange(newItems);

                // Limit cache size for memory efficiency
                if (cache[mapId].Count > maxCacheEntries)
                {
                    cache[mapId].RemoveRange(maxCacheEntries, cache[mapId].Count - maxCacheEntries);
                }
            }
        }

        public static void CleanupAllRuntimeModExtensionsForNewGame()
        {
            Log.Message("[PawnControl] Cleaning up runtime mod extensions for new game");

            int removedCount = 0;

            // Clean all ThingDefs that have our extensions
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def?.modExtensions == null) continue;

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
            _tagCache.Clear();
            _forcedAnimalCache.Clear();
            _workEnabledCache.Clear();
            _workTypeEnabledCache.Clear();
            _workDisabledCache.Clear();
            _forceDraftableCache.Clear();
            _forceEquipWeaponCache.Clear();
            _forceWearApparelCache.Clear();
            _isAnimalCache.Clear();
            _isHumanlikeCache.Clear();
            _isMechanoidCache.Clear();

            Log.Message($"[PawnControl] Removed {removedCount} runtime mod extensions when starting new game");
        }
    }
}
