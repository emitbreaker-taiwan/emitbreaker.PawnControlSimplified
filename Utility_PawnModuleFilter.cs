using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using HarmonyLib;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Maintains, for each pawn, the list of JobModule UniqueIDs
    /// that pass the tag & workSettings filters.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class Utility_PawnModuleFilter
    {
        // pawn ⇒ allowed module IDs
        private static readonly Dictionary<int, HashSet<string>> _allowedByPawnId 
            = new Dictionary<int, HashSet<string>>();
            
        // Mapping of module ID to module instance for type safety
        private static readonly Dictionary<string, JobModule<Thing>> _moduleCache
            = new Dictionary<string, JobModule<Thing>>();

        static Utility_PawnModuleFilter()
        {
            var harmony = new Harmony("emitbreaker.pawncontrol.modulefilter");

            // 1) On Pawn spawn, seed the allowed-module set
            harmony.Patch(
                AccessTools.Method(typeof(Pawn), nameof(Pawn.SpawnSetup)),
                postfix: new HarmonyMethod(typeof(Utility_PawnModuleFilter),
                    nameof(SpawnSetup_Postfix)));

            // 2) When the player toggles a work-type priority, re-compute that pawn
            // FIX: Use the correct parameter name 'w' instead of 'workType'
            harmony.Patch(
                AccessTools.Method(typeof(Pawn_WorkSettings), nameof(Pawn_WorkSettings.SetPriority)),
                postfix: new HarmonyMethod(typeof(Utility_PawnModuleFilter),
                    nameof(WorkSettings_SetPriority_Postfix)));

            Utility_DebugManager.LogNormal("Utility_PawnModuleFilter initialized");
        }

        /// <summary>After a pawn appears, compute its allowed modules once.</summary>
        public static void SpawnSetup_Postfix(Pawn __instance, Map map, bool respawningAfterLoad)
        {
            UpdateAllowed(__instance);
        }

        /// <summary>After any work-type toggle, re-compute for that pawn.</summary>
        /// <param name="__instance">The WorkSettings instance</param>
        /// <param name="w">The work type definition (parameter name must match the original method)</param>
        /// <param name="priority">The priority level</param>
        public static void WorkSettings_SetPriority_Postfix(Pawn_WorkSettings __instance, WorkTypeDef w, int priority)
        {
            // Use Traverse to get the pawn from WorkSettings
            var pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
            if (pawn != null)
                UpdateAllowed(pawn);
        }

        /// <summary>Rebuild the allowed-module list for a pawn.</summary>
        public static void UpdateAllowed(Pawn pawn)
        {
            if (pawn == null || pawn.workSettings == null)
                return;
                
            int pawnId = pawn.thingIDNumber;
            if (!_allowedByPawnId.TryGetValue(pawnId, out var allowed))
            {
                allowed = new HashSet<string>();
                _allowedByPawnId[pawnId] = allowed;
            }
            else
            {
                allowed.Clear();
            }

            // Get pawn's mod extension
            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null) return;

            // If pawn has no tag set, they can't work
            if (!Utility_TagManager.HasTagSet(pawn.def, modExtension)) return;
                
            // Use the non-generic version to access modules
            var allModules = JobGiver_Unified_PawnControl<JobModule<Thing>, Thing>._jobModules;

            foreach (var module in allModules)
            {
                // Skip modules with invalid work types
                if (string.IsNullOrEmpty(module.WorkTypeName))
                    continue;

                // Check if this work type is enabled for this pawn's race in general
                if (!Utility_TagManager.WorkEnabled(pawn.def, module.WorkTypeName))
                    continue;

                // Check if this specific pawn has this work type enabled in their work tab
                var workTypeDef = Utility_Common.WorkTypeDefNamed(module.WorkTypeName);
                if (workTypeDef == null || !Utility_TagManager.WorkTypeSettingEnabled(pawn, workTypeDef))
                    continue;

                // Pre-approved module: add to pawn's allowed list
                allowed.Add(module.UniqueID);
                
                // Add to module cache for type safety when retrieving
                if (!_moduleCache.ContainsKey(module.UniqueID))
                {
                    _moduleCache[module.UniqueID] = module;
                }
            }

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Updated allowed modules for {pawn.LabelShort}: {allowed.Count} modules");
            }
        }

        /// <summary>Get pre-filtered modules for this pawn, ordered by priority.</summary>
        public static IEnumerable<JobModule<TTarget>> GetAllowedModules<TTarget>(Pawn pawn) 
            where TTarget : Thing
        {
            if (pawn == null) yield break;
            
            int pawnId = pawn.thingIDNumber;
            
            // If pawn's allowed list isn't calculated yet, do it now
            if (!_allowedByPawnId.TryGetValue(pawnId, out var allowed))
            {
                UpdateAllowed(pawn);
                
                // Get the newly created set
                if (!_allowedByPawnId.TryGetValue(pawnId, out allowed))
                {
                    yield break; // Still not found, something is wrong with this pawn
                }
            }
            
            // Return modules in priority order
            foreach (var module in JobGiver_Unified_PawnControl<JobModule<TTarget>, TTarget>._jobModules)
            {
                if (allowed.Contains(module.UniqueID))
                {
                    yield return module;
                }
            }
        }
        
        /// <summary>Clears cache when a pawn leaves the map.</summary>
        public static void ClearPawnCache(Pawn pawn)
        {
            if (pawn == null) return;
            _allowedByPawnId.Remove(pawn.thingIDNumber);
        }
        
        /// <summary>Clears all module caches.</summary>
        public static void ClearAllCaches()
        {
            _allowedByPawnId.Clear();
            _moduleCache.Clear();
        }
    }
}