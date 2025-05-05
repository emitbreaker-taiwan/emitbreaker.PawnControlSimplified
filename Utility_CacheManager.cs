using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse.AI;
using Verse;
using RimWorld;
using UnityEngine;

namespace emitbreaker.PawnControl
{
    public static class Utility_CacheManager
    {
        public static readonly Dictionary<ThingDef, NonHumanlikePawnControlExtension> modExtensionCache = new Dictionary<ThingDef, NonHumanlikePawnControlExtension>();
        public static readonly Dictionary<ThingDef, HashSet<string>> tagCache = new Dictionary<ThingDef, HashSet<string>>();

        // Caching ForceColonist Pawns
        private static readonly Dictionary<Map, List<Pawn>> cachedColonistLikePawns = new Dictionary<Map, List<Pawn>>();
        private static readonly Dictionary<Map, int> cachedFrameIndex = new Dictionary<Map, int>();

        private static readonly Dictionary<string, DutyDef> dutyCache = new Dictionary<string, DutyDef>();
        // Cache to store the tag-check result per ThingDef
        private static readonly Dictionary<ThingDef, bool> cachedApparelRestriction = new Dictionary<ThingDef, bool>();
        
        public static readonly Dictionary<ValueTuple<ThingDef, string>, bool> workEnabledCache = new Dictionary<ValueTuple<ThingDef, string>, bool>();
        public static readonly Dictionary<ValueTuple<ThingDef, string>, bool> workDisabledCache = new Dictionary<ValueTuple<ThingDef, string>, bool>();

        // Add this static dictionary to cache jobs
        public static readonly Dictionary<Thing, Job> _cachedJobs = new Dictionary<Thing, Job>();

        public static DutyDef GetDuty(string defName)
        {
            DutyDef result;
            if (!dutyCache.TryGetValue(defName, out result))
            {
                result = DefDatabase<DutyDef>.GetNamedSilentFail(defName);
                dutyCache[defName] = result;
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
            if (modExtensionCache.TryGetValue(def, out cached))
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

            modExtensionCache[def] = modExtension; // Safe to cache null for future checks

            return modExtension;
        }

        public static void PreloadModExtensions()
        {
            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def?.race == null) continue;

                var ext = def.GetModExtension<NonHumanlikePawnControlExtension>();
                if (ext != null)
                {
                    modExtensionCache[def] = ext;

                    // ✅ Add this line to enable skill passion injection support
                    ext.CacheSkillPassions();
                }
            }
            if (Prefs.DevMode)
            {
                Log.Message($"[PawnControl] Non Humanlike Pawn Controller refreshed: {Utility_CacheManager.modExtensionCache.Count} extensions loaded.");
            }
        }

        public static void ClearModExtensionCache()
        {
            modExtensionCache.Clear();
            Log.Message("[PawnControl] Cleared modExtensionCache.");
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
            if (cachedColonistLikePawns.TryGetValue(map, out var cached) &&
                cachedFrameIndex.TryGetValue(map, out var cachedFrame) &&
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

            cachedColonistLikePawns[map] = result;
            cachedFrameIndex[map] = frame;
            return result;
        }

        public static void InvalidateColonistLikeCache(Map map)
        {
            cachedColonistLikePawns.Remove(map);
            cachedFrameIndex.Remove(map);
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
            if (!cachedApparelRestriction.TryGetValue(pawn.def, out result))
            {
                var physicalModExtension = pawn.def.GetModExtension<NonHumanlikePawnControlExtension>();

                if (physicalModExtension != null)
                {
                    result = physicalModExtension != null && physicalModExtension.restrictApparelByBodyType;
                }

                cachedApparelRestriction[pawn.def] = result;
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
        /// Resets a job giver cache system
        /// </summary>
        public static void ResetJobGiverCache<T>(
            Dictionary<int, List<T>> mainCache,
            Dictionary<int, Dictionary<T, bool>> reachabilityCache)
        {
            if (mainCache != null)
                mainCache.Clear();

            if (reachabilityCache != null)
                reachabilityCache.Clear();
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
    }
}
