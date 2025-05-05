using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns prisoner release jobs to eligible wardens.
    /// Optimized to use the PawnControl framework for better performance.
    /// </summary>
    public class JobGiver_Warden_ReleasePrisoner_PawnControl : ThinkNode_JobGiver
    {
        // Cached prisoners to improve performance with large colonies
        private static readonly Dictionary<int, List<Pawn>> _prisonerCache = new Dictionary<int, List<Pawn>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 180; // Update every 3 seconds

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 100f, 400f, 900f }; // 10, 20, 30 tiles

        public override float GetPriority(Pawn pawn)
        {
            // Standard priority for prisoner handling
            return 5.5f;
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
                    return TryCreateReleasePrisonerJob(p);
                },
                debugJobDesc: "release prisoner assignment");
        }

        /// <summary>
        /// Updates the cache of prisoners that need to be released
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

                // Find all prisoners who need to be released
                // Using PrisonersOfColonySpawned instead of PrisonersInColony
                foreach (Pawn prisoner in map.mapPawns.PrisonersOfColonySpawned)
                {
                    if (prisoner == null || prisoner.Destroyed || !prisoner.Spawned)
                        continue;

                    if (prisoner.guest != null &&
                        prisoner.guest.IsInteractionEnabled(PrisonerInteractionModeDefOf.Release) &&
                        !prisoner.Downed &&
                        !prisoner.guest.Released &&
                        !prisoner.InMentalState)
                    {
                        _prisonerCache[mapId].Add(prisoner);
                    }
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Create a job to release a prisoner using manager-driven bucket processing
        /// </summary>
        private Job TryCreateReleasePrisonerJob(Pawn pawn)
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

            // Find the best prisoner to release
            Pawn targetPrisoner = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (prisoner, p) => {
                    // Skip if not actually a prisoner anymore
                    if (prisoner?.guest == null || !prisoner.IsPrisoner || prisoner.Downed ||
                        prisoner.InMentalState || prisoner.guest.Released ||
                        !prisoner.guest.IsInteractionEnabled(PrisonerInteractionModeDefOf.Release))
                        return false;

                    // Check basic requirements
                    if (!prisoner.Spawned || prisoner.IsForbidden(p))
                        return false;

                    // Check if warden can reach prisoner
                    if (!p.CanReserveAndReach(prisoner, PathEndMode.Touch, p.NormalMaxDanger()))
                        return false;

                    // Check for valid release cell
                    IntVec3 releaseCell;
                    return RCellFinder.TryFindPrisonerReleaseCell(prisoner, p, out releaseCell);
                },
                _reachabilityCache
            );

            // Create job if target found
            if (targetPrisoner != null)
            {
                // Find release cell
                IntVec3 releaseCell;
                if (RCellFinder.TryFindPrisonerReleaseCell(targetPrisoner, pawn, out releaseCell))
                {
                    Job job = JobMaker.MakeJob(JobDefOf.ReleasePrisoner, targetPrisoner, releaseCell);
                    job.count = 1;

                    if (Prefs.DevMode)
                    {
                        Log.Message($"[PawnControl] {pawn.LabelShort} created job to release prisoner {targetPrisoner.LabelShort}");
                    }

                    return job;
                }
            }

            return null;
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
            return "JobGiver_Warden_ReleasePrisoner_PawnControl";
        }
    }
}