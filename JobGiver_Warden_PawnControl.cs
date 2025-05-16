using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Base class for warden job givers with specialized cache management
    /// </summary>
    public abstract class JobGiver_Warden_PawnControl : JobGiver_Scan_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Warden jobs typically don't require zone designations
        /// </summary>
        public override bool RequiresMapZoneorArea => false;

        /// <summary>
        /// Required tag for non-humanlike pawns
        /// </summary>
        public override PawnEnumTags RequiredTag => PawnEnumTags.AllowWork_Warden;

        /// <summary>
        /// The work tag for warden job givers
        /// </summary>
        public override string WorkTag => "Warden";

        /// <summary>
        /// Distance thresholds for bucketing (10, 15, 25 tiles squared)
        /// </summary>
        protected virtual float[] DistanceThresholds => new float[] { 100f, 225f, 625f };

        /// <summary>
        /// Cache update interval (in ticks)
        /// </summary>
        protected override int CacheUpdateInterval => base.CacheUpdateInterval;

        /// <summary>
        /// Cache key suffix for prisoners
        /// </summary>
        protected const string PRISONERS_CACHE_SUFFIX = "_Prisoners";

        #endregion

        #region Cache System

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_Warden_PawnControl() : base()
        {
            // Base constructor already initializes the cache system with this job giver's type
        }

        /// <summary>
        /// Reset the cache - implements IResettableCache
        /// </summary>
        public override void Reset()
        {
            // Call base reset first
            base.Reset();

            // Clear any prisoner-specific caches for all maps
            foreach (int mapId in Find.Maps.Select(map => map.uniqueID))
            {
                string cacheKey = this.GetType().Name + PRISONERS_CACHE_SUFFIX;
                var prisonerCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Pawn>>(mapId);

                if (prisonerCache.ContainsKey(cacheKey))
                {
                    prisonerCache.Remove(cacheKey);
                }

                // Clear the update tick record too
                Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, -1);
            }

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Reset prisoner cache for {this.GetType().Name}");
            }
        }

        /// <summary>
        /// Static method to reset all warden caches
        /// </summary>
        public static void ResetWardenCache()
        {
            // Clear all warden-related caches from all maps
            foreach (Map map in Find.Maps)
            {
                int mapId = map.uniqueID;

                // Clear all handling-related caches
                var cacheContainer = Utility_MapCacheManager.GetOrCreateMapCache<string, object>(mapId);

                // Find all keys starting with JobGiver_Warden
                var keysToRemove = cacheContainer.Keys
                    .Where(k => k.Contains("JobGiver_Warden") && k.EndsWith(PRISONERS_CACHE_SUFFIX))
                    .ToList();

                foreach (string key in keysToRemove)
                {
                    cacheContainer.Remove(key);
                    Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, key, -1);
                }
            }

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal("Reset all warden caches");
            }
        }

        #endregion

        #region Core flow

        /// <summary>
        /// Job-specific cache update method
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            if (map == null)
                yield break;

            // Get prisoners that match this job giver's criteria
            List<Pawn> prisoners = GetPrisonersMatchingCriteria(map).ToList();

            // Store in centralized cache
            StorePrisonerCache(map, prisoners);

            // Convert to Things for the base class caching system
            foreach (Pawn prisoner in prisoners)
            {
                yield return prisoner;
            }
        }

        /// <summary>
        /// Gets targets for this warden job giver
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null)
                yield break;

            // Get cached prisoners from centralized cache
            var prisoners = GetOrCreatePrisonerCache(map);

            // Return prisoners as targets
            foreach (Pawn prisoner in prisoners)
            {
                if (prisoner != null && !prisoner.Dead && prisoner.Spawned)
                    yield return prisoner;
            }
        }

        /// <summary>
        /// Gets or creates a cache of prisoners for a specific map
        /// </summary>
        protected List<Pawn> GetOrCreatePrisonerCache(Map map)
        {
            if (map == null)
                return new List<Pawn>();

            int mapId = map.uniqueID;
            string cacheKey = this.GetType().Name + PRISONERS_CACHE_SUFFIX;

            // Try to get cached prisoners from the map cache manager
            var prisonerCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Pawn>>(mapId);

            // Check if we need to update the cache
            int currentTick = Find.TickManager.TicksGame;
            int lastUpdateTick = Utility_MapCacheManager.GetLastCacheUpdateTick(mapId, cacheKey);

            if (currentTick - lastUpdateTick > CacheUpdateInterval ||
                !prisonerCache.TryGetValue(cacheKey, out List<Pawn> prisoners) ||
                prisoners == null ||
                prisoners.Any(p => p == null || p.Dead || !p.Spawned))
            {
                // Cache is invalid or expired, rebuild it
                prisoners = GetPrisonersMatchingCriteria(map).ToList();

                // Store in the central cache
                StorePrisonerCache(map, prisoners);
            }

            return prisoners;
        }

        /// <summary>
        /// Store a list of prisoners in the centralized cache
        /// </summary>
        private void StorePrisonerCache(Map map, List<Pawn> prisoners)
        {
            if (map == null)
                return;

            int mapId = map.uniqueID;
            string cacheKey = this.GetType().Name + PRISONERS_CACHE_SUFFIX;
            int currentTick = Find.TickManager.TicksGame;

            var prisonerCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Pawn>>(mapId);
            prisonerCache[cacheKey] = prisoners;
            Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, currentTick);

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Updated prisoner cache for {this.GetType().Name}, found {prisoners.Count} prisoners");
            }
        }

        /// <summary>
        /// Process cached prisoner targets to find the best one
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            if (pawn == null || targets == null || targets.Count == 0)
                return null;

            // Use distance bucketing for more efficient selection
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                targets,
                (target) => (target.Position - pawn.Position).LengthHorizontalSquared,
                DistanceThresholds);

            // Get reachability cache from central cache system
            var reachabilityCache = Utility_MapCacheManager.GetOrCreateMapCache<int, Dictionary<Thing, bool>>(pawn.Map.uniqueID);

            // Create cache entry if needed
            int pawnId = pawn.thingIDNumber;
            if (!reachabilityCache.ContainsKey(pawnId))
                reachabilityCache[pawnId] = new Dictionary<Thing, bool>();

            // Find the best prisoner to handle
            Thing bestTarget = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (target, p) => IsValidPrisonerTarget(target as Pawn, p),
                reachabilityCache);

            // Create job if we found a valid target
            if (bestTarget != null && bestTarget is Pawn prisoner)
            {
                return CreateJobForPrisoner(pawn, prisoner, forced);
            }

            return null;
        }

        #endregion

        #region Prisoner Selection

        /// <summary>
        /// Get prisoners matching specific criteria for this job giver
        /// </summary>
        protected virtual IEnumerable<Pawn> GetPrisonersMatchingCriteria(Map map)
        {
            if (map == null)
                yield break;

            // Default implementation returns all prisoners
            foreach (Pawn prisoner in map.mapPawns.PrisonersOfColonySpawned)
            {
                if (prisoner != null && !prisoner.Dead && prisoner.Spawned)
                    yield return prisoner;
            }
        }

        /// <summary>
        /// Validate if a prisoner target is valid for this specific job giver
        /// </summary>
        protected virtual bool IsValidPrisonerTarget(Pawn prisoner, Pawn warden)
        {
            if (prisoner == null || warden == null || !prisoner.IsPrisonerOfColony || prisoner.Dead || !prisoner.Spawned)
                return false;

            if (prisoner.IsForbidden(warden) || prisoner.Position.IsForbidden(warden))
                return false;

            // Check if the warden can reach the prisoner
            if (!warden.CanReach((LocalTargetInfo)prisoner, PathEndMode.Touch, Danger.Deadly))
                return false;

            return true;
        }

        /// <summary>
        /// Create a job for the given prisoner - to be implemented by derived classes
        /// </summary>
        protected virtual Job CreateJobForPrisoner(Pawn warden, Pawn prisoner, bool forced)
        {
            // Base implementation - should be overridden by derived classes
            return null;
        }

        #endregion

        #region Debug

        /// <summary>
        /// Debug info for logging
        /// </summary>
        public override string ToString()
        {
            return $"JobGiver_Warden_{this.GetType().Name}";
        }

        #endregion
    }
}