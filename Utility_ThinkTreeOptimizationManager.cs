using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using emitbreaker.PawnControl;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Provides optimization for ThinkTree structures by reducing redundant checks,
    /// implementing dynamic JobGiver bypass, and adding hierarchical decision making.
    /// </summary>
    public static class Utility_ThinkTreeOptimizationManager
    {
        #region Shared State & Caching

        // Cache of condition evaluations to avoid redundant checks
        private static readonly Dictionary<Type, Dictionary<int, Dictionary<string, bool>>> _conditionCache =
            new Dictionary<Type, Dictionary<int, Dictionary<string, bool>>>();

        // Cache of recently returned jobs for each pawn to avoid redundant work
        private static readonly Dictionary<int, Dictionary<Type, JobReport>> _recentJobCache = 
            new Dictionary<int, Dictionary<Type, JobReport>>();

        // Timestamp of last condition evaluation
        private static readonly Dictionary<int, Dictionary<string, int>> _lastConditionCheckTicks =
            new Dictionary<int, Dictionary<string, int>>();

        // Default cache durations
        private const int CONDITION_CACHE_DURATION = 180; // 3 seconds
        private const int JOB_CACHE_DURATION = 60;       // 1 second

        /// <summary>
        /// Structure to store job results and metadata
        /// </summary>
        private class JobReport
        {
            public Job Job { get; set; }
            public int Timestamp { get; set; }
            public bool WasSuccessful => Job != null;
            
            public JobReport(Job job, int timestamp)
            {
                Job = job;
                Timestamp = timestamp;
            }
        }
        
        #endregion

        #region Condition Management

        /// <summary>
        /// A condition that can be evaluated to determine if a JobGiver should be bypassed
        /// </summary>
        public delegate bool BypassCondition(Pawn pawn);

        /// <summary>
        /// Evaluates a condition with caching to avoid redundant checks
        /// </summary>
        /// <param name="pawn">The pawn to evaluate the condition for</param>
        /// <param name="conditionId">A unique identifier for this condition</param>
        /// <param name="condition">The condition to evaluate</param>
        /// <param name="cacheDuration">How long the result should be cached (in ticks)</param>
        /// <returns>Whether the condition is met</returns>
        public static bool EvaluateCondition(Pawn pawn, string conditionId, BypassCondition condition, int cacheDuration = CONDITION_CACHE_DURATION)
        {
            if (pawn == null || condition == null || string.IsNullOrEmpty(conditionId))
                return false;

            int pawnId = pawn.thingIDNumber;
            int currentTick = Find.TickManager.TicksGame;

            // Initialize cache structures if needed
            Type pawnType = pawn.GetType();
            if (!_conditionCache.TryGetValue(pawnType, out var pawnTypeCache))
            {
                pawnTypeCache = new Dictionary<int, Dictionary<string, bool>>();
                _conditionCache[pawnType] = pawnTypeCache;
            }

            if (!pawnTypeCache.TryGetValue(pawnId, out var pawnConditions))
            {
                pawnConditions = new Dictionary<string, bool>();
                pawnTypeCache[pawnId] = pawnConditions;
            }

            if (!_lastConditionCheckTicks.TryGetValue(pawnId, out var checkTimes))
            {
                checkTimes = new Dictionary<string, int>();
                _lastConditionCheckTicks[pawnId] = checkTimes;
            }

            // Check if we can use cached result
            if (pawnConditions.TryGetValue(conditionId, out bool cachedResult))
            {
                // Check if cache is still valid
                int lastCheck = checkTimes.GetValueSafe(conditionId, 0);
                if (currentTick - lastCheck <= cacheDuration)
                {
                    return cachedResult;
                }
            }

            // Evaluate the condition
            bool result = condition(pawn);

            // Cache the result
            pawnConditions[conditionId] = result;
            checkTimes[conditionId] = currentTick;

            return result;
        }

        /// <summary>
        /// Registers common bypass conditions for a JobGiver
        /// </summary>
        public static void RegisterBypassConditions<TJobGiver>(BypassCondition[] conditions) where TJobGiver : ThinkNode_JobGiver
        {
            Type jobGiverType = typeof(TJobGiver);
            // Register conditions with the JobGiver registry for lookup
            Utility_JobGiverManagerOld.RegisterBypassConditions(jobGiverType, conditions);
        }
        
        /// <summary>
        /// Determines if a JobGiver should be bypassed for the current pawn
        /// </summary>
        public static bool ShouldBypassJobGiver(Type jobGiverType, Pawn pawn)
        {
            var conditions = Utility_JobGiverManagerOld.GetBypassConditions(jobGiverType);
            if (conditions == null || conditions.Length == 0)
                return false;

            string conditionGroupId = $"{jobGiverType.Name}_Bypass";
            
            // Evaluate all conditions as a group with caching
            return EvaluateCondition(pawn, conditionGroupId, p => 
            {
                foreach (var condition in conditions)
                {
                    if (condition(p))
                        return true; // Any true condition means bypass
                }
                return false;
            });
        }

        #endregion

        #region Common Bypass Conditions

        /// <summary>
        /// Common condition factory methods for easy reuse
        /// </summary>
        public static class CommonConditions
        {
            /// <summary>
            /// Creates a condition that bypasses if the pawn has below a certain health percentage
            /// </summary>
            public static BypassCondition BelowHealthPercent(float threshold)
            {
                return pawn => pawn?.health?.summaryHealth?.SummaryHealthPercent < threshold;
            }

            /// <summary>
            /// Creates a condition that bypasses if a need is below a certain level
            /// </summary>
            public static BypassCondition NeedBelowLevel(NeedDef needDef, float threshold)
            {
                return pawn =>
                {
                    if (pawn?.needs == null) return false;
                    var need = pawn.needs.TryGetNeed(needDef);
                    return need != null && need.CurLevelPercentage < threshold;
                };
            }

            /// <summary>
            /// Creates a condition that bypasses if any of a set of hediffs are present
            /// </summary>
            public static BypassCondition HasHediffs(params HediffDef[] hediffs)
            {
                return pawn =>
                {
                    if (pawn?.health?.hediffSet == null) return false;
                    foreach (var hediff in hediffs)
                    {
                        if (pawn.health.hediffSet.HasHediff(hediff))
                            return true;
                    }
                    return false;
                };
            }

            /// <summary>
            /// Creates a condition that bypasses if the pawn is in any mental state
            /// </summary>
            public static BypassCondition InMentalState()
            {
                return pawn => pawn?.MentalState != null;
            }

            /// <summary>
            /// Creates a condition that bypasses if the pawn is drafted
            /// </summary>
            public static BypassCondition IsDrafted()
            {
                return pawn => pawn?.Drafted ?? false;
            }

            /// <summary>
            /// Creates a condition that bypasses if the pawn lacks necessary skills
            /// </summary>
            public static BypassCondition LacksSkill(SkillDef skillDef, int minLevel)
            {
                return pawn =>
                {
                    if (pawn?.skills == null) return false;
                    var skill = pawn.skills.GetSkill(skillDef);
                    return skill == null || skill.Level < minLevel;
                };
            }

            /// <summary>
            /// Creates a condition that bypasses if the work type is disabled for the pawn
            /// </summary>
            public static BypassCondition WorkTypeDisabled(string workTypeName)
            {
                return pawn =>
                {
                    if (string.IsNullOrEmpty(workTypeName) || pawn == null) return false;

                    WorkTypeDef workType = Utility_WorkTypeManager.Named(workTypeName);
                    if (workType == null) return true; // If work type doesn't exist, bypass

                    return !Utility_TagManager.WorkTypeSettingEnabled(pawn, workType);
                };
            }

            /// <summary>
            /// Creates a condition that bypasses if there are no potential targets on the map
            /// </summary>
            public static BypassCondition NoTargetsPresent<T>(Func<Map, List<T>> targetGetter) where T : Thing
            {
                return pawn =>
                {
                    if (pawn?.Map == null) return true;
                    var targets = targetGetter(pawn.Map);
                    return targets == null || targets.Count == 0;
                };
            }
        }

        #endregion

        #region Job Result Caching

        /// <summary>
        /// Tries to get a cached job result for a specific JobGiver and pawn
        /// </summary>
        /// <param name="jobGiverType">The type of JobGiver</param>
        /// <param name="pawn">The pawn</param>
        /// <param name="job">The cached job, if available</param>
        /// <returns>True if a valid cached job was found</returns>
        public static bool TryGetCachedJob(Type jobGiverType, Pawn pawn, out Job job)
        {
            job = null;
            if (jobGiverType == null || pawn == null) return false;

            int pawnId = pawn.thingIDNumber;
            int currentTick = Find.TickManager.TicksGame;

            // Try to get cached job
            if (_recentJobCache.TryGetValue(pawnId, out var jobGiverJobs))
            {
                if (jobGiverJobs.TryGetValue(jobGiverType, out var report))
                {
                    // Check if cache is still valid
                    if (currentTick - report.Timestamp <= JOB_CACHE_DURATION)
                    {
                        job = report.Job;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Caches a job result for a specific JobGiver and pawn
        /// </summary>
        public static void CacheJobResult(Type jobGiverType, Pawn pawn, Job job)
        {
            if (jobGiverType == null || pawn == null) return;

            int pawnId = pawn.thingIDNumber;
            int currentTick = Find.TickManager.TicksGame;

            // Initialize cache if needed
            if (!_recentJobCache.TryGetValue(pawnId, out var jobGiverJobs))
            {
                jobGiverJobs = new Dictionary<Type, JobReport>();
                _recentJobCache[pawnId] = jobGiverJobs;
            }

            // Cache the result
            jobGiverJobs[jobGiverType] = new JobReport(job, currentTick);
        }

        #endregion

        #region Hierarchical Decision Making

        /// <summary>
        /// A node in the hierarchical decision tree
        /// </summary>
        public class DecisionNode
        {
            public string Id { get; }
            public BypassCondition Condition { get; }
            public List<Type> JobGiverTypes { get; } = new List<Type>();
            public List<DecisionNode> Children { get; } = new List<DecisionNode>();
            
            public DecisionNode(string id, BypassCondition condition = null)
            {
                Id = id;
                Condition = condition;
            }

            /// <summary>
            /// Adds a JobGiver to this decision node
            /// </summary>
            public DecisionNode AddJobGiver<T>() where T : ThinkNode_JobGiver
            {
                JobGiverTypes.Add(typeof(T));
                return this;
            }

            /// <summary>
            /// Adds multiple JobGivers to this decision node
            /// </summary>
            public DecisionNode AddJobGivers(params Type[] jobGiverTypes)
            {
                foreach (var type in jobGiverTypes)
                {
                    if (typeof(ThinkNode_JobGiver).IsAssignableFrom(type))
                        JobGiverTypes.Add(type);
                }
                return this;
            }

            /// <summary>
            /// Adds a child node to this decision node
            /// </summary>
            public DecisionNode AddChild(DecisionNode child)
            {
                Children.Add(child);
                return this;
            }

            /// <summary>
            /// Creates and adds a new child node to this decision node
            /// </summary>
            public DecisionNode CreateChild(string id, BypassCondition condition = null)
            {
                var child = new DecisionNode(id, condition);
                Children.Add(child);
                return child;
            }
        }

        /// <summary>
        /// A hierarchical decision tree for optimized JobGiver selection
        /// </summary>
        public class DecisionTree
        {
            public DecisionNode Root { get; }
            private readonly Dictionary<string, DecisionNode> _nodesById = new Dictionary<string, DecisionNode>();

            public DecisionTree(string rootId)
            {
                Root = new DecisionNode(rootId);
                _nodesById[rootId] = Root;
            }

            /// <summary>
            /// Gets a node by ID
            /// </summary>
            public DecisionNode GetNode(string id)
            {
                if (string.IsNullOrEmpty(id)) return null;
                return _nodesById.GetValueSafe(id, null);
            }

            /// <summary>
            /// Adds a node to the tree
            /// </summary>
            public void AddNode(DecisionNode node, string parentId = null)
            {
                if (node == null) return;

                // Add to lookup
                _nodesById[node.Id] = node;

                // Add to parent if specified
                if (!string.IsNullOrEmpty(parentId))
                {
                    var parent = GetNode(parentId);
                    parent?.AddChild(node);
                }
            }

            /// <summary>
            /// Evaluates the decision tree for a pawn and returns relevant JobGiver types
            /// </summary>
            public List<Type> GetApplicableJobGivers(Pawn pawn)
            {
                var result = new List<Type>();
                EvaluateNode(Root, pawn, result);
                return result;
            }

            /// <summary>
            /// Recursively evaluates a node and its children
            /// </summary>
            private void EvaluateNode(DecisionNode node, Pawn pawn, List<Type> result)
            {
                if (node == null || pawn == null) return;

                // Check if node condition passes
                bool conditionMet = node.Condition == null || 
                    EvaluateCondition(pawn, $"DecisionTree_{node.Id}", node.Condition);

                if (conditionMet)
                {
                    // Add this node's JobGivers
                    result.AddRange(node.JobGiverTypes);

                    // Evaluate children
                    foreach (var child in node.Children)
                    {
                        EvaluateNode(child, pawn, result);
                    }
                }
            }
        }

        /// <summary>
        /// Registry of decision trees for different pawn types
        /// </summary>
        private static readonly Dictionary<Type, DecisionTree> _decisionTrees = new Dictionary<Type, DecisionTree>();

        /// <summary>
        /// Registers a decision tree for a specific pawn type
        /// </summary>
        public static void RegisterDecisionTree<TPawn>(DecisionTree tree) where TPawn : Pawn
        {
            if (tree == null) return;
            _decisionTrees[typeof(TPawn)] = tree;
        }

        /// <summary>
        /// Gets applicable JobGiver types for a pawn based on its decision tree
        /// </summary>
        public static List<Type> GetApplicableJobGivers(Pawn pawn)
        {
            if (pawn == null) return new List<Type>();

            // Find the most specific tree that applies to this pawn
            Type pawnType = pawn.GetType();
            DecisionTree tree = null;

            // Look for exact match first
            if (_decisionTrees.TryGetValue(pawnType, out tree))
                return tree.GetApplicableJobGivers(pawn);

            // Look for base types
            foreach (var entry in _decisionTrees)
            {
                if (entry.Key.IsAssignableFrom(pawnType))
                {
                    return entry.Value.GetApplicableJobGivers(pawn);
                }
            }

            // No tree found - return empty list
            return new List<Type>();
        }

        #endregion

        #region Memory Management

        /// <summary>
        /// Cleans up cached data for a pawn
        /// </summary>
        public static void CleanupPawnData(Pawn pawn)
        {
            if (pawn == null) return;
            
            int pawnId = pawn.thingIDNumber;
            
            // Clean up condition cache
            foreach (var typeCache in _conditionCache.Values)
                typeCache.Remove(pawnId);
                
            // Clean up job cache
            _recentJobCache.Remove(pawnId);
            
            // Clean up check times
            _lastConditionCheckTicks.Remove(pawnId);
        }

        /// <summary>
        /// Performs periodic cleanup of stale cache entries
        /// </summary>
        public static void PerformCacheCleanup()
        {
            int currentTick = Find.TickManager.TicksGame;
            
            // Clean up stale job cache entries
            const int jobCacheExpiry = JOB_CACHE_DURATION * 2;
            foreach (var pawnEntry in _recentJobCache.ToList())
            {
                foreach (var jobEntry in pawnEntry.Value.ToList())
                {
                    if (currentTick - jobEntry.Value.Timestamp > jobCacheExpiry)
                        pawnEntry.Value.Remove(jobEntry.Key);
                }
                
                if (pawnEntry.Value.Count == 0)
                    _recentJobCache.Remove(pawnEntry.Key);
            }
        }

        #endregion
    }
}