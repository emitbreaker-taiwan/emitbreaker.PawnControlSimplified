using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
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
        protected virtual int CacheUpdateInterval
        {
            get
            {
                // Derive how often to run from the static base priority:
                float priority = GetBasePriority(WorkTag);

                // Avoid div-zero or sub-1 intervals:
                int interval = Math.Max(1, (int)Math.Round(540f / priority));

                return interval;
            }
        }

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        public abstract bool RequiresDesignator { get; }

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        public abstract bool RequiresMapZoneorArea { get; }

        /// <summary>
        /// Whether this job giver requires player faction specifically (for jobs like deconstruct)
        /// </summary>
        public abstract bool RequiresPlayerFaction { get; }

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
        /// Map-based tracking of last update times for this job giver
        /// </summary>
        private readonly Dictionary<int, int> _lastCacheUpdateTicks = new Dictionary<int, int>();

        /// <summary>
        /// Initialize the job giver cache. Should be called in constructor of derived classes.
        /// </summary>
        protected void InitializeCache<T>() where T : Thing
        {
            // Register this job giver type with the cache manager
            Utility_JobGiverCacheManager<T>.RegisterJobGiver(this.GetType());

            if (Utility_DebugManager.ShouldLogDetailed())
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
        /// <summary>
        /// Template method for creating a job that handles cache update logic
        /// </summary>
        protected virtual Job CreateJobFor(Pawn pawn, bool forced)
        {
            if (pawn?.Map == null)
                return null;

            // Debug log to help troubleshoot
            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"CreateJobFor called for {pawn.LabelShort} (def: {pawn.def.defName})");
            }

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

            // CRUCIAL: Filter out targets that are already reserved by ANY pawn
            var filteredTargets = FilterOutAlreadyReservedTarget(pawn, targets);
            if (filteredTargets == null || filteredTargets.Count == 0)
                return null;

            // Process targets to create job
            Job job = ProcessCachedTargets(pawn, filteredTargets, forced);

            // If we got a job, reserve the target
            if (job != null && job.targetA.Thing != null)
            {
                bool reserved = ReserveJobTarget(job.targetA.Thing, pawn);
                if (Prefs.DevMode)
                {
                    if (reserved)
                        Utility_DebugManager.LogNormal($"Job created for {pawn.LabelShort} with target {job.targetA.Thing} and successfully reserved");
                    else
                        Utility_DebugManager.LogNormal($"Job created for {pawn.LabelShort} but failed to reserve target {job.targetA.Thing}");
                }
            }

            return job;
        }

        /// <summary>
        /// Determines whether the specified pawn is eligible for a job based on its faction and other conditions.
        /// </summary>
        /// <remarks>This method checks if the pawn belongs to the player's faction and evaluates
        /// additional conditions such as required designations or map zones. If the pawn does not meet these criteria,
        /// the method returns <see langword="false"/>. This is particularly relevant for jobs that require specific
        /// designations or are restricted to player-controlled pawns.</remarks>
        /// <param name="pawn">The pawn to evaluate for eligibility.</param>
        /// <returns><see langword="true"/> if the pawn meets the faction and designation requirements for the job; otherwise,
        /// <see langword="false"/>.</returns>
        protected virtual bool NonPlayerFactionCheck(Pawn pawn)
        {
            // IMMEDIATE FACTION CHECK - this must happen before any cache access
            if (pawn.Faction != Faction.OfPlayer || (pawn.IsSlave && pawn.HostFaction != Faction.OfPlayer))
            {
                if (RequiresPlayerFaction || RequiresDesignator || RequiresMapZoneorArea)
                {
                    if (Utility_DebugManager.ShouldLogDetailed())
                        Utility_DebugManager.LogNormal($"Skipping job for {pawn.LabelShort} - requires player faction");
                    return false;
                }

                // Also check for specific designations
                if (TargetDesignation != null)
                {
                    if (TargetDesignation == DesignationDefOf.CutPlant ||
                        TargetDesignation == DesignationDefOf.HarvestPlant)
                    {
                        if (Prefs.DevMode)
                            Utility_DebugManager.LogNormal($"Skipping job for {pawn.LabelShort} - plant cutting job requires player faction");
                        return false;
                    }

                    // Check if any of these designations exist on the map
                    if (pawn.Map.designationManager.SpawnedDesignationsOfDef(TargetDesignation).Any())
                    {
                        if (Prefs.DevMode)
                            Utility_DebugManager.LogNormal($"Skipping job for {pawn.LabelShort} - designation job requires player faction");
                        return false;
                    }
                }
            }

            return true;
        }

        protected virtual List<Thing> FilterOutAlreadyReservedTarget(Pawn pawn, List<Thing> targets)
        {
            // Preallocate with expected capacity to avoid resizing
            var filteredTargets = new List<Thing>(Math.Min(targets.Count, 20));

            // Use simple for loop for better performance
            for (int i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (!IsTargetReserved(target, pawn) && pawn.CanReserve(target))
                {
                    filteredTargets.Add(target);
                    // Early exit if we have enough targets
                    if (filteredTargets.Count >= 10) break;
                }
            }

            return filteredTargets.Count > 0 ? filteredTargets : null;
        }

        /// <summary>
        /// Processes cached targets to find a valid job. Called by derived classes.
        /// </summary>
        protected abstract Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced);

        /// <summary>
        /// Helper method to prevent multiple pawns working on the same target
        /// </summary>
        protected bool IsTargetAlreadyReserved(Thing target, Pawn pawn)
        {
            if (target == null || pawn?.Map == null)
                return false;

            // Check if any pawn of the same race is already working on this target
            foreach (Pawn otherPawn in pawn.Map.mapPawns.AllPawnsSpawned)
            {
                // Skip self or pawns of different race
                if (otherPawn == pawn || otherPawn.def != pawn.def)
                    continue;

                // Skip pawns with no job or different job type
                if (otherPawn.CurJob == null || otherPawn.CurJob.def != WorkJobDef)
                    continue;

                // Check if this pawn is working on our target
                if (otherPawn.CurJob.targetA.Thing == target)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Determines if the job giver should execute on this tick.
        /// By default, executes every 5 ticks.
        /// </summary>
        protected virtual bool ShouldExecuteNow(int mapId)
        {
            // Read the global tick count once
            int ticks = Find.TickManager.TicksGame;

            // Get the base priority (3.0f – 9.0f)
            float pri = GetBasePriority(WorkTag);

            // Calculate interval dynamically from priority
            int interval = CalculateDynamicInterval(pri);               // Updated: dynamic mapping

            // Compute a type-specific offset (power-of-two mask)
            int offset = Math.Abs(GetType().GetHashCode()) & (interval - 1);  // Updated: bitmask

            // Fast bitmask check instead of modulo
            return ((ticks + offset) & (interval - 1)) == 0;            // Updated: faster mask test
        }

        /// <summary>
        /// Calculates tick interval based on work priority (minPriority…maxPriority),
        /// scaling linearly between minInterval and maxInterval.
        /// </summary>
        protected virtual int CalculateDynamicInterval(float priority)
        {
            const float minPriority = 3.0f;    // lowest configured priority
            const float maxPriority = 9.0f;    // highest configured priority
            const int minInterval = 1;       // run every tick at max priority
            const int maxInterval = 16;      // slowest for lowest priority

            // Clamp the priority into [minPriority, maxPriority]
            float clamped = Math.Min(maxPriority, Math.Max(minPriority, priority));

            // Normalize so that priority 9f → norm=0, priority 3f → norm=1
            float norm = (maxPriority - clamped) / (maxPriority - minPriority);

            // Scale linearly and round to nearest integer
            return minInterval + (int)Math.Round(norm * (maxInterval - minInterval));
        }

        #endregion

        #region Priority

        /// <summary>
        /// Presort custom job givers beforce ThinkTree_PrioritySorter sorts them.
        /// </summary>
        public virtual bool ShouldSkip(Pawn pawn)
        {
            // Quick null checks first (fastest)
            if (pawn?.Map == null) 
                return true;

            // Check tick interval first (very fast check)
            if (!ShouldExecuteNow(pawn.Map.uniqueID))
                return true;

            // Then check work type settings (still quite fast)
            if (!Utility_TagManager.WorkTypeSettingEnabled(pawn, WorkTag))
                return true;

            // Then faction-related checks
            if (((pawn.Faction != Faction.OfPlayer) ||
                (pawn.IsSlave && pawn.HostFaction != Faction.OfPlayer)) &&
                !Utility_WorkPermissionManager.CanNonPlayerPawnDoWorkType(this, WorkTag))
                return true;

            // More expensive checks last
            if (!Utility_JobGiverManager.IsEligibleForSpecializedJobGiver(pawn, WorkTag))
                return true;

            // More expensive checks for required capabilities (Optional)
            if (!HasRequiredCapabilities(pawn))
                return true;

            // Faction-specific expensive checks last
            if (!NonPlayerFactionCheck(pawn))
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
            return Utility_WorkTypeManager.GetWorkTypePriority(workTag);
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

        #region Job Reservation System

        // Static dictionary to track which targets are reserved by which pawns
        private static readonly Dictionary<int, Dictionary<Thing, Pawn>> _jobReservations = new Dictionary<int, Dictionary<Thing, Pawn>>();
        private static readonly Dictionary<int, int> _lastCleanupTicks = new Dictionary<int, int>();
        private const int RESERVATION_CLEANUP_INTERVAL = 300; // 5 seconds

        /// <summary>
        /// Reserves a target for a specific pawn
        /// </summary>
        protected static bool ReserveJobTarget(Thing target, Pawn pawn)
        {
            if (target == null || pawn == null || pawn.Map == null)
                return false;

            int mapId = pawn.Map.uniqueID;

            // Initialize map dictionary if needed
            if (!_jobReservations.TryGetValue(mapId, out var mapReservations))
            {
                mapReservations = new Dictionary<Thing, Pawn>();
                _jobReservations[mapId] = mapReservations;
                _lastCleanupTicks[mapId] = Find.TickManager.TicksGame;
            }

            // Clean up old reservations if needed
            CleanupStaleReservations(mapId);

            // Check if already reserved by another pawn of the same race and faction
            if (mapReservations.TryGetValue(target, out var existingPawn) &&
                existingPawn != pawn &&
                existingPawn.Spawned &&
                !existingPawn.Dead &&
                existingPawn.def == pawn.def &&
                existingPawn.Faction == pawn.Faction &&
                existingPawn.CurJob != null)
            {
                return false;
            }

            // Reserve it for this pawn
            mapReservations[target] = pawn;

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Reserved job target {target} for {pawn.LabelShort} (def: {pawn.def.defName}, faction: {pawn.Faction?.Name ?? "null"})");
            }

            return true;
        }

        /// <summary>
        /// Checks if a target is already reserved by any pawn - reservation is now tracked per individual pawn
        /// rather than by race to prevent entire species from receiving the same job targets
        /// </summary>
        protected static bool IsTargetReserved(Thing target, Pawn pawn)
        {
            if (target == null || pawn?.Map == null)
                return false;

            int mapId = pawn.Map.uniqueID;

            // First check RimWorld's native reservation system ONLY
            if (pawn.Map.reservationManager.IsReserved(target) &&
                !pawn.Map.reservationManager.ReservedBy(target, pawn))
                return true;

            // Check our custom system with minimal overhead
            return _jobReservations.TryGetValue(mapId, out var mapReservations) &&
                   mapReservations.TryGetValue(target, out var existingPawn) &&
                   existingPawn != pawn &&
                   existingPawn.Spawned &&
                   !existingPawn.Dead &&
                   existingPawn.def == pawn.def;
        }

        /// <summary>
        /// Clean up stale reservations
        /// </summary>
        private static void CleanupStaleReservations(int mapId)
        {
            int currentTick = Find.TickManager.TicksGame;

            // Only clean up periodically
            if (!_lastCleanupTicks.TryGetValue(mapId, out int lastCleanup) ||
                currentTick - lastCleanup < RESERVATION_CLEANUP_INTERVAL)
                return;

            _lastCleanupTicks[mapId] = currentTick;

            if (!_jobReservations.TryGetValue(mapId, out var mapReservations))
                return;

            // Find reservations to remove
            var toRemove = new List<Thing>();
            foreach (var kvp in mapReservations)
            {
                Thing target = kvp.Key;
                Pawn pawn = kvp.Value;

                // Remove if target no longer exists
                if (target == null || target.Destroyed || !target.Spawned)
                {
                    toRemove.Add(target);
                    continue;
                }

                // Remove if pawn no longer exists or is no longer working on this
                if (pawn == null || pawn.Destroyed || !pawn.Spawned || pawn.Dead ||
                    pawn.CurJob == null ||
                    (pawn.CurJob.targetA.Thing != target &&
                     pawn.CurJob.targetB.Thing != target &&
                     pawn.CurJob.targetC.Thing != target))
                {
                    toRemove.Add(target);
                    continue;
                }
            }

            // Remove stale reservations
            foreach (var target in toRemove)
            {
                mapReservations.Remove(target);
            }

            if (Prefs.DevMode && toRemove.Count > 0)
            {
                Utility_DebugManager.LogNormal($"Cleaned up {toRemove.Count} stale job reservations on map {mapId}");
            }
        }

        /// <summary>
        /// Clear all job reservations for a map
        /// </summary>
        public static void ClearJobReservations(int mapId)
        {
            if (_jobReservations.ContainsKey(mapId))
            {
                _jobReservations.Remove(mapId);
            }

            if (_lastCleanupTicks.ContainsKey(mapId))
            {
                _lastCleanupTicks.Remove(mapId);
            }

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Cleared all job reservations for map {mapId}");
            }
        }

        /// <summary>
        /// Release a job reservation for a specific target and pawn
        /// </summary>
        public static void ReleaseJobReservation(Thing target, Pawn pawn)
        {
            if (target == null || pawn?.Map == null)
                return;

            int mapId = pawn.Map.uniqueID;

            if (_jobReservations.TryGetValue(mapId, out var mapReservations))
            {
                if (mapReservations.TryGetValue(target, out var existingPawn) && existingPawn == pawn)
                {
                    mapReservations.Remove(target);

                    if (Prefs.DevMode)
                    {
                        Utility_DebugManager.LogNormal($"Released job reservation for {target} by {pawn.LabelShort}");
                    }
                }
            }
        }

        #endregion
    }
}