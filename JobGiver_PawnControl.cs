using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// A super abstract base class for all PawnControl JobGivers that provides
    /// standardized execution flow, performance optimizations, and caching.
    /// </summary>
    public abstract class JobGiver_PawnControl : ThinkNode_JobGiver
    {
        #region Registry System

        // Central registry of all JobGiver types
        private static readonly Dictionary<Type, JobGiverRegistryEntry> _jobGiverRegistry =
            new Dictionary<Type, JobGiverRegistryEntry>();

        // JobGivers grouped by work type
        private static readonly Dictionary<string, List<Type>> _jobGiversByWorkType =
            new Dictionary<string, List<Type>>(StringComparer.OrdinalIgnoreCase);

        // JobGiver dependencies for runtime dependency mapping
        private static readonly Dictionary<Type, HashSet<Type>> _jobGiverDependencies =
            new Dictionary<Type, HashSet<Type>>();

        // JobGiver category classification
        private static readonly Dictionary<Type, Utility_GlobalStateManager.JobCategory> _jobGiverCategories =
            new Dictionary<Type, Utility_GlobalStateManager.JobCategory>();

        /// <summary>
        /// Registry entry data structure for each JobGiver
        /// </summary>
        public class JobGiverRegistryEntry
        {
            // Basic identification
            public Type JobGiverType { get; }
            public string WorkTypeName { get; }
            public int BasePriority { get; }
            public int? CustomTickInterval { get; }

            // Extended metadata 
            public Utility_GlobalStateManager.JobCategory JobCategory { get; set; }
            public Utility_GlobalStateManager.PawnCapabilityFlags RequiredCapabilities { get; set; }
            public Dictionary<Utility_GlobalStateManager.ColonyNeedType, float> NeedResponsiveness { get; set; }

            // Runtime statistics
            public int TotalJobsCreated { get; set; }
            public int SuccessfulJobsCreated { get; set; }
            public float AverageExecutionTimeMs { get; set; }

            // Constructor
            public JobGiverRegistryEntry(
                Type jobGiverType,
                string workTypeName,
                int basePriority,
                int? customTickInterval)
            {
                JobGiverType = jobGiverType;
                WorkTypeName = workTypeName;
                BasePriority = basePriority;
                CustomTickInterval = customTickInterval;

                // Default values for extended metadata
                JobCategory = Utility_GlobalStateManager.JobCategory.Basic;
                RequiredCapabilities = Utility_GlobalStateManager.PawnCapabilityFlags.None;
                NeedResponsiveness = new Dictionary<Utility_GlobalStateManager.ColonyNeedType, float>();

                // Initialize statistics
                TotalJobsCreated = 0;
                SuccessfulJobsCreated = 0;
                AverageExecutionTimeMs = 0f;
            }

            /// <summary>
            /// Records execution statistics for performance tracking
            /// </summary>
            public void RecordExecution(float executionTimeMs, bool wasSuccessful)
            {
                // Update statistics with new execution data
                TotalJobsCreated++;
                if (wasSuccessful) SuccessfulJobsCreated++;

                // Exponential moving average for execution time (80% old value, 20% new value)
                AverageExecutionTimeMs = AverageExecutionTimeMs * 0.8f + executionTimeMs * 0.2f;
            }

            /// <summary>
            /// Gets the success rate of this JobGiver
            /// </summary>
            public float SuccessRate => TotalJobsCreated > 0 ? (float)SuccessfulJobsCreated / TotalJobsCreated : 0f;

            /// <summary>
            /// Gets the efficiency rating (success rate ÷ execution time)
            /// </summary>
            public float EfficiencyRating
            {
                get
                {
                    float successRate = SuccessRate;
                    if (AverageExecutionTimeMs <= 0.01f) return successRate * 100f;
                    return successRate / (AverageExecutionTimeMs / 10f);
                }
            }
        }

        #endregion

        #region Registry Configuration Properties

        /// <summary>
        /// Job category this JobGiver belongs to
        /// </summary>
        protected virtual Utility_GlobalStateManager.JobCategory JobCategory =>
            Utility_GlobalStateManager.JobCategory.Basic;

        /// <summary>
        /// Capabilities required for pawns to use this JobGiver
        /// </summary>
        protected virtual Utility_GlobalStateManager.PawnCapabilityFlags RequiredCapabilities =>
            Utility_GlobalStateManager.PawnCapabilityFlags.None;

        /// <summary>
        /// How this JobGiver responds to colony needs (need type → response strength 0-1)
        /// </summary>
        protected virtual Dictionary<Utility_GlobalStateManager.ColonyNeedType, float> NeedResponsiveness => null;

        /// <summary>
        /// Dependencies this JobGiver has on other JobGivers
        /// </summary>
        protected virtual Type[] Dependencies => null;

        #endregion

        #region Configuration Properties

        /// <summary>
        /// Work type associated with this JobGiver (can be null for non-work JobGivers)
        /// </summary>
        protected virtual string WorkTypeName => null;

        /// <summary>
        /// Base priority of this JobGiver - affects execution order and frequency
        /// </summary>
        protected virtual int BasePriority => 5; // Default medium priority

        /// <summary>
        /// Custom tick interval for this JobGiver, or null to use priority-based interval
        /// </summary>
        protected virtual int? CustomTickInterval => null;

        /// <summary>
        /// How long this JobGiver's cache should remain valid (in ticks)
        /// </summary>
        protected virtual int CacheUpdateInterval => 120; // 2 seconds default

        /// <summary>
        /// Whether this JobGiver should use distance bucketing for target selection
        /// </summary>
        protected virtual bool UseDistanceBucketing => true;

        /// <summary>
        /// Distance thresholds for bucketing (in squared cells)
        /// </summary>
        protected virtual float[] DistanceThresholds => new float[] { 400f, 900f, 1600f }; // 20, 30, 40 tiles

        /// <summary>
        /// Debug name for logging
        /// </summary>
        protected virtual string DebugName => GetType().Name;

        /// <summary>
        /// Whether to force execution regardless of tick scheduling
        /// </summary>
        protected virtual bool ForceExecution => false;

        /// <summary>
        /// Maximum idle ticks between job checks - integrates with RimWorld's ThinkNode system
        /// </summary>
        protected virtual int MaxThinkTreeIdleTicks => Utility_JobGiverTickManager.GetIntervalForPriority(BasePriority);

        #endregion

        #region Cached WorkType

        // Cache the resolved WorkTypeDef to avoid repeated lookups
        private WorkTypeDef _cachedWorkTypeDef;

        /// <summary>
        /// Gets the cached WorkTypeDef for this JobGiver
        /// </summary>
        protected WorkTypeDef ResolvedWorkTypeDef
        {
            get
            {
                if (_cachedWorkTypeDef == null && !string.IsNullOrEmpty(WorkTypeName))
                    _cachedWorkTypeDef = Utility_WorkTypeManager.WorkTypeDefNamed(WorkTypeName);
                return _cachedWorkTypeDef;
            }
        }

        #endregion

        #region Cache System

        // Static dictionary to store caches per concrete type
        private static readonly Dictionary<Type, Dictionary<int, Dictionary<string, object>>> _typeSpecificCaches = new Dictionary<Type, Dictionary<int, Dictionary<string, object>>>();

        /// <summary>
        /// Generic static cache container for per-type caching without dictionary lookups
        /// </summary>
        protected static class Cache<T> where T : JobGiver_PawnControl
        {
            // Per-derived-class static cache
            public static readonly Dictionary<int, object> MapData = new Dictionary<int, object>();
            public static readonly Dictionary<int, Dictionary<Thing, bool>> Reachability =
                new Dictionary<int, Dictionary<Thing, bool>>();

            // Target bucketing for this JobGiver type
            private static readonly Dictionary<int, List<Thing>[]> _bucketsByMapId =
                new Dictionary<int, List<Thing>[]>();

            /// <summary>
            /// Gets or creates distance buckets for a map
            /// </summary>
            public static List<Thing>[] GetOrCreateBuckets(int mapId, float[] thresholds)
            {
                if (!_bucketsByMapId.TryGetValue(mapId, out var buckets))
                {
                    // Create new buckets
                    buckets = new List<Thing>[thresholds.Length + 1];
                    for (int i = 0; i < buckets.Length; i++)
                        buckets[i] = new List<Thing>();

                    _bucketsByMapId[mapId] = buckets;
                }
                else
                {
                    // Clear existing buckets
                    for (int i = 0; i < buckets.Length; i++)
                        buckets[i].Clear();
                }

                return buckets;
            }

            /// <summary>
            /// Clears all cache data for a map
            /// </summary>
            public static void ClearMapData(int mapId)
            {
                MapData.Remove(mapId);
                Reachability.Remove(mapId);
                _bucketsByMapId.Remove(mapId);
            }

            /// <summary>
            /// Clears all cache data
            /// </summary>
            public static void Clear()
            {
                MapData.Clear();
                Reachability.Clear();
                _bucketsByMapId.Clear();
            }
        }

        #endregion

        #region Initialization and Registry

        // Profiler for execution timing
        private static readonly System.Diagnostics.Stopwatch _profiler = new System.Diagnostics.Stopwatch();

        /// <summary>
        /// Automatically registers this JobGiver during construction
        /// </summary>
        protected JobGiver_PawnControl()
        {
            // Set the maxChildIdleTicks field via reflection if possible
            try
            {
                Type baseType = typeof(ThinkNode_JobGiver);
                var field = baseType.GetField("maxChildIdleTicks",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);

                if (field != null)
                {
                    field.SetValue(this, MaxThinkTreeIdleTicks);

                    if (Prefs.DevMode)
                    {
                        Utility_DebugManager.LogNormal($"Set maxChildIdleTicks to {MaxThinkTreeIdleTicks} for {GetType().Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                if (Prefs.DevMode)
                {
                    Utility_DebugManager.LogError($"Failed to set maxChildIdleTicks for {GetType().Name}: {ex.Message}");
                }
            }

            // Register with tick manager and registry system
            Type jobGiverType = GetType();

            // Only register if this is a concrete (non-abstract) JobGiver
            if (!jobGiverType.IsAbstract)
            {
                // Register with tick manager
                Utility_JobGiverTickManager.RegisterJobGiver(
                    jobGiverType,
                    WorkTypeName,
                    BasePriority,
                    CustomTickInterval);

                // Register with central registry
                RegisterWithCentralRegistry(
                    jobGiverType,
                    WorkTypeName,
                    BasePriority,
                    CustomTickInterval,
                    JobCategory,
                    RequiredCapabilities,
                    NeedResponsiveness,
                    Dependencies);

                if (Prefs.DevMode)
                {
                    Utility_DebugManager.LogNormal($"JobGiver_PawnControl: Registered {jobGiverType.Name}");
                }
            }
        }

        /// <summary>
        /// Registers a JobGiver with the central registry system
        /// </summary>
        private static void RegisterWithCentralRegistry(
            Type jobGiverType,
            string workTypeName,
            int basePriority,
            int? customTickInterval,
            Utility_GlobalStateManager.JobCategory jobCategory,
            Utility_GlobalStateManager.PawnCapabilityFlags requiredCapabilities,
            Dictionary<Utility_GlobalStateManager.ColonyNeedType, float> needResponsiveness,
            Type[] dependencies)
        {
            // Add to main registry
            if (!_jobGiverRegistry.ContainsKey(jobGiverType))
            {
                var entry = new JobGiverRegistryEntry(
                    jobGiverType,
                    workTypeName,
                    basePriority,
                    customTickInterval);

                // Set extended metadata
                entry.JobCategory = jobCategory;
                entry.RequiredCapabilities = requiredCapabilities;

                if (needResponsiveness != null)
                {
                    entry.NeedResponsiveness = new Dictionary<Utility_GlobalStateManager.ColonyNeedType, float>(
                        needResponsiveness);
                }

                _jobGiverRegistry[jobGiverType] = entry;

                // Also register with GlobalStateManager for colony state integration
                Utility_GlobalStateManager.RegisterJobGiverMetadata(
                    jobGiverType,
                    jobCategory,
                    requiredCapabilities,
                    needResponsiveness);
            }

            // Add to work type grouping
            if (!string.IsNullOrEmpty(workTypeName))
            {
                if (!_jobGiversByWorkType.TryGetValue(workTypeName, out var workTypeList))
                {
                    workTypeList = new List<Type>();
                    _jobGiversByWorkType[workTypeName] = workTypeList;
                }

                if (!workTypeList.Contains(jobGiverType))
                {
                    workTypeList.Add(jobGiverType);
                }
            }

            // Store job category for quick lookup
            _jobGiverCategories[jobGiverType] = jobCategory;

            // Register dependencies
            if (dependencies != null && dependencies.Length > 0)
            {
                if (!_jobGiverDependencies.TryGetValue(jobGiverType, out var dependencySet))
                {
                    dependencySet = new HashSet<Type>();
                    _jobGiverDependencies[jobGiverType] = dependencySet;
                }

                foreach (var dependency in dependencies)
                {
                    if (dependency != null && dependency != jobGiverType)
                    {
                        dependencySet.Add(dependency);
                    }
                }
            }
        }

        #endregion

        #region ThinkNode Overrides

        /// <summary>
        /// Gets the priority used for ThinkTree sorting
        /// </summary>
        public override float GetPriority(Pawn pawn)
        {
            if (pawn?.def == null || pawn.Map == null) return -100f;

            // Perform standard checks for this pawn and work type using cached WorkTypeDef
            if (ResolvedWorkTypeDef != null &&
                !Utility_TagManager.WorkTypeSettingEnabled(pawn, ResolvedWorkTypeDef))
                return -100f;

            return GetBasePriority();
        }

        /// <summary>
        /// Main entry point for job assignment - implements the standardized flow
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            // Early validation - fail fast
            if (pawn?.Map == null) return null;

            // Check if this JobGiver should execute on this tick
            int mapId = pawn.Map.uniqueID;
            if (!ShouldExecuteNow(mapId)) return null;

            // Check if pawn can do this job type at all
            if (ResolvedWorkTypeDef != null && !CanPawnDoWorkType(pawn, WorkTypeName))
                return null;

            // Check for global state flags
            if (Utility_GlobalStateManager.ShouldSkipJobGiverDueToGlobalState(this, pawn))
                return null;

            // Update cache if needed
            UpdateCacheIfNeeded(pawn.Map);

            // Quick state check - if no targets exist, exit early
            if (!HasAnyPotentialTargetsOnMap(pawn.Map))
                return null;

            // Start profiling
            _profiler.Restart();

            Job result = null;
            bool wasSuccessful = false;

            try
            {
                // Separate execution paths for dev mode vs production for better branch prediction
                if (Prefs.DevMode)
                {
                    result = ExecuteJobGiverWithLogging(pawn);
                }
                else
                {
                    result = ExecuteJobGiverNoLogging(pawn);
                }

                wasSuccessful = result != null;
                return result;
            }
            finally
            {
                // Stop profiling and record statistics
                _profiler.Stop();
                float executionTimeMs = (float)_profiler.ElapsedTicks / (float)TimeSpan.TicksPerMillisecond;
                RecordJobGiverExecution(GetType(), executionTimeMs, wasSuccessful);
            }
        }

        /// <summary>
        /// Records job giver execution statistics
        /// </summary>
        private static void RecordJobGiverExecution(Type jobGiverType, float executionTimeMs, bool wasSuccessful)
        {
            // Record in registry if this JobGiver is registered
            if (_jobGiverRegistry.TryGetValue(jobGiverType, out var entry))
            {
                entry.RecordExecution(executionTimeMs, wasSuccessful);
            }

            // Could also record with adaptive profiling
            if (Utility_AdaptiveProfilingManager.IsProfilingEnabled)
            {
                // The AdaptiveProfiler needs to know the pawn but we don't have it here
                // This is a limitation that would need to be addressed in a full implementation
            }
        }

        /// <summary>
        /// Execute job creation with full logging (dev mode only)
        /// </summary>
        private Job ExecuteJobGiverWithLogging(Pawn pawn)
        {
            try
            {
                Job job = ExecuteJobGiver(pawn);
                if (job != null)
                {
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} assigned {job.def.defName} job from {DebugName}");
                }
                return job;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error in {DebugName}.TryGiveJob for {pawn.LabelShort}: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Execute job creation without logging (production path)
        /// </summary>
        private Job ExecuteJobGiverNoLogging(Pawn pawn)
        {
            try
            {
                return ExecuteJobGiver(pawn);
            }
            catch
            {
                // Silent failure in production
                return null;
            }
        }

        #endregion

        #region Tick Management

        /// <summary>
        /// Determines if this JobGiver should execute on the current tick
        /// </summary>
        protected virtual bool ShouldExecuteNow(int mapId)
        {
            // Use the centralized tick manager with ForceExecution flag
            return Utility_JobGiverTickManager.ShouldExecute(GetType(), mapId, ForceExecution);
        }

        /// <summary>
        /// Sets this JobGiver as active or inactive for a specific map
        /// </summary>
        protected void SetActiveOnMap(int mapId, bool active)
        {
            Utility_JobGiverTickManager.SetJobGiverActiveForMap(GetType(), mapId, active);
        }

        #endregion

        #region Abstract Methods For Derived Classes

        /// <summary>
        /// The actual job creation logic - must be implemented by derived classes
        /// </summary>
        protected abstract Job ExecuteJobGiver(Pawn pawn);

        /// <summary>
        /// Fast check if there are any potential targets on the map
        /// </summary>
        protected virtual bool HasAnyPotentialTargetsOnMap(Map map)
        {
            // Default implementation assumes targets exist
            // Override in derived class for more efficient checking
            return true;
        }

        /// <summary>
        /// Updates the cache for this JobGiver on the specified map
        /// </summary>
        protected virtual void UpdateCache(Map map)
        {
            // Default no-op implementation
            // Override in derived class to implement caching
        }

        /// <summary>
        /// Get base priority for this JobGiver
        /// </summary>
        protected virtual float GetBasePriority()
        {
            // Default implementation uses static lookup
            return Utility_WorkTypeManager.GetWorkTypePriority(WorkTypeName);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Checks if a pawn can do a specific work type
        /// </summary>
        protected bool CanPawnDoWorkType(Pawn pawn, string workTypeName)
        {
            if (string.IsNullOrEmpty(workTypeName)) return true;

            // Use cached WorkTypeDef if possible
            WorkTypeDef workType = ResolvedWorkTypeDef ??
                Utility_WorkTypeManager.WorkTypeDefNamed(workTypeName);

            if (workType == null) return false;

            // Check if mod extension allows this work type
            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null) return false;

            // Check tag-based work restrictions
            if (!Utility_TagManager.WorkEnabled(pawn.def, workTypeName)) return false;

            // Check pawn work settings
            if (!Utility_TagManager.WorkTypeSettingEnabled(pawn, workType)) return false;

            return true;
        }

        /// <summary>
        /// Updates the cache if needed based on cache interval
        /// </summary>
        protected void UpdateCacheIfNeeded(Map map)
        {
            int mapId = map.uniqueID;
            int currentTick = Find.TickManager.TicksGame;

            // Get or create the cache tracker
            var cacheTracker = GetCache<CacheTracker>(mapId);

            // Check if enough time has passed since last update
            if (currentTick - cacheTracker.LastUpdateTick >= CacheUpdateInterval)
            {
                // Update timestamp first to prevent redundant updates
                cacheTracker.LastUpdateTick = currentTick;

                // Perform actual cache update
                UpdateCache(map);
            }
        }

        /// <summary>
        /// Simple tracker for cache update times
        /// </summary>
        protected class CacheTracker
        {
            public int LastUpdateTick = 0;
        }

        /// <summary>
        /// Gets the cached data for this JobGiver and map using strongly-typed cache
        /// </summary>
        protected T GetCache<T>(int mapId) where T : class, new()
        {
            // Use GetType() to get the actual derived class type
            Type derivedType = GetType();

            // Ensure the Cache class has an entry for this specific derived type
            if (!_typeSpecificCaches.TryGetValue(derivedType, out var typeCache))
            {
                typeCache = new Dictionary<int, Dictionary<string, object>>();
                _typeSpecificCaches[derivedType] = typeCache;
            }

            // Get or create the map-specific cache
            if (!typeCache.TryGetValue(mapId, out var mapCache))
            {
                mapCache = new Dictionary<string, object>();
                typeCache[mapId] = mapCache;
            }

            // Get or create the typed cache object
            string cacheKey = typeof(T).FullName;
            if (!mapCache.TryGetValue(cacheKey, out var cache))
            {
                cache = new T();
                mapCache[cacheKey] = cache;
            }

            return (T)cache;
        }

        /// <summary>
        /// Gets a reachability cache for this JobGiver
        /// </summary>
        protected Dictionary<Thing, bool> GetReachabilityCache(int mapId)
        {
            // Get cache for this specific derived type
            var mapCaches = Cache<JobGiver_PawnControl>.Reachability;

            // Get or create map-specific cache
            if (!mapCaches.TryGetValue(mapId, out var cache))
            {
                cache = new Dictionary<Thing, bool>();
                mapCaches[mapId] = cache;
            }

            return cache;
        }

        // Add a static bucket pool
        private static readonly Dictionary<Type, Dictionary<int, List<Thing>[]>> _bucketPoolByType =
            new Dictionary<Type, Dictionary<int, List<Thing>[]>>();

        /// <summary>
        /// Gets optimized distance buckets for a map with capacity hints
        /// </summary>
        protected List<Thing>[] GetDistanceBuckets(int mapId, int expectedCapacity = 10)
        {
            Type derivedType = GetType();

            // Get or create type-specific bucket dictionary
            if (!_bucketPoolByType.TryGetValue(derivedType, out var bucketsByMap))
            {
                bucketsByMap = new Dictionary<int, List<Thing>[]>();
                _bucketPoolByType[derivedType] = bucketsByMap;
            }

            // Get or create map-specific buckets
            if (!bucketsByMap.TryGetValue(mapId, out var buckets))
            {
                buckets = new List<Thing>[DistanceThresholds.Length + 1];
                for (int i = 0; i < buckets.Length; i++)
                    buckets[i] = new List<Thing>(expectedCapacity);

                bucketsByMap[mapId] = buckets;
            }
            else
            {
                // Clear existing buckets, preserving capacity
                for (int i = 0; i < buckets.Length; i++)
                {
                    buckets[i].Clear();
                    // Expand capacity if needed but don't shrink
                    if (buckets[i].Capacity < expectedCapacity)
                        buckets[i].Capacity = expectedCapacity;
                }
            }

            return buckets;
        }

        #endregion

        #region Static Utility Methods

        /// <summary>
        /// Resets all caches for all JobGivers (e.g., on game load)
        /// </summary>
        public static void ResetAllCaches()
        {
            // Clear type-specific caches
            Cache<JobGiver_PawnControl>.Clear();

            // Also reset the tick manager
            Utility_JobGiverTickManager.ResetAll();
        }

        /// <summary>
        /// Cleans up all caches for a specific map ID
        /// </summary>
        public static void CleanupMap(int mapId)
        {
            // Clear type caches for this map
            foreach (var typeCache in _typeSpecificCaches.Values)
            {
                typeCache.Remove(mapId);
            }

            // Clear bucket pools
            foreach (var bucketsByMap in _bucketPoolByType.Values)
            {
                bucketsByMap.Remove(mapId);
            }

            // Also notify the tick manager
            Utility_JobGiverTickManager.CleanupMap(mapId);
        }

        /// <summary>
        /// Validates if a pawn can reach a target using cached results
        /// </summary>
        protected bool CanReach(Pawn pawn, Thing target, Dictionary<Thing, bool> reachabilityCache = null)
        {
            if (pawn == null || target == null) return false;

            // Use provided cache or get default
            var cache = reachabilityCache ?? GetReachabilityCache(pawn.Map.uniqueID);

            // Check cache first
            if (cache.TryGetValue(target, out bool canReach))
            {
                return canReach;
            }

            // Calculate reachability
            canReach = pawn.CanReserveAndReach(target, PathEndMode.Touch, pawn.NormalMaxDanger());

            // Cache result
            cache[target] = canReach;

            return canReach;
        }

        /// <summary>
        /// Gets the next scheduled execution tick for this JobGiver
        /// </summary>
        protected int GetNextExecutionTick(int mapId)
        {
            return Utility_JobGiverTickManager.GetNextExecutionTick(GetType());
        }

        /// <summary>
        /// Fills the provided buckets with targets based on distance
        /// </summary>
        protected void FillDistanceBuckets<T>(Pawn pawn, IEnumerable<T> targets,
            List<T>[] buckets, Func<T, float> distanceSquaredFunc = null) where T : Thing
        {
            if (pawn == null || targets == null || buckets == null) return;

            // Clear all buckets first
            foreach (var bucket in buckets)
                bucket.Clear();

            // Default distance function if none provided
            distanceSquaredFunc = distanceSquaredFunc ??
                ((T t) => (t.Position - pawn.Position).LengthHorizontalSquared);

            foreach (T target in targets)
            {
                // Skip invalid targets
                if (target == null || target.Destroyed || !target.Spawned)
                    continue;

                // Get distance 
                float distSq = distanceSquaredFunc(target);

                // Assign to appropriate bucket
                int bucketIndex = DistanceThresholds.Length; // Default to last bucket (furthest)
                for (int i = 0; i < DistanceThresholds.Length; i++)
                {
                    if (distSq < DistanceThresholds[i])
                    {
                        bucketIndex = i;
                        break;
                    }
                }

                buckets[bucketIndex].Add(target);
            }
        }

        /// <summary>
        /// Finds the first valid target from distance buckets
        /// </summary>
        protected T FindFirstValidTargetInBuckets<T>(
            List<T>[] buckets,
            Pawn pawn,
            Func<T, Pawn, bool> validationFunc,
            Dictionary<Thing, bool> reachabilityCache = null) where T : Thing
        {
            if (buckets == null || pawn == null || validationFunc == null)
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
                    // Skip validation for invalid things
                    if (thing == null || thing.Destroyed || !thing.Spawned)
                        continue;

                    // Use direct validation function
                    if (validationFunc(thing, pawn))
                        return thing;
                }
            }

            return null;
        }

        #endregion

        #region Registry Query Methods

        /// <summary>
        /// Gets all registered JobGiver types
        /// </summary>
        public static IEnumerable<Type> GetAllRegisteredJobGivers()
        {
            return _jobGiverRegistry.Keys;
        }

        /// <summary>
        /// Gets all JobGiver types for a specific work type
        /// </summary>
        public static IEnumerable<Type> GetJobGiversForWorkType(string workTypeName)
        {
            if (string.IsNullOrEmpty(workTypeName))
                return new Type[0];

            if (_jobGiversByWorkType.TryGetValue(workTypeName, out var jobGivers))
                return jobGivers;

            return new Type[0];
        }

        /// <summary>
        /// Gets the registry entry for a specific JobGiver type
        /// </summary>
        public static JobGiverRegistryEntry GetJobGiverInfo(Type jobGiverType)
        {
            if (jobGiverType == null)
                return null;

            if (_jobGiverRegistry.TryGetValue(jobGiverType, out var entry))
                return entry;

            return null;
        }

        /// <summary>
        /// Gets all JobGiver types in a specific category
        /// </summary>
        public static IEnumerable<Type> GetJobGiversInCategory(Utility_GlobalStateManager.JobCategory category)
        {
            return _jobGiverCategories
                .Where(pair => pair.Value == category)
                .Select(pair => pair.Key);
        }

        /// <summary>
        /// Gets all dependencies of a JobGiver
        /// </summary>
        public static IEnumerable<Type> GetJobGiverDependencies(Type jobGiverType)
        {
            if (jobGiverType == null)
                return new Type[0];

            if (_jobGiverDependencies.TryGetValue(jobGiverType, out var dependencies))
                return dependencies;

            return new Type[0];
        }

        /// <summary>
        /// Gets all JobGivers that depend on the specified JobGiver
        /// </summary>
        public static IEnumerable<Type> GetJobGiverDependents(Type jobGiverType)
        {
            if (jobGiverType == null)
                return new Type[0];

            return _jobGiverDependencies
                .Where(pair => pair.Value.Contains(jobGiverType))
                .Select(pair => pair.Key);
        }

        /// <summary>
        /// Gets the most efficient JobGivers by performance metrics
        /// </summary>
        public static IEnumerable<JobGiverRegistryEntry> GetMostEfficientJobGivers(int count = 10)
        {
            return _jobGiverRegistry.Values
                .Where(entry => entry.TotalJobsCreated > 5) // Only consider JobGivers with enough samples
                .OrderByDescending(entry => entry.EfficiencyRating)
                .Take(count);
        }

        /// <summary>
        /// Gets JobGivers that have been executed but never produced a job
        /// </summary>
        public static IEnumerable<JobGiverRegistryEntry> GetUnproductiveJobGivers(int minExecutions = 10)
        {
            return _jobGiverRegistry.Values
                .Where(entry => entry.TotalJobsCreated >= minExecutions && entry.SuccessfulJobsCreated == 0);
        }

        /// <summary>
        /// Gets the slowest JobGivers by execution time
        /// </summary>
        public static IEnumerable<JobGiverRegistryEntry> GetSlowestJobGivers(int count = 10)
        {
            return _jobGiverRegistry.Values
                .Where(entry => entry.TotalJobsCreated > 5) // Only consider JobGivers with enough samples
                .OrderByDescending(entry => entry.AverageExecutionTimeMs)
                .Take(count);
        }

        /// <summary>
        /// Gets JobGivers that respond to a specific colony need
        /// </summary>
        public static IEnumerable<JobGiverRegistryEntry> GetJobGiversForNeed(Utility_GlobalStateManager.ColonyNeedType needType)
        {
            return _jobGiverRegistry.Values
                .Where(entry => entry.NeedResponsiveness.ContainsKey(needType) &&
                       entry.NeedResponsiveness[needType] > 0.01f)
                .OrderByDescending(entry => entry.NeedResponsiveness[needType]);
        }

        #endregion
    }
}