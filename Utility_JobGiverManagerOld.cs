using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using static emitbreaker.PawnControl.JobGiver_Scan_PawnControl;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Provides helper methods for creating and managing specialized JobGivers more efficiently
    /// </summary>
    public static class Utility_JobGiverManagerOld
    {
        #region Registry Data Structures

        // Registry of all JobGivers with their basic data (for scheduling)
        private static readonly Dictionary<Type, object> _jobGiversRegistry = new Dictionary<Type, object>();

        // JobGivers that have been explicitly deactivated for specific maps
        private static readonly Dictionary<int, HashSet<Type>> _deactivatedJobGivers = new Dictionary<int, HashSet<Type>>();

        // Central registry of all JobGiver types
        private static readonly Dictionary<Type, JobGiverRegistryEntry> _jobGiverRegistry = new Dictionary<Type, JobGiverRegistryEntry>();

        // JobGivers grouped by work type
        private static readonly Dictionary<string, List<Type>> _jobGiversByWorkType = new Dictionary<string, List<Type>>(StringComparer.OrdinalIgnoreCase);

        // JobGiver dependencies (which JobGivers depend on which others)
        private static readonly Dictionary<Type, HashSet<Type>> _jobGiverDependencies = new Dictionary<Type, HashSet<Type>>();

        // JobGivers that provide resources for other JobGivers
        private static readonly Dictionary<Type, HashSet<Type>> _resourceProviderJobGivers = new Dictionary<Type, HashSet<Type>>();

        // Storage for bypass conditions
        private static readonly Dictionary<Type, Utility_ThinkTreeOptimizationManager.BypassCondition[]> _bypassConditions = new Dictionary<Type, Utility_ThinkTreeOptimizationManager.BypassCondition[]>();

        /// <summary>
        /// A delegate type for check functions that may set JobFailReason
        /// </summary>
        /// <param name="pawn">The pawn to check</param>
        /// <param name="setFailReason">Whether to set a JobFailReason if the check fails</param>
        /// <returns>True if the check passes, false otherwise</returns>
        public delegate bool JobEligibilityCheck(Pawn pawn, bool setFailReason = true);

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
                return $"{JobGiverType.Name}: {WorkTypeName ?? "no work type"}, Priority: {BasePriority}, " +
                       $"Success rate: {SuccessRate * 100:F1}%, Avg time: {AverageExecutionTime:F2}ms";
            }
        }

        #endregion

        #region Registration Methods

        /// <summary>
        /// Registers a JobGiver with the registry system
        /// </summary>
        public static void RegisterJobGiver(
            Type jobGiverType,
            string workTypeName = null,
            int basePriority = 5,
            int? customTickInterval = null,
            Utility_GlobalStateManager.JobCategory jobCategory = Utility_GlobalStateManager.JobCategory.Basic,
            Utility_GlobalStateManager.PawnCapabilityFlags requiredCapabilities = Utility_GlobalStateManager.PawnCapabilityFlags.None,
            Dictionary<Utility_GlobalStateManager.ColonyNeedType, float> needResponsiveness = null,
            Type[] dependsOnJobGivers = null)
        {
            if (jobGiverType == null) return;

            // Create or update registry entry
            if (!_jobGiverRegistry.TryGetValue(jobGiverType, out JobGiverRegistryEntry entry))
            {
                // Create new entry
                entry = new JobGiverRegistryEntry(jobGiverType, workTypeName, basePriority, customTickInterval);
                _jobGiverRegistry[jobGiverType] = entry;

                // Register with JobGiverTickManager for scheduling
                Utility_JobGiverTickManager.RegisterJobGiver(jobGiverType, workTypeName, basePriority, customTickInterval);

                // Log registration in dev mode
                if (Prefs.DevMode)
                {
                    Utility_DebugManager.LogNormal($"Registered JobGiver: {jobGiverType.Name} (WorkType: {workTypeName ?? "none"})");
                }
            }
            else
            {
                // Update existing entry (keeping statistics)
                entry.BasePriority = basePriority;
                entry.CustomTickInterval = customTickInterval;
            }

            // Set extended metadata
            entry.JobCategory = jobCategory;
            entry.RequiredCapabilities = requiredCapabilities;

            if (needResponsiveness != null)
            {
                entry.NeedResponsiveness = new Dictionary<Utility_GlobalStateManager.ColonyNeedType, float>(needResponsiveness);
            }

            // Register with GlobalStateManager
            Utility_GlobalStateManager.RegisterJobGiverMetadata(
                jobGiverType, jobCategory, requiredCapabilities, needResponsiveness);

            // Add to work type grouping
            if (!string.IsNullOrEmpty(workTypeName))
            {
                if (!_jobGiversByWorkType.TryGetValue(workTypeName, out var workTypeGroup))
                {
                    workTypeGroup = new List<Type>();
                    _jobGiversByWorkType[workTypeName] = workTypeGroup;
                }

                if (!workTypeGroup.Contains(jobGiverType))
                {
                    workTypeGroup.Add(jobGiverType);
                }
            }

            // Register dependencies
            if (dependsOnJobGivers != null && dependsOnJobGivers.Length > 0)
            {
                RegisterDependencies(jobGiverType, dependsOnJobGivers);
            }
        }

        /// <summary>
        /// Registers dependencies between JobGivers
        /// </summary>
        public static void RegisterDependencies(Type jobGiverType, params Type[] dependsOnTypes)
        {
            if (jobGiverType == null || dependsOnTypes == null || dependsOnTypes.Length == 0)
                return;

            // Get or create dependency set
            if (!_jobGiverDependencies.TryGetValue(jobGiverType, out var dependencies))
            {
                dependencies = new HashSet<Type>();
                _jobGiverDependencies[jobGiverType] = dependencies;
            }

            // Register each dependency
            foreach (Type dependsOn in dependsOnTypes)
            {
                if (dependsOn == null || dependsOn == jobGiverType)
                    continue;

                // Add to dependencies
                dependencies.Add(dependsOn);

                // Add this JobGiver to the "required by" set of the dependency
                if (!_resourceProviderJobGivers.TryGetValue(dependsOn, out var requiredBy))
                {
                    requiredBy = new HashSet<Type>();
                    _resourceProviderJobGivers[dependsOn] = requiredBy;
                }

                requiredBy.Add(jobGiverType);

                // Add to registry entry if it exists
                if (_jobGiverRegistry.TryGetValue(jobGiverType, out var entry))
                {
                    entry.DependsOn.Add(dependsOn);
                }

                if (_jobGiverRegistry.TryGetValue(dependsOn, out var depEntry))
                {
                    depEntry.RequiredBy.Add(jobGiverType);
                }
            }
        }

        #endregion

        #region Registry Query Methods

        /// <summary>
        /// Gets all registered JobGivers
        /// </summary>
        public static IEnumerable<Type> GetAllRegisteredJobGivers()
        {
            return _jobGiverRegistry.Keys;
        }

        /// <summary>
        /// Gets all JobGivers for a specific work type
        /// </summary>
        public static IEnumerable<Type> GetJobGiversByWorkType(string workTypeName)
        {
            if (string.IsNullOrEmpty(workTypeName))
                return Enumerable.Empty<Type>();

            if (_jobGiversByWorkType.TryGetValue(workTypeName, out var jobGivers))
                return jobGivers;

            return Enumerable.Empty<Type>();
        }

        /// <summary>
        /// Gets the registry entry for a JobGiver
        /// </summary>
        public static JobGiverRegistryEntry GetJobGiverInfo(Type jobGiverType)
        {
            if (jobGiverType == null)
                return null;

            _jobGiverRegistry.TryGetValue(jobGiverType, out var entry);
            return entry;
        }

        /// <summary>
        /// Gets all JobGivers this JobGiver depends on
        /// </summary>
        public static IEnumerable<Type> GetJobGiverDependencies(Type jobGiverType)
        {
            if (jobGiverType == null)
                return Enumerable.Empty<Type>();

            if (_jobGiverDependencies.TryGetValue(jobGiverType, out var dependencies))
                return dependencies;

            return Enumerable.Empty<Type>();
        }

        /// <summary>
        /// Gets all JobGivers that depend on this JobGiver
        /// </summary>
        public static IEnumerable<Type> GetJobGiverDependents(Type jobGiverType)
        {
            if (jobGiverType == null)
                return Enumerable.Empty<Type>();

            if (_resourceProviderJobGivers.TryGetValue(jobGiverType, out var dependents))
                return dependents;

            return Enumerable.Empty<Type>();
        }

        /// <summary>
        /// Checks if one JobGiver depends on another
        /// </summary>
        public static bool DependsOn(Type jobGiverType, Type dependencyType)
        {
            if (jobGiverType == null || dependencyType == null)
                return false;

            if (_jobGiverDependencies.TryGetValue(jobGiverType, out var dependencies))
                return dependencies.Contains(dependencyType);

            return false;
        }

        /// <summary>
        /// Gets JobGivers that respond to a specific colony need
        /// </summary>
        public static IEnumerable<Type> GetJobGiversForNeed(Utility_GlobalStateManager.ColonyNeedType needType)
        {
            return _jobGiverRegistry
                .Where(kvp => kvp.Value.NeedResponsiveness != null &&
                       kvp.Value.NeedResponsiveness.TryGetValue(needType, out float response) &&
                       response > 0)
                .Select(kvp => kvp.Key);
        }

        /// <summary>
        /// Gets all JobGivers that require specific capabilities
        /// </summary>
        public static IEnumerable<Type> GetJobGiversRequiringCapabilities(Utility_GlobalStateManager.PawnCapabilityFlags capabilities)
        {
            return _jobGiverRegistry
                .Where(kvp => (kvp.Value.RequiredCapabilities & capabilities) == capabilities)
                .Select(kvp => kvp.Key);
        }

        #endregion

        #region Performance Tracking

        /// <summary>
        /// Records execution metrics for a JobGiver
        /// </summary>
        public static void RecordJobGiverExecution(Type jobGiverType, bool successful, float executionTimeMs)
        {
            if (jobGiverType == null)
                return;

            int currentTick = Find.TickManager.TicksGame;

            // Record in registry
            if (_jobGiverRegistry.TryGetValue(jobGiverType, out var entry))
            {
                entry.RecordExecution(successful, executionTimeMs, currentTick);
            }

            // Integrate with adaptive profiler if enabled
            if (Utility_AdaptiveProfilingManager.IsProfilingEnabled)
            {
                Utility_AdaptiveProfilingManager.RecordJobGiverExecution(jobGiverType, executionTimeMs, successful);
            }
        }

        /// <summary>
        /// Gets the most efficient JobGivers by success rate and execution time
        /// </summary>
        public static IEnumerable<JobGiverRegistryEntry> GetMostEfficientJobGivers(int count = 10)
        {
            return _jobGiverRegistry.Values
                .Where(entry => entry.TotalExecutions > 5)  // Only consider JobGivers with enough data
                .OrderByDescending(entry => entry.SuccessRate / (Math.Max(0.1f, entry.AverageExecutionTime)))
                .Take(count);
        }

        /// <summary>
        /// Gets the slowest JobGivers by execution time
        /// </summary>
        public static IEnumerable<JobGiverRegistryEntry> GetSlowestJobGivers(int count = 10)
        {
            return _jobGiverRegistry.Values
                .Where(entry => entry.TotalExecutions > 5)
                .OrderByDescending(entry => entry.AverageExecutionTime)
                .Take(count);
        }

        /// <summary>
        /// Gets the most successful JobGivers by success rate
        /// </summary>
        public static IEnumerable<JobGiverRegistryEntry> GetMostSuccessfulJobGivers(int count = 10)
        {
            return _jobGiverRegistry.Values
                .Where(entry => entry.TotalExecutions > 5)
                .OrderByDescending(entry => entry.SuccessRate)
                .Take(count);
        }

        #endregion

        #region Dependency Management

        /// <summary>
        /// Validates that all dependencies of a JobGiver are satisfied for a pawn
        /// </summary>
        public static bool AreDependenciesSatisfied(Type jobGiverType, Pawn pawn)
        {
            if (jobGiverType == null || pawn == null)
                return false;

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


        #region Cleanup Methods

        /// <summary>
        /// Resets all JobGiver registry data
        /// </summary>
        public static void ResetRegistry()
        {
            _jobGiverRegistry.Clear();
            _jobGiversByWorkType.Clear();
            _jobGiverDependencies.Clear();
            _resourceProviderJobGivers.Clear();
            _bypassConditions.Clear();

            // Also reset related systems
            Utility_JobGiverTickManager.ResetAll();
        }

        /// <summary>
        /// Cleans up data for a specific map
        /// </summary>
        public static void CleanupMapData(int mapId)
        {
            // Nothing specific to clean up here since data is not stored per-map
            // Just forward the call to other systems
            Utility_JobGiverTickManager.CleanupMap(mapId);
        }

        #endregion



        /// <summary>
        /// Executes a generic designation-based job creation pattern
        /// </summary>
        public static Job TryCreateDesignatedJob<T>(
            Pawn pawn,
            Dictionary<int, List<T>> designationCache,
            Dictionary<int, Dictionary<T, bool>> reachabilityCache,
            string workTypeTag,
            DesignationDef designationDef,
            JobDef jobDef,
            Func<T, bool> extraValidation = null,
            Func<T, Pawn, bool> reachabilityFunc = null,
            float[] distanceThresholds = null) where T : Thing
        {
            // Basic eligibility check
            if (!Utility_JobGiverManager.IsEligibleForSpecializedJobGiver(pawn, workTypeTag))
                return null;

            // IMPORTANT: For designation-based jobs, ONLY player faction pawns or pawns slaved to player faction can perform them
            if (pawn.Faction != Faction.OfPlayer &&
                !(pawn.IsSlave && pawn.HostFaction == Faction.OfPlayer))
                return null;

            // Quick skip check - if no designations on map
            if (!pawn.Map.designationManager.AnySpawnedDesignationOfDef(designationDef))
                return null;
                
            // Default distance thresholds if not provided
            if (distanceThresholds == null)
                distanceThresholds = new float[] { 625f, 2500f, 10000f }; // 25, 50, 100 tiles
                
            // Default reachability function if not provided
            if (reachabilityFunc == null)
                reachabilityFunc = (T t, Pawn p) => !t.IsForbidden(p) && p.CanReserveAndReach(t, PathEndMode.Touch, p.NormalMaxDanger());
                
            int mapId = pawn.Map.uniqueID;
            if (!designationCache.ContainsKey(mapId) || designationCache[mapId].Count == 0)
                return null;
                
            // Create distance buckets
            var buckets = CreateDistanceBuckets(
                pawn, 
                designationCache[mapId],
                (T t) => (t.Position - pawn.Position).LengthHorizontalSquared,
                distanceThresholds
            );

            // Apply extra validation if provided
            Func<T, Pawn, bool> finalValidation = (T t, Pawn p) =>
            {
                // IMPORTANT: Check faction interaction validity first since this is a designator-based job
                if (!IsValidFactionInteraction(t, p, requiresDesignator: true))
                    return false;

                if (t.Destroyed || !t.Spawned ||
                    pawn.Map.designationManager.DesignationOn(t, designationDef) == null)
                    return false;

                if (extraValidation != null && !extraValidation(t))
                    return false;

                return reachabilityFunc(t, p);
            };

            // Find target
            T target = FindFirstValidTargetInBuckets(buckets, pawn, finalValidation, reachabilityCache);
            
            // Create job if target found
            if (target != null)
            {
                Job job = JobMaker.MakeJob(jobDef, target);
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job {jobDef.defName} for {target.Label}");                
                return job;
            }
            
            return null;
        }

        /// <summary>
        /// Attempts to create a job for a pawn to work on bills at a work station.
        /// Works with any kind of bill giver (campfires, crematoriums, etc.)
        /// </summary>
        public static Job TryCreateBillGiverJob<T>(
            Pawn pawn,
            Dictionary<int, List<T>> billGiversCache,
            Dictionary<int, Dictionary<T, bool>> reachabilityCache,
            float[] distanceThresholds = null) where T : Thing, IBillGiver
        {
            if (pawn?.Map == null) return null;

            // Use default distance thresholds if not provided
            if (distanceThresholds == null)
                distanceThresholds = new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

            int mapId = pawn.Map.uniqueID;
            if (!billGiversCache.ContainsKey(mapId) || billGiversCache[mapId].Count == 0)
                return null;

            // Use distance bucketing
            var buckets = CreateDistanceBuckets(
                pawn,
                billGiversCache[mapId],
                (building) => (building.Position - pawn.Position).LengthHorizontalSquared,
                distanceThresholds
            );

            // Find the best bill giver to use
            T targetBillGiver = FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (building, p) => {
                    // Skip if no longer valid
                    if (building == null || building.Destroyed || !building.Spawned)
                        return false;

                    // Skip if not usable as a bill giver
                    if (building.BillStack == null || !building.BillStack.AnyShouldDoNow || !building.UsableForBillsAfterFueling())
                        return false;

                    // Check if refueling is needed first
                    CompRefuelable refuelable = building.TryGetComp<CompRefuelable>();
                    if (refuelable != null && !refuelable.HasFuel)
                    {
                        if (!RefuelWorkGiverUtility.CanRefuel(p, building, false))
                            return false;

                        // Skip - a refueling job will be created instead
                        return false;
                    }

                    // Skip if forbidden or unreachable
                    if (building.IsForbidden(p) || building.IsBurning() ||
                        !p.CanReserve(building))
                        return false;

                    // Check for interaction cell
                    if (building.def.hasInteractionCell &&
                        !p.CanReserve(building.InteractionCell))
                        return false;

                    return true;
                },
                reachabilityCache
            );

            // Create job if target found
            if (targetBillGiver != null)
            {
                // Determine if we need to refuel first
                CompRefuelable refuelable = targetBillGiver.TryGetComp<CompRefuelable>();
                if (refuelable != null && !refuelable.HasFuel)
                {
                    if (RefuelWorkGiverUtility.CanRefuel(pawn, targetBillGiver, false))
                    {
                        Job refuelJob = RefuelWorkGiverUtility.RefuelJob(pawn, targetBillGiver, false);
                        if (refuelJob != null)
                        {
                            Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to refuel {targetBillGiver.def.label} first");
                            return refuelJob;
                        }
                    }
                }

                // Find a valid bill to work on
                targetBillGiver.BillStack.RemoveIncompletableBills();

                for (int i = 0; i < targetBillGiver.BillStack.Count; i++)
                {
                    Bill bill = targetBillGiver.BillStack[i];
                    if (bill.ShouldDoNow() && bill.PawnAllowedToStartAnew(pawn))
                    {
                        // Check skill requirements
                        SkillRequirement skillReq = bill.recipe?.FirstSkillRequirementPawnDoesntSatisfy(pawn);
                        if (skillReq != null)
                            continue;

                        if (bill is Bill_ProductionWithUft billProduction)
                        {
                            // Check for unfinished things to work on
                            if (billProduction.BoundUft != null)
                            {
                                if (billProduction.BoundWorker == pawn &&
                                    pawn.CanReserveAndReach(billProduction.BoundUft, PathEndMode.Touch, Danger.Deadly) &&
                                    !billProduction.BoundUft.IsForbidden(pawn))
                                {
                                    // Work on the existing unfinished thing
                                    Job finishJob = FinishUnfinishedThingJob(pawn, billProduction.BoundUft, billProduction);
                                    if (finishJob != null)
                                    {
                                        Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to finish {bill.recipe?.defName ?? "unfinished task"}");
                                        return finishJob;
                                    }
                                }
                                continue;
                            }

                            // Look for unfinished tasks
                            UnfinishedThing uft = FindClosestUnfinishedThingForBill(pawn, billProduction);
                            if (uft != null)
                            {
                                Job finishJob = FinishUnfinishedThingJob(pawn, uft, billProduction);
                                if (finishJob != null)
                                {
                                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to finish {bill.recipe?.defName ?? "unfinished task"}");
                                    return finishJob;
                                }
                            }
                        }

                        // Try to find ingredients for the job
                        List<ThingCount> chosenIngredients = new List<ThingCount>();
                        if (TryFindBestBillIngredients(bill, pawn, targetBillGiver, chosenIngredients))
                        {
                            Job billJob = CreateStartBillJob(pawn, bill, targetBillGiver, chosenIngredients);
                            if (billJob != null)
                            {
                                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to work on {bill.recipe?.defName ?? "bill"} at {targetBillGiver.def.label}");
                                return billJob;
                            }
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Creates a job for warden to interact with a prisoner
        /// </summary>
        public static Job TryCreatePrisonerInteractionJob(
            Pawn warden,
            Dictionary<int, List<Pawn>> prisonerCache,
            Dictionary<int, Dictionary<Pawn, bool>> reachabilityCache,
            Func<Pawn, Pawn, bool> validator,
            Func<Pawn, Pawn, Job> jobCreator,
            float[] distanceThresholds = null)
        {
            if (warden?.Map == null || warden.Faction == null) return null;

            int mapId = warden.Map.uniqueID;
            if (!prisonerCache.ContainsKey(mapId) || prisonerCache[mapId].Count == 0)
                return null;

            // Use distance bucketing
            var buckets = CreateDistanceBuckets(
                warden,
                prisonerCache[mapId],
                prisoner => (prisoner.Position - warden.Position).LengthHorizontalSquared,
                distanceThresholds ?? new float[] { 100f, 400f, 900f }
            );

            // Find best prisoner to interact with
            Pawn targetPrisoner = FindFirstValidTargetInBuckets(
                buckets,
                warden,
                (prisoner, p) => {
                    // Base validation checks
                    if (!IsValidFactionInteraction(prisoner, p, requiresDesignator: false))
                        return false;

                    if (prisoner?.guest == null || !prisoner.IsPrisoner)
                        return false;

                    // Custom validation from caller
                    return validator(prisoner, p);
                },
                reachabilityCache
            );

            // Create job if we found a valid target
            if (targetPrisoner != null)
            {
                return jobCreator(warden, targetPrisoner);
            }

            return null;
        }

        /// <summary>
        /// Updates a cache of prisoners matching specific criteria
        /// </summary>
        public static void UpdatePrisonerCache(
            Map map,
            ref int lastUpdateTick,
            int updateInterval,
            Dictionary<int, List<Pawn>> prisonerCache,
            Dictionary<int, Dictionary<Pawn, bool>> reachabilityCache,
            Func<Pawn, bool> prisonerFilter)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > lastUpdateTick + updateInterval ||
                !prisonerCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (prisonerCache.ContainsKey(mapId))
                    prisonerCache[mapId].Clear();
                else
                    prisonerCache[mapId] = new List<Pawn>();

                // Clear reachability cache too
                if (reachabilityCache.ContainsKey(mapId))
                    reachabilityCache[mapId].Clear();
                else
                    reachabilityCache[mapId] = new Dictionary<Pawn, bool>();

                // Find all matching prisoners using the provided filter
                foreach (Pawn prisoner in map.mapPawns.PrisonersOfColonySpawned)
                {
                    if (prisoner == null || prisoner.Destroyed || !prisoner.Spawned)
                        continue;

                    if (prisonerFilter(prisoner))
                    {
                        prisonerCache[mapId].Add(prisoner);
                    }
                }

                lastUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Creates a job to finish an unfinished thing
        /// </summary>
        public static Job FinishUnfinishedThingJob(Pawn pawn, UnfinishedThing uft, Bill_ProductionWithUft bill)
        {
            if (uft.Creator != pawn)
            {
                Utility_DebugManager.LogError("Tried to get FinishUftJob for " + pawn + " finishing " + uft + " but its creator is " + uft.Creator);
                return null;
            }

            Thing billGiverThing = bill.billStack.billGiver as Thing;
            if (billGiverThing == null)
                return null;

            Job job = JobMaker.MakeJob(JobDefOf.DoBill, billGiverThing);
            job.bill = bill;
            job.targetQueueB = new List<LocalTargetInfo>() { uft };
            job.countQueue = new List<int>() { 1 };
            job.haulMode = HaulMode.ToCellNonStorage;

            return job;
        }

        /// <summary>
        /// Finds the closest unfinished thing for a bill
        /// </summary>
        public static UnfinishedThing FindClosestUnfinishedThingForBill(Pawn pawn, Bill_ProductionWithUft bill)
        {
            return (UnfinishedThing)GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForDef(bill.recipe.unfinishedThingDef),
                PathEndMode.InteractionCell,
                TraverseParms.For(pawn),
                validator: t =>
                    !t.IsForbidden(pawn) &&
                    ((UnfinishedThing)t).Recipe == bill.recipe &&
                    ((UnfinishedThing)t).Creator == pawn &&
                    ((UnfinishedThing)t).ingredients.TrueForAll(x => bill.IsFixedOrAllowedIngredient(x.def)) &&
                    pawn.CanReserve(t)
            );
        }

        /// <summary>
        /// Creates a job to start working on a bill
        /// </summary>
        public static Job CreateStartBillJob(Pawn pawn, Bill bill, Thing billGiver, List<ThingCount> chosenIngredients)
        {
            IBillGiver giver = billGiver as IBillGiver;
            if (giver == null)
                return null;

            Job haulOffJob = WorkGiverUtility.HaulStuffOffBillGiverJob(pawn, giver, null);
            if (haulOffJob != null)
                return haulOffJob;

            Job job = JobMaker.MakeJob(JobDefOf.DoBill, billGiver);
            job.targetQueueB = new List<LocalTargetInfo>(chosenIngredients.Count);
            job.countQueue = new List<int>(chosenIngredients.Count);

            for (int i = 0; i < chosenIngredients.Count; i++)
            {
                job.targetQueueB.Add(chosenIngredients[i].Thing);
                job.countQueue.Add(chosenIngredients[i].Count);
            }

            job.haulMode = HaulMode.ToCellNonStorage;
            job.bill = bill;

            return job;
        }

        /// <summary>
        /// Tries to find the best ingredients for a bill
        /// </summary>
        public static bool TryFindBestBillIngredients(Bill bill, Pawn pawn, Thing billGiver, List<ThingCount> chosen)
        {
            chosen.Clear();
            float searchRadius = bill.ingredientSearchRadius;

            // For a bill, we're looking for ingredients that match the bill filter
            List<Thing> availableThings = new List<Thing>();

            // Use the same search logic as in WorkGiver_DoBill's TryFindBestIngredientsHelper
            Predicate<Thing> thingValidator = (t => IsUsableIngredient(t, bill));
            float radiusSq = searchRadius * searchRadius;

            // Custom search implementation - simpler than the vanilla version for performance
            foreach (Thing t in pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
            {
                if (t.Spawned &&
                    thingValidator(t) &&
                    (t.Position - billGiver.Position).LengthHorizontalSquared < radiusSq &&
                    !t.IsForbidden(pawn) &&
                    pawn.CanReserve(t))
                {
                    availableThings.Add(t);
                }
            }

            if (availableThings.Count == 0)
                return false;

            // Sort by distance for better efficiency
            availableThings.Sort((t1, t2) =>
                (t1.Position - pawn.Position).LengthHorizontalSquared.CompareTo(
                (t2.Position - pawn.Position).LengthHorizontalSquared));

            // Pick the right ingredients based on the bill type
            if (bill.recipe.allowMixingIngredients)
            {
                return TryFindBestBillIngredientsInSet_AllowMix(availableThings, bill, chosen, pawn.Position);
            }
            else
            {
                return TryFindBestBillIngredientsInSet_NoMix(availableThings, bill, chosen, pawn.Position);
            }
        }

        /// <summary>
        /// Checks if a thing is a usable ingredient for a bill
        /// </summary>
        public static bool IsUsableIngredient(Thing t, Bill bill)
        {
            if (!bill.IsFixedOrAllowedIngredient(t))
                return false;

            foreach (IngredientCount ingredient in bill.recipe.ingredients)
            {
                if (ingredient.filter.Allows(t))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Tries to find the best ingredients for a bill that doesn't allow mixing
        /// </summary>
        public static bool TryFindBestBillIngredientsInSet_NoMix(List<Thing> availableThings, Bill bill, List<ThingCount> chosen, IntVec3 rootCell)
        {
            chosen.Clear();

            // Simple implementation - just pick the first valid ingredient for each requirement
            foreach (IngredientCount ingredient in bill.recipe.ingredients)
            {
                bool foundIngredient = false;

                foreach (Thing thing in availableThings)
                {
                    if (ingredient.filter.Allows(thing) && bill.ingredientFilter.Allows(thing))
                    {
                        // Use the ingredient's CountRequiredOfFor method instead of accessing it through recipe
                        int countToAdd = Mathf.Min(ingredient.CountRequiredOfFor(thing.def, bill.recipe, bill), thing.stackCount);
                        if (countToAdd > 0)
                        {
                            chosen.Add(new ThingCount(thing, countToAdd));
                            foundIngredient = true;
                            break;
                        }
                    }
                }

                if (!foundIngredient)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Tries to find the best ingredients for a bill that allows mixing
        /// </summary>
        public static bool TryFindBestBillIngredientsInSet_AllowMix(List<Thing> availableThings, Bill bill, List<ThingCount> chosen, IntVec3 rootCell)
        {
            chosen.Clear();

            // Sort by distance only - we can't use ValuePerUnitOf from an ingredientValueGetter that doesn't exist
            availableThings.Sort((t1, t2) =>
                (t1.Position - rootCell).LengthHorizontalSquared.CompareTo(
                (t2.Position - rootCell).LengthHorizontalSquared));

            foreach (IngredientCount ingredient in bill.recipe.ingredients)
            {
                float baseCount = ingredient.GetBaseCount();

                foreach (Thing thing in availableThings)
                {
                    if (ingredient.filter.Allows(thing) && bill.ingredientFilter.Allows(thing))
                    {
                        // We need to calculate based on the count required
                        int countRequired = ingredient.CountRequiredOfFor(thing.def, bill.recipe, bill);
                        // Instead of using a non-existent ValuePerUnitOf method, we'll use 1.0 as default value per unit
                        float valuePerUnit = 1f;
                        int countToAdd = Mathf.Min(Mathf.CeilToInt(baseCount / valuePerUnit), thing.stackCount);

                        if (countToAdd > 0)
                        {
                            chosen.Add(new ThingCount(thing, countToAdd));
                            baseCount -= countToAdd * valuePerUnit;

                            if (baseCount <= 0.0001f)
                                break;
                        }
                    }
                }

                if (baseCount > 0.0001f)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Checks if there's an active emergency that should override normal work priorities
        /// </summary>
        public static bool IsEmergencyActive(Pawn pawn, string emergencyType)
        {
            if (pawn?.Map == null)
                return false;

            switch (emergencyType)
            {
                case "Fire":
                    {
                        List<Thing> fires = pawn.Map.listerThings.ThingsOfDef(ThingDefOf.Fire);
                        for (int i = 0, c = fires.Count; i < c; i++)
                        {
                            var f = fires[i] as Fire;
                            if (f != null && f.Spawned && pawn.Map.areaManager.Home[f.Position])
                                return true;
                        }
                        return false;
                    }

                case "MedicalEmergency":
                    {
                        List<Pawn> colonists = pawn.Map.mapPawns.FreeColonists;
                        for (int i = 0, c = colonists.Count; i < c; i++)
                        {
                            Pawn other = colonists[i];
                            if (other != pawn && other.health.HasHediffsNeedingTend())
                                return true;
                        }
                        return false;
                    }

                default:
                    return false;
            }
        }


        /// <summary>
        /// Registers bypass conditions for a JobGiver
        /// </summary>
        public static void RegisterBypassConditions(Type jobGiverType, Utility_ThinkTreeOptimizationManager.BypassCondition[] conditions)
        {
            if (jobGiverType == null || conditions == null) return;

            _bypassConditions[jobGiverType] = conditions;
        }

        /// <summary>
        /// Gets the registered bypass conditions for a JobGiver
        /// </summary>
        public static Utility_ThinkTreeOptimizationManager.BypassCondition[] GetBypassConditions(Type jobGiverType)
        {
            if (jobGiverType == null) return null;

            if (_bypassConditions.TryGetValue(jobGiverType, out var conditions))
                return conditions;

            return null;
        }

        /// <summary>
        /// Checks if a JobGiver is active for a specific map
        /// </summary>
        public static bool IsJobGiverActive(Type jobGiverType, int mapId)
        {
            if (jobGiverType == null)
                return true; // Default to active if not found

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
    }
}