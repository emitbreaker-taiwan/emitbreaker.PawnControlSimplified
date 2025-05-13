using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Provides a common base structure for all PawnControl JobGivers.
    /// This abstract class defines the shared interface and functionality
    /// while allowing derived classes to implement their own caching systems.
    /// </summary>
    public abstract class JobGiver_PawnControl : ThinkNode_JobGiver, IResettableCache
    {
        #region Configuration

        /// <summary>
        /// Tag used for eligibility checks in the wrapper
        /// </summary>
        public abstract string WorkTag { get; }

        /// <summary>
        /// Human-readable name for debug logging
        /// </summary>
        protected virtual string DebugName => GetType().Name;

        /// <summary>
        /// How many ticks between cache rebuilds
        /// </summary>
        protected virtual int CacheUpdateInterval => 120;

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        protected abstract bool RequiresDesignator { get; }

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        protected abstract bool RequiresMapZoneorArea { get; }

        /// <summary>
        /// Whether this job giver requires player faction specifically (for jobs like deconstruct)
        /// </summary>
        protected abstract bool RequiresPlayerFaction { get; }

        /// <summary>
        /// Whether this construction job requires specific tag for non-humanlike pawns
        /// </summary>
        public abstract PawnEnumTags RequiredTag { get; }

        /// <summary>
        /// Checks if a non-humanlike pawn has the required capabilities for this job giver
        /// </summary>
        protected abstract bool HasRequiredCapabilities(Pawn pawn);

        /// <summary>
        /// The designation type this job giver handles
        /// </summary>
        protected abstract DesignationDef TargetDesignation { get; }

        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected abstract JobDef WorkJobDef { get; }

        #endregion

        #region Cache System

        /// <summary>
        /// Instance cache for this job giver - one per map
        /// </summary>
        private readonly Dictionary<int, List<Thing>> _targetCache = new Dictionary<int, List<Thing>>();

        /// <summary>
        /// Map-based tracking of last update times for this job giver
        /// </summary>
        private readonly Dictionary<int, int> _lastCacheUpdateTicks = new Dictionary<int, int>();

        /// <summary>
        /// Optional reachability cache to avoid duplicate pathfinding calculations
        /// </summary>
        private readonly Dictionary<int, Dictionary<Thing, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Thing, bool>>();

        /// <summary>
        /// Initialize the job giver cache. Should be called in constructor of derived classes.
        /// </summary>
        protected void InitializeCache<T>() where T : Thing
        {
            // Register this job giver type with the cache manager
            Utility_JobGiverCacheManager<T>.RegisterJobGiver(this.GetType());

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Initialized cache for {GetType().Name}");
            }
        }

        /// <summary>
        /// Check if the cache needs to be updated for a specific map
        /// </summary>
        protected bool ShouldUpdateCache(int mapId)
        {
            return Utility_JobGiverCacheManager<Thing>.NeedsUpdate(
                this.GetType(),
                mapId,
                CacheUpdateInterval
            );
        }

        /// <summary>
        /// Update the job giver's cache for a specific map using the job-specific update method
        /// </summary>
        protected void UpdateCache(int mapId, Map map)
        {
            if (map == null) return;

            try
            {
                // Use the job-specific method to update the cache targets
                IEnumerable<Thing> targets = UpdateJobSpecificCache(map);

                // Update the cache
                Utility_JobGiverCacheManager<Thing>.UpdateCache(
                    this.GetType(),
                    mapId,
                    targets
                );

                if (Prefs.DevMode)
                {
                    int count = targets.Count();
                    Utility_DebugManager.LogNormal($"{GetType().Name} cache updated for map {mapId}: {count} items");
                }
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error updating cache for {GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Get targets from this job giver's cache for a specific map
        /// </summary>
        protected List<Thing> GetCachedTargets(int mapId)
        {
            return Utility_JobGiverCacheManager<Thing>.GetTargets(this.GetType(), mapId);
        }

        /// <summary>
        /// Try to get the cached reachability result for a target
        /// </summary>
        protected bool TryGetReachabilityResult(int mapId, Thing target, out bool result)
        {
            return Utility_JobGiverCacheManager<Thing>.TryGetReachabilityResult(
                this.GetType(),
                mapId,
                target,
                out result
            );
        }

        /// <summary>
        /// Cache a reachability result for a target
        /// </summary>
        protected void CacheReachabilityResult(int mapId, Thing target, bool result)
        {
            Utility_JobGiverCacheManager<Thing>.CacheReachabilityResult(
                this.GetType(),
                mapId,
                target,
                result
            );
        }

        /// <summary>
        /// Job-specific cache update method that derived classes should override to implement
        /// specialized target collection logic. Similar to UpdateSowableCellsCache in the example.
        /// </summary>
        protected virtual IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            // Default implementation calls GetTargets for backward compatibility
            return GetTargets(map);
        }

        /// <summary>
        /// Gets targets for this job giver - for backward compatibility
        /// Derived classes should prefer overriding UpdateJobSpecificCache instead
        /// </summary>
        protected abstract IEnumerable<Thing> GetTargets(Map map);

        /// <summary>
        /// Reset the cache - implements IResettableCache
        /// </summary>
        public virtual void Reset()
        {
            // Reset just this job giver's cache
            Utility_JobGiverCacheManager<Thing>.ResetJobGiverCache(this.GetType());

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Cache reset for {GetType().Name}");
            }
        }

        #endregion

        #region Core flow

        /// <summary>
        /// Standard implementation of TryGiveJob that delegates to the derived class's
        /// job creation logic.
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (ShouldSkip(pawn))
            {
                return null;
            }

            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) => CreateJobFor(p, forced),
                debugJobDesc: DebugName,
                skipEmergencyCheck: false,
                jobGiverType: GetType()
            );
        }

        /// <summary>
        /// Template method for creating a job that handles cache update logic
        /// </summary>
        protected virtual Job CreateJobFor(Pawn pawn, bool forced)
        {
            if (pawn?.Map == null)
                return null;

            int mapId = pawn.Map.uniqueID;

            // Update cache if needed
            if (ShouldUpdateCache(mapId))
            {
                UpdateCache(mapId, pawn.Map);
            }

            // Get targets from cache
            var targets = GetCachedTargets(mapId);
            if (targets == null || targets.Count == 0)
                return null;

            // Process targets to create job
            return ProcessCachedTargets(pawn, targets, forced);
        }

        /// <summary>
        /// Processes cached targets to find a valid job. Called by derived classes.
        /// </summary>
        protected abstract Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced);

        /// <summary>
        /// Determines if the job giver should execute on this tick.
        /// By default, executes every 5 ticks.
        /// </summary>
        protected virtual bool ShouldExecuteNow(int mapId)
        {
            // Derive how often to run from the static base priority:
            float pri = GetBasePriority(WorkTag);

            // Avoid div-zero or sub-1 intervals:
            int interval = Math.Max(1, (int)Math.Round(60f / pri));
            return Find.TickManager.TicksGame % interval == 0;
        }

        #endregion

        #region Priority

        /// <summary>
        /// Presort custom job givers beforce ThinkTree_PrioritySorter sorts them.
        /// </summary>
        public virtual bool ShouldSkip(Pawn pawn)
        {
            // 1) Skip if no map/pawn
            if (pawn?.Map == null) return true;

            // 2) Skip if the Work-tab toggle is off
            if (!Utility_TagManager.WorkTypeSettingEnabled(pawn, WorkTag))
                return true;

            // 3) Skip if this jobgiver shouldn't run on this tick
            if (!ShouldExecuteNow(pawn.Map.uniqueID))
                return true;

            // 4) Skip if mod-extension or global-state rules block it
            if (!Utility_JobGiverManager.IsEligibleForSpecializedJobGiver(pawn, WorkTag))
                return true;

            // 5) Skip if pawn has no required capabilities
            if (!HasRequiredCapabilities(pawn))
                return true;

            return false;
        }

        /// <summary>
        /// Unified priority lookup based on WorkTag
        /// </summary>
        public override float GetPriority(Pawn pawn)
        {
            return GetBasePriority(WorkTag);
        }

        /// <summary>
        /// Standard priority lookup table based on work type
        /// </summary>
        protected virtual float GetBasePriority(string workTag)
        {
            switch (workTag)
            {
                // Emergency/Critical
                case "Firefighter": return 9.0f;
                case "Patient": return 8.8f;
                case "Doctor": return 8.5f;

                // High Priority
                case "PatientBedRest": return 8.0f;
                case "BasicWorker": return 7.8f;
                case "Childcare": return 7.5f;
                case "Warden": return 7.2f;
                case "Handling": return 7.0f;
                case "Cooking": return 6.8f;

                // Medium-High Priority
                case "Hunting": return 6.5f;
                case "Construction": return 6.2f;
                case "Growing": return 5.8f;
                case "Mining": return 5.5f;

                // Medium Priority
                case "PlantCutting": return 5.2f;
                case "Smithing": return 4.9f;
                case "Tailoring": return 4.7f;
                case "Art": return 4.5f;
                case "Crafting": return 4.3f;

                // Low Priority
                case "Hauling": return 3.9f;
                case "Cleaning": return 3.5f;
                case "Research": return 3.2f;
                case "DarkStudy": return 3.0f;

                default: return 5.0f;
            }
        }

        #endregion

        #region Debug support

        /// <summary>
        /// Diagnostic method to check cache status
        /// </summary>
        protected void DiagnoseCache(int mapId)
        {
            if (!Prefs.DevMode) return;

            var targets = GetCachedTargets(mapId);

            Utility_DebugManager.LogNormal(
                $"Cache diagnosis for {GetType().Name} on map {mapId}:\n" +
                $"- Targets count: {targets?.Count ?? 0}\n" +
                $"- Last update: {(_lastCacheUpdateTicks.TryGetValue(mapId, out int tick) ? tick : -1)}\n" +
                $"- Current tick: {Find.TickManager.TicksGame}\n" +
                $"- Update interval: {CacheUpdateInterval}"
            );
        }

        public override string ToString()
        {
            return DebugName;
        }

        #endregion
    }
}