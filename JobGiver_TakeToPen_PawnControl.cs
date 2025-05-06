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
    /// Optimized using the PawnControl framework with caching and distance bucketing.
    /// </summary>
    public class JobGiver_TakeToPen_PawnControl : ThinkNode_JobGiver
    {
        // Cache system to improve performance
        private static readonly Dictionary<int, List<Pawn>> _animalCache = new Dictionary<int, List<Pawn>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 300; // Update every 5 seconds

        // Configuration options (mirrored from WorkGiver_TakeToPen)
        protected bool targetRoamingAnimals = false; // Default value
        protected bool allowUnenclosedPens = false;  // Default value
        protected RopingPriority ropingPriority = RopingPriority.Closest; // Default value

        // Cache for pen balance calculator
        private readonly Dictionary<Map, AnimalPenBalanceCalculator> _balanceCalculatorsCached = new Dictionary<Map, AnimalPenBalanceCalculator>();
        private int _balanceCalculatorsCachedTick = -999;
        private Pawn _balanceCalculatorsCachedForPawn;

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        public override float GetPriority(Pawn pawn)
        {
            // Animal handling is an important task
            return 5.7f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<Pawn>(
                pawn,
                "Handling", // Use the Handling work tag
                (p, forced) => {
                    // Update animal cache
                    UpdateAnimalCache(p.Map);
                    
                    // Find and create a job for handling animals
                    return TryCreateTakeToPenJob(p, forced);
                },
                debugJobDesc: "take animals to pen");
        }

        /// <summary>
        /// Updates the cache of animals that need to be taken to pens
        /// </summary>
        private void UpdateAnimalCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_animalCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_animalCache.ContainsKey(mapId))
                    _animalCache[mapId].Clear();
                else
                    _animalCache[mapId] = new List<Pawn>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Pawn, bool>();

                // Find all animals that can be taken to pens
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

                    // Add this animal to the cache
                    _animalCache[mapId].Add(animal);
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Create a job to take an animal to a pen
        /// </summary>
        private Job TryCreateTakeToPenJob(Pawn pawn, bool forced = false)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_animalCache.ContainsKey(mapId) || _animalCache[mapId].Count == 0)
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

            // Use distance bucketing to find the closest animal
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                _animalCache[mapId],
                (animal) => (animal.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Find the best animal to handle
            Pawn targetAnimal = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (animal, p) => {
                    // IMPORTANT: Check faction interaction validity first
                    if (!Utility_JobGiverManager.IsValidFactionInteraction(animal, p, requiresDesignator: true))
                        return false;

                    // Skip if animal is forbidden or in forbidden area
                    if (animal.IsForbidden(p) || animal.Position.IsForbidden(p))
                        return false;

                    // Check if this animal needs to be taken to a pen
                    string failReason;
                    CompAnimalPenMarker targetPen = AnimalPenUtility.GetPenAnimalShouldBeTakenTo(
                        p, animal, out failReason, forced, true,
                        mode: ropingPriority, balanceCalculator: balanceCalculator);

                    if (targetPen != null)
                    {
                        // Try to make a job for this pen
                        Job job = WorkGiver_TakeToPen.MakeJob(
                            p, animal, targetPen, false, ropingPriority, out failReason);
                        if (job != null)
                            return true;
                    }

                    // Try hitching post if no pen found
                    Building hitchingPost = AnimalPenUtility.GetHitchingPostAnimalShouldBeTakenTo(
                        p, animal, out failReason, forced);
                    if (hitchingPost != null)
                        return true;

                    // Try unenclosed pens if allowed
                    if (allowUnenclosedPens)
                    {
                        targetPen = AnimalPenUtility.GetPenAnimalShouldBeTakenTo(
                            p, animal, out failReason, forced, true, true,
                            mode: ropingPriority, balanceCalculator: balanceCalculator);
                        if (targetPen != null)
                        {
                            // Try to make a job for this unenclosed pen
                            Job job = WorkGiver_TakeToPen.MakeJob(
                                p, animal, targetPen, true, ropingPriority, out failReason);
                            if (job != null)
                                return true;
                        }
                    }

                    // Check for unnecessary roping
                    if (AnimalPenUtility.IsUnnecessarilyRoped(animal))
                    {
                        // Check if we can unrope the animal
                        if (AnimalPenUtility.RopeAttachmentInteractionCell(p, animal) != IntVec3.Invalid &&
                            p.CanReserve((LocalTargetInfo)animal, 1, -1, null, forced) &&
                            p.CanReach((LocalTargetInfo)animal, PathEndMode.Touch, Danger.Deadly))
                        {
                            return true;
                        }
                    }

                    return false;
                },
                _reachabilityCache
            );

            // Create the job based on what we found
            if (targetAnimal != null)
            {
                // Check for a pen first
                string failReason;
                CompAnimalPenMarker targetPen = AnimalPenUtility.GetPenAnimalShouldBeTakenTo(
                    pawn, targetAnimal, out failReason, forced, true,
                    mode: ropingPriority, balanceCalculator: balanceCalculator);

                if (targetPen != null)
                {
                    Job job = WorkGiver_TakeToPen.MakeJob(
                        pawn, targetAnimal, targetPen, false, ropingPriority, out failReason);
                    if (job != null)
                    {
                        if (Prefs.DevMode)
                            Log.Message($"[PawnControl] {pawn.LabelShort} created job to take {targetAnimal.LabelShort} to pen {targetPen.parent.Label}");
                        return job;
                    }
                }

                // Try hitching post if no pen found
                Building hitchingPost = AnimalPenUtility.GetHitchingPostAnimalShouldBeTakenTo(
                    pawn, targetAnimal, out failReason, forced);
                if (hitchingPost != null)
                {
                    Job hitchJob = JobMaker.MakeJob(JobDefOf.RopeRoamerToHitchingPost, 
                        (LocalTargetInfo)targetAnimal, (LocalTargetInfo)hitchingPost);
                    
                    if (Prefs.DevMode)
                        Log.Message($"[PawnControl] {pawn.LabelShort} created job to take {targetAnimal.LabelShort} to hitching post");
                    
                    return hitchJob;
                }

                // Try unenclosed pens if allowed
                if (allowUnenclosedPens)
                {
                    targetPen = AnimalPenUtility.GetPenAnimalShouldBeTakenTo(
                        pawn, targetAnimal, out failReason, forced, true, true,
                        mode: ropingPriority, balanceCalculator: balanceCalculator);
                    
                    if (targetPen != null)
                    {
                        Job job = WorkGiver_TakeToPen.MakeJob(
                            pawn, targetAnimal, targetPen, true, ropingPriority, out failReason);
                        
                        if (job != null)
                        {
                            if (Prefs.DevMode)
                                Log.Message($"[PawnControl] {pawn.LabelShort} created job to take {targetAnimal.LabelShort} to unenclosed pen {targetPen.parent.Label}");
                            
                            return job;
                        }
                    }
                }

                // Check for unnecessary roping
                if (AnimalPenUtility.IsUnnecessarilyRoped(targetAnimal) &&
                    AnimalPenUtility.RopeAttachmentInteractionCell(pawn, targetAnimal) != IntVec3.Invalid)
                {
                    Job unropeJob = JobMaker.MakeJob(JobDefOf.Unrope, (LocalTargetInfo)targetAnimal);
                    
                    if (Prefs.DevMode)
                        Log.Message($"[PawnControl] {pawn.LabelShort} created job to unrope {targetAnimal.LabelShort}");
                    
                    return unropeJob;
                }
            }

            return null;
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_animalCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_TakeToPen_PawnControl";
        }
    }
}