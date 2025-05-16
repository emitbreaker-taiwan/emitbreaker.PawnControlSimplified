using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns prisoner release jobs to eligible wardens.
    /// Optimized to use the PawnControl framework for better performance.
    /// </summary>
    public class JobGiver_Warden_ReleasePrisoner_PawnControl : JobGiver_Warden_PawnControl
    {
        #region Configuration

        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.ReleasePrisoner;

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "ReleasePrisoner";

        /// <summary>
        /// Cache update interval in ticks (180 ticks = 3 seconds)
        /// </summary>
        protected override int CacheUpdateInterval => base.CacheUpdateInterval;

        /// <summary>
        /// Distance thresholds for bucketing (10, 20, 30 tiles)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 100f, 400f, 900f };

        /// <summary>
        /// Cache key identifier for this specific job giver
        /// </summary>
        private const string CACHE_KEY_SUFFIX = "_ReleasePrisoner";

        #endregion

        #region Initialization

        /// <summary>
        /// Reset the cache for release prisoner job giver
        /// </summary>
        public static void ResetReleasePrisonerCache()
        {
            // Clear all release-prisoner related caches from all maps
            foreach (Map map in Find.Maps)
            {
                int mapId = map.uniqueID;
                string cacheKey = typeof(JobGiver_Warden_ReleasePrisoner_PawnControl).Name + PRISONERS_CACHE_SUFFIX + CACHE_KEY_SUFFIX;
                string reachCacheKey = typeof(JobGiver_Warden_ReleasePrisoner_PawnControl).Name + "_ReachCache" + CACHE_KEY_SUFFIX;

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
                Utility_DebugManager.LogNormal("Reset all release prisoner caches");
            }
        }

        #endregion

        #region Core flow

        /// <summary>
        /// Standard priority for prisoner handling
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            // Standard priority for prisoner handling
            return 5.5f;
        }

        /// <summary>
        /// Try to give a job, using the centralized cache system
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (ShouldSkip(pawn))
            {
                return null;
            }

            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Warden_ReleasePrisoner_PawnControl>(
                pawn,
                WorkTag,
                CreateJobFromCachedTargets,
                debugJobDesc: "release prisoner");
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

        #endregion

        #region Target processing

        /// <summary>
        /// Get all prisoners eligible for release
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null) yield break;

            // Get all prisoner pawns on the map
            foreach (Pawn prisoner in map.mapPawns.PrisonersOfColonySpawned)
            {
                if (FilterReleasablePrisoners(prisoner))
                {
                    yield return prisoner;
                }
            }
        }

        /// <summary>
        /// Get prisoners matching specific criteria for this job giver
        /// </summary>
        protected override IEnumerable<Pawn> GetPrisonersMatchingCriteria(Map map)
        {
            if (map == null) yield break;

            // Get all releasable prisoners on the map
            foreach (Pawn prisoner in map.mapPawns.PrisonersOfColonySpawned)
            {
                if (FilterReleasablePrisoners(prisoner))
                {
                    yield return prisoner;
                }
            }
        }

        /// <summary>
        /// Filter function to identify prisoners that can be released
        /// </summary>
        private bool FilterReleasablePrisoners(Pawn prisoner)
        {
            return prisoner?.guest != null &&
                   prisoner.guest.IsInteractionEnabled(PrisonerInteractionModeDefOf.Release) &&
                   !prisoner.Downed &&
                   !prisoner.guest.Released &&
                   !prisoner.InMentalState;
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

            // Find the first valid prisoner to release
            Pawn targetPrisoner = Utility_JobGiverManager.FindFirstValidTargetInBuckets<Pawn>(
                buckets,
                warden,
                (prisoner, p) => IsValidPrisonerTarget(prisoner, p),
                WorkTag
            );

            if (targetPrisoner == null)
                return null;

            // Create job for the prisoner
            return CreateJobForPrisoner(warden, targetPrisoner, forced);
        }

        /// <summary>
        /// Validates if a warden can release a specific prisoner
        /// </summary>
        protected override bool IsValidPrisonerTarget(Pawn prisoner, Pawn warden)
        {
            // Skip if not actually a valid prisoner for release anymore
            if (prisoner?.guest == null ||
                !prisoner.IsPrisoner ||
                prisoner.Downed ||
                prisoner.InMentalState ||
                prisoner.guest.Released ||
                !prisoner.guest.IsInteractionEnabled(PrisonerInteractionModeDefOf.Release))
                return false;

            // Check if warden can reach prisoner
            if (!prisoner.Spawned || prisoner.IsForbidden(warden) ||
                !warden.CanReserveAndReach(prisoner, PathEndMode.Touch, warden.NormalMaxDanger()))
                return false;

            // Check for valid release cell
            IntVec3 releaseCell;
            return RCellFinder.TryFindPrisonerReleaseCell(prisoner, warden, out releaseCell);
        }

        /// <summary>
        /// Determines if the job giver should execute based on cache status
        /// </summary>
        protected override bool ShouldExecuteNow(int mapId)
        {
            // Check if there's any prisoners of the colony on the map
            Map map = Find.Maps.Find(m => m.uniqueID == mapId);
            if (map == null || map.mapPawns.PrisonersOfColonySpawnedCount == 0)
                return false;

            // Check for releasable prisoners to avoid unnecessary cache updates
            bool hasReleasablePrisoners = map.mapPawns.PrisonersOfColonySpawned.Any(p =>
                p?.guest != null && p.guest.IsInteractionEnabled(PrisonerInteractionModeDefOf.Release) &&
                !p.Downed && !p.guest.Released && !p.InMentalState);

            if (!hasReleasablePrisoners)
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
            return CreateReleasePrisonerJob(warden, prisoner);
        }

        /// <summary>
        /// Creates a job to release a prisoner
        /// </summary>
        private Job CreateReleasePrisonerJob(Pawn warden, Pawn prisoner)
        {
            // Find release cell
            IntVec3 releaseCell;
            if (RCellFinder.TryFindPrisonerReleaseCell(prisoner, warden, out releaseCell))
            {
                Job job = JobMaker.MakeJob(WorkJobDef, prisoner, releaseCell);
                job.count = 1;

                Utility_DebugManager.LogNormal($"{warden.LabelShort} created job to release prisoner {prisoner.LabelShort}");

                return job;
            }

            return null;
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
                Utility_DebugManager.LogNormal($"Reset release prisoner cache for {this.GetType().Name}");
            }
        }

        #endregion

        #region Debug support

        /// <summary>
        /// For debugging purposes
        /// </summary>
        public override string ToString()
        {
            return "JobGiver_Warden_ReleasePrisoner_PawnControl";
        }

        #endregion
    }
}