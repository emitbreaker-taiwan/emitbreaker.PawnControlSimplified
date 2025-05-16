using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to slaughter animals marked for slaughter.
    /// Uses the Handling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Handling_Slaughter_PawnControl : JobGiver_Handling_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        public override bool RequiresMapZoneorArea => false;

        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.Slaughter;

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "Slaughter";

        /// <summary>
        /// Update cache every ~4 seconds
        /// </summary>
        protected override int CacheUpdateInterval => base.CacheUpdateInterval;

        /// <summary>
        /// Distance thresholds for bucketing (15, 25, 40 tiles)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 225f, 625f, 1600f };

        /// <summary>
        /// Cache key suffix specifically for slaughterable animals
        /// </summary>
        private const string SLAUGHTERABLE_CACHE_SUFFIX = "_SlaughterableAnimals";

        #endregion

        #region Core flow

        protected override float GetBasePriority(string workTag)
        {
            // Slaughtering is a moderate priority task
            return 5.4f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Quick early exit if slaughtering is not possible
            if (pawn?.Map == null || pawn.WorkTagIsDisabled(WorkTags.Violent))
                return null;

            if (ShouldSkip(pawn))
            {
                return null;
            }

            // Quick early exit if no animals are marked for slaughter
            if (!pawn.Map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.Slaughter) &&
                pawn.Map.autoSlaughterManager.AnimalsToSlaughter.Count == 0)
                return null;

            // Use the parent class job creation logic
            return base.TryGiveJob(pawn);
        }

        #endregion

        #region Target Selection

        /// <summary>
        /// Get animals marked for slaughter on the given map
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null)
                yield break;

            // Get cached slaughterable animals from centralized cache
            var animals = GetOrCreateSlaughterableAnimalsCache(map);

            // Return animals as targets
            foreach (Pawn animal in animals)
            {
                if (animal != null && !animal.Dead && animal.Spawned)
                    yield return animal;
            }
        }

        /// <summary>
        /// Update the job-specific cache with animals that need to be slaughtered
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            if (map == null)
                yield break;

            // Find all animals marked for slaughter
            List<Pawn> slaughterableAnimals = new List<Pawn>();

            // Add animals with slaughter designations
            foreach (Designation designation in map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Slaughter))
            {
                if (designation.target.Thing is Pawn animal && animal.IsNonMutantAnimal)
                {
                    slaughterableAnimals.Add(animal);
                }
            }

            // Add auto-slaughter manager animals
            foreach (Pawn animal in map.autoSlaughterManager.AnimalsToSlaughter)
            {
                if (animal != null && animal.Spawned && animal.IsNonMutantAnimal && !slaughterableAnimals.Contains(animal))
                {
                    slaughterableAnimals.Add(animal);
                }
            }

            // Store in the centralized cache
            StoreSlaughterableAnimalsCache(map, slaughterableAnimals);

            // Convert to Things for the base class caching system
            foreach (Pawn animal in slaughterableAnimals)
            {
                yield return animal;
            }
        }

        /// <summary>
        /// Gets or creates a cache of slaughterable animals for a specific map
        /// </summary>
        protected List<Pawn> GetOrCreateSlaughterableAnimalsCache(Map map)
        {
            if (map == null)
                return new List<Pawn>();

            int mapId = map.uniqueID;
            string cacheKey = this.GetType().Name + SLAUGHTERABLE_CACHE_SUFFIX;

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

                // Add animals with slaughter designations
                foreach (Designation designation in map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Slaughter))
                {
                    if (designation.target.Thing is Pawn animal && animal.IsNonMutantAnimal)
                    {
                        animals.Add(animal);
                    }
                }

                // Add auto-slaughter manager animals
                foreach (Pawn animal in map.autoSlaughterManager.AnimalsToSlaughter)
                {
                    if (animal != null && animal.Spawned && animal.IsNonMutantAnimal && !animals.Contains(animal))
                    {
                        animals.Add(animal);
                    }
                }

                // Store in the central cache
                StoreSlaughterableAnimalsCache(map, animals);
            }

            return animals;
        }

        /// <summary>
        /// Store a list of slaughterable animals in the centralized cache
        /// </summary>
        private void StoreSlaughterableAnimalsCache(Map map, List<Pawn> animals)
        {
            if (map == null)
                return;

            int mapId = map.uniqueID;
            string cacheKey = this.GetType().Name + SLAUGHTERABLE_CACHE_SUFFIX;
            int currentTick = Find.TickManager.TicksGame;

            var animalCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Pawn>>(mapId);
            animalCache[cacheKey] = animals;
            Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, currentTick);

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Updated slaughterable animals cache for {this.GetType().Name}, found {animals.Count} animals");
            }
        }

        #endregion

        #region Job Processing

        /// <summary>
        /// Creates a job for a specific animal
        /// </summary>
        protected override Job CreateJobForAnimal(Pawn pawn, Pawn animal, bool forced)
        {
            if (!IsValidSlaughterTarget(animal, pawn, forced))
                return null;

            // Create job if target found
            Job job = JobMaker.MakeJob(WorkJobDef, animal);

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to slaughter {animal.LabelShort}");
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

            // Must be a valid animal marked for slaughter
            if (!animal.IsNonMutantAnimal || !animal.ShouldBeSlaughtered())
                return false;

            // Must be same faction
            if (handler.Faction != animal.Faction)
                return false;

            // Handler must not have violent work disabled
            if (handler.WorkTagIsDisabled(WorkTags.Violent))
                return false;

            return true;
        }

        /// <summary>
        /// Implements the animal-specific validity check
        /// </summary>
        protected override bool IsValidAnimalTarget(Pawn animal, Pawn handler)
        {
            return IsValidSlaughterTarget(animal, handler, false);
        }

        /// <summary>
        /// Determines if an animal is a valid target for slaughtering
        /// </summary>
        private bool IsValidSlaughterTarget(Pawn animal, Pawn handler, bool forced)
        {
            // CRITICAL: Don't slaughter yourself!
            if (animal == handler)
                return false;

            // IMPORTANT: Check faction interaction validity first
            if (!Utility_JobGiverManager.IsValidFactionInteraction(animal, handler, requiresDesignator: true))
                return false;

            // Skip if no longer a valid slaughter target
            if (!animal.IsNonMutantAnimal || !animal.ShouldBeSlaughtered())
                return false;

            // Skip if wrong faction
            if (handler.Faction != animal.Faction)
                return false;

            // Skip if in mental state
            if (animal.InAggroMentalState)
                return false;

            // Skip if forbidden or cannot reserve
            if (animal.IsForbidden(handler) || !handler.CanReserve(animal, 1, -1, null, forced))
                return false;

            // Check ideological restrictions
            if (ModsConfig.IdeologyActive)
            {
                if (!new HistoryEvent(HistoryEventDefOf.SlaughteredAnimal, handler.Named(HistoryEventArgsNames.Doer))
                    .Notify_PawnAboutToDo_Job())
                    return false;

                if (HistoryEventUtility.IsKillingInnocentAnimal(handler, animal) &&
                    !new HistoryEvent(HistoryEventDefOf.KilledInnocentAnimal, handler.Named(HistoryEventArgsNames.Doer))
                    .Notify_PawnAboutToDo_Job())
                    return false;

                if (handler.Ideo != null && handler.Ideo.IsVeneratedAnimal(animal) &&
                    !new HistoryEvent(HistoryEventDefOf.SlaughteredVeneratedAnimal, handler.Named(HistoryEventArgsNames.Doer))
                    .Notify_PawnAboutToDo_Job())
                    return false;
            }

            return true;
        }

        #endregion

        #region Reset

        /// <summary>
        /// Reset the slaughter-specific cache
        /// </summary>
        public override void Reset()
        {
            // Call base reset first
            base.Reset();

            // Now clear slaughter-specific caches
            foreach (int mapId in Find.Maps.Select(map => map.uniqueID))
            {
                string cacheKey = this.GetType().Name + SLAUGHTERABLE_CACHE_SUFFIX;
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
                Utility_DebugManager.LogNormal($"Reset slaughter caches for {this.GetType().Name}");
            }
        }

        #endregion

        #region Debug

        public override string ToString()
        {
            return "JobGiver_Slaughter_PawnControl";
        }

        #endregion
    }
}