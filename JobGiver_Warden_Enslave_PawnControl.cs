using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks for wardens to enslave prisoners or reduce their will.
    /// Optimized to use the PawnControl framework for better performance.
    /// </summary>
    public class JobGiver_Warden_Enslave_PawnControl : JobGiver_Warden_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "Enslave";

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
        private const string CACHE_KEY_SUFFIX = "_Enslave";

        #endregion

        #region Core flow

        /// <summary>
        /// Enslave has high priority as it changes prisoner status
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            // Enslaving is a higher priority task than regular warden duties
            return 6.5f;
        }

        /// <summary>
        /// Try to give a job, using the centralized cache system
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            // Check for Ideology DLC first
            if (!ModLister.CheckIdeology("WorkGiver_Warden_Enslave"))
            {
                return null;
            }

            if (ShouldSkip(pawn))
            {
                return null;
            }

            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Warden_Enslave_PawnControl>(
                pawn,
                WorkTag,
                CreateJobFromCachedTargets,
                debugJobDesc: "enslave prisoner or reduce will");
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
            // Use base checks first
            if (base.ShouldSkip(pawn))
                return true;

            // Ideology required
            if (!ModLister.CheckIdeology("WorkGiver_Warden_Enslave"))
                return true;

            // Check pawn capable of talking
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Talking))
                return true;

            return false;
        }

        #endregion

        #region Target processing

        /// <summary>
        /// Get all prisoners eligible for enslavement interaction
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null) yield break;

            // Get all prisoner pawns on the map
            foreach (Pawn prisoner in map.mapPawns.PrisonersOfColonySpawned)
            {
                if (FilterEnslavablePrisoners(prisoner))
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

            foreach (Pawn prisoner in map.mapPawns.PrisonersOfColonySpawned)
            {
                if (FilterEnslavablePrisoners(prisoner))
                {
                    yield return prisoner;
                }
            }
        }

        /// <summary>
        /// Filter function to identify prisoners ready for enslavement interaction
        /// </summary>
        private bool FilterEnslavablePrisoners(Pawn prisoner)
        {
            // Skip prisoners in mental states
            if (prisoner.InMentalState)
                return false;

            // Check if either Enslave or ReduceWill interaction is enabled
            bool canEnslave = prisoner.guest.IsInteractionEnabled(PrisonerInteractionModeDefOf.Enslave);
            bool canReduceWill = prisoner.guest.IsInteractionEnabled(PrisonerInteractionModeDefOf.ReduceWill);

            if (!canEnslave && !canReduceWill)
                return false;

            // Check if scheduled for interaction
            if (!prisoner.guest.ScheduledForInteraction)
                return false;

            // Skip if attempting to reduce will but will is already 0
            if (canReduceWill && !canEnslave && prisoner.guest.will <= 0f)
                return false;

            // Check if downed but not in bed
            if (prisoner.Downed && !prisoner.InBed())
                return false;

            // Check if awake
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

            // Find the first valid prisoner to enslave or reduce will
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
        /// Validates if a warden can enslave a specific prisoner
        /// </summary>
        protected override bool IsValidPrisonerTarget(Pawn prisoner, Pawn warden)
        {
            if (prisoner?.guest == null)
                return false;

            // Check if either Enslave or ReduceWill interaction is enabled
            bool canEnslave = prisoner.guest.IsInteractionEnabled(PrisonerInteractionModeDefOf.Enslave);
            bool canReduceWill = prisoner.guest.IsInteractionEnabled(PrisonerInteractionModeDefOf.ReduceWill);

            if (!canEnslave && !canReduceWill)
                return false;

            // Check if scheduled for interaction
            if (!prisoner.guest.ScheduledForInteraction)
                return false;

            // Check for reduce will when will is at 0
            if (canReduceWill && !canEnslave && prisoner.guest.will <= 0f)
                return false;

            // Check if prisoner is downed but not in bed
            if (prisoner.Downed && !prisoner.InBed())
                return false;

            // Check if prisoner is awake
            if (!prisoner.Awake())
                return false;

            // Check if warden can talk
            if (!warden.health.capacities.CapableOf(PawnCapacityDefOf.Talking))
                return false;

            // Check basic reachability
            if (!prisoner.Spawned || prisoner.IsForbidden(warden) ||
                !warden.CanReserve(prisoner, 1, -1, null, false))
                return false;

            // Check history event notification for enslaving
            if (!new HistoryEvent(HistoryEventDefOf.EnslavedPrisoner, warden.Named(HistoryEventArgsNames.Doer)).Notify_PawnAboutToDo_Job())
                return false;

            return true;
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
            return CreateEnslaveJob(warden, prisoner);
        }

        /// <summary>
        /// Creates the enslave job for the warden
        /// </summary>
        private Job CreateEnslaveJob(Pawn warden, Pawn prisoner)
        {
            bool canEnslave = prisoner.guest.IsInteractionEnabled(PrisonerInteractionModeDefOf.Enslave);
            bool canReduceWill = prisoner.guest.IsInteractionEnabled(PrisonerInteractionModeDefOf.ReduceWill);

            Job job;

            if (canEnslave)
            {
                job = JobMaker.MakeJob(JobDefOf.PrisonerEnslave, prisoner);
                Utility_DebugManager.LogNormal($"{warden.LabelShort} created job to enslave prisoner {prisoner.LabelShort}");
            }
            else
            {
                job = JobMaker.MakeJob(JobDefOf.PrisonerReduceWill, prisoner);
                Utility_DebugManager.LogNormal($"{warden.LabelShort} created job to reduce will of prisoner {prisoner.LabelShort}");
            }

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
        }

        #endregion

        #region Debug support

        /// <summary>
        /// For debugging purposes
        /// </summary>
        public override string ToString()
        {
            return "JobGiver_Warden_Enslave_PawnControl";
        }

        #endregion
    }
}