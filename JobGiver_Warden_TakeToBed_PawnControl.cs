using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to take prisoners to beds.
    /// Optimized to use the PawnControl framework for better performance.
    /// </summary>
    public class JobGiver_Warden_TakeToBed_PawnControl : JobGiver_Warden_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "TakeToBed";

        /// <summary>
        /// Cache update interval in ticks (120 ticks = 2 seconds)
        /// </summary>
        protected override int CacheUpdateInterval => base.CacheUpdateInterval;

        /// <summary>
        /// Distance thresholds for bucketing (10, 20, 30 tiles)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 100f, 400f, 900f };

        /// <summary>
        /// Cache key suffix for this specific job giver
        /// </summary>
        private const string TAKETOBED_CACHE_SUFFIX = "_TakeToBed";

        #endregion

        #region Core flow

        protected override float GetBasePriority(string workTag)
        {
            // Taking prisoners to beds is important for their care
            return 6.0f;
        }

        /// <summary>
        /// Standard job creation using the framework
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (ShouldSkip(pawn))
            {
                return null;
            }

            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Warden_TakeToBed_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) => CreateJobFor(p, forced),
                debugJobDesc: "take prisoner to bed");
        }

        /// <summary>
        /// Creates a job for the given pawn according to targets found
        /// </summary>
        protected override Job CreateJobFor(Pawn pawn, bool forced)
        {
            int mapId = pawn.Map.uniqueID;

            if (!ShouldExecuteNow(mapId))
                return null;

            return base.CreateJobFor(pawn, forced);
        }

        #endregion

        #region Target processing

        /// <summary>
        /// Get all prisoners that need to be taken to beds
        /// </summary>
        protected override IEnumerable<Pawn> GetPrisonersMatchingCriteria(Map map)
        {
            if (map == null) yield break;

            // Get all prisoner pawns on the map
            foreach (Pawn prisoner in map.mapPawns.PrisonersOfColonySpawned)
            {
                if (FilterPrisonersNeedingBeds(prisoner))
                {
                    yield return prisoner;
                }
            }
        }

        /// <summary>
        /// Filter function to identify prisoners that need to be taken to beds
        /// </summary>
        private bool FilterPrisonersNeedingBeds(Pawn prisoner)
        {
            if (prisoner?.guest == null || prisoner.InAggroMentalState || prisoner.IsFormingCaravan())
                return false;

            // Add downed prisoners needing medical rest
            if (prisoner.Downed && HealthAIUtility.ShouldSeekMedicalRest(prisoner) && !prisoner.InBed())
                return true;

            // Add non-downed prisoners without proper beds
            if (!prisoner.Downed && RestUtility.FindBedFor(prisoner, prisoner, true, guestStatus: new GuestStatus?(GuestStatus.Prisoner)) == null)
                return true;

            return false;
        }

        /// <summary>
        /// Validates if a prisoner target is valid for this warden job
        /// </summary>
        protected override bool IsValidPrisonerTarget(Pawn prisoner, Pawn warden)
        {
            // Skip if not actually a valid prisoner
            if (!base.IsValidPrisonerTarget(prisoner, warden))
                return false;

            if (prisoner.InAggroMentalState || prisoner.IsFormingCaravan())
                return false;

            // Try to create a job for this prisoner
            return TryMakeJob(warden, prisoner, false) != null;
        }

        /// <summary>
        /// Process the cached targets to create jobs
        /// </summary>
        protected override Job CreateJobForPrisoner(Pawn warden, Pawn prisoner, bool forced)
        {
            if (warden == null || prisoner == null)
                return null;

            // Create take to bed job
            Job job = TryMakeJob(warden, prisoner, forced);

            if (job != null && Prefs.DevMode)
            {
                string jobType = job.def == JobDefOf.TakeWoundedPrisonerToBed ? "medical bed" : "assigned bed";
                Utility_DebugManager.LogNormal($"{warden.LabelShort} created job to take prisoner {prisoner.LabelShort} to {jobType}");
            }

            return job;
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

            // Check for prisoners needing beds to avoid unnecessary cache updates
            bool hasPrisonersNeedingBeds = false;
            foreach (Pawn prisoner in map.mapPawns.PrisonersOfColonySpawned)
            {
                if (FilterPrisonersNeedingBeds(prisoner))
                {
                    hasPrisonersNeedingBeds = true;
                    break;
                }
            }

            if (!hasPrisonersNeedingBeds)
                return false;

            string cacheKey = this.GetType().Name + PRISONERS_CACHE_SUFFIX;
            int lastUpdate = Utility_MapCacheManager.GetLastCacheUpdateTick(mapId, cacheKey);
            return lastUpdate == -1 || Find.TickManager.TicksGame > lastUpdate + CacheUpdateInterval;
        }

        #endregion

        #region Job creation

        /// <summary>
        /// Try to create a job to take a prisoner to bed, mirroring the original WorkGiver logic
        /// </summary>
        private Job TryMakeJob(Pawn pawn, Pawn prisoner, bool forced = false)
        {
            // Try taking a downed prisoner to bed first
            Job downedToBedJob = TakeDownedToBedJob(prisoner, pawn);
            if (downedToBedJob != null)
                return downedToBedJob;

            // Try taking prisoner to preferred bed
            Job toPreferredBedJob = TakeToPreferredBedJob(prisoner, pawn);
            if (toPreferredBedJob != null)
                return toPreferredBedJob;

            return null;
        }

        /// <summary>
        /// Try to create a job to take a prisoner to their preferred bed
        /// </summary>
        private Job TakeToPreferredBedJob(Pawn prisoner, Pawn warden)
        {
            if (prisoner.Downed || !warden.CanReserve((LocalTargetInfo)(Thing)prisoner))
                return null;

            if (RestUtility.FindBedFor(prisoner, prisoner, true, guestStatus: new GuestStatus?(GuestStatus.Prisoner)) != null)
                return null;

            Room room = prisoner.GetRoom();
            Building_Bed bedFor = RestUtility.FindBedFor(prisoner, warden, false, guestStatus: new GuestStatus?(GuestStatus.Prisoner));

            if (bedFor == null || bedFor.GetRoom() == room)
                return null;

            Job toPreferredBedJob = JobMaker.MakeJob(JobDefOf.EscortPrisonerToBed, (LocalTargetInfo)(Thing)prisoner, (LocalTargetInfo)(Thing)bedFor);
            toPreferredBedJob.count = 1;

            return toPreferredBedJob;
        }

        /// <summary>
        /// Try to create a job to take a downed prisoner to a hospital bed
        /// </summary>
        private Job TakeDownedToBedJob(Pawn prisoner, Pawn warden)
        {
            if (!prisoner.Downed || !HealthAIUtility.ShouldSeekMedicalRest(prisoner) ||
                prisoner.InBed() || !warden.CanReserve((LocalTargetInfo)(Thing)prisoner))
                return null;

            Building_Bed bedFor = RestUtility.FindBedFor(prisoner, warden, true, guestStatus: new GuestStatus?(GuestStatus.Prisoner));
            if (bedFor == null)
                return null;

            Job downedToBedJob = JobMaker.MakeJob(JobDefOf.TakeWoundedPrisonerToBed, (LocalTargetInfo)(Thing)prisoner, (LocalTargetInfo)(Thing)bedFor);
            downedToBedJob.count = 1;

            return downedToBedJob;
        }

        #endregion

        #region Cache Management

        /// <summary>
        /// Reset this job giver's cache
        /// </summary>
        public static void ResetTakeToBedCache()
        {
            // Clear all TakeToBed-related caches from all maps
            foreach (Map map in Find.Maps)
            {
                int mapId = map.uniqueID;

                // Clear specific cache key
                string cacheKey = typeof(JobGiver_Warden_TakeToBed_PawnControl).Name + TAKETOBED_CACHE_SUFFIX;
                var mapCache = Utility_MapCacheManager.GetOrCreateMapCache<string, object>(mapId);

                if (mapCache.ContainsKey(cacheKey))
                {
                    mapCache.Remove(cacheKey);
                    Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, -1);
                }
            }

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal("Reset TakeToBed prisoner cache");
            }
        }

        #endregion

        #region Utility

        /// <summary>
        /// For debugging purposes
        /// </summary>
        public override string ToString()
        {
            return "JobGiver_Warden_TakeToBed_PawnControl";
        }

        #endregion
    }
}