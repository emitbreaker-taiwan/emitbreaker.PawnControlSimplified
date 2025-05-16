using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Jobs;
using UnityEngine;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    public static class Utility_JobGiverManager
    {
        #region Standardized Job Creation

        /// <summary>
        /// A delegate type for check functions that may set JobFailReason
        /// </summary>
        /// <param name="pawn">The pawn to check</param>
        /// <param name="setFailReason">Whether to set a JobFailReason if the check fails</param>
        /// <returns>True if the check passes, false otherwise</returns>
        public delegate bool JobEligibilityCheck(Pawn pawn, bool setFailReason = true);

        /// <summary>
        /// Standardized job-giving method wrapper to be used by all JobGiver classes for consistent behavior
        /// </summary>
        public static Job StandardTryGiveJob<T>(
            Pawn pawn,
            string workTag,
            Func<Pawn, bool, Job> jobCreator,
            List<JobEligibilityCheck> additionalChecks = null,
            string debugJobDesc = null,
            bool skipEmergencyCheck = false,
            Type jobGiverType = null) where T : class
        {
            if (!Utility_TagManager.IsWorkEnabled(pawn, workTag))
                return null;

            // Start timing for profiling
            var stopwatch = new System.Diagnostics.Stopwatch();
            stopwatch.Start();

            // Declare job variable at this scope so it's accessible in the finally block
            Job job = null;

            try
            {
                // Early exit if pawn is drafted
                if (pawn?.drafter != null && pawn.drafter.Drafted)
                {
                    Utility_DebugManager.LogNormal($"Injected {pawn.LabelShort} is drafted, skipping {debugJobDesc ?? workTag} job");
                    return null;
                }

                // 0. Progressive scheduling check - determine if this pawn should run this job on this tick
                // Now using workTag instead of jobGiverType for more efficient scheduling
                if (!Utility_JobGiverTickManager.ShouldExecuteForPawn(workTag, pawn))
                {
                    return null;
                }

                // 1. Emergency check - yield to firefighting if there's a fire (unless we should skip it)
                if (!skipEmergencyCheck && ShouldYieldToEmergencyJob(pawn))
                {
                    Utility_DebugManager.LogNormal($"Injected {pawn.LabelShort} yielding from {debugJobDesc ?? workTag} job to handle emergency");
                    return null;
                }

                // 2. Basic eligibility check with the specified work tag
                if (!IsEligibleForSpecializedJobGiver(pawn, workTag))
                    return null;

                // 3. Check global state flags if available
                if (Utility_GlobalStateManager.ShouldSkipJobGiverDueToGlobalState(workTag, pawn))
                    return null;

                // 4. Check dependencies if available
                if (!AreDependenciesSatisfied(workTag, pawn))
                    return null;

                // 5. Perform any additional checks provided by the specific JobGiver
                if (additionalChecks != null)
                {
                    foreach (var check in additionalChecks)
                    {
                        if (!check(pawn))
                            return null;
                    }
                }

                // 6. Call the job creator function with the pawn and forced=false
                job = jobCreator(pawn, false);

                // 7. Debug logging if a job was found
                if (job != null && Prefs.DevMode)
                {
                    Utility_DebugManager.LogNormal($"Injected {pawn.LabelShort} created {debugJobDesc ?? workTag} job: {job.def.defName}");
                }

                return job;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error creating {debugJobDesc ?? workTag} job for {pawn.LabelShort}: {ex}");
                return null;
            }
            finally
            {
                // Stop timing and record metrics
                stopwatch.Stop();

                // Record execution metrics with work tag
                if (!string.IsNullOrEmpty(workTag))
                {
                    float executionTimeMs = (float)stopwatch.Elapsed.TotalMilliseconds;
                    bool successful = job != null;

                    // Record in local registry
                    int currentTick = Find.TickManager.TicksGame;
                    if (_workTagRegistry.TryGetValue(workTag, out var entry))
                    {
                        entry.RecordExecution(successful, executionTimeMs, currentTick);
                    }

                    // Forward to adaptive profiling system if enabled
                    if (Utility_AdaptiveProfilingManager.IsProfilingEnabled)
                    {
                        Utility_AdaptiveProfilingManager.RecordWorkTagExecution(workTag, executionTimeMs, successful);
                    }
                }
            }
        }

        // Updated simplified version using workTag
        public static Job StandardTryGiveJob<T>(
            Pawn pawn,
            string workTag,
            Func<Pawn, bool, Job> jobCreator,
            JobEligibilityCheck additionalCheck,
            string debugJobDesc = null,
            bool skipEmergencyCheck = false) where T : class
        {
            if (!Utility_TagManager.IsWorkEnabled(pawn, workTag))
                return null;

            try
            {
                if (pawn?.Map == null || pawn.Dead || !pawn.Spawned || pawn?.Faction == null)
                {
                    return null;
                }
                return StandardTryGiveJob<T>(
                pawn,
                workTag,
                jobCreator,
                additionalCheck != null ? new List<JobEligibilityCheck> { additionalCheck } : null,
                debugJobDesc,
                skipEmergencyCheck,
                typeof(T));
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error in StandardTryGiveJob for {pawn?.LabelShort ?? "null"}, tag {workTag}, error: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Check if pawn should yield to emergency jobs like firefighting
        /// </summary>
        public static bool ShouldYieldToEmergencyJob(Pawn pawn)
        {
            if (pawn?.Map == null)
                return false;

            // Manually scan for spawned fires in the home area
            List<Thing> fires = pawn.Map.listerThings.ThingsOfDef(ThingDefOf.Fire);
            for (int i = 0, c = fires.Count; i < c; i++)
            {
                var f = fires[i] as Fire;
                if (f != null && f.Spawned && pawn.Map.areaManager.Home[f.Position])
                {
                    // only yield if pawn can do firefighting work
                    if (Utility_TagManager.IsWorkEnabled(pawn, "Firefighter"))
                        return true;
                    break;
                }
            }

            return false;
        }

        #endregion

        #region Validation

        /// <summary>
        /// Checks if a pawn is eligible for specialized JobGiver processing
        /// </summary>
        public static bool IsEligibleForSpecializedJobGiver(Pawn pawn, string workTypeName)
        {
            if (pawn == null || !pawn.Spawned || pawn.Dead)
                return false;

            // Skip pawns without mod extension - let vanilla handle them
            var modExtension = Utility_UnifiedCache.GetModExtension(pawn.def);
            if (modExtension == null)
                return false;

            // Using ThinkTreeManager for consistent tagging across codebase
            if (!Utility_ThinkTreeManager.HasAllowWorkTag(pawn))
                return false;

            if (!Utility_TagManager.WorkTypeSettingEnabled(pawn, workTypeName))
            {
                if (Prefs.DevMode)
                {
                    Utility_DebugManager.LogNormal(
                        $"{pawn.LabelShort} (non-humanlike bypass = False) is not eligible for {workTypeName} work, skipping job");
                }
                return false;
            }

            return true;
        }

        /// <summary>
        /// Checks if a pawn is allowed to perform work on a target based on faction rules
        /// </summary>
        public static bool IsValidFactionInteraction(Thing target, Pawn pawn, bool requiresDesignator = false)
        {
            // Handle null faction case first
            if (pawn?.Faction == null)
            {
                return false;
            }

            // If target is null, we're just checking if the pawn's faction can perform the job
            // This is used for floor construction and other cell-based jobs
            if (target == null)
            {
                // For designator-oriented tasks, only player pawns can perform them
                if (requiresDesignator && pawn.Faction != Faction.OfPlayer)
                {
                    return false;
                }
                return true;
            }

            Pawn targetPawn = target as Pawn;

            // Skip faction check for dead or downed pawns with strip designation
            // This is consistent with vanilla behavior where pawns can strip anyone
            if (requiresDesignator && targetPawn != null &&
                (targetPawn.Dead || targetPawn.Downed) &&
                targetPawn.Map?.designationManager.DesignationOn(targetPawn, DesignationDefOf.Strip) != null)
            {
                return true;
            }

            // For corpses - allow stripping regardless of faction if designated
            if (requiresDesignator && target is Corpse corpse &&
                corpse.Map?.designationManager.DesignationOn(corpse, DesignationDefOf.Strip) != null)
            {
                return true;
            }

            // Check if target has a different non-null faction
            if (target.Faction != null && target.Faction != pawn.Faction)
            {
                if (targetPawn == null)
                {
                    return false;
                }

                // Special handling for prisoners - wardens can interact with prisoner pawns
                if ((targetPawn.IsPrisoner || targetPawn.IsSlave) && targetPawn.HostFaction == pawn.Faction)
                {
                    return true;
                }

                // Special handling for patients - doctors can treat patients from other factions
                if (FeedPatientUtility.ShouldBeFed(targetPawn) || targetPawn.health.HasHediffsNeedingTend())
                {
                    Building_Bed bed = targetPawn.CurrentBed();
                    if (bed != null && bed.Medical)
                        return true;
                }

                return false;
            }

            return true;
        }

        #endregion

        #region Bucketing

        /// <summary>
        /// Creates a distance-based buckets system for optimized job target selection
        /// </summary>
        public static List<T>[] CreateDistanceBuckets<T>(Pawn pawn, IEnumerable<T> candidates,
            Func<T, float> distanceSquaredFunc, float[] distanceThresholds) where T : Thing
        {
            if (pawn == null || candidates == null || distanceThresholds == null)
                return null;

            // Initialize buckets
            List<T>[] buckets = new List<T>[distanceThresholds.Length + 1];
            for (int i = 0; i < buckets.Length; i++)
                buckets[i] = new List<T>();

            foreach (T target in candidates)
            {
                // Skip invalid targets
                if (target == null || target.Destroyed || !target.Spawned)
                    continue;

                // Get distance 
                float distSq = distanceSquaredFunc(target);

                // Assign to appropriate bucket
                int bucketIndex = distanceThresholds.Length; // Default to last bucket (furthest)
                for (int i = 0; i < distanceThresholds.Length; i++)
                {
                    if (distSq < distanceThresholds[i])
                    {
                        bucketIndex = i;
                        break;
                    }
                }

                buckets[bucketIndex].Add(target);
            }

            return buckets;
        }

        /// <summary>
        /// Process buckets from closest to farthest, finding first valid target
        /// </summary>
        public static T FindFirstValidTargetInBuckets<T>(
            List<T>[] buckets,
            Pawn pawn,
            Func<T, Pawn, bool> validationFunc,
            string workTag = null) where T : Thing
        {
            if (buckets == null || pawn == null)
                return null;

            // Process buckets from closest to farthest
            for (int b = 0; b < buckets.Length; b++)
            {
                if (buckets[b].Count == 0)
                    continue;

                // Randomize within each distance band for better distribution
                buckets[b].Shuffle();

                // Check each thing in this distance band
                foreach (T thing in buckets[b])
                {
                    bool canUse = false;

                    // Use workTag-based cache if available
                    if (!string.IsNullOrEmpty(workTag) && pawn.Map != null)
                    {
                        int mapId = pawn.Map.uniqueID;

                        // Try to get reachability result from cache
                        if (!Utility_JobGiverCacheManager<T>.TryGetReachabilityResult(workTag, mapId, thing, out canUse))
                        {
                            canUse = validationFunc(thing, pawn);

                            // Cache the result
                            Utility_JobGiverCacheManager<T>.CacheReachabilityResult(workTag, mapId, thing, canUse);
                        }
                    }
                    else
                    {
                        canUse = validationFunc(thing, pawn);
                    }

                    if (canUse)
                        return thing;
                }
            }

            return null;
        }

        #endregion

        #region Registry Data Structures

        // Central registry of all job givers by work type
        private static readonly Dictionary<string, JobGiverRegistryEntry> _workTagRegistry = new Dictionary<string, JobGiverRegistryEntry>();

        // Legacy registry of job giver types (for backward compatibility)
        private static readonly Dictionary<Type, JobGiverRegistryEntry> _jobGiverRegistry = new Dictionary<Type, JobGiverRegistryEntry>();

        // Work tag dependencies (which work tags depend on which others)
        private static readonly Dictionary<string, HashSet<string>> _workTagDependencies = new Dictionary<string, HashSet<string>>();

        // Legacy dependencies (for backward compatibility)
        private static readonly Dictionary<Type, HashSet<Type>> _jobGiverDependencies = new Dictionary<Type, HashSet<Type>>();

        // Registry of all work tags with their basic data (for scheduling)
        private static readonly Dictionary<string, object> _workTagsRegistry = new Dictionary<string, object>();

        // Legacy registry (for backward compatibility)
        private static readonly Dictionary<Type, object> _jobGiversRegistry = new Dictionary<Type, object>();

        // Work tags that have been explicitly deactivated for specific maps
        private static readonly Dictionary<int, HashSet<string>> _deactivatedWorkTags = new Dictionary<int, HashSet<string>>();

        // Legacy deactivated job givers (for backward compatibility)
        private static readonly Dictionary<int, HashSet<Type>> _deactivatedJobGivers = new Dictionary<int, HashSet<Type>>();

        // Storage for bypass conditions (work tag-based)
        private static readonly Dictionary<string, Utility_ThinkTreeOptimizationManager.BypassCondition[]> _workTagBypassConditions =
            new Dictionary<string, Utility_ThinkTreeOptimizationManager.BypassCondition[]>();

        // Legacy bypass conditions (for backward compatibility)
        private static readonly Dictionary<Type, Utility_ThinkTreeOptimizationManager.BypassCondition[]> _bypassConditions =
            new Dictionary<Type, Utility_ThinkTreeOptimizationManager.BypassCondition[]>();

        // Mapping between job giver types and work tags
        private static readonly Dictionary<Type, string> _jobGiverTypeToWorkTag = new Dictionary<Type, string>();
        private static readonly Dictionary<string, HashSet<Type>> _workTagToJobGiverTypes = new Dictionary<string, HashSet<Type>>();

        #endregion

        #region JobGiver Registry Entry
        /// <summary>
        /// Storage class for JobGiver metadata and runtime statistics
        /// </summary>
        public class JobGiverRegistryEntry
        {
            // Basic identification
            public Type JobGiverType { get; }
            public string WorkTypeName { get; }
            public int BasePriority { get; set; }
            public int? CustomTickInterval { get; set; }

            // Extended metadata
            public Utility_GlobalStateManager.JobCategory JobCategory { get; set; }
            public Utility_GlobalStateManager.PawnCapabilityFlags RequiredCapabilities { get; set; }
            public Dictionary<Utility_GlobalStateManager.ColonyNeedType, float> NeedResponsiveness { get; set; }

            // Runtime statistics
            public int TotalExecutions { get; private set; }
            public int SuccessfulExecutions { get; private set; }
            public float TotalExecutionTime { get; private set; }
            public float MaxExecutionTime { get; private set; }
            public int LastExecutionTick { get; private set; }
            public float AverageExecutionTime => TotalExecutions > 0 ? TotalExecutionTime / TotalExecutions : 0f;
            public float SuccessRate => TotalExecutions > 0 ? (float)SuccessfulExecutions / TotalExecutions : 0f;

            // Dependency tracking
            public HashSet<Type> DependsOn { get; } = new HashSet<Type>();
            public HashSet<Type> RequiredBy { get; } = new HashSet<Type>();

            // Work tag dependencies
            public HashSet<string> WorkTagDependsOn { get; } = new HashSet<string>();
            public HashSet<string> WorkTagRequiredBy { get; } = new HashSet<string>();

            public JobGiverRegistryEntry(Type jobGiverType, string workTypeName, int basePriority = 5, int? customTickInterval = null)
            {
                JobGiverType = jobGiverType;
                WorkTypeName = workTypeName;
                BasePriority = basePriority;
                CustomTickInterval = customTickInterval;

                // Default values for metadata
                JobCategory = Utility_GlobalStateManager.JobCategory.Basic;
                RequiredCapabilities = Utility_GlobalStateManager.PawnCapabilityFlags.None;
                NeedResponsiveness = new Dictionary<Utility_GlobalStateManager.ColonyNeedType, float>();

                // Initialize statistics
                TotalExecutions = 0;
                SuccessfulExecutions = 0;
                TotalExecutionTime = 0f;
                MaxExecutionTime = 0f;
            }

            // Constructor for work tag-based entry
            public JobGiverRegistryEntry(string workTypeName, int basePriority = 5, int? customTickInterval = null)
            {
                JobGiverType = null;
                WorkTypeName = workTypeName;
                BasePriority = basePriority;
                CustomTickInterval = customTickInterval;

                // Default values for metadata
                JobCategory = Utility_GlobalStateManager.JobCategory.Basic;
                RequiredCapabilities = Utility_GlobalStateManager.PawnCapabilityFlags.None;
                NeedResponsiveness = new Dictionary<Utility_GlobalStateManager.ColonyNeedType, float>();

                // Initialize statistics
                TotalExecutions = 0;
                SuccessfulExecutions = 0;
                TotalExecutionTime = 0f;
                MaxExecutionTime = 0f;
            }

            /// <summary>
            /// Records execution metrics for this JobGiver
            /// </summary>
            public void RecordExecution(bool successful, float executionTimeMs, int tick)
            {
                TotalExecutions++;
                if (successful) SuccessfulExecutions++;

                TotalExecutionTime += executionTimeMs;
                LastExecutionTick = tick;

                if (executionTimeMs > MaxExecutionTime)
                    MaxExecutionTime = executionTimeMs;
            }

            public override string ToString()
            {
                string typeDesc = JobGiverType != null ? JobGiverType.Name : "work tag";
                return $"{typeDesc}: {WorkTypeName ?? "no work type"}, Priority: {BasePriority}, " +
                       $"Success rate: {SuccessRate * 100:F1}%, Avg time: {AverageExecutionTime:F2}ms";
            }
        }

        #endregion

        #region Performance Tracking

        /// <summary>
        /// Records execution metrics for a job giver by work tag
        /// </summary>
        public static void RecordJobGiverExecution(string workTag, bool successful, float executionTimeMs)
        {
            if (string.IsNullOrEmpty(workTag))
                return;

            int currentTick = Find.TickManager.TicksGame;

            // Record in work tag registry
            if (_workTagRegistry.TryGetValue(workTag, out var entry))
            {
                entry.RecordExecution(successful, executionTimeMs, currentTick);
            }

            // Integrate with adaptive profiler if enabled
            if (Utility_AdaptiveProfilingManager.IsProfilingEnabled)
            {
                Utility_AdaptiveProfilingManager.RecordWorkTagExecution(workTag, executionTimeMs, successful);
            }
        }

        #endregion

        #region Dependency Management

        /// <summary>
        /// Validates that all dependencies of a work tag are satisfied for a pawn
        /// </summary>
        public static bool AreDependenciesSatisfied(string workTag, Pawn pawn)
        {
            if (string.IsNullOrEmpty(workTag) || pawn == null)
                return false;

            // Skip dependency check if no dependencies registered
            if (!_workTagDependencies.TryGetValue(workTag, out var dependencies) ||
                dependencies.Count == 0)
                return true;

            // Check each dependency
            foreach (string dependencyTag in dependencies)
            {
                // Skip if dependency check is registered
                bool bypassAllow = false;
                Utility_ThinkTreeOptimizationManager.BypassCondition[] conditions = GetWorkTagBypassConditions(dependencyTag);
                if (conditions != null)
                {
                    foreach (var condition in conditions)
                    {
                        if (condition(pawn))
                        {
                            bypassAllow = true;
                            break;
                        }
                    }
                }

                // If bypass conditions allow it, skip this dependency check
                if (bypassAllow)
                    continue;

                // Check if resources from this dependency exist
                // This would be dependency-specific logic, possibly through a delegate system
                // Here we just check if the dependency is active for this pawn's map
                if (!IsWorkTagActive(dependencyTag, pawn.Map.uniqueID))
                {
                    if (Prefs.DevMode)
                    {
                        Utility_DebugManager.LogNormal($"Work tag {workTag} skipped for {pawn.LabelShort} - " +
                                                     $"dependency {dependencyTag} not active");
                    }
                    return false;
                }

                // Additional dependency validation would go here
            }

            return true;
        }

        /// <summary>
        /// Legacy method for backward compatibility
        /// </summary>
        public static bool AreDependenciesSatisfied(Type jobGiverType, Pawn pawn)
        {
            if (jobGiverType == null || pawn == null)
                return false;

            // If we have a work tag mapping, use the work tag-based method
            if (_jobGiverTypeToWorkTag.TryGetValue(jobGiverType, out string workTag))
            {
                return AreDependenciesSatisfied(workTag, pawn);
            }

            // Legacy fallback
            // Skip dependency check if no dependencies registered
            if (!_jobGiverDependencies.TryGetValue(jobGiverType, out var dependencies) ||
                dependencies.Count == 0)
                return true;

            // Check each dependency
            foreach (Type dependencyType in dependencies)
            {
                // Skip if dependency check is registered
                bool bypassAllow = false;
                Utility_ThinkTreeOptimizationManager.BypassCondition[] conditions = GetBypassConditions(dependencyType);
                if (conditions != null)
                {
                    foreach (var condition in conditions)
                    {
                        if (condition(pawn))
                        {
                            bypassAllow = true;
                            break;
                        }
                    }
                }

                // If bypass conditions allow it, skip this dependency check
                if (bypassAllow)
                    continue;

                // Check if resources from this dependency exist
                // This would be dependency-specific logic, possibly through a delegate system
                // Here we just check if the dependency is active for this pawn's map
                if (!IsJobGiverActive(dependencyType, pawn.Map.uniqueID))
                {
                    if (Prefs.DevMode)
                    {
                        Utility_DebugManager.LogNormal($"JobGiver {jobGiverType.Name} skipped for {pawn.LabelShort} - " +
                                                     $"dependency {dependencyType.Name} not active");
                    }
                    return false;
                }

                // Additional dependency validation would go here
            }

            return true;
        }

        #endregion

        #region ThinkTree Bypass Conditions

        /// <summary>
        /// Registers bypass conditions for a work tag
        /// </summary>
        public static void RegisterWorkTagBypassConditions(string workTag, Utility_ThinkTreeOptimizationManager.BypassCondition[] conditions)
        {
            if (string.IsNullOrEmpty(workTag) || conditions == null) return;

            _workTagBypassConditions[workTag] = conditions;
        }

        /// <summary>
        /// Gets the registered bypass conditions for a work tag
        /// </summary>
        public static Utility_ThinkTreeOptimizationManager.BypassCondition[] GetWorkTagBypassConditions(string workTag)
        {
            if (string.IsNullOrEmpty(workTag)) return null;

            if (_workTagBypassConditions.TryGetValue(workTag, out var conditions))
                return conditions;

            return null;
        }

        /// <summary>
        /// Registers bypass conditions for a JobGiver (legacy method)
        /// </summary>
        public static void RegisterBypassConditions(Type jobGiverType, Utility_ThinkTreeOptimizationManager.BypassCondition[] conditions)
        {
            if (jobGiverType == null || conditions == null) return;

            _bypassConditions[jobGiverType] = conditions;

            // Also register for work tag if mapping exists
            if (_jobGiverTypeToWorkTag.TryGetValue(jobGiverType, out string workTag))
            {
                RegisterWorkTagBypassConditions(workTag, conditions);
            }
        }

        /// <summary>
        /// Gets the registered bypass conditions for a JobGiver (legacy method)
        /// </summary>
        public static Utility_ThinkTreeOptimizationManager.BypassCondition[] GetBypassConditions(Type jobGiverType)
        {
            if (jobGiverType == null) return null;

            if (_bypassConditions.TryGetValue(jobGiverType, out var conditions))
                return conditions;

            return null;
        }

        /// <summary>
        /// Checks if a work tag is active for a specific map
        /// </summary>
        public static bool IsWorkTagActive(string workTag, int mapId)
        {
            if (string.IsNullOrEmpty(workTag))
                return true; // Default to active if not found

            // Check if work tag is registered
            bool isRegistered = _workTagsRegistry.ContainsKey(workTag);
            if (!isRegistered)
                return true; // If not registered, consider it active

            // Check if explicitly deactivated for this map
            if (_deactivatedWorkTags.TryGetValue(mapId, out var deactivated) &&
                deactivated.Contains(workTag))
                return false;

            // Otherwise, it's active
            return true;
        }

        /// <summary>
        /// Checks if a JobGiver is active for a specific map (legacy method)
        /// </summary>
        public static bool IsJobGiverActive(Type jobGiverType, int mapId)
        {
            if (jobGiverType == null)
                return true; // Default to active if not found

            // If we have a work tag mapping, use the work tag-based method
            if (_jobGiverTypeToWorkTag.TryGetValue(jobGiverType, out string workTag))
            {
                return IsWorkTagActive(workTag, mapId);
            }

            // Legacy fallback
            // Check if jobGiver is registered
            bool isRegistered = _jobGiversRegistry.ContainsKey(jobGiverType);
            if (!isRegistered)
                return true; // If not registered, consider it active

            // Check if explicitly deactivated for this map
            if (_deactivatedJobGivers.TryGetValue(mapId, out var deactivated) &&
                deactivated.Contains(jobGiverType))
                return false;

            // Otherwise, it's active
            return true;
        }

        #endregion

        #region Type-Tag Mapping

        /// <summary>
        /// Registers a job giver type with its corresponding work tag
        /// </summary>
        public static void RegisterJobGiverTypeWithWorkTag(Type jobGiverType, string workTag)
        {
            if (jobGiverType == null || string.IsNullOrEmpty(workTag))
                return;

            _jobGiverTypeToWorkTag[jobGiverType] = workTag;

            if (!_workTagToJobGiverTypes.TryGetValue(workTag, out var types))
            {
                types = new HashSet<Type>();
                _workTagToJobGiverTypes[workTag] = types;
            }

            types.Add(jobGiverType);

            // Register work tag with the cache manager
            Utility_JobGiverCacheManager<Thing>.RegisterWorkTag(workTag);
        }

        /// <summary>
        /// Gets the work tag for a job giver type
        /// </summary>
        public static string GetWorkTagForJobGiverType(Type jobGiverType)
        {
            if (jobGiverType == null)
                return string.Empty;

            if (_jobGiverTypeToWorkTag.TryGetValue(jobGiverType, out string workTag))
                return workTag;

            // If not registered, try to derive a work tag from the type name
            workTag = DeriveWorkTagFromType(jobGiverType);

            // Register the derived mapping for future use
            RegisterJobGiverTypeWithWorkTag(jobGiverType, workTag);

            return workTag;
        }

        /// <summary>
        /// Helper method to derive a work tag from a job giver type
        /// </summary>
        private static string DeriveWorkTagFromType(Type jobGiverType)
        {
            if (jobGiverType == null)
                return string.Empty;

            // Remove common prefixes like "JobGiver_" or "ThinkNode_JobGiver_"
            string typeName = jobGiverType.Name;
            if (typeName.StartsWith("JobGiver_"))
            {
                typeName = typeName.Substring("JobGiver_".Length);
            }
            else if (typeName.StartsWith("ThinkNode_JobGiver_"))
            {
                typeName = typeName.Substring("ThinkNode_JobGiver_".Length);
            }

            // Convert to proper work tag format (usually a single word like "Hauling", "Cleaning", etc.)
            return typeName;
        }

        #endregion

        #region Cleanup Methods

        /// <summary>
        /// Resets all registry data
        /// </summary>
        public static void ResetRegistry()
        {
            // Reset work tag-based registries
            _workTagRegistry.Clear();
            _workTagDependencies.Clear();
            _workTagsRegistry.Clear();
            _deactivatedWorkTags.Clear();
            _workTagBypassConditions.Clear();

            // Reset mapping dictionaries
            _jobGiverTypeToWorkTag.Clear();
            _workTagToJobGiverTypes.Clear();

            // Reset legacy registries
            _jobGiverRegistry.Clear();
            _jobGiverDependencies.Clear();
            _jobGiversRegistry.Clear();
            _deactivatedJobGivers.Clear();
            _bypassConditions.Clear();

            // Also reset related systems
            Utility_JobGiverTickManager.ResetAll();
        }

        /// <summary>
        /// Cleans up data for a specific map
        /// </summary>
        public static void CleanupMapData(int mapId)
        {
            // Clean up map-specific data for work tags
            if (_deactivatedWorkTags.TryGetValue(mapId, out _))
            {
                _deactivatedWorkTags.Remove(mapId);
            }

            // Clean up legacy map-specific data
            if (_deactivatedJobGivers.TryGetValue(mapId, out _))
            {
                _deactivatedJobGivers.Remove(mapId);
            }

            // Forward the call to other systems
            Utility_JobGiverTickManager.CleanupMap(mapId);
        }

        #endregion
    }
}