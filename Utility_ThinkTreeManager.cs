﻿using HarmonyLib;
using RimWorld;
using System;
using System.Reflection;
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
        public static ThinkTreeDef ThinkTreeDefNamed(string defName)
        {
            return DefDatabase<ThinkTreeDef>.GetNamed(defName);
        }

        /// <summary>Cached reflection access to the ResolveSubnodes method</summary>
        private static readonly MethodInfo resolveMethod = AccessTools.Method(typeof(ThinkNode), "ResolveSubnodes");

        /// <summary>
        /// Determines if a ThingDef has any allow or block work tags.
        /// </summary>
        /// <param name="def">The ThingDef to check</param>
        /// <returns>True if the def has any allow or block work tags, false otherwise</returns>
        public static bool HasAllowOrBlockWorkTag(Pawn pawn)
        {
            if (!Utility_Common.RaceDefChecker(pawn.def))
                return false;

            // Quick reject if no extension
            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null)
            {
                Utility_CacheManager.CombinedWorkTag[pawn] = false;
                return false;
            }

            // Calculate once and cache all results
            bool allowResult = Utility_TagManager.HasTag(pawn.def, ManagedTags.AllowAllWork) ||
                              Utility_TagManager.HasAnyTagWithPrefix(pawn.def, ManagedTags.AllowWorkPrefix);

            bool blockResult = Utility_TagManager.HasTag(pawn.def, ManagedTags.BlockAllWork) ||
                               Utility_TagManager.HasAnyTagWithPrefix(pawn.def, ManagedTags.BlockWorkPrefix);

            // Store in respective caches
            Utility_CacheManager.AllowWorkTag[pawn] = allowResult;
            Utility_CacheManager.BlockWorkTag[pawn] = blockResult;
            Utility_CacheManager.CombinedWorkTag[pawn] = allowResult || blockResult;

            return allowResult || blockResult;
        }

        /// <summary>
        /// Checks if a ThingDef has any allow work tags.
        /// </summary>
        /// <param name="def">The ThingDef to check</param>
        /// <returns>True if the def has any allow work tags, false otherwise</returns>
        public static bool HasAllowWorkTag(Pawn pawn)
        {
            if (!Utility_Common.RaceDefChecker(pawn.def))
                return false;

            if (Utility_CacheManager.AllowWorkTag.TryGetValue(pawn, out bool hasTag))
            {
                return hasTag;
            }

            if (Utility_CacheManager.GetModExtension(pawn.def) == null)
            {
                Utility_CacheManager.AllowWorkTag[pawn] = false;
                return false; // No mod extension, no tags
            }

            bool result =
                Utility_TagManager.HasTag(pawn.def, ManagedTags.AllowAllWork) ||
                Utility_TagManager.HasAnyTagWithPrefix(pawn.def, ManagedTags.AllowWorkPrefix);

            Utility_CacheManager.AllowWorkTag[pawn] = result; // ✅ Cache the computed result
            return result;
        }

        /// <summary>
        /// Checks if a ThingDef has any block work tags.
        /// </summary>
        /// <param name="def">The ThingDef to check</param>
        /// <returns>True if the def has any block work tags, false otherwise</returns>
        public static bool HasBlockWorkTag(Pawn pawn)
        {
            if (!Utility_Common.RaceDefChecker(pawn.def))
                return false;

            if (Utility_CacheManager.BlockWorkTag.TryGetValue(pawn, out bool hasTag))
            {
                return hasTag;
            }

            if (Utility_CacheManager.GetModExtension(pawn.def) == null)
            {
                Utility_CacheManager.BlockWorkTag[pawn] = false;
                return false; // No mod extension, no tags
            }

            bool result =
                Utility_TagManager.HasTag(pawn.def, ManagedTags.BlockAllWork) ||
                Utility_TagManager.HasAnyTagWithPrefix(pawn.def, ManagedTags.BlockWorkPrefix);

            Utility_CacheManager.BlockWorkTag[pawn] = result; // ✅ Cache the computed result
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
            try
            {
                // Get all pawns in the game world
                foreach (Map map in Find.Maps)
                {
                    if (map?.mapPawns?.AllPawnsSpawned == null) continue;

                    foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                    {
                        if (pawn?.def?.race == null)
                            continue;

                        try
                        {
                            // Get the mod extension from the pawn's race def
                            var modExtension = pawn.def.GetModExtension<NonHumanlikePawnControlExtension>();
                            if (modExtension == null)
                                continue;

                            // Check if the pawn has work tags
                            if (!HasAllowOrBlockWorkTag(pawn))
                                continue;

                            // ✅ Replace Main Work ThinkTree if specified
                            if (!string.IsNullOrEmpty(modExtension.mainWorkThinkTreeDefName))
                            {
                                var thinkTree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(modExtension.mainWorkThinkTreeDefName);
                                if (thinkTree != null)
                                {
                                    // Update the race-level think tree
                                    pawn.def.race.thinkTreeMain = thinkTree;

                                    // Also update the individual pawn's think tree if it exists
                                    var thinkTreeField = AccessTools.Field(typeof(Pawn_MindState), "thinkTree");
                                    if (thinkTreeField != null && pawn.mindState != null)
                                    {
                                        thinkTreeField.SetValue(pawn.mindState, thinkTree);
                                    }

                                    Utility_DebugManager.LogNormal($"Replaced MainThinkTree '{modExtension.mainWorkThinkTreeDefName}' for pawn {pawn.LabelShort} of race {pawn.def.defName}.");
                                }
                                else
                                {
                                    Utility_DebugManager.LogWarning($"MainThinkTreeDef '{modExtension.mainWorkThinkTreeDefName}' not found for {pawn.LabelShort} of race {pawn.def.defName}.");
                                }
                            }

                            // ✅ Replace Constant ThinkTree if specified
                            if (!string.IsNullOrEmpty(modExtension.constantThinkTreeDefName))
                            {
                                var constantTree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(modExtension.constantThinkTreeDefName);
                                if (constantTree != null)
                                {
                                    // Update the race-level constant think tree
                                    pawn.def.race.thinkTreeConstant = constantTree;

                                    // Also update the individual pawn's constant think tree if it exists
                                    var constThinkTreeField = AccessTools.Field(typeof(Pawn_MindState), "thinkTreeConstant");
                                    if (constThinkTreeField != null && pawn.mindState != null)
                                    {
                                        constThinkTreeField.SetValue(pawn.mindState, constantTree);
                                    }

                                    Utility_DebugManager.LogNormal($"Replaced ConstantThinkTree '{modExtension.constantThinkTreeDefName}' for pawn {pawn.LabelShort} of race {pawn.def.defName}.");
                                }
                                else
                                {
                                    Utility_DebugManager.LogWarning($"ConstantThinkTreeDef '{modExtension.constantThinkTreeDefName}' not found for {pawn.LabelShort} of race {pawn.def.defName}.");
                                }
                            }

                            // Force the pawn's thinker to rebuild its think nodes
                            if (pawn.thinker != null)
                            {
                                // Get the thinkRoot field via reflection
                                Type thinkerType = pawn.thinker.GetType();
                                FieldInfo thinkRootField = AccessTools.Field(thinkerType, "thinkRoot");
                                if (thinkRootField != null)
                                {
                                    // Set it to null to force a rebuild on next think cycle
                                    thinkRootField.SetValue(pawn.thinker, null);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Utility_DebugManager.LogError($"Error processing pawn {pawn?.LabelShort ?? "null"}: {ex.Message}");
                        }
                    }
                }

                // Handle CaravanPawns if needed
                if (Find.WorldPawns?.AllPawnsAlive != null)
                {
                    foreach (Pawn pawn in Find.WorldPawns.AllPawnsAlive)
                    {
                        if (pawn?.def?.race == null)
                            continue;

                        try
                        {
                            var modExtension = pawn.def.GetModExtension<NonHumanlikePawnControlExtension>();
                            if (modExtension == null || !HasAllowOrBlockWorkTag(pawn))
                                continue;

                            // Apply think trees similar to the code above
                            // (Similar code block as before for world pawns)
                        }
                        catch (Exception ex)
                        {
                            Utility_DebugManager.LogError($"Error processing world pawn {pawn?.LabelShort ?? "null"}: {ex.Message}");
                        }
                    }
                }

                // Also run once for ThingDefs to ensure newly spawned pawns get the right think trees
                try
                {
                    ApplyThinkTreeToRaceDefs();
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"Error in ApplyThinkTreeToRaceDefs: {ex.Message}");
                }

                Utility_DebugManager.LogNormal("Finished pawn-level ThinkTree injection phase.");
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Critical error in InjectPawnControlThinkTreesStaticRaceLevel: {ex}");
            }
        }

        // Helper method to ensure ThingDefs still get updated for future pawns
        public static void ApplyThinkTreeToRaceDefs()
        {
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def?.race == null)
                    continue;

                var modExtension = def.GetModExtension<NonHumanlikePawnControlExtension>();
                if (modExtension == null)
                    continue;

                // Use HasAllowOrBlockWorkTag with a generic pawn from this race def
                // We can't create a real pawn, so we'll simulate the check
                bool hasTag = Utility_TagManager.HasTag(def, ManagedTags.AllowAllWork) ||
                             Utility_TagManager.HasAnyTagWithPrefix(def, ManagedTags.AllowWorkPrefix) ||
                             Utility_TagManager.HasTag(def, ManagedTags.BlockAllWork) ||
                             Utility_TagManager.HasAnyTagWithPrefix(def, ManagedTags.BlockWorkPrefix);

                if (!hasTag)
                    continue;

                // ✅ Replace Main Work ThinkTree if specified
                if (!string.IsNullOrEmpty(modExtension.mainWorkThinkTreeDefName))
                {
                    var thinkTree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(modExtension.mainWorkThinkTreeDefName);
                    if (thinkTree != null)
                    {
                        def.race.thinkTreeMain = thinkTree;
                        Utility_DebugManager.LogNormal($"Replaced MainThinkTree '{modExtension.mainWorkThinkTreeDefName}' on race def {def.defName}.");
                    }
                }

                // ✅ Replace Constant ThinkTree if specified
                if (!string.IsNullOrEmpty(modExtension.constantThinkTreeDefName))
                {
                    var constantTree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(modExtension.constantThinkTreeDefName);
                    if (constantTree != null)
                    {
                        def.race.thinkTreeConstant = constantTree;
                        Utility_DebugManager.LogNormal($"Replaced ConstantThinkTree '{modExtension.constantThinkTreeDefName}' on race def {def.defName}.");
                    }
                }
            }
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
                    if (node is JobGiver_Work)
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
                        ? ThinkTreeDefNamed("Humanlike")
                        : (pawn.RaceProps.Animal ? ThinkTreeDefNamed("Animal")
                        : (pawn.RaceProps.IsMechanoid ? ThinkTreeDefNamed("Mechanoid")
                        : null));

                    // Ultimate fallback - try to find ANY think tree if all else fails
                    if (mainThinkTree == null)
                    {
                        // First try the "Animal" tree
                        var animalTree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail("Animal");
                        if (animalTree != null)
                        {
                            mainThinkTree = animalTree;
                        }
                        else
                        {
                            // Manual equivalent of FirstOrDefault()
                            var allTrees = DefDatabase<ThinkTreeDef>.AllDefsListForReading;
                            mainThinkTree = (allTrees.Count > 0) ? allTrees[0] : null;
                        }

                        if (mainThinkTree != null)
                        {
                            if (debugLogging)
                                Utility_DebugManager.LogWarning(
                                    $"Using ultimate fallback think tree {mainThinkTree.defName} for {pawn.LabelShort}");
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
                        ? ThinkTreeDefNamed("HumanlikeConstant")
                        : (pawn.RaceProps.Animal ? ThinkTreeDefNamed("AnimalConstant")
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
            Utility_CacheManager.AllowWorkTag.Clear();
            Utility_CacheManager.BlockWorkTag.Clear();
            Utility_CacheManager.CombinedWorkTag.Clear();
        }
    }
}