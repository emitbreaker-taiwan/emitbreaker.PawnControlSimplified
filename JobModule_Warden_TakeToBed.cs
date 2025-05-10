using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for taking prisoners to beds
    /// </summary>
    public class JobModule_Warden_TakeToBed : JobModule_Warden
    {
        public override string UniqueID => "TakeToBed";
        public override float Priority => 6.0f;
        public override string Category => "PrisonerCare"; // Added category for consistency

        // Static caches for map-specific data persistence
        private static readonly Dictionary<int, List<Pawn>> _prisonerNeedsBedCache = new Dictionary<int, List<Pawn>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static int _lastPrisonerCacheUpdateTick = -999;

        // Cache for bed assignments - map specific
        private static readonly Dictionary<int, Dictionary<Pawn, Building_Bed>> _prisonerToBedMap = new Dictionary<int, Dictionary<Pawn, Building_Bed>>();

        public override bool ShouldProcessPrisoner(Pawn prisoner)
        {
            try
            {
                if (prisoner == null || prisoner.guest == null || prisoner.InAggroMentalState || prisoner.IsFormingCaravan())
                    return false;

                if (prisoner.Downed && HealthAIUtility.ShouldSeekMedicalRest(prisoner) && !prisoner.InBed())
                    return true;

                if (!prisoner.Downed && RestUtility.FindBedFor(prisoner, prisoner, true, guestStatus: GuestStatus.Prisoner) == null)
                    return true;

                return false;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in ShouldProcessPrisoner: {ex.Message}");
                return false;
            }
        }

        public override void UpdateCache(Map map, List<Pawn> targetCache)
        {
            base.UpdateCache(map, targetCache);

            if (map == null) return;
            int mapId = map.uniqueID;
            bool hasTargets = false;

            int currentTick = Find.TickManager.TicksGame;

            // Initialize caches if needed
            if (!_prisonerNeedsBedCache.ContainsKey(mapId))
                _prisonerNeedsBedCache[mapId] = new List<Pawn>();

            if (!_reachabilityCache.ContainsKey(mapId))
                _reachabilityCache[mapId] = new Dictionary<Pawn, bool>();

            if (!_prisonerToBedMap.ContainsKey(mapId))
                _prisonerToBedMap[mapId] = new Dictionary<Pawn, Building_Bed>();

            // Only do a full update if needed
            if (currentTick > _lastPrisonerCacheUpdateTick + CacheUpdateInterval ||
                !_prisonerNeedsBedCache.ContainsKey(mapId))
            {
                try
                {
                    // Clear existing caches for this map
                    _prisonerNeedsBedCache[mapId].Clear();
                    _reachabilityCache[mapId].Clear();
                    _prisonerToBedMap[mapId].Clear();

                    // Find all prisoners that need beds
                    foreach (Pawn prisoner in map.mapPawns.PrisonersOfColonySpawned)
                    {
                        if (ShouldProcessPrisoner(prisoner))
                        {
                            _prisonerNeedsBedCache[mapId].Add(prisoner);
                            targetCache.Add(prisoner);

                            // Pre-cache bed assignments
                            if (prisoner.Downed && HealthAIUtility.ShouldSeekMedicalRest(prisoner))
                            {
                                Building_Bed bed = RestUtility.FindBedFor(prisoner, null, true, guestStatus: GuestStatus.Prisoner);
                                if (bed != null)
                                    _prisonerToBedMap[mapId][prisoner] = bed;
                            }
                            else if (!prisoner.Downed)
                            {
                                Building_Bed bed = RestUtility.FindBedFor(prisoner, null, false, guestStatus: GuestStatus.Prisoner);
                                if (bed != null && bed.GetRoom() != prisoner.GetRoom())
                                    _prisonerToBedMap[mapId][prisoner] = bed;
                            }
                        }
                    }

                    if (_prisonerNeedsBedCache[mapId].Count > 0)
                    {
                        Utility_DebugManager.LogNormal(
                            $"Found {_prisonerNeedsBedCache[mapId].Count} prisoners needing bed assignments on map {map.uniqueID}");
                    }

                    _lastPrisonerCacheUpdateTick = currentTick;
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"Error updating prisoner bed cache: {ex}");
                }
            }
            else
            {
                // Just add the cached prisoners to the target cache
                foreach (Pawn prisoner in _prisonerNeedsBedCache[mapId])
                {
                    targetCache.Add(prisoner);
                }
            }

            SetHasTargets(map, hasTargets || (targetCache != null && targetCache.Count > 0));
        }

        public override bool ValidateTarget(Pawn prisoner, Pawn warden)
        {
            try
            {
                if (prisoner == null || warden == null || !prisoner.Spawned || !warden.Spawned)
                    return false;

                // Check faction interaction
                if (!Utility_JobGiverManager.IsValidFactionInteraction(prisoner, warden, requiresDesignator: false))
                    return false;

                int mapId = warden.Map.uniqueID;

                // Check if prisoner still needs a bed
                if (!ShouldProcessPrisoner(prisoner))
                    return false;

                // Check if warden can reach the prisoner
                if (!warden.CanReserveAndReach(prisoner, PathEndMode.OnCell, warden.NormalMaxDanger()))
                    return false;

                // Try to create a job (checks for valid beds)
                Job job = TryMakeJob(warden, prisoner, false);
                if (job == null)
                    return false;

                // Cache the bed assignment for future use
                if (_prisonerToBedMap.ContainsKey(mapId) && job.targetB.Thing is Building_Bed bed)
                    _prisonerToBedMap[mapId][prisoner] = bed;

                return true;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error validating warden take to bed job: {ex.Message}");
                return false;
            }
        }

        protected override Job CreateWardenJob(Pawn warden, Pawn prisoner)
        {
            try
            {
                Job job = TryMakeJob(warden, prisoner, false);
                if (job != null)
                {
                    string jobType = job.def == JobDefOf.TakeWoundedPrisonerToBed
                        ? "medical bed"
                        : "assigned bed";

                    Utility_DebugManager.LogNormal(
                        $"{warden.LabelShort} created job to take prisoner {prisoner.LabelShort} to {jobType}");
                }
                return job;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating warden take to bed job: {ex.Message}");
                return null;
            }
        }

        private Job TryMakeJob(Pawn warden, Pawn prisoner, bool forced = false)
        {
            try
            {
                // Try to use cached bed assignments first
                int mapId = warden.Map.uniqueID;
                Building_Bed cachedBed = null;

                if (_prisonerToBedMap.ContainsKey(mapId) &&
                    _prisonerToBedMap[mapId].TryGetValue(prisoner, out cachedBed))
                {
                    // Validate the cached bed is still valid
                    if (cachedBed != null && !cachedBed.DestroyedOrNull() && cachedBed.Spawned)
                    {
                        if (prisoner.Downed)
                        {
                            Job job = JobMaker.MakeJob(JobDefOf.TakeWoundedPrisonerToBed, prisoner, cachedBed);
                            job.count = 1;
                            return job;
                        }
                        else
                        {
                            Job job = JobMaker.MakeJob(JobDefOf.EscortPrisonerToBed, prisoner, cachedBed);
                            job.count = 1;
                            return job;
                        }
                    }
                }

                // If no valid cached bed, try the regular method
                var downedJob = TakeDownedToBedJob(prisoner, warden);
                if (downedJob != null) return downedJob;

                var preferredJob = TakeToPreferredBedJob(prisoner, warden);
                return preferredJob;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in TryMakeJob: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Mirroring vanilla: take a downed prisoner to a hospital bed if they need it.
        /// </summary>
        private Job TakeDownedToBedJob(Pawn prisoner, Pawn warden)
        {
            try
            {
                if (!prisoner.Downed ||
                    !HealthAIUtility.ShouldSeekMedicalRest(prisoner) ||
                     prisoner.InBed() ||
                    !warden.CanReserve(prisoner))
                    return null;

                var bed = RestUtility.FindBedFor(prisoner, warden, true, guestStatus: GuestStatus.Prisoner);
                if (bed == null) return null;

                var job = JobMaker.MakeJob(JobDefOf.TakeWoundedPrisonerToBed, prisoner, bed);
                job.count = 1;
                return job;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in TakeDownedToBedJob: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Mirror vanilla: escort non-downed prisoner to their assigned/guest-bed.
        /// </summary>
        private Job TakeToPreferredBedJob(Pawn prisoner, Pawn warden)
        {
            try
            {
                if (prisoner.Downed || !warden.CanReserve(prisoner))
                    return null;

                // if they're already in a proper bed, skip
                var assigned = RestUtility.FindBedFor(prisoner, prisoner, true, guestStatus: GuestStatus.Prisoner);
                if (assigned != null) return null;

                var room = prisoner.GetRoom();
                var bed = RestUtility.FindBedFor(prisoner, warden, false, guestStatus: GuestStatus.Prisoner);
                if (bed == null || bed.GetRoom() == room) return null;

                var job = JobMaker.MakeJob(JobDefOf.EscortPrisonerToBed, prisoner, bed);
                job.count = 1;
                return job;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in TakeToPreferredBedJob: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public override void ResetStaticData()
        {
            base.ResetStaticData();

            // Clear all specialized caches
            Utility_CacheManager.ResetJobGiverCache(_prisonerNeedsBedCache, _reachabilityCache);

            foreach (var bedMap in _prisonerToBedMap.Values)
            {
                bedMap.Clear();
            }
            _prisonerToBedMap.Clear();

            _lastPrisonerCacheUpdateTick = -999;
        }
    }
}