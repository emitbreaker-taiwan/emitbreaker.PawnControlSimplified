using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Abstract base class for all building removal JobGivers in PawnControl.
    /// This allows non-humanlike pawns to remove buildings with appropriate designation.
    /// </summary>
    public abstract class JobGiver_RemoveBuilding_PawnControl : ThinkNode_JobGiver
    {
        // Store cached buildings to remove per map
        protected static readonly Dictionary<int, List<Thing>> _targetCache = new Dictionary<int, List<Thing>>();
        protected static readonly Dictionary<int, Dictionary<Thing, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Thing, bool>>();
        protected static int _lastCacheUpdateTick = -999;
        protected const int CACHE_UPDATE_INTERVAL = 180; // 3 seconds
        protected const int MAX_CACHE_SIZE = 100;

        // Define distance thresholds for bucketing
        protected static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        // Must be implemented by subclasses to specify which designation to target
        protected abstract DesignationDef Designation { get; }

        // Must be implemented by subclasses to specify which job to use for removal
        protected abstract JobDef RemoveBuildingJob { get; }

        public override float GetPriority(Pawn pawn)
        {
            // Construction is a medium priority task
            return 5.9f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Standardized approach to job giving using your utility class
            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_RemoveBuilding_PawnControl>(
                pawn,
                "Construction",
                (p, forced) => {
                    // Update cache first, matching the pattern from JobGiver_GrowerSow_PawnControl
                    UpdateTargetCache(p.Map);

                    return TryCreateRemovalJob(p, forced);
                },
                debugJobDesc: $"{Designation.defName} assignment",
                skipEmergencyCheck: true);
        }

        /// <summary>
        /// Updates the cache of things that need to be removed
        /// </summary>
        protected void UpdateTargetCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_targetCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_targetCache.ContainsKey(mapId))
                    _targetCache[mapId].Clear();
                else
                    _targetCache[mapId] = new List<Thing>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Thing, bool>();

                // Find all designated things for removal
                var designations = map.designationManager.SpawnedDesignationsOfDef(Designation);
                foreach (Designation designation in designations)
                {
                    Thing thing = designation.target.Thing;
                    if (thing != null && thing.Spawned)
                    {
                        _targetCache[mapId].Add(thing);

                        // Limit cache size for performance
                        if (_targetCache[mapId].Count >= MAX_CACHE_SIZE)
                            break;
                    }
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Gets the cached list of things to remove from the given map
        /// </summary>
        private List<Thing> GetTargets(Map map)
        {
            if (map == null)
                return new List<Thing>();

            int mapId = map.uniqueID;

            if (_targetCache.TryGetValue(mapId, out var cachedTargets))
                return cachedTargets;

            return new List<Thing>();
        }

        /// <summary>
        /// Create a job for removing a building using manager-driven bucket processing
        /// </summary>
        private Job TryCreateRemovalJob(Pawn pawn, bool forced)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_targetCache.ContainsKey(mapId) || _targetCache[mapId].Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing and target selection
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                _targetCache[mapId],
                (thing) => (thing.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Find the best target to remove
            Thing bestTarget = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (thing, p) => {
                    // IMPORTANT: Check faction interaction validity first
                    if (!Utility_JobGiverManager.IsValidFactionInteraction(thing, p, requiresDesignator: true))
                        return false;

                    // Skip if no longer valid
                    if (thing == null || thing.Destroyed || !thing.Spawned)
                        return false;

                    // Skip if no longer designated
                    if (thing.Map.designationManager.DesignationOn(thing, Designation) == null)
                        return false;

                    // Check for timed explosives - avoid removing things about to explode
                    CompExplosive explosive = thing.TryGetComp<CompExplosive>();
                    if (explosive != null && explosive.wickStarted)
                        return false;

                    // Skip if forbidden or unreachable
                    if (thing.IsForbidden(p) ||
                        !p.CanReserve(thing, 1, -1, null, forced) ||
                        !p.CanReach(thing, PathEndMode.Touch, Danger.Some))
                        return false;

                    return true;
                },
                _reachabilityCache
            );

            // Create job if target found
            if (bestTarget != null)
            {
                Job job = JobMaker.MakeJob(RemoveBuildingJob, bestTarget);
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to {RemoveBuildingJob.defName} {bestTarget.LabelCap}");
                return job;
            }

            return null;
        }

        /// <summary>
        /// Resets all caches maintained by this JobGiver class
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_targetCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return $"JobGiver_RemoveBuilding_PawnControl({Designation?.defName ?? "null"})";
        }
    }
}