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
        public override bool RequiresMapZoneorArea => false;

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "ReleaseAnimalsToWild";

        /// <summary>
        /// Update cache every ~4 seconds
        /// </summary>
        protected override int CacheUpdateInterval => base.CacheUpdateInterval;

        /// <summary>
        /// Distance thresholds for bucketing (15, 25, 40 tiles)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 225f, 625f, 1600f };

        /// <summary>
        /// Cache key suffix specifically for releasable animals
        /// </summary>
        private const string RELEASABLE_CACHE_SUFFIX = "_ReleasableAnimals";

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
            if (pawn?.Map == null || !pawn.Map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.ReleaseAnimalToWild))
                return null;

            if (ShouldSkip(pawn))
            {
                return null;
            }

            // Use the parent class job creation logic
            return base.TryGiveJob(pawn);
        }

        #endregion

        #region Target Selection

        /// <summary>
        /// Get animals with release designations
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null)
                yield break;

            // Get cached releasable animals from centralized cache
            var animals = GetOrCreateReleasableAnimalsCache(map);

            // Return animals as targets
            foreach (Pawn animal in animals)
            {
                if (animal != null && !animal.Dead && animal.Spawned)
                    yield return animal;
            }
        }

        /// <summary>
        /// Update the job-specific cache with animals that have release designations
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            if (map == null)
                yield break;

            // Find all animals with release designation
            List<Pawn> releasableAnimals = new List<Pawn>();

            // Get animals with release designations - most efficient approach
            foreach (Designation designation in map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.ReleaseAnimalToWild))
            {
                if (designation.target.Thing is Pawn animal && animal.IsNonMutantAnimal)
                {
                    releasableAnimals.Add(animal);
                }
            }

            // Store in the centralized cache
            StoreReleasableAnimalsCache(map, releasableAnimals);

            // Convert to Things for the base class caching system
            foreach (Pawn animal in releasableAnimals)
            {
                yield return animal;
            }
        }

        /// <summary>
        /// Gets or creates a cache of releasable animals for a specific map
        /// </summary>
        protected List<Pawn> GetOrCreateReleasableAnimalsCache(Map map)
        {
            if (map == null)
                return new List<Pawn>();

            int mapId = map.uniqueID;
            string cacheKey = this.GetType().Name + RELEASABLE_CACHE_SUFFIX;

            // Try to get cached animals from the map cache manager
            var animalCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Pawn>>(mapId);

            // Check if we need to update the cache
            int currentTick = Find.TickManager.TicksGame;
            int lastUpdateTick = Utility_MapCacheManager.GetLastCacheUpdateTick(mapId, cacheKey);

            if (currentTick - lastUpdateTick > CacheUpdateInterval ||
                !animalCache.TryGetValue(cacheKey, out List<Pawn> animals) ||
                animals == null ||
                animals.Any(a => a == null || a.Dead || !a.Spawned))
            {
                // Cache is invalid or expired, rebuild it
                animals = new List<Pawn>();

                // Find animals with release designations
                if (map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.ReleaseAnimalToWild))
                {
                    foreach (Designation designation in map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.ReleaseAnimalToWild))
                    {
                        if (designation.target.Thing is Pawn animal && animal.IsNonMutantAnimal)
                        {
                            animals.Add(animal);
                        }
                    }
                }

                // Store in the central cache
                StoreReleasableAnimalsCache(map, animals);
            }

            return animals;
        }

        /// <summary>
        /// Store a list of releasable animals in the centralized cache
        /// </summary>
        private void StoreReleasableAnimalsCache(Map map, List<Pawn> animals)
        {
            if (map == null)
                return;

            int mapId = map.uniqueID;
            string cacheKey = this.GetType().Name + RELEASABLE_CACHE_SUFFIX;
            int currentTick = Find.TickManager.TicksGame;

            var animalCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Pawn>>(mapId);
            animalCache[cacheKey] = animals;
            Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, currentTick);

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Updated releasable animals cache for {this.GetType().Name}, found {animals.Count} animals");
            }
        }

        #endregion

        #region Job Processing

        /// <summary>
        /// Creates a job for a specific animal
        /// </summary>
        protected override Job CreateJobForAnimal(Pawn pawn, Pawn animal, bool forced)
        {
            if (!IsValidReleaseTarget(animal, pawn, forced))
                return null;

            // Create job if target found
            Job job = JobMaker.MakeJob(WorkJobDef, animal);
            job.count = 1;

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to release {animal.LabelShort} to the wild");
            }

            return job;
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

        #region Reset

        /// <summary>
        /// Reset the animal-specific cache
        /// </summary>
        public override void Reset()
        {
            // Call base reset first
            base.Reset();

            // Now clear release-specific caches
            foreach (int mapId in Find.Maps.Select(map => map.uniqueID))
            {
                string cacheKey = this.GetType().Name + RELEASABLE_CACHE_SUFFIX;
                var animalCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Pawn>>(mapId);

                if (animalCache.ContainsKey(cacheKey))
                {
                    animalCache.Remove(cacheKey);
                }

                // Clear the update tick record too
                Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, -1);
            }

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Reset release caches for {this.GetType().Name}");
            }
        }

        #endregion

        #region Debug

        public override string ToString()
        {
            return "JobGiver_ReleaseAnimalToWild_PawnControl";
        }

        #endregion
    }
}