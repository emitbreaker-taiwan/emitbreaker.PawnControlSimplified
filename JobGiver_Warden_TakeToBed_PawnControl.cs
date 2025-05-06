using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to take prisoners to beds.
    /// Optimized to use the PawnControl framework for better performance.
    /// </summary>
    public class JobGiver_Warden_TakeToBed_PawnControl : ThinkNode_JobGiver
    {
        // Cached prisoners to improve performance with large colonies
        private static readonly Dictionary<int, List<Pawn>> _prisonerCache = new Dictionary<int, List<Pawn>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 120; // Update every 2 seconds

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 100f, 400f, 900f }; // 10, 20, 30 tiles

        public override float GetPriority(Pawn pawn)
        {
            // Taking prisoners to beds is important for their care
            return 6.0f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<Plant>(
                pawn,
                "Warden",
                (p, forced) => {
                    // Update plant cache
                    UpdatePrisonerCache(p.Map);

                    // Find and create a job for cutting plants with VALID DESIGNATORS ONLY
                    return TryCreateTakeToBedJob(p);
                },
                debugJobDesc: "prisoner bed assignment");
        }

        /// <summary>
        /// Updates the cache of prisoners that may need to be taken to beds
        /// </summary>
        private void UpdatePrisonerCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_prisonerCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_prisonerCache.ContainsKey(mapId))
                    _prisonerCache[mapId].Clear();
                else
                    _prisonerCache[mapId] = new List<Pawn>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Pawn, bool>();

                // Find all prisoners who might need to be taken to bed
                foreach (Pawn prisoner in map.mapPawns.PrisonersOfColonySpawned)
                {
                    if (prisoner == null || prisoner.Destroyed || !prisoner.Spawned)
                        continue;

                    if (prisoner.guest != null && !prisoner.InAggroMentalState && !prisoner.IsFormingCaravan())
                    {
                        // Add downed prisoners needing medical rest or non-downed prisoners without proper beds
                        if ((prisoner.Downed && HealthAIUtility.ShouldSeekMedicalRest(prisoner) && !prisoner.InBed()) ||
                            (!prisoner.Downed && RestUtility.FindBedFor(prisoner, prisoner, true, guestStatus: new GuestStatus?(GuestStatus.Prisoner)) == null))
                        {
                            _prisonerCache[mapId].Add(prisoner);
                        }
                    }
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Create a job to take a prisoner to bed using manager-driven bucket processing
        /// </summary>
        private Job TryCreateTakeToBedJob(Pawn pawn)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_prisonerCache.ContainsKey(mapId) || _prisonerCache[mapId].Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing and target selection
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                _prisonerCache[mapId],
                (prisoner) => (prisoner.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Find the best prisoner to take to bed
            Pawn targetPrisoner = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (prisoner, p) => {
                    // IMPORTANT: Check faction interaction validity first
                    if (!Utility_JobGiverManager.IsValidFactionInteraction(prisoner, p, requiresDesignator: false))
                        return false;

                    // Skip if no longer a valid prisoner
                    if (prisoner?.guest == null || !prisoner.IsPrisoner ||
                        prisoner.InAggroMentalState || prisoner.IsFormingCaravan())
                        return false;

                    // Check basic requirements
                    if (!prisoner.Spawned || prisoner.IsForbidden(p))
                        return false;

                    // Check if warden can reach prisoner
                    if (!p.CanReserveAndReach(prisoner, PathEndMode.OnCell, p.NormalMaxDanger()))
                        return false;

                    // Try to create a job for this prisoner
                    return TryMakeJob(p, prisoner, false) != null;
                },
                _reachabilityCache
            );

            // Create job if target found
            if (targetPrisoner != null)
            {
                Job job = TryMakeJob(pawn, targetPrisoner, false);

                if (job != null && Prefs.DevMode)
                {
                    Log.Message($"[PawnControl] {pawn.LabelShort} created job to take prisoner {targetPrisoner.LabelShort} to bed");
                }

                return job;
            }

            return null;
        }

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

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_prisonerCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_Warden_TakeToBed_PawnControl";
        }
    }
}