using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Manages ThinkTree operations for pawns, including injecting subtrees, checking work tags,
    /// and modifying AI behavior trees at runtime or during game startup.
    /// </summary>
    public static class Utility_ThinkTreeManager
    {
        /// <summary>Cached reflection access to the ResolveSubnodes method</summary>
        private static readonly MethodInfo resolveMethod = AccessTools.Method(typeof(ThinkNode), "ResolveSubnodes");

        // Add cache for ThinkTreeDefs to avoid repeated lookups
        private static readonly Dictionary<string, ThinkTreeDef> _thinkTreeCache = new Dictionary<string, ThinkTreeDef>();

        /// <summary>
        /// Eagerly initializes ThinkTreeDef cache for all extensions
        /// </summary>
        public static void EagerCacheThinkTreeDefs()
        {
            // Clear existing cache
            _thinkTreeCache.Clear();

            // Cache all ThinkTreeDefs in the database
            foreach (ThinkTreeDef def in DefDatabase<ThinkTreeDef>.AllDefsListForReading)
            {
                _thinkTreeCache[def.defName] = def;
            }

            // Pre-cache think tree references for all extensions
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def?.race == null)
                    continue;

                var modExtension = def.GetModExtension<NonHumanlikePawnControlExtension>();
                if (modExtension == null)
                    continue;

                // Cache MainWorkThinkTreeDef
                if (!string.IsNullOrEmpty(modExtension.mainWorkThinkTreeDefName))
                {
                    if (!_thinkTreeCache.ContainsKey(modExtension.mainWorkThinkTreeDefName))
                    {
                        var tree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(modExtension.mainWorkThinkTreeDefName);
                        if (tree != null)
                        {
                            _thinkTreeCache[modExtension.mainWorkThinkTreeDefName] = tree;
                        }
                    }
                }

                // Cache ConstantThinkTreeDef
                if (!string.IsNullOrEmpty(modExtension.constantThinkTreeDefName))
                {
                    if (!_thinkTreeCache.ContainsKey(modExtension.constantThinkTreeDefName))
                    {
                        var tree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(modExtension.constantThinkTreeDefName);
                        if (tree != null)
                        {
                            _thinkTreeCache[modExtension.constantThinkTreeDefName] = tree;
                        }
                    }
                }

                // Store references directly in the extension for faster access
                if (!string.IsNullOrEmpty(modExtension.mainWorkThinkTreeDefName))
                {
                    modExtension.CachedMainThinkTree = GetCachedThinkTreeDef(modExtension.mainWorkThinkTreeDefName);
                }

                if (!string.IsNullOrEmpty(modExtension.constantThinkTreeDefName))
                {
                    modExtension.CachedConstantThinkTree = GetCachedThinkTreeDef(modExtension.constantThinkTreeDefName);
                }
            }

            Utility_DebugManager.LogNormal($"Eagerly cached {_thinkTreeCache.Count} ThinkTreeDefs");
        }

        /// <summary>
        /// Gets a ThinkTreeDef from cache, falling back to database lookup if needed
        /// </summary>
        public static ThinkTreeDef GetCachedThinkTreeDef(string defName)
        {
            if (string.IsNullOrEmpty(defName))
                return null;

            ThinkTreeDef result;
            if (_thinkTreeCache.TryGetValue(defName, out result))
                return result;

            // If not in cache, try to get from database and cache the result
            result = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(defName);
            if (result != null)
            {
                _thinkTreeCache[defName] = result;
            }

            return result;
        }

        /// <summary>
        /// Determines if a ThingDef has any allow or block work tags.
        /// </summary>
        /// <param name="def">The ThingDef to check</param>
        /// <returns>True if the def has any allow or block work tags, false otherwise</returns>
        public static bool HasAllowOrBlockWorkTag(ThingDef def)
        {
            if (def == null)
                return false;

            // Check combined cache first
            if (Utility_CacheManager._combinedWorkTagCache.TryGetValue(def, out bool result))
                return result;

            // Quick reject if no extension
            var modExtension = Utility_CacheManager.GetModExtension(def);
            if (modExtension == null)
            {
                Utility_CacheManager._combinedWorkTagCache[def] = false;
                return false;
            }

            // Calculate once and cache all results
            bool allowResult = Utility_TagManager.HasTag(def, ManagedTags.AllowAllWork) ||
                              Utility_TagManager.HasAnyTagWithPrefix(def, ManagedTags.AllowWorkPrefix);

            bool blockResult = Utility_TagManager.HasTag(def, ManagedTags.BlockAllWork) ||
                               Utility_TagManager.HasAnyTagWithPrefix(def, ManagedTags.BlockWorkPrefix);

            // Store in respective caches
            Utility_CacheManager._allowWorkTagCache[def] = allowResult;
            Utility_CacheManager._blockWorkTagCache[def] = blockResult;
            Utility_CacheManager._combinedWorkTagCache[def] = allowResult || blockResult;

            return allowResult || blockResult;
        }

        /// <summary>
        /// Checks if a ThingDef has any allow work tags.
        /// </summary>
        /// <param name="def">The ThingDef to check</param>
        /// <returns>True if the def has any allow work tags, false otherwise</returns>
        public static bool HasAllowWorkTag(ThingDef def)
        {
            if (def == null)
            {
                Utility_DebugManager.LogWarning($"{def.label} is null.");
                return false;
            }

            if (Utility_CacheManager._allowWorkTagCache.TryGetValue(def, out bool hasTag))
            {
                return hasTag;
            }

            if (Utility_CacheManager.GetModExtension(def) == null)
            {
                Utility_CacheManager._allowWorkTagCache[def] = false;
                return false; // No mod extension, no tags
            }

            bool result =
                Utility_TagManager.HasTag(def, ManagedTags.AllowAllWork) ||
                Utility_TagManager.HasAnyTagWithPrefix(def, ManagedTags.AllowWorkPrefix);

            Utility_CacheManager._allowWorkTagCache[def] = result; // ✅ Cache the computed result
            return result;
        }

        /// <summary>
        /// Checks if a ThingDef has any block work tags.
        /// </summary>
        /// <param name="def">The ThingDef to check</param>
        /// <returns>True if the def has any block work tags, false otherwise</returns>
        public static bool HasBlockWorkTag(ThingDef def)
        {
            if (def == null)
            {
                Utility_DebugManager.LogWarning($"{def.label} is null.");
                return false;
            }

            if (Utility_CacheManager._blockWorkTagCache.TryGetValue(def, out bool hasTag))
            {
                return hasTag;
            }

            if (Utility_CacheManager.GetModExtension(def) == null)
            {
                Utility_CacheManager._blockWorkTagCache[def] = false;
                return false; // No mod extension, no tags
            }

            bool result =
                Utility_TagManager.HasTag(def, ManagedTags.BlockAllWork) ||
                Utility_TagManager.HasAnyTagWithPrefix(def, ManagedTags.BlockWorkPrefix);

            Utility_CacheManager._blockWorkTagCache[def] = result; // ✅ Cache the computed result
            return result;
        }

        private static ThinkNode_PrioritySorter FindFirstPrioritySorter(ThinkNode root)
        {
            if (root == null)
                return null;

            if (root is ThinkNode_Tagger tagger)
            {
                if (tagger.subNodes != null)
                {
                    foreach (var subNode in tagger.subNodes)
                    {
                        var found = FindFirstPrioritySorter(subNode);
                        if (found != null)
                            return found;
                    }
                }
            }
            else if (root is ThinkNode_PrioritySorter sorter)
            {
                return sorter;
            }

            if (root.subNodes != null)
            {
                foreach (var subNode in root.subNodes)
                {
                    var found = FindFirstPrioritySorter(subNode);
                    if (found != null)
                        return found;
                }
            }

            return null;
        }

        public static void InjectPawnControlThinkTreesStaticRaceLevel()
        {
            // First ensure all ThinkTreeDefs are cached
            EagerCacheThinkTreeDefs();

            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def?.race == null)
                    continue;

                var modExtension = def.GetModExtension<NonHumanlikePawnControlExtension>();
                if (modExtension == null)
                    continue;

                if (!HasAllowOrBlockWorkTag(def))
                    continue;

                // ✅ Replace Main Work ThinkTree if specified - use cached version
                if (!string.IsNullOrEmpty(modExtension.mainWorkThinkTreeDefName))
                {
                    // Use the cached version from the extension if available
                    ThinkTreeDef thinkTree = modExtension.CachedMainThinkTree;
                    if (thinkTree == null)
                    {
                        // Fall back to cache lookup
                        thinkTree = GetCachedThinkTreeDef(modExtension.mainWorkThinkTreeDefName);
                    }

                    if (thinkTree != null)
                    {
                        def.race.thinkTreeMain = thinkTree;
                        Utility_DebugManager.LogNormal($"Replaced MainThinkTree '{modExtension.mainWorkThinkTreeDefName}' to {def.defName}.");
                    }
                    else
                    {
                        Utility_DebugManager.LogWarning($"MainThinkTreeDef '{modExtension.mainWorkThinkTreeDefName}' not found for {def.defName}.");
                    }
                }

                // ✅ Replace Constant ThinkTree if specified - use cached version
                if (!string.IsNullOrEmpty(modExtension.constantThinkTreeDefName))
                {
                    // Use the cached version from the extension if available
                    ThinkTreeDef constantTree = modExtension.CachedConstantThinkTree;
                    if (constantTree == null)
                    {
                        // Fall back to cache lookup
                        constantTree = GetCachedThinkTreeDef(modExtension.constantThinkTreeDefName);
                    }

                    if (constantTree != null)
                    {
                        def.race.thinkTreeConstant = constantTree;
                        Utility_DebugManager.LogNormal($"Replaced ConstantThinkTree '{modExtension.constantThinkTreeDefName}' to {def.defName}.");
                    }
                    else
                    {
                        Utility_DebugManager.LogWarning($"ConstantThinkTreeDef '{modExtension.constantThinkTreeDefName}' not found for {def.defName}.");
                    }
                }
            }

            Utility_DebugManager.LogNormal("Finished static ThinkTree injection phase.");
        }

        public static void ValidateThinkTree(Pawn pawn)
        {
            if (pawn?.thinker?.MainThinkNodeRoot == null)
                return;

            try
            {
                // Traverse the think tree
                bool hasWorkGiver = false;
                bool hasConditionalColonist = false;

                foreach (var node in pawn.thinker.MainThinkNodeRoot.ThisAndChildrenRecursive)
                {
                    if (node is JobGiver_Work || node is JobGiver_WorkNonHumanlike)
                        hasWorkGiver = true;

                    if (node is ThinkNode_ConditionalColonist)
                        hasConditionalColonist = true;
                }

                Utility_DebugManager.LogNormal($"[PawnControl DEBUG] ThinkTree validation for {pawn.LabelCap}:");
                Utility_DebugManager.LogNormal($"- Has JobGiver_Work: {hasWorkGiver}");
                Utility_DebugManager.LogNormal($"- Has ConditionalColonist: {hasConditionalColonist}");
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"ThinkTree validation error: {ex}");
            }
        }

        /// <summary>
        /// Resets a pawn's think trees back to vanilla defaults
        /// </summary>
        public static void ResetThinkTreeToVanilla(Pawn pawn, string originalMainTreeName = null, string originalConstTreeName = null)
        {
            if (pawn == null) return;

            // Check if there's a mod extension with stored original think trees
            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            bool debugLogging = modExtension?.debugMode == true;

            try
            {
                // ---- MAIN THINK TREE HANDLING ----
                // Get the original main think tree - first check passed parameters
                ThinkTreeDef mainThinkTree = null;

                // First try passed parameters (useful when mod extension is already removed)
                if (!string.IsNullOrEmpty(originalMainTreeName))
                {
                    mainThinkTree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(originalMainTreeName);
                    if (mainThinkTree != null)
                    {
                        if (debugLogging)
                            Utility_DebugManager.LogNormal($"Using provided original main think tree: {mainThinkTree.defName} for {pawn.LabelShort}");
                    }
                    else
                    {
                        if (debugLogging)
                            Utility_DebugManager.LogWarning($"Provided original main think tree '{originalMainTreeName}' not found - using fallbacks");
                    }
                }

                // Then check mod extension if available
                if (mainThinkTree == null)
                {
                    if (modExtension != null && !string.IsNullOrEmpty(modExtension.originalMainWorkThinkTreeDefName))
                    {
                        mainThinkTree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(modExtension.originalMainWorkThinkTreeDefName);
                        if (mainThinkTree != null)
                        {
                            if (debugLogging)
                                Utility_DebugManager.LogNormal($"Using stored original main think tree: {mainThinkTree.defName} for {pawn.LabelShort}");
                        }
                        else
                        {
                            if (debugLogging)
                                Utility_DebugManager.LogWarning($"Stored original main think tree '{modExtension.originalMainWorkThinkTreeDefName}' not found - using fallbacks");
                        }
                    }
                }

                // Finally fall back to vanilla defaults based on race properties
                if (mainThinkTree == null)
                {
                    // If no race-specific tree, use appropriate default
                    mainThinkTree = pawn.RaceProps.Humanlike
                        ? Utility_Common.ThinkTreeDefNamed("Humanlike")
                        : (pawn.RaceProps.Animal ? Utility_Common.ThinkTreeDefNamed("Animal")
                        : (pawn.RaceProps.IsMechanoid ? Utility_Common.ThinkTreeDefNamed("Mechanoid")
                        : null));

                    // Ultimate fallback - try to find ANY think tree if all else fails
                    if (mainThinkTree == null)
                    {
                        mainThinkTree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail("Animal") ??
                                       DefDatabase<ThinkTreeDef>.AllDefsListForReading.FirstOrDefault();

                        if (mainThinkTree != null)
                        {
                            if (debugLogging)
                                Utility_DebugManager.LogWarning($"Using ultimate fallback think tree {mainThinkTree.defName} for {pawn.LabelShort}");
                        }
                    }
                }

                // Reset main think tree if it was customized
                var thinkTreeField = AccessTools.Field(typeof(Pawn_MindState), "thinkTree");
                if (thinkTreeField != null && mainThinkTree != null)
                {
                    thinkTreeField.SetValue(pawn.mindState, mainThinkTree);
                }

                // ---- CONSTANT THINK TREE HANDLING ----
                // Similar process for constant think tree
                ThinkTreeDef constantThinkTree = null;

                // First try passed parameter
                if (!string.IsNullOrEmpty(originalConstTreeName))
                {
                    constantThinkTree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(originalConstTreeName);
                    if (constantThinkTree != null)
                    {
                        if (debugLogging)
                            Utility_DebugManager.LogNormal($"Using provided original constant think tree: {constantThinkTree.defName} for {pawn.LabelShort}");
                    }
                    else
                    {
                        if (debugLogging)
                            Utility_DebugManager.LogWarning($"Provided original constant think tree '{originalConstTreeName}' not found - using fallbacks");
                    }
                }

                // Then check mod extension
                if (constantThinkTree == null)
                {
                    if (modExtension != null && !string.IsNullOrEmpty(modExtension.originalConstantThinkTreeDefName))
                    {
                        constantThinkTree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(modExtension.originalConstantThinkTreeDefName);
                        if (constantThinkTree != null)
                        {
                            if (debugLogging)
                                Utility_DebugManager.LogNormal($"Using stored original constant think tree: {constantThinkTree.defName} for {pawn.LabelShort}");
                        }
                        else
                        {
                            if (debugLogging)
                                Utility_DebugManager.LogWarning($"Stored original constant think tree '{modExtension.originalConstantThinkTreeDefName}' not found - using fallbacks");
                        }
                    }
                }

                // Finally fall back to defaults (note: constant think trees are optional, so null is OK)
                if (constantThinkTree == null)
                {
                    constantThinkTree = pawn.RaceProps.Humanlike
                        ? Utility_Common.ThinkTreeDefNamed("HumanlikeConstant")
                        : (pawn.RaceProps.Animal ? Utility_Common.ThinkTreeDefNamed("AnimalConstant")
                        : null);
                }

                // Reset constant think tree
                var constThinkTreeField = AccessTools.Field(typeof(Pawn_MindState), "thinkTreeConstant");
                if (constThinkTreeField != null && constantThinkTree != null && pawn.mindState != null)
                {
                    constThinkTreeField.SetValue(pawn.mindState, constantThinkTree);
                }

                // Reset any cached think nodes using reflection exclusively
                if (pawn.mindState != null)
                {
                    // Get the thinker field via reflection
                    var thinkerField = AccessTools.Field(typeof(Pawn_MindState), "thinker");
                    if (thinkerField != null)
                    {
                        // Get the actual thinker object
                        object thinkerObj = thinkerField.GetValue(pawn.mindState);
                        if (thinkerObj != null)
                        {
                            // Find the thinkRoot field in whatever type thinkerObj is
                            var thinkRootField = AccessTools.Field(thinkerObj.GetType(), "thinkRoot");
                            if (thinkRootField != null)
                            {
                                // Set it to null to force a rebuild
                                thinkRootField.SetValue(thinkerObj, null);
                            }

                            // Optionally, look for a "nextThink" field and reset it too
                            var nextThinkField = AccessTools.Field(thinkerObj.GetType(), "nextThink");
                            if (nextThinkField != null)
                            {
                                nextThinkField.SetValue(thinkerObj, 0);
                            }
                        }
                    }
                }

                if (debugLogging)
                    Utility_DebugManager.LogNormal($"Reset think tree for {pawn.LabelShort} to vanilla");
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error resetting think tree for {pawn.LabelShort}: {ex}");
                // Attempt emergency fallback for critical failures
                try
                {
                    EmergencyThinkTreeFallback(pawn);
                }
                catch
                {
                    // Silent fail for emergency handler
                }
            }
        }

        /// <summary>
        /// Last resort emergency fallback used when normal reset fails
        /// </summary>
        private static void EmergencyThinkTreeFallback(Pawn pawn)
        {
            if (pawn?.mindState == null) return;

            // Try to get the most basic think trees
            ThinkTreeDef animalTree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail("Animal");
            ThinkTreeDef animalConstTree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail("AnimalConstant");

            if (animalTree != null)
            {
                var thinkTreeField = AccessTools.Field(typeof(Pawn_MindState), "thinkTree");
                if (thinkTreeField != null)
                {
                    thinkTreeField.SetValue(pawn.mindState, animalTree);
                    Utility_DebugManager.LogWarning($"Applied emergency Animal think tree for {pawn.LabelShort}");
                }
            }

            if (animalConstTree != null)
            {
                var constThinkTreeField = AccessTools.Field(typeof(Pawn_MindState), "thinkTreeConstant");
                if (constThinkTreeField != null)
                {
                    constThinkTreeField.SetValue(pawn.mindState, animalConstTree);
                }
            }

            // Force think node rebuild
            if (pawn.thinker != null)
            {
                Type thinkerType = pawn.thinker.GetType();
                FieldInfo thinkRootField = AccessTools.Field(thinkerType, "thinkRoot");
                if (thinkRootField != null)
                {
                    thinkRootField.SetValue(pawn.thinker, null);
                }
            }
        }

        public static void ResolveSubnodesAndRecur(ThinkNode node)
        {
            if (node == null) return;

            resolveMethod?.Invoke(node, null);

            if (node.subNodes != null)
            {
                foreach (var sub in node.subNodes)
                {
                    ResolveSubnodesAndRecur(sub);
                }
            }
        }

        public static void ResetCache()
        {
            Utility_CacheManager._allowWorkTagCache.Clear();
            Utility_CacheManager._blockWorkTagCache.Clear();
            Utility_CacheManager._combinedWorkTagCache.Clear();
            _thinkTreeCache.Clear();
        }
    }
}