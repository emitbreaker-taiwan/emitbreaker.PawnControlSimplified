using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    public static class Utility_DebugManager
    {
        /// <summary>
        /// Dump Class use-cases
        /// </summary>
        /// 
        /// Normal dump (no expand):
        /// Utility_ThinkTreeDumper.SafeExpandAndDump(myPawn.thinker.MainThinkTree, false, "MainTree");
        /// 
        /// Expand subtrees first, then dump:
        /// Utility_ThinkTreeDumper.SafeExpandAndDump(myPawn.thinker.MainThinkTree, true, "MainTree");
        /// 
        /// Manual control:
        /// var clone = Utility_ThinkTreeDumper.CloneThinkTree(myPawn.thinker.MainThinkTree);
        /// Utility_ThinkTreeDumper.ExpandSubtrees(clone);
        /// Utility_ThinkTreeDumper.DumpThinkTree(clone, "CustomCloneExpanded");

        private static readonly FieldInfo TreeDefField = typeof(ThinkNode_Subtree).GetField("treeDef", BindingFlags.NonPublic | BindingFlags.Instance);
        
        /// <summary>
        /// Common ThinkTree dumper entry. Can dump normal or expanded tree depending on parameters.
        /// </summary>
        /// <param name="rootNode">The root ThinkNode to dump.</param>
        /// <param name="rootName">Optional name for the root node in the log.</param>
        public static void DumpThinkTree(ThinkNode rootNode, string rootName = "RootNode")
        {
            if (rootNode == null)
            {
                Log.Warning("[PawnControl] ThinkTreeDumper: Root node is null.");
                return;
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine($"[PawnControl] ThinkTree Dump Start - {rootName}");
            DumpNodeRecursive(rootNode, builder, 0);
            builder.AppendLine($"[PawnControl] ThinkTree Dump End - {rootName}");

            Log.Message(builder.ToString());
        }

        /// <summary>
        /// Recursively dumps a ThinkNode and its subnodes into a StringBuilder.
        /// </summary>
        /// <param name="node">Node to dump.</param>
        /// <param name="builder">StringBuilder to accumulate the dump.</param>
        /// <param name="indentLevel">Current depth level for indentation.</param>
        public static void DumpNodeRecursive(ThinkNode node, StringBuilder builder, int indentLevel)
        {
            if (node == null)
            {
                builder.AppendLine($"{Indent(indentLevel)}[Null Node]");
                return;
            }

            builder.AppendLine($"{Indent(indentLevel)}- {node.GetType().Name}");

            if (node.subNodes != null)
            {
                for (int i = 0; i < node.subNodes.Count; i++)
                {
                    DumpNodeRecursive(node.subNodes[i], builder, indentLevel + 1);
                }
            }
        }

        /// <summary>
        /// Expand all ThinkNode_Subtree into their referenced thinkRoot nodes. Does not dump, only modifies structure.
        /// </summary>
        /// <param name="rootNode">Root node to expand.</param>
        public static void ExpandSubtrees(ThinkNode rootNode)
        {
            if (rootNode == null)
            {
                Log.Warning("[PawnControl] ThinkTreeDumper: ExpandSubtrees called with null rootNode.");
                return;
            }

            ExpandSubtreesRecursive(rootNode);
        }

        /// <summary>
        /// Internal recursive logic to expand subtrees.
        /// </summary>
        public static void ExpandSubtreesRecursive(ThinkNode node)
        {
            if (node.subNodes == null || node.subNodes.Count == 0)
            {
                return;
            }

            for (int i = 0; i < node.subNodes.Count; i++)
            {
                ThinkNode child = node.subNodes[i];

                if (child is ThinkNode_Subtree subtreeNode)
                {
                    ThinkTreeDef treeDef = TreeDefField?.GetValue(subtreeNode) as ThinkTreeDef;

                    if (treeDef == null || treeDef.thinkRoot == null)
                    {
                        Log.Warning($"[PawnControl] ThinkTreeDumper: Found ThinkNode_Subtree with missing thinkRoot at {node.GetType().Name}.");
                        continue;
                    }

                    Log.Message($"[PawnControl] ThinkTreeDumper: Expanding subtree '{treeDef.defName}' into parent '{node.GetType().Name}'.");

                    node.subNodes[i] = treeDef.thinkRoot;

                    ExpandSubtreesRecursive(treeDef.thinkRoot);
                }
                else
                {
                    ExpandSubtreesRecursive(child);
                }
            }
        }

        /// <summary>
        /// Safe wrapper to clone, optionally expand, and then dump a ThinkTree.
        /// </summary>
        /// <param name="originalRoot">Original ThinkNode tree root.</param>
        /// <param name="expandBeforeDump">If true, will expand all subtrees before dumping.</param>
        /// <param name="label">Optional label for the root in log output.</param>
        public static void SafeExpandAndDump(ThinkNode originalRoot, bool expandBeforeDump, string label = "RootNode", bool showFullDump = true)
        {
            if (originalRoot == null)
            {
                Log.Warning("[PawnControl] ThinkTreeDumper: SafeExpandAndDump called with null originalRoot.");
                return;
            }

            ThinkNode clonedTree = CloneThinkTree(originalRoot);
            if (clonedTree == null)
            {
                Log.Warning("[PawnControl] ThinkTreeDumper: SafeExpandAndDump failed to clone original tree.");
                return;
            }

            if (expandBeforeDump)
            {
                ExpandSubtrees(clonedTree);
            }

            if (showFullDump)
            {
                DumpThinkTree(clonedTree, label + "_Expanded");
            }
            else
            {
                Log.Message($"[PawnControl] ThinkTree Dump: Successfully dumped ({label}), full output suppressed.");
            }
        }

        /// <summary>
        /// Safely clone, optionally expand, and dump a ThinkTree to a file without modifying the original tree.
        /// </summary>
        /// <param name="originalRoot">Original ThinkNode tree root.</param>
        /// <param name="expandBeforeDump">If true, will expand all subtrees before dumping.</param>
        /// <param name="filePath">Full file path to save the dump output.</param>
        public static void SafeExpandAndDumpToFile(ThinkNode originalRoot, bool expandBeforeDump, string filePath)
        {
            if (originalRoot == null)
            {
                Log.Warning("[PawnControl] ThinkTreeDumper: SafeExpandAndDumpToFile called with null originalRoot.");
                return;
            }

            if (string.IsNullOrEmpty(filePath))
            {
                Log.Warning("[PawnControl] ThinkTreeDumper: SafeExpandAndDumpToFile called with invalid file path.");
                return;
            }

            ThinkNode clonedTree = CloneThinkTree(originalRoot);
            if (clonedTree == null)
            {
                Log.Warning("[PawnControl] ThinkTreeDumper: SafeExpandAndDumpToFile failed to clone original tree.");
                return;
            }

            if (expandBeforeDump)
            {
                ExpandSubtrees(clonedTree);
            }

            try
            {
                StringBuilder builder = new StringBuilder();
                builder.AppendLine($"[PawnControl] ThinkTree Dump Start - {(expandBeforeDump ? "Expanded" : "Normal")}");

                DumpNodeRecursive(clonedTree, builder, 0);

                builder.AppendLine($"[PawnControl] ThinkTree Dump End");

                System.IO.File.WriteAllText(filePath, builder.ToString());

                Log.Message($"[PawnControl] ThinkTreeDumper: Dump written to file: {filePath}");
            }
            catch (System.Exception ex)
            {
                Log.Error($"[PawnControl] ThinkTreeDumper: Failed to write dump to file: {filePath}\n{ex}");
            }
        }

        /// <summary>
        /// Dumps all ThinkNodes for a pawn's MainThinkTree
        /// </summary>
        /// <param name="pawn">The pawn whose ThinkTree to dump</param>
        /// <param name="expandSubtrees">Whether to expand all subtrees (true) or keep them collapsed (false)</param>
        /// <param name="label">Optional label prefix for the log output</param>
        public static void DumpPawnThinkTree(Pawn pawn, bool expandSubtrees = true, string label = null)
        {
            if (pawn == null)
            {
                Log.Warning("[PawnControl] DumpPawnThinkTree: Pawn is null.");
                return;
            }

            if (pawn.thinker == null || pawn.thinker.MainThinkNodeRoot == null)
            {
                Log.Warning($"[PawnControl] DumpPawnThinkTree: {pawn.LabelShort} has no thinker or MainThinkNodeRoot.");
                return;
            }

            string pawnLabel = label ?? pawn.LabelShort;

            // Use the existing method to dump the entire think tree
            SafeExpandAndDump(pawn.thinker.MainThinkNodeRoot, expandSubtrees,
                             $"{pawnLabel}_MainThinkTree", true);

            // Optionally, you can also dump the constant think tree if needed
            if (pawn.def?.race?.thinkTreeConstant?.thinkRoot != null)
            {
                SafeExpandAndDump(pawn.def.race.thinkTreeConstant.thinkRoot, expandSubtrees,
                                 $"{pawnLabel}_ConstantThinkTree", true);
            }
        }

        /// <summary>
        /// Dumps all ThinkNodes for a pawn's MainThinkTree with additional source information
        /// </summary>
        /// <param name="pawn">The pawn whose ThinkTree to dump</param>
        public static void DumpPawnThinkTreeDetailed(Pawn pawn)
        {
            if (pawn == null)
            {
                Log.Warning("[PawnControl] DumpPawnThinkTreeDetailed: Pawn is null.");
                return;
            }

            if (pawn.thinker == null)
            {
                Log.Warning($"[PawnControl] DumpPawnThinkTreeDetailed: {pawn.LabelShort} has no thinker.");
                return;
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine($"[PawnControl] Detailed ThinkTree Dump for {pawn.LabelShort} ({pawn.def.defName})");
            builder.AppendLine("=====================================");

            // Dump basic pawn information
            builder.AppendLine($"Pawn Type: {pawn.def.defName}");
            builder.AppendLine($"Race: {(pawn.RaceProps.Humanlike ? "Humanlike" : pawn.RaceProps.Animal ? "Animal" : "Other")}");
            builder.AppendLine($"Has PawnControl Tags: {Utility_ThinkTreeManager.HasAllowOrBlockWorkTag(pawn.def)}");

            // Dump main ThinkTree information
            builder.AppendLine("\nMAIN THINK TREE:");
            if (pawn.thinker.MainThinkNodeRoot != null)
            {
                builder.AppendLine($"Original ThinkTreeDef: {pawn.def.race.thinkTreeMain?.defName ?? "Unknown"}");

                // Clone and expand for detailed dump
                ThinkNode clonedTree = CloneThinkTree(pawn.thinker.MainThinkNodeRoot);
                ExpandSubtrees(clonedTree);

                // Dump the expanded tree
                DumpNodeRecursive(clonedTree, builder, 1);
            }
            else
            {
                builder.AppendLine("  [No MainThinkNodeRoot found]");
            }

            // Dump constant ThinkTree if available
            if (pawn.def?.race?.thinkTreeConstant != null)
            {
                builder.AppendLine("\nCONSTANT THINK TREE:");
                builder.AppendLine($"ThinkTreeDef: {pawn.def.race.thinkTreeConstant.defName}");

                if (pawn.def.race.thinkTreeConstant.thinkRoot != null)
                {
                    // Clone and expand for detailed dump
                    ThinkNode clonedConstTree = CloneThinkTree(pawn.def.race.thinkTreeConstant.thinkRoot);
                    ExpandSubtrees(clonedConstTree);

                    // Dump the expanded tree
                    DumpNodeRecursive(clonedConstTree, builder, 1);
                }
                else
                {
                    builder.AppendLine("  [No thinkRoot in constant ThinkTree]");
                }
            }

            // Add some helpful debug context about the pawn
            builder.AppendLine("\nCURRENT STATE:");
            builder.AppendLine($"Current Job: {pawn.CurJob?.def.defName ?? "None"}");
            builder.AppendLine($"Current Activity: {pawn.CurJobDef?.reportString ?? "None"}");

            // Output the full log
            Log.Message(builder.ToString());
        }

        /// <summary>
        /// Clone an entire ThinkNode tree recursively.
        /// </summary>
        private static ThinkNode CloneThinkTree(ThinkNode original)
        {
            if (original == null)
            {
                return null;
            }

            ThinkNode clone = (ThinkNode)System.Activator.CreateInstance(original.GetType());

            if (original.subNodes != null && original.subNodes.Count > 0)
            {
                clone.subNodes = new List<ThinkNode>(original.subNodes.Count);
                for (int i = 0; i < original.subNodes.Count; i++)
                {
                    ThinkNode subClone = CloneThinkTree(original.subNodes[i]);
                    clone.subNodes.Add(subClone);
                }
            }

            return clone;
        }

        /// <summary>
        /// Create indentation string based on depth.
        /// </summary>
        private static string Indent(int level)
        {
            return new string(' ', level * 4);
        }

        /// <summary>
        /// Dumps all WorkGivers for a specific pawn, including their priorities and custom replacements.
        /// </summary>
        /// <param name="pawn">The pawn to inspect.</param>
        public static void DumpWorkGiversForPawn(Pawn pawn)
        {
            if (pawn == null || pawn.workSettings == null)
            {
                Log.Warning("[PawnControl] DumpWorkGiversForPawn: Pawn or WorkSettings is null.");
                return;
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine($"[PawnControl] WorkGivers Dump for {pawn.LabelCap}:");

            var workGivers = pawn.workSettings.WorkGiversInOrderNormal;
            if (workGivers == null || workGivers.Count == 0)
            {
                builder.AppendLine("  No WorkGivers assigned.");
            }
            else
            {
                foreach (var workGiver in workGivers)
                {
                    builder.AppendLine($"  - {workGiver.def.defName} (Class: {workGiver.def.giverClass.Name})");
                }
            }

            Log.Message(builder.ToString());
        }

        /// <summary>
        /// Dump status of Think Tree Injected Pawn.
        /// </summary>
        public static void DumpThinkerStatus(Pawn pawn)
        {
            Log.Message($"[PawnControl Debug] {pawn.LabelShort} thinker status:");
            Log.Message($"- thinker is null? {pawn.thinker == null}");
            Log.Message($"- thinker.MainThinkNodeRoot null? {pawn.thinker?.MainThinkNodeRoot == null}");
            Log.Message($"- pawn.downed? {pawn.Downed}");
            Log.Message($"- pawn.RaceProps.Animal? {pawn.RaceProps.Animal}");
            Log.Message($"- pawn.RaceProps.ToolUser? {pawn.RaceProps.ToolUser}");
            Log.Message($"- pawn.RaceProps.Humanlike? {pawn.RaceProps.Humanlike}");
            Log.Message($"- pawn.RaceProps.IsMechanoid? {pawn.RaceProps.IsMechanoid}");
            Log.Message($"- pawn.IsColonist? {pawn.IsColonist}");
            Log.Message($"- pawn.IsPrisoner? {pawn.IsPrisoner}");
            Log.Message($"- pawn.IsSlave? {pawn.IsSlave}");
            Log.Message($"- pawn.IsColonyMech? {pawn.IsColonyMech}");
            Log.Message($"- pawn.IsWildMan? {pawn.IsWildMan()}");
            Log.Message($"- pawn.jobs.curDriver? {(pawn.jobs?.curDriver != null ? pawn.jobs.curDriver.ToString() : "null")}");
            Log.Message($"- pawn.jobs.curJob? {(pawn.jobs?.curJob != null ? pawn.jobs.curJob.def.defName : "null")}");
        }

        /// <summary>
        /// Debug messages for Utility_TagManager.HasTag
        /// </summary>
        public static void TagManager_HasTag_HasTag(ThingDef def, string tag, bool hasTag)
        {
            //Log.Message($"[PawnControl] Checking tag '{tag}' for def={def.defName}: {hasTag}");
        }

        /// <summary>
        /// Debug messages for Utility_ThinkTreeManager.HasAllowOrBlockWorkTag
        /// </summary>
        public static void ThinkTreeManager_HasTag(ThingDef def, bool result)
        {
            //Log.Message($"[PawnControl] Computed HasAllowOrBlockWorkTag for {def.defName}: {result}");
        }

        public static class StatMutationLogger
        {
            private static readonly Dictionary<Pawn, Dictionary<StatDef, float>> pawnStatMutations = new Dictionary<Pawn, Dictionary<StatDef, float>>();
            private static readonly Dictionary<ThingDef, Dictionary<StatDef, float>> raceStatBaseCache = new Dictionary<ThingDef, Dictionary<StatDef, float>>();

            public static void LogStatMutation(Pawn pawn, StatDef statDef, float value)
            {
                if (pawn == null || statDef == null) return;

                if (!pawnStatMutations.TryGetValue(pawn, out var dict))
                    pawnStatMutations[pawn] = dict = new Dictionary<StatDef, float>();

                dict[statDef] = value;

                if (Prefs.DevMode)
                    Log.Message($"[PawnControl] Stat mutation: {pawn.LabelShort} -> {statDef.defName} = {value:F2}");
            }

            public static void LogRaceBase(Pawn pawn)
            {
                if (pawn?.def == null || raceStatBaseCache.ContainsKey(pawn.def)) return;

                var dict = new Dictionary<StatDef, float>();
                foreach (StatDef stat in DefDatabase<StatDef>.AllDefsListForReading)
                {
                    try { dict[stat] = pawn.GetStatValue(stat, false); } catch { }
                }
                raceStatBaseCache[pawn.def] = dict;

                if (Prefs.DevMode)
                    Log.Message($"[PawnControl] Cached base stats for race {pawn.def.defName}");
            }

            public static IReadOnlyDictionary<StatDef, float> GetPawnStats(Pawn pawn)
                => pawnStatMutations.TryGetValue(pawn, out var dict) ? dict : null;

            public static IReadOnlyDictionary<StatDef, float> GetRaceStats(ThingDef race)
                => raceStatBaseCache.TryGetValue(race, out var dict) ? dict : null;
        }

        public static class StatMutationValidator
        {
            /// <summary>
            /// Validates current pawn stats against expected values.
            /// Filters out stats that do not apply to pawns (e.g., building-only stats).
            /// </summary>
            public static void Validate(Pawn pawn)
            {
                if (pawn == null || pawn.def == null || pawn.RaceProps == null)
                {
                    Log.Warning("[PawnControl] Validation failed: null pawn or pawn.def.");
                    return;
                }

                Log.Message($"[PawnControl] === Stat Mutation Validation for {pawn.LabelShort} ({pawn.def.defName}) ===");

                foreach (var stat in DefDatabase<StatDef>.AllDefs)
                {
                    // ✅ Skip stats that are clearly not intended for pawns
                    if (stat.defaultBaseValue < 0 ||
                            (stat.category != StatCategoryDefOf.PawnWork
                            && stat.category != StatCategoryDefOf.PawnCombat
                            && stat.category != StatCategoryDefOf.BasicsPawn
                            && stat.category != StatCategoryDefOf.PawnSocial) ||
                        (stat.Worker?.IsDisabledFor(pawn) ?? true)) // skip if disabled or no worker
                    {
                        continue;
                    }

                    try
                    {
                        float value = pawn.GetStatValue(stat, applyPostProcess: false);
                        float raceValue = pawn.def.GetStatValueAbstract(stat, null);
                        Log.Message($" - {stat.defName}: {value} (Race Base: {raceValue})");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[PawnControl] Stat '{stat.defName}' failed on {pawn.LabelShort}: {ex.Message}");
                    }
                }

                Log.Message("[PawnControl] === End of Validation ===");
            }


            public static void ValidateAll()
            {
                foreach (Map map in Find.Maps)
                    foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                        Validate(pawn);
            }
        }

        public static void LogRaceAndPawnStats(Pawn pawn)
        {
            if (pawn == null || pawn.def == null || pawn.RaceProps == null)
            {
                Log.Warning("[PawnControl] LogRaceAndPawnStats failed: pawn or def is null.");
                return;
            }

            Log.Message($"[PawnControl] === Logging Race and Pawn Stat Differences for {pawn.LabelShort} ({pawn.def.defName}) ===");

            foreach (var stat in DefDatabase<StatDef>.AllDefs)
            {
                // ✅ Skip clearly incompatible stats
                if (stat.defaultBaseValue < 0 ||
                        (stat.category != StatCategoryDefOf.PawnWork 
                        && stat.category != StatCategoryDefOf.PawnCombat 
                        && stat.category != StatCategoryDefOf.BasicsPawn
                        && stat.category != StatCategoryDefOf.PawnSocial) ||
                    (stat.Worker?.IsDisabledFor(pawn) ?? true)) // skip if disabled or no worker
                {
                    continue;
                }

                try
                {
                    float pawnValue = pawn.GetStatValue(stat, applyPostProcess: false);
                    float raceValue = pawn.def.GetStatValueAbstract(stat, null);

                    if (!Mathf.Approximately(pawnValue, raceValue))
                    {
                        Log.Message($" - {stat.defName}: Pawn = {pawnValue}, Race Base = {raceValue}");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[PawnControl] Failed reading stat '{stat.defName}' for {pawn.LabelShort}: {ex.Message}");
                }
            }

            Log.Message("[PawnControl] === End Log ===");
        }

        public static void RunStatValidator()
        {
            StatMutationValidator.ValidateAll();
        }

        /// <summary>
        /// Diagnoses WorkGiver initialization issues for a pawn.
        /// Logs detailed information about WorkGiver lists and related settings to help
        /// troubleshoot why work scans might not be triggering.
        /// </summary>
        public static void DiagnoseWorkGiversForPawn(Pawn pawn)
        {
            if (pawn?.workSettings == null || !Prefs.DevMode)
                return;

            try
            {
                // Log WorkGiver status
                var normalList = pawn.workSettings.WorkGiversInOrderNormal;
                var emergencyList = pawn.workSettings.WorkGiversInOrderEmergency;

                Log.Message($"[DEBUG] Work scan diagnostic for {pawn.LabelCap}:");
                Log.Message($"- Initialized: {pawn.workSettings.Initialized}");
                Log.Message($"- EverWork: {pawn.workSettings.EverWork}");
                Log.Message($"- Normal list count: {normalList?.Count ?? 0}");
                Log.Message($"- Emergency list count: {emergencyList?.Count ?? 0}");

                // Check if the pawn has valid mod extension
                var modExt = Utility_CacheManager.GetModExtension(pawn.def);
                Log.Message($"- Has mod extension: {modExt != null}");

                // Check if ThinkTree is properly configured
                Log.Message($"- Main ThinkTree: {pawn.def.race?.thinkTreeMain?.defName}");

                // Verify if HasAllowWorkTag returns the expected value
                Log.Message($"- HasAllowWorkTag: {Utility_ThinkTreeManager.HasAllowWorkTag(pawn.def)}");

                // Check think tree status
                Log.Message($"- Pawn thinker: {pawn.thinker != null}");
                Log.Message($"- Current job: {pawn.jobs?.curJob?.def?.defName ?? "none"}");
            }
            catch (Exception ex)
            {
                Log.Error($"[ERROR] Diagnostic failed: {ex}");
            }
        }

        // Add to Utility_DebugManager
        public static void DiagnoseAllPawnSystems(Pawn pawn)
        {
            if (!Prefs.DevMode || pawn == null)
                return;

            Log.Message($"[PawnControl] Starting comprehensive diagnostic for {pawn.LabelCap}");

            // Log basic pawn info
            Log.Message($"- Race: {pawn.def.defName}");
            Log.Message($"- Mod extension: {(Utility_CacheManager.GetModExtension(pawn.def) != null ? "Present" : "Missing")}");
            Log.Message($"- Work tags: AllowWork={Utility_ThinkTreeManager.HasAllowWorkTag(pawn.def)}, HasBlock={Utility_ThinkTreeManager.HasBlockWorkTag(pawn.def)}");

            // Check WorkSettings
            if (pawn.workSettings != null)
            {
                Log.Message($"- WorkSettings initialized: {pawn.workSettings.Initialized}");
                Log.Message($"- EverWork: {pawn.workSettings.EverWork}");
                Log.Message($"- WorkGiversNormal count: {pawn.workSettings.WorkGiversInOrderNormal?.Count ?? 0}");
            }
            else
            {
                Log.Message("- WorkSettings: NULL");
            }

            // Validate ThinkTree
            Utility_ThinkTreeManager.ValidateThinkTree(pawn);

            // Check current job state
            Log.Message($"- Current job: {pawn.jobs?.curJob?.def?.defName ?? "None"}");
            Log.Message($"- Job queue: {pawn.jobs?.jobQueue?.Count ?? 0} items");

            Log.Message("[PawnControl] Comprehensive diagnostic complete");
        }
    }
}
