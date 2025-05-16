using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to take animals to pens.
    /// Uses the Handling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Handling_TakeToPen_PawnControl : JobGiver_Handling_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        public override bool RequiresMapZoneorArea => true;

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "TakeToPen";

        /// <summary>
        /// Keep the existing WorkTag from the original implementation
        /// </summary>
        public override string WorkTag => "Handling";

        /// <summary>
        /// Update cache every 5 seconds
        /// </summary>
        protected override int CacheUpdateInterval => base.CacheUpdateInterval;

        /// <summary>
        /// Distance thresholds for bucketing (15, 25, 40 tiles)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 225f, 625f, 1600f };

        /// <summary>
        /// Configuration options (mirrored from WorkGiver_TakeToPen)
        /// </summary>
        protected bool targetRoamingAnimals = false; // Default value
        protected bool allowUnenclosedPens = false;  // Default value
        protected RopingPriority ropingPriority = RopingPriority.Closest; // Default value

        /// <summary>
        /// Cache key suffix specifically for pen-eligible animals
        /// </summary>
        private const string PEN_ANIMALS_CACHE_SUFFIX = "_PenAnimals";

        /// <summary>
        /// Define the base priority for this job
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            // Animal handling is an important task
            return 5.7f;
        }

        #endregion

        #region Caching

        // Cache for pen balance calculator
        private readonly Dictionary<Map, AnimalPenBalanceCalculator> _balanceCalculatorsCached = new Dictionary<Map, AnimalPenBalanceCalculator>();
        private int _balanceCalculatorsCachedTick = -999;
        private Pawn _balanceCalculatorsCachedForPawn;

        #endregion

        #region Core flow

        /// <summary>
        /// Override TryGiveJob to implement pen-specific logic
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (pawn?.Map == null)
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
        /// Get animals that need to be taken to pens on the given map
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null)
                yield break;

            // Get cached pen animals from centralized cache
            var animals = GetOrCreatePenAnimalsCache(map);

            // Return animals as targets
            foreach (Pawn animal in animals)
            {
                if (animal != null && !animal.Dead && animal.Spawned)
                    yield return animal;
            }
        }

        /// <summary>
        /// Update the job-specific cache with animals that need to be taken to pens
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            if (map == null)
                yield break;

            // Find all animals that can be taken to pens
            List<Pawn> penAnimals = new List<Pawn>();

            // Add player faction animals
            foreach (Pawn animal in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
            {
                // Skip if not an animal
                if (!animal.IsNonMutantAnimal)
                    continue;

                // Handle roaming animals based on config
                bool isRoaming = animal.MentalStateDef == MentalStateDefOf.Roaming;
                if (targetRoamingAnimals && !isRoaming)
                    continue;

                // Skip animals with mental states unless roaming (if configured)
                if (!targetRoamingAnimals && !isRoaming && animal.MentalStateDef != null)
                    continue;

                // Skip animals marked for release to wild
                if (map.designationManager.DesignationOn(animal, DesignationDefOf.ReleaseAnimalToWild) != null)
                    continue;

                // Add this animal to the list
                penAnimals.Add(animal);
            }

            // Store in the centralized cache
            StorePenAnimalsCache(map, penAnimals);

            // Convert to Things for the base class caching system
            foreach (Pawn animal in penAnimals)
            {
                yield return animal;
            }
        }

        /// <summary>
        /// Gets or creates a cache of animals that need to be taken to pens for a specific map
        /// </summary>
        protected List<Pawn> GetOrCreatePenAnimalsCache(Map map)
        {
            if (map == null)
                return new List<Pawn>();

            int mapId = map.uniqueID;
            string cacheKey = this.GetType().Name + PEN_ANIMALS_CACHE_SUFFIX;

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

                // Add player faction animals
                foreach (Pawn animal in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
                {
                    // Skip if not an animal
                    if (!animal.IsNonMutantAnimal)
                        continue;

                    // Handle roaming animals based on config
                    bool isRoaming = animal.MentalStateDef == MentalStateDefOf.Roaming;
                    if (targetRoamingAnimals && !isRoaming)
                        continue;

                    // Skip animals with mental states unless roaming (if configured)
                    if (!targetRoamingAnimals && !isRoaming && animal.MentalStateDef != null)
                        continue;

                    // Skip animals marked for release to wild
                    if (map.designationManager.DesignationOn(animal, DesignationDefOf.ReleaseAnimalToWild) != null)
                        continue;

                    // Add this animal to the list
                    animals.Add(animal);
                }

                // Store in the central cache
                StorePenAnimalsCache(map, animals);
            }

            return animals;
        }

        /// <summary>
        /// Store a list of pen-eligible animals in the centralized cache
        /// </summary>
        private void StorePenAnimalsCache(Map map, List<Pawn> animals)
        {
            if (map == null)
                return;

            int mapId = map.uniqueID;
            string cacheKey = this.GetType().Name + PEN_ANIMALS_CACHE_SUFFIX;
            int currentTick = Find.TickManager.TicksGame;

            var animalCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<Pawn>>(mapId);
            animalCache[cacheKey] = animals;
            Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, currentTick);

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Updated pen animals cache for {this.GetType().Name}, found {animals.Count} animals");
            }
        }

        #endregion

        #region Job Processing

        /// <summary>
        /// Processes cached targets to find a valid job
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            if (targets == null || targets.Count == 0 || pawn?.Map == null)
                return null;

            // Convert to pawns (we know they're all animals)
            List<Pawn> animals = targets.OfType<Pawn>().ToList();
            if (animals.Count == 0)
                return null;

            // Update balance calculators cache if needed
            if (_balanceCalculatorsCachedTick != Find.TickManager.TicksGame || _balanceCalculatorsCachedForPawn != pawn)
            {
                foreach (KeyValuePair<Map, AnimalPenBalanceCalculator> keyValuePair in _balanceCalculatorsCached)
                    keyValuePair.Value.MarkDirty();
                _balanceCalculatorsCachedTick = Find.TickManager.TicksGame;
                _balanceCalculatorsCachedForPawn = pawn;
            }

            // Get or create the balance calculator
            AnimalPenBalanceCalculator balanceCalculator;
            if (_balanceCalculatorsCached.ContainsKey(pawn.Map))
            {
                balanceCalculator = _balanceCalculatorsCached[pawn.Map];
            }
            else
            {
                balanceCalculator = new AnimalPenBalanceCalculator(pawn.Map, true);
                _balanceCalculatorsCached.Add(pawn.Map, balanceCalculator);
            }

            // Use the bucketing system to find the closest valid animal
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                animals,
                (animal) => (animal.Position - pawn.Position).LengthHorizontalSquared,
                DistanceThresholds);

            // Get reachability cache from central cache system with the correct type structure
            var reachabilityCache = Utility_MapCacheManager.GetOrCreateMapCache<int, Dictionary<Pawn, bool>>(pawn.Map.uniqueID);

            // Create the inner dictionary if it doesn't exist
            int handlerId = pawn.thingIDNumber;
            if (!reachabilityCache.ContainsKey(handlerId))
                reachabilityCache[handlerId] = new Dictionary<Pawn, bool>();

            // Find the best animal to handle with the correct cache structure
            Pawn targetAnimal = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (animal, p) => NeedsPenHandling(animal, p, forced, balanceCalculator),
                reachabilityCache);

            // Create job based on what we found
            if (targetAnimal != null)
            {
                return CreateJobForAnimal(pawn, targetAnimal, forced, balanceCalculator);
            }

            return null;
        }

        /// <summary>
        /// Creates a job for a specific animal
        /// </summary>
        protected override Job CreateJobForAnimal(Pawn pawn, Pawn animal, bool forced)
        {
            // Update balance calculators cache if needed
            if (_balanceCalculatorsCachedTick != Find.TickManager.TicksGame || _balanceCalculatorsCachedForPawn != pawn)
            {
                foreach (KeyValuePair<Map, AnimalPenBalanceCalculator> keyValuePair in _balanceCalculatorsCached)
                    keyValuePair.Value.MarkDirty();
                _balanceCalculatorsCachedTick = Find.TickManager.TicksGame;
                _balanceCalculatorsCachedForPawn = pawn;
            }

            // Get or create the balance calculator
            AnimalPenBalanceCalculator balanceCalculator;
            if (_balanceCalculatorsCached.ContainsKey(pawn.Map))
            {
                balanceCalculator = _balanceCalculatorsCached[pawn.Map];
            }
            else
            {
                balanceCalculator = new AnimalPenBalanceCalculator(pawn.Map, true);
                _balanceCalculatorsCached.Add(pawn.Map, balanceCalculator);
            }

            return CreateJobForAnimal(pawn, animal, forced, balanceCalculator);
        }

        /// <summary>
        /// Creates a job for a specific animal with the provided balance calculator
        /// </summary>
        private Job CreateJobForAnimal(Pawn pawn, Pawn animal, bool forced, AnimalPenBalanceCalculator balanceCalculator)
        {
            if (!IsValidAnimalTarget(animal, pawn))
                return null;

            string failReason;

            // Check for a pen first
            CompAnimalPenMarker targetPen = AnimalPenUtility.GetPenAnimalShouldBeTakenTo(
                pawn, animal, out failReason, forced, true,
                mode: ropingPriority, balanceCalculator: balanceCalculator);

            if (targetPen != null)
            {
                Job job = WorkGiver_TakeToPen.MakeJob(
                    pawn, animal, targetPen, false, ropingPriority, out failReason);
                if (job != null)
                {
                    if (Prefs.DevMode)
                    {
                        Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to take {animal.LabelShort} to pen {targetPen.parent.Label}");
                    }
                    return job;
                }
            }

            // Try hitching post if no pen found
            Building hitchingPost = AnimalPenUtility.GetHitchingPostAnimalShouldBeTakenTo(
                pawn, animal, out failReason, forced);
            if (hitchingPost != null)
            {
                Job hitchJob = JobMaker.MakeJob(JobDefOf.RopeRoamerToHitchingPost,
                    (LocalTargetInfo)animal, (LocalTargetInfo)hitchingPost);
                if (Prefs.DevMode)
                {
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to take {animal.LabelShort} to hitching post");
                }
                return hitchJob;
            }

            // Try unenclosed pens if allowed
            if (allowUnenclosedPens)
            {
                targetPen = AnimalPenUtility.GetPenAnimalShouldBeTakenTo(
                    pawn, animal, out failReason, forced, true, true,
                    mode: ropingPriority, balanceCalculator: balanceCalculator);

                if (targetPen != null)
                {
                    Job job = WorkGiver_TakeToPen.MakeJob(
                        pawn, animal, targetPen, true, ropingPriority, out failReason);

                    if (job != null)
                    {
                        if (Prefs.DevMode)
                        {
                            Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to take {animal.LabelShort} to unenclosed pen {targetPen.parent.Label}");
                        }
                        return job;
                    }
                }
            }

            // Check for unnecessary roping
            if (AnimalPenUtility.IsUnnecessarilyRoped(animal) &&
                AnimalPenUtility.RopeAttachmentInteractionCell(pawn, animal) != IntVec3.Invalid)
            {
                Job unropeJob = JobMaker.MakeJob(JobDefOf.Unrope, (LocalTargetInfo)animal);
                if (Prefs.DevMode)
                {
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to unrope {animal.LabelShort}");
                }
                return unropeJob;
            }

            return null;
        }

        /// <summary>
        /// Override the base class method for animal handling
        /// </summary>
        protected override bool CanHandleAnimal(Pawn animal, Pawn handler)
        {
            // Basic checks
            if (animal == null || handler == null || !animal.IsNonMutantAnimal)
                return false;

            // Basic hauling checks
            if (animal.IsForbidden(handler) || animal.Position.IsForbidden(handler))
                return false;

            // Check faction interaction validity
            return Utility_JobGiverManager.IsValidFactionInteraction(animal, handler, requiresDesignator: true);
        }

        /// <summary>
        /// Implements the animal-specific validity check
        /// </summary>
        protected override bool IsValidAnimalTarget(Pawn animal, Pawn handler)
        {
            // Check basic validity first
            if (animal == null || handler == null || !animal.IsNonMutantAnimal)
                return false;

            // Basic hauling checks
            if (animal.IsForbidden(handler) || animal.Position.IsForbidden(handler))
                return false;

            // Handle roaming animals based on config
            bool isRoaming = animal.MentalStateDef == MentalStateDefOf.Roaming;
            if (targetRoamingAnimals && !isRoaming)
                return false;

            // Skip animals with mental states unless roaming (if configured)
            if (!targetRoamingAnimals && !isRoaming && animal.MentalStateDef != null)
                return false;

            // Skip animals marked for release to wild
            if (handler.Map.designationManager.DesignationOn(animal, DesignationDefOf.ReleaseAnimalToWild) != null)
                return false;

            // Check faction interaction validity
            return Utility_JobGiverManager.IsValidFactionInteraction(animal, handler, requiresDesignator: true);
        }

        /// <summary>
        /// Determines if an animal needs to be taken to a pen or otherwise handled
        /// </summary>
        private bool NeedsPenHandling(Pawn animal, Pawn handler, bool forced, AnimalPenBalanceCalculator balanceCalculator)
        {
            // Use the IsValidAnimalTarget method to check basic validity
            if (!IsValidAnimalTarget(animal, handler))
                return false;

            // Check if this animal needs to be taken to a pen
            string failReason;
            CompAnimalPenMarker targetPen = AnimalPenUtility.GetPenAnimalShouldBeTakenTo(
                handler, animal, out failReason, forced, true,
                mode: ropingPriority, balanceCalculator: balanceCalculator);

            if (targetPen != null)
            {
                // Try to make a job for this pen
                Job job = WorkGiver_TakeToPen.MakeJob(
                    handler, animal, targetPen, false, ropingPriority, out failReason);
                if (job != null)
                    return true;
            }

            // Try hitching post if no pen found
            Building hitchingPost = AnimalPenUtility.GetHitchingPostAnimalShouldBeTakenTo(
                handler, animal, out failReason, forced);
            if (hitchingPost != null)
                return true;

            // Try unenclosed pens if allowed
            if (allowUnenclosedPens)
            {
                targetPen = AnimalPenUtility.GetPenAnimalShouldBeTakenTo(
                    handler, animal, out failReason, forced, true, true,
                    mode: ropingPriority, balanceCalculator: balanceCalculator);
                if (targetPen != null)
                {
                    // Try to make a job for this unenclosed pen
                    Job job = WorkGiver_TakeToPen.MakeJob(
                        handler, animal, targetPen, true, ropingPriority, out failReason);
                    if (job != null)
                        return true;
                }
            }

            // Check for unnecessary roping
            if (AnimalPenUtility.IsUnnecessarilyRoped(animal))
            {
                // Check if we can unrope the animal
                if (AnimalPenUtility.RopeAttachmentInteractionCell(handler, animal) != IntVec3.Invalid &&
                    handler.CanReserve((LocalTargetInfo)animal, 1, -1, null, forced) &&
                    handler.CanReach((LocalTargetInfo)animal, PathEndMode.Touch, Danger.Deadly))
                {
                    return true;
                }
            }

            return false;
        }

        #endregion

        #region Reset

        /// <summary>
        /// Reset the cache - implements IResettableCache
        /// </summary>
        public override void Reset()
        {
            // Call base reset first
            base.Reset();

            // Clear the balance calculators
            _balanceCalculatorsCached.Clear();
            _balanceCalculatorsCachedTick = -999;
            _balanceCalculatorsCachedForPawn = null;

            // Now clear pen-specific caches
            foreach (int mapId in Find.Maps.Select(map => map.uniqueID))
            {
                string cacheKey = this.GetType().Name + PEN_ANIMALS_CACHE_SUFFIX;
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
                Utility_DebugManager.LogNormal($"Reset pen animals cache for {this.GetType().Name}");
            }
        }

        #endregion

        #region Debug

        public override string ToString()
        {
            return "JobGiver_TakeToPen_PawnControl";
        }

        #endregion
    }
}