using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks for wardens to convert prisoners to their ideology.
    /// Optimized to use the PawnControl framework for better performance.
    /// </summary>
    public class JobGiver_Warden_Convert_PawnControl : JobGiver_Warden_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "Convert";

        /// <summary>
        /// Cache update interval in ticks (180 ticks = 3 seconds)
        /// </summary>
        protected override int CacheUpdateInterval => 180;

        /// <summary>
        /// Distance thresholds for bucketing (10, 20, 30 tiles)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 100f, 400f, 900f };

        #endregion

        #region Core flow

        protected override float GetBasePriority(string workTag)
        {
            return 5.7f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Warden_Convert_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) => {
                    // Process cached targets to create job
                    if (p?.Map == null) return null;

                    int mapId = p.Map.uniqueID;
                    List<Thing> targets;
                    if (_prisonerCache.TryGetValue(mapId, out var prisonerList) && prisonerList != null)
                    {
                        targets = prisonerList.Cast<Thing>().ToList();
                    }
                    else
                    {
                        targets = new List<Thing>();
                    }

                    return ProcessCachedTargets(p, targets, forced);
                },
                debugJobDesc: "convert prisoner");
        }

        public override bool ShouldSkip(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.InMentalState)
                return true;
            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null)
                return true;
            // Skip if pawn is not a warden
            if (!Utility_TagManager.WorkEnabled(pawn.def, WorkTag))
                return true;
            // Check if Ideology is active
            if (!ModLister.CheckIdeology("IsValidPrisonerTarget"))
                return true;
            // Check if pawn is in a mental state
            if (pawn.InMentalState)
                return true;
            return false;
        }

        #endregion

        #region Target processing

        /// <summary>
        /// Get all prisoners eligible for conversion interaction
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null) yield break;
            
            // Check if Ideology is active
            if (!ModLister.CheckIdeology("JobGiver_Warden_Convert_PawnControl"))
                yield break;

            // Get all prisoner pawns on the map
            foreach (Pawn prisoner in map.mapPawns.PrisonersOfColonySpawned)
            {
                if (FilterConvertablePrisoners(prisoner))
                {
                    yield return prisoner;
                }
            }
        }

        /// <summary>
        /// Filter function to identify prisoners ready for conversion interaction
        /// </summary>
        private bool FilterConvertablePrisoners(Pawn prisoner)
        {
            // Skip prisoners in mental states
            if (prisoner.InMentalState)
                return false;

            // Only include prisoners set for Convert
            if (prisoner.guest?.IsInteractionEnabled(PrisonerInteractionModeDefOf.Convert) == true)
            {
                // Only include prisoners scheduled for interaction
                if (prisoner.guest.ScheduledForInteraction)
                {
                    return true;
                }
            }

            return false;
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

            // Find the first valid prisoner to convert
            Pawn targetPrisoner = Utility_JobGiverManager.FindFirstValidTargetInBuckets<Pawn>(
                buckets,
                warden,
                (prisoner, p) => IsValidPrisonerTarget(prisoner, p),
                _prisonerReachabilityCache.TryGetValue(mapId, out var cache) ?
                    new Dictionary<int, Dictionary<Pawn, bool>> { { mapId, cache } } :
                    new Dictionary<int, Dictionary<Pawn, bool>>()
            );

            if (targetPrisoner == null)
                return null;

            // Create conversion job
            return CreateConvertJob(warden, targetPrisoner);
        }

        /// <summary>
        /// Validates if a warden can convert a specific prisoner
        /// </summary>
        protected override bool IsValidPrisonerTarget(Pawn prisoner, Pawn warden)
        {
            if (prisoner?.guest == null)
                return false;

            // Check if Ideology is active
            if (!ModLister.CheckIdeology("IsValidPrisonerTarget"))
                return false;

            // Check for valid interaction mode and scheduling
            if (!prisoner.guest.IsInteractionEnabled(PrisonerInteractionModeDefOf.Convert) ||
                !prisoner.guest.ScheduledForInteraction)
                return false;

            // Skip if prisoner and warden already share the same ideology
            if (prisoner.Ideo == warden.Ideo)
                return false;

            // Check if warden's ideo matches the target ideology for conversion
            if (warden.Ideo != prisoner.guest.ideoForConversion)
                return false;

            // Check if warden can talk
            if (!warden.health.capacities.CapableOf(PawnCapacityDefOf.Talking))
                return false;

            // Check if prisoner is downed but not in bed
            if (prisoner.Downed && !prisoner.InBed())
                return false;

            // Check if prisoner is awake
            if (!prisoner.Awake())
                return false;

            // Check basic reachability
            if (!prisoner.Spawned || prisoner.IsForbidden(warden) ||
                !warden.CanReserve(prisoner, 1, -1, null, false))
                return false;

            return true;
        }

        /// <summary>
        /// Determines if the job giver should execute based on cache status
        /// </summary>
        protected override bool ShouldExecuteNow(int mapId)
        {
            // Check if Ideology is active
            if (!ModLister.CheckIdeology("ShouldExecuteNow"))
                return false;

            // Check if there's any prisoners of the colony on the map
            Map map = Find.Maps.Find(m => m.uniqueID == mapId);
            if (map == null || map.mapPawns.PrisonersOfColonySpawnedCount == 0)
                return false;

            return !_lastWardenCacheUpdate.TryGetValue(mapId, out int lastUpdate) ||
                   Find.TickManager.TicksGame > lastUpdate + CacheUpdateInterval;
        }

        #endregion

        #region Job creation

        /// <summary>
        /// Creates the convert job for the warden
        /// </summary>
        private Job CreateConvertJob(Pawn warden, Pawn prisoner)
        {
            Job job = JobMaker.MakeJob(JobDefOf.PrisonerConvert, prisoner);

            Utility_DebugManager.LogNormal($"{warden.LabelShort} created job to convert prisoner {prisoner.LabelShort} to {warden.Ideo.name}");

            return job;
        }

        #endregion

        #region Utility

        /// <summary>
        /// For debugging purposes
        /// </summary>
        public override string ToString()
        {
            return "JobGiver_Warden_Convert_PawnControl";
        }

        #endregion
    }
}