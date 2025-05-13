using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks for wardens to interrogate prisoners about their identity
    /// </summary>
    public class JobGiver_Warden_InterrogateIdentity_PawnControl : JobGiver_Warden_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "InterrogateIdentity";

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
        private const string CACHE_KEY_SUFFIX = "_InterrogateIdentity";

        #endregion

        #region Initialization

        /// <summary>
        /// Reset the cache for interrogate identity job giver
        /// </summary>
        public static void ResetInterrogateIdentityCache()
        {
            // Clear all interrogate-identity related caches from all maps
            foreach (Map map in Find.Maps)
            {
                int mapId = map.uniqueID;
                string cacheKey = typeof(JobGiver_Warden_InterrogateIdentity_PawnControl).Name + PRISONERS_CACHE_SUFFIX + CACHE_KEY_SUFFIX;
                string reachCacheKey = typeof(JobGiver_Warden_InterrogateIdentity_PawnControl).Name + "_ReachCache" + CACHE_KEY_SUFFIX;

                var prisonerCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Pawn>>(mapId);
                if (prisonerCache.ContainsKey(cacheKey))
                {
                    prisonerCache.Remove(cacheKey);
                    Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, -1);
                }

                // Clear reachability cache
                var reachabilityCache = Utility_MapCacheManager.GetOrCreateMapCache<Pawn, bool>(mapId);
                reachabilityCache.Clear();
                Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, reachCacheKey, -1);
            }

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal("Reset all interrogate identity caches");
            }
        }

        #endregion

        #region Core flow

        /// <summary>
        /// Interrogation has medium priority
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            // Interrogation has medium priority
            return 5.0f;
        }

        /// <summary>
        /// Try to give a job, using the centralized cache system
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            // Check if anomaly mod is active
            if (!ModsConfig.AnomalyActive)
                return null;

            // Check if pawn is capable of talking
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Talking))
                return null;

            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Warden_InterrogateIdentity_PawnControl>(
                pawn,
                WorkTag,
                CreateJobFromCachedTargets,
                debugJobDesc: "interrogate identity");
        }

        /// <summary>
        /// Create a job using the cached targets
        /// </summary>
        private Job CreateJobFromCachedTargets(Pawn pawn, bool forced)
        {
            // Process cached targets to create job
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;

            // Get prisoners from cache using the proper cache key
            string cacheKey = this.GetType().Name + PRISONERS_CACHE_SUFFIX + CACHE_KEY_SUFFIX;
            var prisonerCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Pawn>>(mapId);
            List<Pawn> prisonerList = prisonerCache.TryGetValue(cacheKey, out var cachedList) ? cachedList : null;

            List<Thing> targets;
            if (prisonerList != null)
            {
                targets = new List<Thing>(prisonerList.Cast<Thing>());
            }
            else
            {
                // If cache miss, update the cache
                var freshPrisoners = GetPrisonersMatchingCriteria(pawn.Map).ToList();
                prisonerCache[cacheKey] = freshPrisoners;
                Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, Find.TickManager.TicksGame);
                targets = new List<Thing>(freshPrisoners.Cast<Thing>());
            }

            return ProcessCachedTargets(pawn, targets, forced);
        }

        /// <summary>
        /// Determines if this job giver should be skipped for the given pawn
        /// </summary>
        public override bool ShouldSkip(Pawn pawn)
        {
            // Check if anomaly mod is active
            if (!ModsConfig.AnomalyActive)
                return true;

            // Check if pawn is capable of talking
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Talking))
            {
                return true;
            }

            // Use base checks next
            return base.ShouldSkip(pawn);
        }

        #endregion

        #region Target processing

        /// <summary>
        /// Get all prisoners eligible for identity interrogation
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null) yield break;

            // Find all prisoners on the map
            foreach (Pawn prisoner in map.mapPawns.PrisonersOfColonySpawned)
            {
                if (prisoner != null && !prisoner.Destroyed && prisoner.Spawned)
                {
                    if (CanBeInterrogated(prisoner))
                    {
                        yield return prisoner;
                    }
                }
            }
        }

        /// <summary>
        /// Get prisoners matching specific criteria for this job giver
        /// </summary>
        protected override IEnumerable<Pawn> GetPrisonersMatchingCriteria(Map map)
        {
            if (map == null) yield break;

            // Find all prisoners eligible for identity interrogation
            foreach (Pawn prisoner in map.mapPawns.PrisonersOfColonySpawned)
            {
                if (prisoner != null && !prisoner.Destroyed && prisoner.Spawned)
                {
                    if (CanBeInterrogated(prisoner))
                    {
                        yield return prisoner;
                    }
                }
            }
        }

        /// <summary>
        /// Check if prisoner can be interrogated about identity
        /// </summary>
        private bool CanBeInterrogated(Pawn prisoner)
        {
            // Check if has valid guest data
            if (prisoner?.guest == null)
                return false;

            // Skip prisoners in mental states
            if (prisoner.InMentalState)
                return false;

            // Check if prisoner is set for interrogation mode
            if (!prisoner.guest.IsInteractionEnabled(PrisonerInteractionModeDefOf.Interrogate))
                return false;

            // Check if prisoner is scheduled for interaction
            if (!prisoner.guest.ScheduledForInteraction)
                return false;

            // Prisoner must be either in a bed or not downed
            if (prisoner.Downed && !prisoner.InBed())
                return false;

            // Prisoner must be awake
            if (!prisoner.Awake())
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
                (prisoner) => (prisoner.Position - warden.Position).LengthHorizontalSquared,
                DistanceThresholds
            );

            // Get reachability cache for this job giver
            string reachCacheKey = this.GetType().Name + "_ReachCache" + CACHE_KEY_SUFFIX;
            var reachabilityCache = Utility_MapCacheManager.GetOrCreateMapCache<Pawn, bool>(mapId);

            // Find the first valid prisoner to interrogate
            Pawn targetPrisoner = Utility_JobGiverManager.FindFirstValidTargetInBuckets<Pawn>(
                buckets,
                warden,
                (prisoner, p) => IsValidPrisonerTarget(prisoner, p),
                new Dictionary<int, Dictionary<Pawn, bool>> { { mapId, reachabilityCache } }
            );

            if (targetPrisoner == null)
                return null;

            // Create job for the prisoner
            return CreateJobForPrisoner(warden, targetPrisoner, forced);
        }

        /// <summary>
        /// Check if this prisoner is a valid target for interrogation
        /// </summary>
        protected override bool IsValidPrisonerTarget(Pawn prisoner, Pawn warden)
        {
            // Check if the prisoner exists
            if (prisoner == null || !prisoner.Spawned)
                return false;

            // Check if prisoner can be interrogated
            if (!CanBeInterrogated(prisoner))
                return false;

            // Check if warden can talk
            if (!warden.health.capacities.CapableOf(PawnCapacityDefOf.Talking))
                return false;

            // Check if warden can reserve the prisoner
            if (!warden.CanReserve(prisoner))
                return false;

            return true;
        }

        /// <summary>
        /// Determines if the job giver should execute based on cache status
        /// </summary>
        protected override bool ShouldExecuteNow(int mapId)
        {
            // Check if there are any prisoners on the map
            Map map = Find.Maps.Find(m => m.uniqueID == mapId);
            if (map == null || !map.mapPawns.PrisonersOfColonySpawned.Any(p =>
                p.guest?.IsInteractionEnabled(PrisonerInteractionModeDefOf.Interrogate) == true &&
                p.guest.ScheduledForInteraction))
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
        /// Creates a job for the given prisoner
        /// </summary>
        protected override Job CreateJobForPrisoner(Pawn warden, Pawn prisoner, bool forced)
        {
            return CreateInterrogationJob(warden, prisoner);
        }

        /// <summary>
        /// Creates the interrogation job for the warden
        /// </summary>
        private Job CreateInterrogationJob(Pawn warden, Pawn prisoner)
        {
            Job job = JobMaker.MakeJob(JobDefOf.PrisonerInterrogateIdentity, prisoner);

            Utility_DebugManager.LogNormal($"{warden.LabelShort} created job to interrogate prisoner {prisoner.LabelShort} about identity");

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
                string prisonerCacheKey = this.GetType().Name + PRISONERS_CACHE_SUFFIX + CACHE_KEY_SUFFIX;
                string reachCacheKey = this.GetType().Name + "_ReachCache" + CACHE_KEY_SUFFIX;

                // Clear prisoner cache
                var prisonerCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Pawn>>(mapId);
                if (prisonerCache.ContainsKey(prisonerCacheKey))
                {
                    prisonerCache.Remove(prisonerCacheKey);
                    Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, prisonerCacheKey, -1);
                }

                // Clear reachability cache
                var reachabilityCache = Utility_MapCacheManager.GetOrCreateMapCache<Pawn, bool>(mapId);
                reachabilityCache.Clear();
                Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, reachCacheKey, -1);
            }

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Reset interrogate identity cache for {this.GetType().Name}");
            }
        }

        #endregion

        #region Debug support

        /// <summary>
        /// For debugging purposes
        /// </summary>
        public override string ToString()
        {
            return "JobGiver_Warden_InterrogateIdentity_PawnControl";
        }

        #endregion
    }
}