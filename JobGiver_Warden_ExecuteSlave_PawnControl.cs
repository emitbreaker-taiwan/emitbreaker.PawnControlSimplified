using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks for wardens to execute slaves.
    /// Requires the Ideology DLC.
    /// </summary>
    public class JobGiver_Warden_ExecuteSlave_PawnControl : JobGiver_Warden_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "ExecuteSlave";

        /// <summary>
        /// Cache update interval in ticks (180 ticks = 3 seconds)
        /// </summary>
        protected override int CacheUpdateInterval => 180;

        /// <summary>
        /// Distance thresholds for bucketing (10, 15, 25 tiles)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 100f, 225f, 625f };

        /// <summary>
        /// Cache key identifier for this specific job giver
        /// </summary>
        private const string CACHE_KEY_SUFFIX = "_ExecuteSlave";

        /// <summary>
        /// Static translation cache for performance
        /// </summary>
        private static string IncapableOfViolenceLowerTrans;

        #endregion

        #region Initialization

        /// <summary>
        /// Reset static data when language changes
        /// </summary>
        public static void ResetStaticData()
        {
            IncapableOfViolenceLowerTrans = "IncapableOfViolenceLower".Translate();
        }

        /// <summary>
        /// Reset the cache for execute slave job giver
        /// </summary>
        public static void ResetExecuteSlaveCache()
        {
            // Clear all execute slave related caches from all maps
            foreach (Map map in Find.Maps)
            {
                int mapId = map.uniqueID;
                string cacheKey = typeof(JobGiver_Warden_ExecuteSlave_PawnControl).Name + PRISONERS_CACHE_SUFFIX + CACHE_KEY_SUFFIX;
                string reachCacheKey = typeof(JobGiver_Warden_ExecuteSlave_PawnControl).Name + "_ReachCache" + CACHE_KEY_SUFFIX;

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
                Utility_DebugManager.LogNormal("Reset all execute slave caches");
            }
        }

        #endregion

        #region Core flow

        /// <summary>
        /// Executing slaves has high priority
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            // Executing slaves has high priority
            return 7.0f;
        }

        /// <summary>
        /// Try to give a job, using the centralized cache system
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            // Check for Ideology DLC first
            if (!ModLister.CheckIdeology("SlaveExecution"))
            {
                return null;
            }

            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Warden_ExecuteSlave_PawnControl>(
                pawn,
                WorkTag,
                CreateJobFromCachedTargets,
                debugJobDesc: "execute slave");
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
                // If cache miss, update the cache
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
            // Use base checks first
            if (base.ShouldSkip(pawn))
                return true;

            // Ideology required
            if (!ModLister.CheckIdeology("SlaveExecution"))
                return true;

            // Cannot execute if incapable of violence
            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                JobFailReason.Is(IncapableOfViolenceLowerTrans);
                return true;
            }

            return false;
        }

        #endregion

        #region Target processing

        /// <summary>
        /// Get all slaves eligible for execution
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null) yield break;

            // Get all slave pawns on the map
            foreach (Pawn slave in map.mapPawns.SlavesOfColonySpawned)
            {
                if (FilterExecutableSlaves(slave))
                {
                    yield return slave;
                }
            }
        }

        /// <summary>
        /// Get slaves matching specific criteria for this job giver
        /// </summary>
        protected override IEnumerable<Pawn> GetPrisonersMatchingCriteria(Map map)
        {
            if (map == null) yield break;

            // Get all executable slaves on the map
            foreach (Pawn slave in map.mapPawns.SlavesOfColonySpawned)
            {
                if (FilterExecutableSlaves(slave))
                {
                    yield return slave;
                }
            }
        }

        /// <summary>
        /// Filter function to identify slaves ready for execution
        /// </summary>
        private bool FilterExecutableSlaves(Pawn slave)
        {
            // Check if the slave exists and has guest component
            if (slave?.guest == null)
                return false;

            // Skip slaves in mental states
            if (slave.InMentalState)
                return false;

            // Only include slaves set for execution
            if (slave.guest.slaveInteractionMode != SlaveInteractionModeDefOf.Execute)
                return false;

            return true;
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

            // Find the first valid slave to execute
            Pawn targetSlave = Utility_JobGiverManager.FindFirstValidTargetInBuckets<Pawn>(
                buckets,
                warden,
                (slave, p) => IsValidSlaveTarget(slave, p),
                new Dictionary<int, Dictionary<Pawn, bool>> { { mapId, reachabilityCache } }
            );

            if (targetSlave == null)
                return null;

            // Create job for the slave
            return CreateJobForPrisoner(warden, targetSlave, forced);
        }

        /// <summary>
        /// Validates if a warden can execute a specific slave
        /// </summary>
        protected override bool IsValidPrisonerTarget(Pawn slave, Pawn warden)
        {
            return IsValidSlaveTarget(slave, warden);
        }

        /// <summary>
        /// Specialized check for slaves to be executed
        /// </summary>
        private bool IsValidSlaveTarget(Pawn slave, Pawn warden)
        {
            if (slave?.guest == null)
                return false;

            // Check if slave is set for execution
            if (slave.guest.slaveInteractionMode != SlaveInteractionModeDefOf.Execute)
                return false;

            // Check if warden is capable of violence
            if (warden.WorkTagIsDisabled(WorkTags.Violent))
                return false;

            // Check basic reachability
            if (!slave.Spawned || slave.IsForbidden(warden) ||
                !warden.CanReserve(slave, 1, -1, null, false))
                return false;

            // Check ideology compatibility
            if (!IsExecutionIdeoAllowed(warden, slave))
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
            if (map == null || map.mapPawns.SlavesOfColonySpawned == null || map.mapPawns.SlavesOfColonySpawned.Count == 0)
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
            return CreateExecutionJob(warden, slave);
        }

        /// <summary>
        /// Creates the execution job for the warden
        /// </summary>
        private Job CreateExecutionJob(Pawn warden, Pawn slave)
        {
            Job job = JobMaker.MakeJob(JobDefOf.SlaveExecution, slave);
            job.count = 1;

            Utility_DebugManager.LogNormal($"{warden.LabelShort} created job to execute slave {slave.LabelShort}");

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
                Utility_DebugManager.LogNormal($"Reset execute slave cache for {this.GetType().Name}");
            }
        }

        #endregion

        #region Utility

        /// <summary>
        /// Check if the execution is allowed by the warden's ideology
        /// </summary>
        private bool IsExecutionIdeoAllowed(Pawn warden, Pawn slave)
        {
            // This method is imported from the original WorkGiver
            // Additional implementation is needed based on the game's requirements
            return true;
        }

        /// <summary>
        /// For debugging purposes
        /// </summary>
        public override string ToString()
        {
            return "JobGiver_Warden_ExecuteSlave_PawnControl";
        }

        #endregion
    }
}