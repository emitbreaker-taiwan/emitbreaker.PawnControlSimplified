using System;
using System.Collections.Generic;
using RimWorld;
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

        #endregion

        #region Cache Fields

        // Shared cache storage - base class maintains the dictionary structure
        private static readonly Dictionary<Type, Dictionary<int, object>> _caches =
            new Dictionary<Type, Dictionary<int, object>>();

        // Reachability caches
        private static readonly Dictionary<Type, Dictionary<int, Dictionary<Thing, bool>>> _reachabilityCaches =
            new Dictionary<Type, Dictionary<int, Dictionary<Thing, bool>>>();

        #endregion

        #region Initialization

        /// <summary>
        /// Automatically registers this JobGiver during static construction
        /// </summary>
        protected JobGiver_PawnControl()
        {
            // Register with tick manager on instantiation
            Type jobGiverType = GetType();

            // Only register if this is a concrete (non-abstract) JobGiver
            if (!jobGiverType.IsAbstract)
            {
                Utility_JobGiverTickManager.RegisterJobGiver(
                    jobGiverType,
                    WorkTypeName,
                    BasePriority,
                    CustomTickInterval);

                if (Prefs.DevMode)
                {
                    Utility_DebugManager.LogNormal($"AbstractJobGiver_PawnControl: Registered {jobGiverType.Name}");
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

            // Perform standard checks for this pawn and work type
            if (!string.IsNullOrEmpty(WorkTypeName))
            {
                var workTypeDef = Utility_Common.WorkTypeDefNamed(WorkTypeName);
                if (workTypeDef == null) return -100f;

                // Skip if work type disabled for this pawn
                if (!Utility_TagManager.WorkTypeSettingEnabled(pawn, workTypeDef)) return -100f;
            }

            return GetBasePriority();
        }

        /// <summary>
        /// Main entry point for job assignment - implements the standardized flow
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            // Early validation - fail fast
            if (pawn?.Map == null) return null;

            // *** INTEGRATED TICK MANAGEMENT ***
            // Check if this JobGiver should execute on this tick
            Type jobGiverType = GetType();
            int mapId = pawn.Map.uniqueID;

            if (!ShouldExecuteNow(mapId))
            {
                return null;
            }

            // Check if pawn can do this job type at all
            if (!string.IsNullOrEmpty(WorkTypeName) && !CanPawnDoWorkType(pawn, WorkTypeName))
            {
                return null;
            }

            // Update cache if needed
            UpdateCacheIfNeeded(pawn.Map);

            // Quick state check - if no targets exist, exit early
            if (!HasAnyPotentialTargetsOnMap(pawn.Map))
            {
                return null;
            }

            // Perform the actual job creation with standardized flow
            try
            {
                Job job = ExecuteJobGiver(pawn);

                if (job != null && Prefs.DevMode)
                {
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} assigned {job.def.defName} job from {DebugName}");
                }

                return job;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error in {DebugName}.TryGiveJob: {ex}");
                return null;
            }
        }

        #endregion

        #region Tick Management

        /// <summary>
        /// Determines if this JobGiver should execute on the current tick,
        /// integrating with the central Utility_JobGiverTickManager
        /// </summary>
        protected virtual bool ShouldExecuteNow(int mapId)
        {
            // Get the current JobGiver Type for tick management
            Type jobGiverType = GetType();

            // Delegate to the centralized tick manager with ForceExecution flag
            return Utility_JobGiverTickManager.ShouldExecute(jobGiverType, mapId, ForceExecution);
        }

        /// <summary>
        /// Sets this JobGiver as active or inactive for a specific map
        /// </summary>
        protected void SetActiveOnMap(int mapId, bool active)
        {
            Type jobGiverType = GetType();
            Utility_JobGiverTickManager.SetJobGiverActiveForMap(jobGiverType, mapId, active);
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
            return Utility_JobGiverManager.GetWorkTypePriority(WorkTypeName);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Checks if a pawn can do a specific work type
        /// </summary>
        protected bool CanPawnDoWorkType(Pawn pawn, string workTypeName)
        {
            if (string.IsNullOrEmpty(workTypeName)) return true;

            // Check pawn-specific work settings
            WorkTypeDef workType = Utility_Common.WorkTypeDefNamed(workTypeName);
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
            // Use the fact that ShouldExecute already handles tick timing
            // We know if we've reached here, it's time to update
            UpdateCache(map);
        }

        /// <summary>
        /// Gets the cached data for this JobGiver and map
        /// </summary>
        protected T GetCache<T>(int mapId) where T : class, new()
        {
            Type jobGiverType = GetType();

            // Get or create JobGiver-specific cache dictionary
            if (!_caches.TryGetValue(jobGiverType, out var mapCaches))
            {
                mapCaches = new Dictionary<int, object>();
                _caches[jobGiverType] = mapCaches;
            }

            // Get or create map-specific cache
            if (!mapCaches.TryGetValue(mapId, out object cache))
            {
                cache = new T();
                mapCaches[mapId] = cache;
            }

            return cache as T;
        }

        /// <summary>
        /// Gets a reachability cache for this JobGiver
        /// </summary>
        protected Dictionary<Thing, bool> GetReachabilityCache(int mapId)
        {
            Type jobGiverType = GetType();

            // Get or create JobGiver-specific reachability cache
            if (!_reachabilityCaches.TryGetValue(jobGiverType, out var mapCaches))
            {
                mapCaches = new Dictionary<int, Dictionary<Thing, bool>>();
                _reachabilityCaches[jobGiverType] = mapCaches;
            }

            // Get or create map-specific reachability cache
            if (!mapCaches.TryGetValue(mapId, out var cache))
            {
                cache = new Dictionary<Thing, bool>();
                mapCaches[mapId] = cache;
            }

            return cache;
        }

        #endregion

        #region Static Utility Methods

        /// <summary>
        /// Resets all caches for all JobGivers (e.g., on game load)
        /// </summary>
        public static void ResetAllCaches()
        {
            _caches.Clear();
            _reachabilityCaches.Clear();

            // Also reset the tick manager
            Utility_JobGiverTickManager.ResetAll();
        }

        /// <summary>
        /// Validates if a pawn can reach a target
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
            Type jobGiverType = GetType();
            return Utility_JobGiverTickManager.GetNextExecutionTick(jobGiverType);
        }

        #endregion
    }
}