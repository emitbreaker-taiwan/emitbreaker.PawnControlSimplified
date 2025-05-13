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
        /// Distance thresholds for bucketing (10, 20, 30 tiles squared)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 100f, 400f, 900f };

        #endregion

        #region Core flow

        /// <summary>
        /// Gets base priority for the job giver
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            // Converting prisoners to ideology is important
            return 5.7f;
        }

        /// <summary>
        /// Checks whether this job giver should be skipped for a pawn
        /// </summary>
        public override bool ShouldSkip(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.InMentalState)
                return true;

            // Check if pawn has required capabilities
            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null)
                return true;

            // Skip if pawn is not a warden
            if (!Utility_TagManager.WorkEnabled(pawn.def, WorkTag))
                return true;

            // Check if Ideology is active
            if (!ModLister.CheckIdeology("IsValidPrisonerTarget"))
                return true;

            return false;
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

            // Check if cache needs updating
            string cacheKey = this.GetType().Name + PRISONERS_CACHE_SUFFIX;
            int lastUpdateTick = Utility_MapCacheManager.GetLastCacheUpdateTick(mapId, cacheKey);

            return Find.TickManager.TicksGame > lastUpdateTick + CacheUpdateInterval;
        }

        #endregion

        #region Prisoner Selection

        /// <summary>
        /// Get prisoners that match the criteria for conversion interactions
        /// </summary>
        protected override IEnumerable<Pawn> GetPrisonersMatchingCriteria(Map map)
        {
            if (map == null)
                yield break;

            // Check if Ideology is active
            if (!ModLister.CheckIdeology("GetPrisonersMatchingCriteria"))
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
        /// Validates if a warden can convert a specific prisoner
        /// </summary>
        protected override bool IsValidPrisonerTarget(Pawn prisoner, Pawn warden)
        {
            // First check base class validation
            if (!base.IsValidPrisonerTarget(prisoner, warden))
                return false;

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

            return true;
        }

        /// <summary>
        /// Create a job for the given prisoner
        /// </summary>
        protected override Job CreateJobForPrisoner(Pawn warden, Pawn prisoner, bool forced)
        {
            Job job = JobMaker.MakeJob(JobDefOf.PrisonerConvert, prisoner);

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"{warden.LabelShort} created job to convert prisoner {prisoner.LabelShort} to {warden.Ideo.name}");
            }

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