using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks for wardens to imprison slaves
    /// </summary>
    public class JobGiver_Warden_ImprisonSlave_PawnControl : JobGiver_Warden_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "ImprisonSlave";

        /// <summary>
        /// Cache update interval in ticks (180 ticks = 3 seconds)
        /// </summary>
        protected override int CacheUpdateInterval => base.CacheUpdateInterval;

        /// <summary>
        /// Distance thresholds for bucketing (10, 15, 25 tiles)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 100f, 225f, 625f };

        /// <summary>
        /// Cache key identifier for this specific job giver
        /// </summary>
        private const string CACHE_KEY_SUFFIX = "_ImprisonSlave";

        #endregion

        #region Initialization

        /// <summary>
        /// Reset the cache for imprison slave job giver
        /// </summary>
        public static void ResetImprisonSlaveCache()
        {
            // Clear all imprison-slave related caches from all maps
            foreach (Map map in Find.Maps)
            {
                int mapId = map.uniqueID;
                string cacheKey = typeof(JobGiver_Warden_ImprisonSlave_PawnControl).Name + PRISONERS_CACHE_SUFFIX + CACHE_KEY_SUFFIX;
                string reachCacheKey = typeof(JobGiver_Warden_ImprisonSlave_PawnControl).Name + "_ReachCache" + CACHE_KEY_SUFFIX;

                var slaveCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Pawn>>(mapId);
                if (slaveCache.ContainsKey(cacheKey))
                {
                    slaveCache.Remove(cacheKey);
                    Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, -1);
                }

                // Clear reachability cache
                var reachabilityCache = Utility_MapCacheManager.GetOrCreateMapCache<Pawn, bool>(mapId);
                reachabilityCache.Clear();
                Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, reachCacheKey, -1);
            }

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal("Reset all imprison slave caches");
            }
        }

        #endregion

        #region Core flow

        /// <summary>
        /// Imprisoning slaves has high priority but lower than emergency tasks
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            // Imprisoning slaves has high priority but lower than emergency tasks
            return 6.0f;
        }

        /// <summary>
        /// Try to give a job, using the centralized cache system
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            // Check if ideology is active
            if (!ModLister.CheckIdeology("Slave imprisonment"))
            {
                return null;
            }

            if (ShouldSkip(pawn))
            {
                return null;
            }

            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Warden_ImprisonSlave_PawnControl>(
                pawn,
                WorkTag,
                CreateJobFromCachedTargets,
                debugJobDesc: "imprison slave");
        }

        /// <summary>
        /// Create a job using the cached targets
        /// </summary>
        private Job CreateJobFromCachedTargets(Pawn pawn, bool forced)
        {
            // Process cached targets to create job
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;

            // Get slaves from cache using the proper cache key
            string cacheKey = this.GetType().Name + PRISONERS_CACHE_SUFFIX + CACHE_KEY_SUFFIX;
            var slaveCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Pawn>>(mapId);
            List<Pawn> slaveList = slaveCache.TryGetValue(cacheKey, out var cachedList) ? cachedList : null;

            List<Thing> targets;
            if (slaveList != null)
            {
                targets = new List<Thing>(slaveList.Cast<Thing>());
            }
            else
            {
                // If cache miss, update the cache with eligible slaves
                var freshSlaves = GetPrisonersMatchingCriteria(pawn.Map).ToList();
                slaveCache[cacheKey] = freshSlaves;
                Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, Find.TickManager.TicksGame);
                targets = new List<Thing>(freshSlaves.Cast<Thing>());
            }

            return ProcessCachedTargets(pawn, targets, forced);
        }

        /// <summary>
        /// Determines if this job giver should be skipped for the given pawn
        /// </summary>
        public override bool ShouldSkip(Pawn pawn)
        {
            // Check if ideology is active
            if (!ModLister.CheckIdeology("Slave imprisonment"))
                return true;

            // Use base checks next
            return base.ShouldSkip(pawn);
        }

        #endregion

        #region Target processing

        /// <summary>
        /// Get all slaves eligible for imprisonment
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null) yield break;

            // Find all slaves on the map
            foreach (Pawn slave in map.mapPawns.SlavesOfColonySpawned)
            {
                if (slave != null && !slave.Destroyed && slave.Spawned)
                {
                    if (slave.guest.slaveInteractionMode == SlaveInteractionModeDefOf.Imprison)
                    {
                        yield return slave;
                    }
                }
            }
        }

        /// <summary>
        /// Get slaves matching specific criteria for this job giver
        /// </summary>
        protected override IEnumerable<Pawn> GetPrisonersMatchingCriteria(Map map)
        {
            if (map == null) yield break;

            // Find all slaves on the map eligible for imprisonment
            foreach (Pawn slave in map.mapPawns.SlavesOfColonySpawned)
            {
                if (slave != null && !slave.Destroyed && slave.Spawned)
                {
                    if (slave.guest.slaveInteractionMode == SlaveInteractionModeDefOf.Imprison)
                    {
                        yield return slave;
                    }
                }
            }
        }

        /// <summary>
        /// Process the cached targets to create jobs
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn warden, List<Thing> targets, bool forced)
        {
            if (warden?.Map == null || targets.Count == 0)
                return null;

            int mapId = warden.Map.uniqueID;

            // Create distance buckets for optimized searching
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets<Pawn>(
                warden,
                targets.ConvertAll(t => t as Pawn),
                (slave) => (slave.Position - warden.Position).LengthHorizontalSquared,
                DistanceThresholds
            );

            // Get reachability cache for this job giver
            string reachCacheKey = this.GetType().Name + "_ReachCache" + CACHE_KEY_SUFFIX;
            var reachabilityCache = Utility_MapCacheManager.GetOrCreateMapCache<Pawn, bool>(mapId);

            // Find the first valid slave to imprison
            Pawn targetSlave = Utility_JobGiverManager.FindFirstValidTargetInBuckets<Pawn>(
                buckets,
                warden,
                (slave, p) => IsValidPrisonerTarget(slave, p),
                new Dictionary<int, Dictionary<Pawn, bool>> { { mapId, reachabilityCache } }
            );

            if (targetSlave == null)
                return null;

            // Create job for the slave
            return CreateJobForPrisoner(warden, targetSlave, forced);
        }

        /// <summary>
        /// Check if this slave is a valid target for imprisonment
        /// </summary>
        protected override bool IsValidPrisonerTarget(Pawn slave, Pawn warden)
        {
            // Check if the slave exists and is valid
            if (slave == null || !slave.Spawned || slave.Downed || slave.IsPrisoner)
                return false;

            // Check if the slave is actually a slave and set to be imprisoned
            if (!slave.IsSlave || slave.guest.slaveInteractionMode != SlaveInteractionModeDefOf.Imprison)
                return false;

            // Check if warden can access the slave
            if (!warden.CanReach(slave, PathEndMode.Touch, Danger.Deadly))
                return false;

            // Check if the slave can be reserved
            if (!warden.CanReserve(slave, 1, -1, null, false))
                return false;

            // Check if a bed is available for imprisonment
            Building_Bed bed = RestUtility.FindBedFor(slave, warden, checkSocialProperness: false, ignoreOtherReservations: false, GuestStatus.Prisoner);
            if (bed == null)
                return false;

            return true;
        }

        /// <summary>
        /// Determines if the job giver should execute based on cache status
        /// </summary>
        protected override bool ShouldExecuteNow(int mapId)
        {
            // Check if there are any slaves on the map
            Map map = Find.Maps.Find(m => m.uniqueID == mapId);
            if (map == null || !map.mapPawns.SlavesOfColonySpawned.Any(s =>
                s.guest.slaveInteractionMode == SlaveInteractionModeDefOf.Imprison))
                return false;

            // Check cache update interval
            string cacheKey = this.GetType().Name + PRISONERS_CACHE_SUFFIX + CACHE_KEY_SUFFIX;
            int currentTick = Find.TickManager.TicksGame;
            int lastUpdateTick = Utility_MapCacheManager.GetLastCacheUpdateTick(mapId, cacheKey);

            return currentTick - lastUpdateTick >= CacheUpdateInterval;
        }

        #endregion

        #region Job creation

        /// <summary>
        /// Creates a job for the given slave
        /// </summary>
        protected override Job CreateJobForPrisoner(Pawn warden, Pawn slave, bool forced)
        {
            return CreateImprisonmentJob(warden, slave);
        }

        /// <summary>
        /// Creates the imprisonment job for the warden
        /// </summary>
        private Job CreateImprisonmentJob(Pawn warden, Pawn slave)
        {
            Building_Bed bed = RestUtility.FindBedFor(slave, warden, checkSocialProperness: false,
                ignoreOtherReservations: false, GuestStatus.Prisoner);

            if (bed == null)
                return null;

            Job job = JobMaker.MakeJob(JobDefOf.Arrest, slave, bed);
            job.count = 1;

            Utility_DebugManager.LogNormal($"{warden.LabelShort} created job to imprison slave {slave.LabelShort}");

            return job;
        }

        #endregion

        #region Cache management

        /// <summary>
        /// Reset the cache for this job giver
        /// </summary>
        public override void Reset()
        {
            base.Reset();

            // Clear specific caches for this job giver
            foreach (Map map in Find.Maps)
            {
                int mapId = map.uniqueID;
                string slaveCacheKey = this.GetType().Name + PRISONERS_CACHE_SUFFIX + CACHE_KEY_SUFFIX;
                string reachCacheKey = this.GetType().Name + "_ReachCache" + CACHE_KEY_SUFFIX;

                // Clear slave cache
                var slaveCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Pawn>>(mapId);
                if (slaveCache.ContainsKey(slaveCacheKey))
                {
                    slaveCache.Remove(slaveCacheKey);
                    Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, slaveCacheKey, -1);
                }

                // Clear reachability cache
                var reachabilityCache = Utility_MapCacheManager.GetOrCreateMapCache<Pawn, bool>(mapId);
                reachabilityCache.Clear();
                Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, reachCacheKey, -1);
            }

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Reset imprison slave cache for {this.GetType().Name}");
            }
        }

        #endregion

        #region Debug support

        /// <summary>
        /// For debugging purposes
        /// </summary>
        public override string ToString()
        {
            return "JobGiver_Warden_ImprisonSlave_PawnControl";
        }

        #endregion
    }
}