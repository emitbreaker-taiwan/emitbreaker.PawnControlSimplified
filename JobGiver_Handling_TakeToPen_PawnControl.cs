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

        protected override string WorkTag => "Handling";
        protected override int CacheUpdateInterval => 300; // Update every 5 seconds
        protected override float[] DistanceThresholds => new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        // Configuration options (mirrored from WorkGiver_TakeToPen)
        protected bool targetRoamingAnimals = false; // Default value
        protected bool allowUnenclosedPens = false;  // Default value
        protected RopingPriority ropingPriority = RopingPriority.Closest; // Default value

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

        #region Overrides

        /// <summary>
        /// Override TryGiveJob to implement pen-specific logic
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<Pawn>(
                pawn,
                WorkTag,
                (p, forced) => {
                    // Update animal cache
                    UpdateAnimalCache(p.Map);

                    // Find and create a job for handling animals
                    return TryCreateTakeToPenJob(p, forced);
                },
                debugJobDesc: "take animals to pen");
        }

        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // Implementing required method but we'll use our custom cache instead
            // because we only want to target pawns (animals)
            return Enumerable.Empty<Thing>();
        }

        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // We're using our custom TryCreateTakeToPenJob method instead
            return null;
        }

        protected override bool CanHandleAnimal(Pawn animal, Pawn handler)
        {
            // Override for animal-specific logic
            if (animal == null || !animal.IsNonMutantAnimal)
                return false;

            // Basic hauling checks
            if (animal.IsForbidden(handler) || animal.Position.IsForbidden(handler))
                return false;

            // Check faction interaction validity
            return Utility_JobGiverManager.IsValidFactionInteraction(animal, handler, requiresDesignator: true);
        }

        /// <summary>
        /// Implementation of IsValidAnimalTarget from the parent class
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

        #endregion

        #region Animal handling methods

        /// <summary>
        /// Updates the cache of animals that need to be taken to pens
        /// </summary>
        private void UpdateAnimalCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > GetLastCacheUpdateTick(mapId) + CacheUpdateInterval ||
                !_animalCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_animalCache.ContainsKey(mapId))
                    _animalCache[mapId].Clear();
                else
                    _animalCache[mapId] = new List<Pawn>();

                // Clear reachability cache too
                if (_animalReachabilityCache.ContainsKey(mapId))
                    _animalReachabilityCache[mapId].Clear();
                else
                    _animalReachabilityCache[mapId] = new Dictionary<Pawn, bool>();

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

                // Update the last check time
                UpdateLastCacheUpdateTick(mapId, currentTick);
            }
        }

        /// <summary>
        /// Gets the last cache update tick for a map
        /// </summary>
        private int GetLastCacheUpdateTick(int mapId)
        {
            if (!_lastHandlingCacheUpdate.TryGetValue(mapId, out int tick))
                return -999;
            return tick;
        }

        /// <summary>
        /// Updates the last cache update tick for a map
        /// </summary>
        private void UpdateLastCacheUpdateTick(int mapId, int tick)
        {
            _lastHandlingCacheUpdate[mapId] = tick;
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
                DistanceThresholds
            );

            // Find the best animal to handle
            Pawn targetAnimal = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (animal, p) => {
                    // Use the IsValidAnimalTarget method to check basic validity
                    if (!IsValidAnimalTarget(animal, p))
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
                _animalReachabilityCache
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
                        Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to take {targetAnimal.LabelShort} to pen {targetPen.parent.Label}");
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
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to take {targetAnimal.LabelShort} to hitching post");
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
                            Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to take {targetAnimal.LabelShort} to unenclosed pen {targetPen.parent.Label}");
                            return job;
                        }
                    }
                }

                // Check for unnecessary roping
                if (AnimalPenUtility.IsUnnecessarilyRoped(targetAnimal) &&
                    AnimalPenUtility.RopeAttachmentInteractionCell(pawn, targetAnimal) != IntVec3.Invalid)
                {
                    Job unropeJob = JobMaker.MakeJob(JobDefOf.Unrope, (LocalTargetInfo)targetAnimal);
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to unrope {targetAnimal.LabelShort}");
                    return unropeJob;
                }
            }

            return null;
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return "JobGiver_TakeToPen_PawnControl";
        }

        #endregion
    }
}