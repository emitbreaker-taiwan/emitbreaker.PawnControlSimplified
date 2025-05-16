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
        public static bool ShouldLog()
        {
            // Only log if both RimWorld DevMode AND our mod's debug mode are enabled
            var settings = LoadedModManager.GetMod<Mod_SimpleNonHumanlikePawnControl>().GetSettings<ModSettings_SimpleNonHumanlikePawnControl>();
            return Prefs.DevMode && settings.debugMode;
        }

        public static bool ShouldLogDetailed()
        {
            // Only log if both RimWorld DevMode AND our mod's detailed debug mode are enabled
            var settings = LoadedModManager.GetMod<Mod_SimpleNonHumanlikePawnControl>().GetSettings<ModSettings_SimpleNonHumanlikePawnControl>();
            return Prefs.DevMode && settings.debugMode && settings.detailedDebugMode;
        }

        // Helper method for consistent logging
        public static void LogNormal(string message)
        {
            if (ShouldLog())
            {
                Log.Message($"[PawnControl] {message}");
            }
        }

        // Helper method for warning logs
        public static void LogWarning(string message)
        {
            if (ShouldLog())
            {
                Log.Warning($"[PawnControl] {message}");
            }
        }

        // Helper method for error logs (these often should show regardless of debug mode)
        public static void LogError(string message)
        {
            // Errors usually should be logged regardless of debug mode
            Log.Error($"[PawnControl] {message}");
        }

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
                LogWarning("ThinkTreeDumper: Root node is null.");
                return;
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine($"[PawnControl] ThinkTree Dump Start - {rootName}");
            DumpNodeRecursive(rootNode, builder, 0);
            builder.AppendLine($"[PawnControl] ThinkTree Dump End - {rootName}");

            LogNormal(builder.ToString());
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
                LogWarning("ThinkTreeDumper: ExpandSubtrees called with null rootNode.");
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
                        LogWarning($"ThinkTreeDumper: Found ThinkNode_Subtree with missing thinkRoot at {node.GetType().Name}.");
                        continue;
                    }

                    LogNormal($"ThinkTreeDumper: Expanding subtree '{treeDef.defName}' into parent '{node.GetType().Name}'.");

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
                LogWarning("ThinkTreeDumper: SafeExpandAndDump called with null originalRoot.");
                return;
            }

            ThinkNode clonedTree = CloneThinkTree(originalRoot);
            if (clonedTree == null)
            {
                LogWarning("ThinkTreeDumper: SafeExpandAndDump failed to clone original tree.");
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
                LogWarning($"ThinkTree Dump: Successfully dumped ({label}), full output suppressed.");
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
                LogWarning("ThinkTreeDumper: SafeExpandAndDumpToFile called with null originalRoot.");
                return;
            }

            if (string.IsNullOrEmpty(filePath))
            {
                LogWarning("ThinkTreeDumper: SafeExpandAndDumpToFile called with invalid file path.");
                return;
            }

            ThinkNode clonedTree = CloneThinkTree(originalRoot);
            if (clonedTree == null)
            {
                LogWarning("ThinkTreeDumper: SafeExpandAndDumpToFile failed to clone original tree.");
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

                LogNormal($"ThinkTreeDumper: Dump written to file: {filePath}");
            }
            catch (System.Exception ex)
            {
                LogError($"ThinkTreeDumper: Failed to write dump to file: {filePath}\n{ex}");
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
                LogWarning("DumpPawnThinkTree: Pawn is null.");
                return;
            }

            if (pawn.thinker == null || pawn.thinker.MainThinkNodeRoot == null)
            {
                LogWarning($"DumpPawnThinkTree: {pawn.LabelShort} has no thinker or MainThinkNodeRoot.");
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
                LogWarning("DumpPawnThinkTreeDetailed: Pawn is null.");
                return;
            }

            if (pawn.thinker == null)
            {
                LogWarning($"DumpPawnThinkTreeDetailed: {pawn.LabelShort} has no thinker.");
                return;
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine($"Detailed ThinkTree Dump for {pawn.LabelShort} ({pawn.def.defName})");
            builder.AppendLine("=====================================");

            // Dump basic pawn information
            builder.AppendLine($"Pawn Type: {pawn.def.defName}");
            builder.AppendLine($"Race: {(pawn.RaceProps.Humanlike ? "Humanlike" : pawn.RaceProps.Animal ? "Animal" : "Other")}");
            builder.AppendLine($"Has PawnControl Tags: {Utility_ThinkTreeManager.HasAllowOrBlockWorkTag(pawn)}");

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
            LogNormal(builder.ToString());
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
                LogWarning("DumpWorkGiversForPawn: Pawn or WorkSettings is null.");
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

            LogNormal(builder.ToString());
        }

        /// <summary>
        /// Dump status of Think Tree Injected Pawn.
        /// </summary>
        public static void DumpThinkerStatus(Pawn pawn)
        {
            LogNormal($"{pawn.LabelShort} thinker status:");
            LogNormal($"- thinker is null? {pawn.thinker == null}");
            LogNormal($"- thinker.MainThinkNodeRoot null? {pawn.thinker?.MainThinkNodeRoot == null}");
            LogNormal($"- pawn.downed? {pawn.Downed}");
            LogNormal($"- pawn.RaceProps.Animal? {pawn.RaceProps.Animal}");
            LogNormal($"- pawn.RaceProps.ToolUser? {pawn.RaceProps.ToolUser}");
            LogNormal($"- pawn.RaceProps.Humanlike? {pawn.RaceProps.Humanlike}");
            LogNormal($"- pawn.RaceProps.IsMechanoid? {pawn.RaceProps.IsMechanoid}");
            LogNormal($"- pawn.IsColonist? {pawn.IsColonist}");
            LogNormal($"- pawn.IsPrisoner? {pawn.IsPrisoner}");
            LogNormal($"- pawn.IsSlave? {pawn.IsSlave}");
            LogNormal($"- pawn.IsColonyMech? {pawn.IsColonyMech}");
            LogNormal($"- pawn.IsWildMan? {pawn.IsWildMan()}");
            LogNormal($"- pawn.jobs.curDriver? {(pawn.jobs?.curDriver != null ? pawn.jobs.curDriver.ToString() : "null")}");
            LogNormal($"- pawn.jobs.curJob? {(pawn.jobs?.curJob != null ? pawn.jobs.curJob.def.defName : "null")}");
        }

        /// <summary>
        /// Debug messages for Utility_ThinkTreeManager.HasAllowOrBlockWorkTag
        /// </summary>
        public static void ThinkTreeManager_HasTag(ThingDef def, bool result)
        {
            LogNormal($"Computed HasAllowOrBlockWorkTag for {def.defName}: {result}");
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
                
                LogNormal($"Stat mutation: {pawn.LabelShort} -> {statDef.defName} = {value:F2}");
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
                
                LogNormal($"Cached base stats for race {pawn.def.defName}");
            }

            public static IReadOnlyDictionary<StatDef, float> GetPawnStats(Pawn pawn) => pawnStatMutations.TryGetValue(pawn, out var dict) ? dict : null;

            public static IReadOnlyDictionary<StatDef, float> GetRaceStats(ThingDef race) => raceStatBaseCache.TryGetValue(race, out var dict) ? dict : null;
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
                    LogWarning("Validation failed: null pawn or pawn.def.");
                    return;
                }

                LogNormal($"=== Stat Mutation Validation for {pawn.LabelShort} ({pawn.def.defName}) ===");

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
                        LogNormal($" - {stat.defName}: {value} (Race Base: {raceValue})");
                    }
                    catch (Exception ex)
                    {
                        LogWarning($"Stat '{stat.defName}' failed on {pawn.LabelShort}: {ex.Message}");
                    }
                }

                LogNormal("=== End of Validation ===");
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
                LogWarning("LogRaceAndPawnStats failed: pawn or def is null.");
                return;
            }

            LogNormal($"=== Logging Race and Pawn Stat Differences for {pawn.LabelShort} ({pawn.def.defName}) ===");

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
                        LogNormal($" - {stat.defName}: Pawn = {pawnValue}, Race Base = {raceValue}");
                    }
                }
                catch (Exception ex)
                {
                    LogWarning($"Failed reading stat '{stat.defName}' for {pawn.LabelShort}: {ex.Message}");
                }
            }

            LogNormal("=== End Log ===");
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

                LogNormal($"[DEBUG] Work scan diagnostic for {pawn.LabelCap}:");
                LogNormal($"- Initialized: {pawn.workSettings.Initialized}");
                LogNormal($"- EverWork: {pawn.workSettings.EverWork}");
                LogNormal($"- Normal list count: {normalList?.Count ?? 0}");
                LogNormal($"- Emergency list count: {emergencyList?.Count ?? 0}");

                // Check if the pawn has valid mod extension
                var modExtension = Utility_UnifiedCache.GetModExtension(pawn.def);
                LogNormal($"- Has mod extension: {modExtension != null}");

                // Check if ThinkTree is properly configured
                LogNormal($"- Main ThinkTree: {pawn.def.race?.thinkTreeMain?.defName}");

                // Verify if HasAllowWorkTag returns the expected value
                LogNormal($"- HasAllowWorkTag: {Utility_ThinkTreeManager.HasAllowWorkTag(pawn)}");

                // Check think tree status
                LogNormal($"- Pawn thinker: {pawn.thinker != null}");
                LogNormal($"- Current job: {pawn.jobs?.curJob?.def?.defName ?? "none"}");
            }
            catch (Exception ex)
            {
                LogError($"[ERROR] Diagnostic failed: {ex}");
            }
        }

        // Add to Utility_DebugManager
        public static void DiagnoseAllPawnSystems(Pawn pawn)
        {
            if (!Prefs.DevMode || pawn == null)
                return;

            LogNormal($"Starting comprehensive diagnostic for {pawn.LabelCap}");

            // Log basic pawn info
            LogNormal($"- Race: {pawn.def.defName}");
            LogNormal($"- Mod extension: {(Utility_UnifiedCache.GetModExtension(pawn.def) != null ? "Present" : "Missing")}");
            LogNormal($"- Work tags: AllowWork={Utility_ThinkTreeManager.HasAllowWorkTag(pawn)}, HasBlock={Utility_ThinkTreeManager.HasBlockWorkTag(pawn)}");

            // Check WorkSettings
            if (pawn.workSettings != null)
            {
                LogNormal($"- WorkSettings initialized: {pawn.workSettings.Initialized}");
                LogNormal($"- EverWork: {pawn.workSettings.EverWork}");
                LogNormal($"- WorkGiversNormal count: {pawn.workSettings.WorkGiversInOrderNormal?.Count ?? 0}");
            }
            else
            {
                LogNormal("- WorkSettings: NULL");
            }

            // Validate ThinkTree
            Utility_ThinkTreeManager.ValidateThinkTree(pawn);

            // Check current job state
            LogNormal($"- Current job: {pawn.jobs?.curJob?.def?.defName ?? "None"}");
            LogNormal($"- Job queue: {pawn.jobs?.jobQueue?.Count ?? 0} items");

            LogNormal("Comprehensive diagnostic complete");
        }
    }
}
