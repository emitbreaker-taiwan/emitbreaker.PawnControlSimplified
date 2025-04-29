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
        /// <summary>Cache of ThingDefs with their work tag allowance status</summary>
        // Use separate caches for different tag types
        private static Dictionary<ThingDef, bool> _allowWorkTagCache = new Dictionary<ThingDef, bool>();
        private static Dictionary<ThingDef, bool> _blockWorkTagCache = new Dictionary<ThingDef, bool>();
        private static Dictionary<ThingDef, bool> _combinedWorkTagCache = new Dictionary<ThingDef, bool>();

        /// <summary>Cached reflection access to the ResolveSubnodes method</summary>
        private static readonly MethodInfo resolveMethod = AccessTools.Method(typeof(ThinkNode), "ResolveSubnodes");
        private static readonly FieldInfo treeDefField = AccessTools.Field(typeof(ThinkNode_Subtree), "treeDef");

        /// <summary>Cached reflection access to the tags field in ThinkNode</summary>
        private static readonly FieldInfo tagsField = AccessTools.Field(typeof(ThinkNode), "tags");

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
            if (_combinedWorkTagCache.TryGetValue(def, out bool result))
                return result;

            // Quick reject if no extension
            var modExtension = Utility_CacheManager.GetModExtension(def);
            if (modExtension == null)
            {
                _combinedWorkTagCache[def] = false;
                return false;
            }

            // Calculate once and cache all results
            bool allowResult = Utility_TagManager.HasTag(def, ManagedTags.AllowAllWork) ||
                              Utility_TagManager.HasAnyTagWithPrefix(def, ManagedTags.AllowWorkPrefix);

            bool blockResult = Utility_TagManager.HasTag(def, ManagedTags.BlockAllWork) ||
                               Utility_TagManager.HasAnyTagWithPrefix(def, ManagedTags.BlockWorkPrefix);

            // Store in respective caches
            _allowWorkTagCache[def] = allowResult;
            _blockWorkTagCache[def] = blockResult;
            _combinedWorkTagCache[def] = allowResult || blockResult;

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

            if (_allowWorkTagCache.TryGetValue(def, out bool hasTag))
            {
                return hasTag;
            }

            if (Utility_CacheManager.GetModExtension(def) == null)
            {
                _allowWorkTagCache[def] = false;
                return false; // No mod extension, no tags
            }

            bool result =
                Utility_TagManager.HasTag(def, ManagedTags.AllowAllWork) ||
                Utility_TagManager.HasAnyTagWithPrefix(def, ManagedTags.AllowWorkPrefix);

            _allowWorkTagCache[def] = result; // ✅ Cache the computed result
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

            if (_blockWorkTagCache.TryGetValue(def, out bool hasTag))
            {
                return hasTag;
            }

            if (Utility_CacheManager.GetModExtension(def) == null)
            {
                _blockWorkTagCache[def] = false;
                return false; // No mod extension, no tags
            }

            bool result =
                Utility_TagManager.HasTag(def, ManagedTags.BlockAllWork) ||
                Utility_TagManager.HasAnyTagWithPrefix(def, ManagedTags.BlockWorkPrefix);

            _blockWorkTagCache[def] = result; // ✅ Cache the computed result
            return result;
        }

        public static bool PawnWillingToCutPlant_Job(Thing plant, Pawn pawn)
        {
            if (pawn.RaceProps.Humanlike && Utility_CacheManager.GetModExtension(pawn.def) == null && plant.def.plant.IsTree && plant.def.plant.treeLoversCareIfChopped)
            {
                return new HistoryEvent(HistoryEventDefOf.CutTree, pawn.Named(HistoryEventArgsNames.Doer)).Notify_PawnAboutToDo_Job();
            }

            return true;
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

        /// <summary>
        /// Try injecting a ThinkTree subtree into a race's Main ThinkTree (ThingDef based).
        /// Only usable during static startup or Def loading phase.
        /// </summary>
        //public static bool TryInjectSubtreeToRace(ThingDef raceDef, string subtreeDefName)
        //{
        //    if (raceDef?.race?.thinkTreeMain == null)
        //        return false;

        //    //var mainThinkTree = raceDef.race.thinkTreeMain;
        //    var thinkTreeDefName = raceDef.race.thinkTreeMain?.defName;
        //    var mainThinkTree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(thinkTreeDefName);
        //    if (mainThinkTree == null)
        //    {
        //        Log.Error($"[PawnControl] Failed to find ThinkTreeDef '{thinkTreeDefName}' for race '{raceDef.defName}'");
        //        return false;
        //    }            
        //    var subtreeDef = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(subtreeDefName);

        //    if (subtreeDef == null || subtreeDef.thinkRoot == null)
        //        return false;

        //    // Already injected?
        //    if (IsSubtreeAlreadyInjected(mainThinkTree.thinkRoot, subtreeDef.defName))
        //        return false;

        //    // Deep copy
        //    ThinkNode subtreeCopy = subtreeDef.thinkRoot.DeepCopy();
        //    CleanSubtree(subtreeCopy); // ✅ Clean it immediately after deep copy
        //    subtreeCopy.AddThinkTreeTag($"PawnControl_Injected_{subtreeDef.defName}");

        //    // Find PrioritySorter
        //    var sorter = FindFirstPrioritySorter(mainThinkTree.thinkRoot);

        //    if (sorter != null)
        //    {
        //        if (sorter.subNodes == null)
        //            sorter.subNodes = new List<ThinkNode>();

        //        bool isHumanlike = raceDef?.race?.Humanlike ?? false;

        //        var root = mainThinkTree.thinkRoot;

        //        if (!(root is ThinkNode_Priority))
        //        {
        //            Log.Warning($"[PawnControl] Cannot inject: root node of {mainThinkTree.defName} is not ThinkNode_Priority.");
        //            return false;
        //        }

        //        ThinkNode_Priority thinkNode_Priority = root as ThinkNode_Priority;

        //        // ✅ Use smart insertion logic
        //        InsertSubtreeSmart(thinkNode_Priority, subtreeCopy, isHumanlike);

        //        ResolveSubnodesAndRecur(sorter);
        //    }
        //    else
        //    {
        //        if (mainThinkTree.thinkRoot.subNodes == null)
        //            mainThinkTree.thinkRoot.subNodes = new List<ThinkNode>();

        //        mainThinkTree.thinkRoot.subNodes.Add(subtreeCopy);

        //        ResolveSubnodesAndRecur(mainThinkTree.thinkRoot);
        //    }
            
        //    FinalizeThinkTreeInjection(raceDef);
            
        //    if (Prefs.DevMode)
        //    {
        //        Log.Message($"[PawnControl] Fallback: Appended {subtreeDefName} at end of ThinkNode_Priority.");
        //    }

        //    return true;
        //}

        //private static bool InsertSubtreeSmart(ThinkNode_Priority thinkNode_Priority, ThinkNode subtreeToInject, bool isHumanlike = false)
        //{
        //    if (thinkNode_Priority?.subNodes == null || subtreeToInject == null)
        //    {
        //        return false;
        //    }

        //    // 1. Find ThinkNode_SubtreesByTag
        //    for (int i = 0; i < thinkNode_Priority.subNodes.Count; i++)
        //    {
        //        if (thinkNode_Priority.subNodes[i]?.GetType().Name == "ThinkNode_SubtreesByTag")
        //        {
        //            thinkNode_Priority.subNodes.Insert(i, subtreeToInject);

        //            if (Prefs.DevMode)
        //            {
        //                Log.Message($"[PawnControl] Inserted {subtreeToInject} before ThinkNode_SubtreesByTag at index {i}.");
        //            }

        //            return true;
        //        }
        //    }

        //    // 2. Find ThinkNode_Subtree with treeDef "SatisfyBasicNeeds"
        //    for (int i = 0; i < thinkNode_Priority.subNodes.Count; i++)
        //    {
        //        if (thinkNode_Priority.subNodes[i] is ThinkNode_Subtree subtree && treeDefField != null)
        //        {
        //            var treeDef = treeDefField.GetValue(subtree) as ThinkTreeDef;
        //            if (treeDef != null && treeDef.defName == "SatisfyBasicNeeds")
        //            {
        //                thinkNode_Priority.subNodes.Insert(i, subtreeToInject);
        //                if (Prefs.DevMode)
        //                {
        //                    Log.Message($"[PawnControl] Inserted {subtreeToInject} before SatisfyBasicNeeds at index {i}.");
        //                }
        //                return true;
        //            }
        //        }
        //    }

        //    // 3. Special Handling: Humanlike-specific Insert
        //    if (isHumanlike)
        //    {
        //        for (int i = 0; i < thinkNode_Priority.subNodes.Count; i++)
        //        {
        //            var node = thinkNode_Priority.subNodes[i];

        //            if (node is ThinkNode_ConditionalColonist colonistNode && colonistNode.subNodes != null)
        //            {
        //                for (int j = 0; j < colonistNode.subNodes.Count; j++)
        //                {
        //                    if (colonistNode.subNodes[j] is ThinkNode_Subtree subtree && treeDefField != null)
        //                    {
        //                        ThinkTreeDef treeDef = treeDefField.GetValue(subtree) as ThinkTreeDef;
        //                        if (treeDef != null && treeDef.defName == "MainColonistBehaviorCore")
        //                        {
        //                            colonistNode.subNodes.Insert(j, subtreeToInject);
        //                            if (Prefs.DevMode)
        //                            {
        //                                Log.Message($"[PawnControl] Inserted {subtreeToInject} before MainColonistBehaviorCore inside ThinkNode_ConditionalColonist at subnode index {j}.");
        //                            }

        //                            return true;
        //                        }
        //                    }
        //                }
        //            }

        //            if (node is ThinkNode_ConditionalPawnKind pawnKindNode && pawnKindNode.subNodes != null)
        //            {
        //                if (pawnKindNode is ThinkNode_ConditionalPawnKind kindCheck && kindCheck.pawnKind == PawnKindDefOf.WildMan)
        //                {
        //                    for (int j = 0; j < pawnKindNode.subNodes.Count; j++)
        //                    {
        //                        if (pawnKindNode.subNodes[j] is ThinkNode_Subtree subtree && treeDefField != null)
        //                        {
        //                            ThinkTreeDef treeDef = treeDefField.GetValue(subtree) as ThinkTreeDef;
        //                            if (treeDef != null && treeDef.defName == "MainWildManBehaviorCore")
        //                            {
        //                                pawnKindNode.subNodes.Insert(j, subtreeToInject);
        //                                if (Prefs.DevMode)
        //                                {
        //                                    Log.Message($"[PawnControl] Inserted {subtreeToInject} before MainWildManBehaviorCore inside ThinkNode_ConditionalColonist at subnode index {j}.");
        //                                }
        //                                return true;
        //                            }
        //                        }
        //                    }
        //                }
        //            }
        //        }
        //    }

        //    // 4. Find ThinkNode_Subtree with treeDef "LordDuty"
        //    for (int i = 0; i < thinkNode_Priority.subNodes.Count; i++)
        //    {
        //        if (thinkNode_Priority.subNodes[i] is ThinkNode_Subtree subtree && treeDefField != null)
        //        {
        //            var treeDef = treeDefField.GetValue(subtree) as ThinkTreeDef;
        //            if (treeDef != null && treeDef.defName == "LordDuty")
        //            {
        //                thinkNode_Priority.subNodes.Insert(i, subtreeToInject);
        //                if (Prefs.DevMode)
        //                {
        //                    Log.Message($"[PawnControl] Inserted {subtreeToInject} before LordDuty at index {i}.");
        //                }
        //                return true;
        //            }
        //        }
        //    }

        //    // 5. Fallback: add at the end
        //    thinkNode_Priority.subNodes.Add(subtreeToInject);
        //    return false;
        //}

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

        public static void ClearTagCaches()
        {
            _allowWorkTagCache.Clear();
            _blockWorkTagCache.Clear();
            _combinedWorkTagCache.Clear();
        }
   }
}