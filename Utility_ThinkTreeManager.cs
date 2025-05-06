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
                Log.Warning($"[PawnControl] {def.label} is null.");
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
                Log.Warning($"[PawnControl] {def.label} is null.");
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
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def?.race == null)
                    continue;

                var modExtension = def.GetModExtension<NonHumanlikePawnControlExtension>();
                if (modExtension == null)
                    continue;

                if (!HasAllowOrBlockWorkTag(def))
                    continue;

                // ✅ Replace Main Work ThinkTree if specified
                if (!string.IsNullOrEmpty(modExtension.mainWorkThinkTreeDefName))
                {
                    var thinkTree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(modExtension.mainWorkThinkTreeDefName);
                    if (thinkTree != null)
                    {
                        def.race.thinkTreeMain = thinkTree;

                        if (Prefs.DevMode)
                        {
                            Log.Message($"[PawnControl] Replaced MainThinkTree '{modExtension.mainWorkThinkTreeDefName}' to {def.defName}.");
                        }
                    }
                    else if (Prefs.DevMode)
                    {
                        Log.Warning($"[PawnControl] MainThinkTreeDef '{modExtension.mainWorkThinkTreeDefName}' not found for {def.defName}.");
                    }
                }

                //// ✅ Inject additional Main ThinkTree subtrees
                //if (modExtension.additionalMain != null && modExtension.additionalMain.Count > 0)
                //{
                //    foreach (string additionalDefName in modExtension.additionalMain)
                //    {
                //        if (!string.IsNullOrWhiteSpace(additionalDefName))
                //        {
                //            TryInjectSubtreeToRace(def, additionalDefName);
                //            if (Prefs.DevMode)
                //            {
                //                Log.Message($"[PawnControl] Injected additional ThinkTree '{additionalDefName}' into '{def.race.thinkTreeMain?.defName}' for {def.defName}.");
                //            }
                //        }
                //    }
                //}

                // ✅ Replace Constant ThinkTree if specified
                if (!string.IsNullOrEmpty(modExtension.constantThinkTreeDefName))
                {
                    var constantTree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(modExtension.constantThinkTreeDefName);
                    if (constantTree != null)
                    {
                        def.race.thinkTreeConstant = constantTree;

                        if (Prefs.DevMode)
                        {
                            Log.Message($"[PawnControl] Replaced ConstantThinkTree '{modExtension.constantThinkTreeDefName}' to {def.defName}.");
                        }
                    }
                    else if (Prefs.DevMode)
                    {
                        Log.Warning($"[PawnControl] ConstantThinkTreeDef '{modExtension.constantThinkTreeDefName}' not found for {def.defName}.");
                    }
                }
            }

            if (Prefs.DevMode)
            {
                Log.Message("[PawnControl] Finished static ThinkTree injection phase.");
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
                    if (node is JobGiver_Work || node is JobGiver_WorkNonHumanlike)
                        hasWorkGiver = true;

                    if (node is ThinkNode_ConditionalColonist)
                        hasConditionalColonist = true;
                }

                Log.Message($"[PawnControl DEBUG] ThinkTree validation for {pawn.LabelCap}:");
                Log.Message($"- Has JobGiver_Work: {hasWorkGiver}");
                Log.Message($"- Has ConditionalColonist: {hasConditionalColonist}");
            }
            catch (Exception ex)
            {
                Log.Error($"[PawnControl] ThinkTree validation error: {ex}");
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
        }
    }
}