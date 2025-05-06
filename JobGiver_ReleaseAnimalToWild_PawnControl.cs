using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to release animals to the wild when marked with the appropriate designation.
    /// Uses the Handling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_ReleaseAnimalToWild_PawnControl : ThinkNode_JobGiver
    {
        // Cache for animals marked for release
        private static readonly Dictionary<int, List<Pawn>> _releaseCache = new Dictionary<int, List<Pawn>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 250; // Update every ~4 seconds

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        public override float GetPriority(Pawn pawn)
        {
            // Releasing animals is a moderate priority task
            return 5.3f; // Slightly lower than slaughter
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Quick early exit if no animals are marked for release
            if (!pawn.Map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.ReleaseAnimalToWild))
                return null;

            return Utility_JobGiverManager.StandardTryGiveJob<Pawn>(
                pawn,
                "Handling",
                (p, forced) => {
                    // Update release cache
                    UpdateReleaseCache(p.Map);

                    // Find and create a job for releasing animals
                    return TryCreateReleaseJob(p, forced);
                },
                debugJobDesc: "release animal to wild");
        }

        /// <summary>
        /// Updates the cache of animals that need to be released to the wild
        /// </summary>
        private void UpdateReleaseCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_releaseCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_releaseCache.ContainsKey(mapId))
                    _releaseCache[mapId].Clear();
                else
                    _releaseCache[mapId] = new List<Pawn>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Pawn, bool>();

                // Add animals with release designations
                foreach (Designation designation in map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.ReleaseAnimalToWild))
                {
                    if (designation.target.Thing is Pawn animal && animal.IsNonMutantAnimal)
                    {
                        _releaseCache[mapId].Add(animal);
                    }
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Create a job to release an animal to the wild
        /// </summary>
        private Job TryCreateReleaseJob(Pawn pawn, bool forced = false)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_releaseCache.ContainsKey(mapId) || _releaseCache[mapId].Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                _releaseCache[mapId],
                (animal) => (animal.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Find the best animal to release
            Pawn targetAnimal = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (animal, p) => {
                    // CRITICAL: Don't release yourself!
                    if (animal == p)
                        return false;

                    // IMPORTANT: Check faction interaction validity first
                    if (!Utility_JobGiverManager.IsValidFactionInteraction(animal, p, requiresDesignator: true))
                        return false;

                    // Skip if no longer a valid release target
                    if (!animal.IsNonMutantAnimal ||
                        p.Map.designationManager.DesignationOn(animal, DesignationDefOf.ReleaseAnimalToWild) == null)
                        return false;

                    // Skip if wrong faction
                    if (p.Faction != animal.Faction)
                        return false;

                    // Skip if in mental state or dead
                    if (animal.InAggroMentalState || animal.Dead)
                        return false;

                    // Skip if forbidden or cannot reserve
                    if (animal.IsForbidden(p) || !p.CanReserve(animal, 1, -1, null, forced))
                        return false;

                    // Check if there's a valid outside cell to release to
                    IntVec3 outsideCell;
                    if (!JobDriver_ReleaseAnimalToWild.TryFindClosestOutsideCell_NewTemp(
                        animal.Position, animal.Map, TraverseParms.For(p), p, out outsideCell))
                        return false;

                    return true;
                },
                _reachabilityCache
            );

            // Create job if target found
            if (targetAnimal != null)
            {
                Job job = JobMaker.MakeJob(JobDefOf.ReleaseAnimalToWild, targetAnimal);
                job.count = 1;

                if (Prefs.DevMode)
                {
                    Log.Message($"[PawnControl] {pawn.LabelShort} created job to release {targetAnimal.LabelShort} to the wild");
                }

                return job;
            }

            return null;
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_releaseCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_ReleaseAnimalToWild_PawnControl";
        }
    }
}