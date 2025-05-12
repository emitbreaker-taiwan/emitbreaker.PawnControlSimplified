using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to release animals to the wild when marked with the appropriate designation.
    /// Uses the Handling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Handling_ReleaseAnimalsToWild_PawnControl : JobGiver_Handling_PawnControl
    {
        #region Configuration

        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.ReleaseAnimalToWild;

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        protected override bool RequiresMapZoneorArea => false;

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "ReleaseAnimalsToWild";

        /// <summary>
        /// Update cache every ~4 seconds
        /// </summary>
        protected override int CacheUpdateInterval => 250;

        /// <summary>
        /// Distance thresholds for bucketing (15, 25, 40 tiles)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 225f, 625f, 1600f };

        #endregion

        #region Cache Management

        // Release-specific cache
        private static readonly Dictionary<int, List<Pawn>> _releaseCache = new Dictionary<int, List<Pawn>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _releaseReachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static int _lastReleaseCacheUpdateTick = -999;

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetReleaseCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_releaseCache, _releaseReachabilityCache);
            _lastReleaseCacheUpdateTick = -999;
        }

        #endregion

        #region Core flow

        protected override float GetBasePriority(string workTag)
        {
            // Releasing animals is a moderate priority task
            return 5.3f; // Slightly lower than slaughter
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Quick early exit if no animals are marked for release
            if (!pawn.Map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.ReleaseAnimalToWild))
                return null;

            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Handling_ReleaseAnimalsToWild_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) => {
                    if (p?.Map == null)
                        return null;

                    int mapId = p.Map.uniqueID;
                    int now = Find.TickManager.TicksGame;

                    if (!ShouldExecuteNow(mapId))
                        return null;

                    // Update the cache if needed
                    if (now > _lastReleaseCacheUpdateTick + CacheUpdateInterval ||
                        !_releaseCache.ContainsKey(mapId))
                    {
                        UpdateReleaseCache(p.Map);
                    }

                    // Process cached targets
                    return TryCreateReleaseJob(p, forced);
                },
                debugJobDesc: DebugName);
        }

        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // We're using our custom release cache instead of the standard Thing targets
            if (map?.designationManager != null)
            {
                foreach (Designation designation in map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.ReleaseAnimalToWild))
                {
                    if (designation.target.HasThing && designation.target.Thing is Pawn animal && animal.IsNonMutantAnimal)
                    {
                        yield return animal;
                    }
                }
            }
        }

        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            if (targets == null || targets.Count == 0 || pawn?.Map == null)
                return null;

            // Convert to pawns (we know they're all animals)
            List<Pawn> animals = targets.OfType<Pawn>().ToList();
            if (animals.Count == 0)
                return null;

            // Use the bucketing system to find the closest valid animal
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                animals,
                (animal) => (animal.Position - pawn.Position).LengthHorizontalSquared,
                DistanceThresholds);

            // Find the best animal to release
            Pawn targetAnimal = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (animal, p) => IsValidReleaseTarget(animal, p, forced),
                _releaseReachabilityCache);

            // Create job if target found
            if (targetAnimal != null)
            {
                Job job = JobMaker.MakeJob(WorkJobDef, targetAnimal);
                job.count = 1;
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to release {targetAnimal.LabelShort} to the wild");
                return job;
            }

            return null;
        }

        /// <summary>
        /// Determines if the job giver should execute based on cache status
        /// </summary>
        protected override bool ShouldExecuteNow(int mapId)
        {
            return Find.TickManager.TicksGame > _lastReleaseCacheUpdateTick + CacheUpdateInterval ||
                  !_releaseCache.ContainsKey(mapId);
        }

        /// <summary>
        /// Override the base class method for animal handling
        /// </summary>
        protected override bool CanHandleAnimal(Pawn animal, Pawn handler)
        {
            // Basic checks
            if (animal == null || handler == null || animal == handler)
                return false;

            // Must be a valid animal with release designation
            if (!animal.IsNonMutantAnimal ||
                handler.Map.designationManager.DesignationOn(animal, DesignationDefOf.ReleaseAnimalToWild) == null)
                return false;

            // Must be same faction
            if (handler.Faction != animal.Faction)
                return false;

            return true;
        }

        /// <summary>
        /// Implements the animal-specific validity check
        /// </summary>
        protected override bool IsValidAnimalTarget(Pawn animal, Pawn handler)
        {
            return IsValidReleaseTarget(animal, handler, false);
        }

        #endregion

        #region Target processing

        /// <summary>
        /// Updates the cache of animals that need to be released to the wild
        /// </summary>
        private void UpdateReleaseCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            // Clear outdated cache
            if (_releaseCache.ContainsKey(mapId))
                _releaseCache[mapId].Clear();
            else
                _releaseCache[mapId] = new List<Pawn>();

            // Clear reachability cache too
            if (_releaseReachabilityCache.ContainsKey(mapId))
                _releaseReachabilityCache[mapId].Clear();
            else
                _releaseReachabilityCache[mapId] = new Dictionary<Pawn, bool>();

            // Add animals with release designations
            foreach (Designation designation in map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.ReleaseAnimalToWild))
            {
                if (designation.target.Thing is Pawn animal && animal.IsNonMutantAnimal)
                {
                    _releaseCache[mapId].Add(animal);
                }
            }

            _lastReleaseCacheUpdateTick = currentTick;
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

            return ProcessCachedTargets(pawn, _releaseCache[mapId].Cast<Thing>().ToList(), forced);
        }

        /// <summary>
        /// Determines if an animal is a valid target for releasing
        /// </summary>
        private bool IsValidReleaseTarget(Pawn animal, Pawn handler, bool forced)
        {
            // CRITICAL: Don't release yourself!
            if (animal == handler)
                return false;

            // IMPORTANT: Check faction interaction validity first
            if (!Utility_JobGiverManager.IsValidFactionInteraction(animal, handler, requiresDesignator: true))
                return false;

            // Skip if no longer a valid release target
            if (!animal.IsNonMutantAnimal ||
                handler.Map.designationManager.DesignationOn(animal, DesignationDefOf.ReleaseAnimalToWild) == null)
                return false;

            // Skip if wrong faction
            if (handler.Faction != animal.Faction)
                return false;

            // Skip if in mental state or dead
            if (animal.InAggroMentalState || animal.Dead)
                return false;

            // Skip if forbidden or cannot reserve
            if (animal.IsForbidden(handler) || !handler.CanReserve(animal, 1, -1, null, forced))
                return false;

            // Check if there's a valid outside cell to release to
            IntVec3 outsideCell;
            if (!JobDriver_ReleaseAnimalToWild.TryFindClosestOutsideCell_NewTemp(
                animal.Position, animal.Map, TraverseParms.For(handler), handler, out outsideCell))
                return false;

            return true;
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return "JobGiver_ReleaseAnimalToWild_PawnControl";
        }

        #endregion
    }
}