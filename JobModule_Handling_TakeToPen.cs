using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for taking animals to pens or hitching spots
    /// </summary>
    public class JobModule_Handling_TakeToPen : JobModule_Handling_InteractAnimal
    {
        public override string UniqueID => "TakeToPen";
        public override float Priority => 4.5f;
        public override string Category => "AnimalHandling";
        public override int CacheUpdateInterval => 300; // Update every 5 seconds

        // Configuration options (mirrored from WorkGiver_TakeToPen)
        protected bool targetRoamingAnimals = false;
        protected bool allowUnenclosedPens = false;
        protected RopingPriority ropingPriority = RopingPriority.Closest;

        // Static caches for map-specific data persistence
        private static readonly Dictionary<int, List<Pawn>> _penableAnimalsCache = new Dictionary<int, List<Pawn>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static int _lastUpdateCacheTick = -999;

        // Cache for pen balance calculator
        private readonly Dictionary<Map, AnimalPenBalanceCalculator> _balanceCalculatorsCached = new Dictionary<Map, AnimalPenBalanceCalculator>();
        private int _balanceCalculatorsCachedTick = -999;
        private Pawn _balanceCalculatorsCachedForPawn;

        // Translation strings for rope-specific errors
        private static string CantRopeAnimalCantTouchTrans;
        private static string CantRopeAnimalNoSpaceTrans;
        private static string CantRopeAnimalMentalStateTrans;
        private static string CannotPrioritizeForbiddenTrans;

        /// <summary>
        /// Constructor to initialize animal interaction settings
        /// </summary>
        public JobModule_Handling_TakeToPen()
        {
            // Initialize interaction flags from parent class
            canInteractWhileSleeping = true; // Can take animals to pens while they're sleeping
            canInteractWhileRoaming = true;  // Can specifically work with roaming animals
        }

        /// <summary>
        /// Reset static translation strings
        /// </summary>
        public override void ResetStaticData()
        {
            base.ResetStaticData();

            CantRopeAnimalCantTouchTrans = "CantRopeAnimalCantTouch".Translate();
            CantRopeAnimalNoSpaceTrans = "CantRopeAnimalNoSpace".Translate();
            CantRopeAnimalMentalStateTrans = "CantRopeAnimalMentalState".Translate();
            CannotPrioritizeForbiddenTrans = "CannotPrioritizeForbiddenOutsideAllowedArea".Translate();

            // Clear caches
            Utility_CacheManager.ResetJobGiverCache(_penableAnimalsCache, _reachabilityCache);

            foreach (var calculators in _balanceCalculatorsCached.Values)
            {
                calculators.MarkDirty();
            }
            _balanceCalculatorsCached.Clear();

            _lastUpdateCacheTick = -999;
            _balanceCalculatorsCachedTick = -999;
        }

        /// <summary>
        /// Determine if an animal should be processed for taking to pen
        /// </summary>
        public override bool ShouldProcessAnimal(Pawn animal, Map map)
        {
            try
            {
                if (animal == null || !animal.Spawned || animal.Dead || !animal.IsNonMutantAnimal)
                    return false;

                // Check if it's a valid animal for taking to pen
                bool isRoaming = animal.MentalStateDef == MentalStateDefOf.Roaming;

                // Handle roaming animals based on config
                if (targetRoamingAnimals && !isRoaming)
                    return false;

                // Skip animals with mental states unless roaming (if configured)
                if (!targetRoamingAnimals && !isRoaming && animal.MentalStateDef != null)
                    return false;

                // Skip animals that are from player faction but marked for release to wild
                if (animal.Faction == Faction.OfPlayer &&
                    map.designationManager.DesignationOn(animal, DesignationDefOf.ReleaseAnimalToWild) != null)
                    return false;

                // Animal needs to be from player faction
                if (animal.Faction != Faction.OfPlayer)
                    return false;

                return true;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in ShouldProcessAnimal for taking to pen: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Update the cache of animals that need to be taken to pens
        /// </summary>
        public override void UpdateCache(Map map, List<Pawn> targetCache)
        {
            base.UpdateCache(map, targetCache);

            if (map == null) return;
            int mapId = map.uniqueID;
            bool hasTargets = false;
            int currentTick = Find.TickManager.TicksGame;

            // Initialize caches if needed
            if (!_penableAnimalsCache.ContainsKey(mapId))
                _penableAnimalsCache[mapId] = new List<Pawn>();

            if (!_reachabilityCache.ContainsKey(mapId))
                _reachabilityCache[mapId] = new Dictionary<Pawn, bool>();

            // Only do a full update if needed
            if (currentTick > _lastUpdateCacheTick + CacheUpdateInterval ||
                !_penableAnimalsCache.ContainsKey(mapId))
            {
                try
                {
                    // Clear existing caches for this map
                    _penableAnimalsCache[mapId].Clear();
                    _reachabilityCache[mapId].Clear();

                    // Find all animals that can be taken to pens
                    foreach (Pawn animal in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
                    {
                        if (ShouldProcessAnimal(animal, map))
                        {
                            _penableAnimalsCache[mapId].Add(animal);
                            targetCache.Add(animal);
                        }
                    }

                    if (_penableAnimalsCache[mapId].Count > 0)
                    {
                        Utility_DebugManager.LogNormal(
                            $"Found {_penableAnimalsCache[mapId].Count} animals that can be taken to pens on map {map.uniqueID}");
                        hasTargets = true;
                    }

                    _lastUpdateCacheTick = currentTick;
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"Error updating penable animals cache: {ex}");
                }
            }
            else
            {
                // Just add the cached animals to the target cache
                foreach (Pawn animal in _penableAnimalsCache[mapId])
                {
                    // Skip animals that are no longer valid
                    if (!animal.Spawned || !ShouldProcessAnimal(animal, map))
                        continue;

                    targetCache.Add(animal);
                    hasTargets = true;
                }
            }

            SetHasTargets(map, hasTargets);
        }

        /// <summary>
        /// Validate if this specific animal can be taken to a pen by this handler
        /// </summary>
        public override bool ValidateHandlingJob(Pawn animal, Pawn handler)
        {
            try
            {
                // Check if handler is on cooldown for this module
                if (IsPawnOnCooldown(handler))
                {
                    return false;
                }

                // Check if animal position is forbidden
                if (animal.Position.IsForbidden(handler))
                {
                    JobFailReason.Is(CannotPrioritizeForbiddenTrans);
                    return false;
                }

                // Check mental state compatibility
                bool isRoaming = animal.MentalStateDef == MentalStateDefOf.Roaming;
                if (!targetRoamingAnimals && !isRoaming && animal.MentalStateDef != null)
                {
                    JobFailReason.Is(CantRopeAnimalMentalStateTrans.Formatted(animal, animal.MentalStateDef.label));
                    return false;
                }

                // Skip the standard animal interaction validation for some cases
                // We override rather than call base because we need special handling for roaming animals
                if (!IsValidHandlingTarget(animal, handler))
                    return false;

                // Update balance calculators cache if needed
                UpdateBalanceCalculators(handler);

                // Check if this animal needs to be taken to a pen or hitching post
                string failReason;
                AnimalPenBalanceCalculator balanceCalculator = GetBalanceCalculator(handler.Map);

                // Try enclosed pen first
                CompAnimalPenMarker targetPen = AnimalPenUtility.GetPenAnimalShouldBeTakenTo(
                    handler, animal, out failReason, false, canInteractWhileSleeping, false, true,
                    ropingPriority, balanceCalculator);

                if (targetPen != null)
                {
                    // Check if we can make a valid job for this pen
                    IntVec3 standPosition = AnimalPenUtility.FindPlaceInPenToStand(targetPen, handler);
                    if (standPosition.IsValid)
                        return true;
                    else
                    {
                        JobFailReason.Is(CantRopeAnimalNoSpaceTrans);
                        return false;
                    }
                }

                // Try hitching post if no pen found
                Building hitchingPost = AnimalPenUtility.GetHitchingPostAnimalShouldBeTakenTo(
                    handler, animal, out failReason, false);
                if (hitchingPost != null)
                    return true;

                // Try unenclosed pens if allowed
                if (allowUnenclosedPens)
                {
                    targetPen = AnimalPenUtility.GetPenAnimalShouldBeTakenTo(
                        handler, animal, out failReason, false, canInteractWhileSleeping, true, true,
                        ropingPriority, balanceCalculator);

                    if (targetPen != null)
                    {
                        // Check if we can make a valid job for this unenclosed pen
                        IntVec3 standPosition = AnimalPenUtility.FindPlaceInPenToStand(targetPen, handler);
                        if (standPosition.IsValid)
                            return true;
                        else
                        {
                            JobFailReason.Is(CantRopeAnimalNoSpaceTrans);
                            return false;
                        }
                    }
                }

                // Check for unnecessary roping
                if (AnimalPenUtility.IsUnnecessarilyRoped(animal))
                {
                    if (AnimalPenUtility.RopeAttachmentInteractionCell(handler, animal) != IntVec3.Invalid &&
                        handler.CanReserve(animal) &&
                        handler.CanReach(animal, PathEndMode.Touch, Danger.Deadly))
                    {
                        return true;
                    }
                    else
                    {
                        JobFailReason.Is(CantRopeAnimalCantTouchTrans);
                        return false;
                    }
                }

                // Set the failure reason if there is one
                if (failReason != null)
                {
                    JobFailReason.Is(failReason);
                }

                return false;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error validating take to pen job: {ex.Message}");
                // Start cooldown on exception
                StartCooldown(handler);
                return false;
            }
        }

        /// <summary>
        /// Create a job to take an animal to a pen
        /// </summary>
        protected override Job CreateHandlingJob(Pawn handler, Pawn animal)
        {
            try
            {
                // Update balance calculators cache if needed
                UpdateBalanceCalculators(handler);

                // Try enclosed pen first
                string jobFailReason;
                AnimalPenBalanceCalculator balanceCalculator = GetBalanceCalculator(handler.Map);

                CompAnimalPenMarker targetPen = AnimalPenUtility.GetPenAnimalShouldBeTakenTo(
                    handler, animal, out jobFailReason, false, canInteractWhileSleeping, false, true,
                    ropingPriority, balanceCalculator);

                if (targetPen != null)
                {
                    Job job = MakeTakeToPenJob(handler, animal, targetPen, false, out jobFailReason);
                    if (job != null)
                    {
                        Utility_DebugManager.LogNormal($"{handler.LabelShort} created job to take {animal.LabelShort} to enclosed pen {targetPen.parent.Label}");
                        return job;
                    }
                }

                // Try hitching post if no pen found
                Building hitchingPost = AnimalPenUtility.GetHitchingPostAnimalShouldBeTakenTo(
                    handler, animal, out jobFailReason, false);

                if (hitchingPost != null)
                {
                    Job hitchJob = JobMaker.MakeJob(JobDefOf.RopeRoamerToHitchingPost, animal, hitchingPost);
                    Utility_DebugManager.LogNormal($"{handler.LabelShort} created job to take {animal.LabelShort} to hitching post");
                    return hitchJob;
                }

                // Try unenclosed pens if allowed
                if (allowUnenclosedPens)
                {
                    targetPen = AnimalPenUtility.GetPenAnimalShouldBeTakenTo(
                        handler, animal, out jobFailReason, false, canInteractWhileSleeping, true, true,
                        ropingPriority, balanceCalculator);

                    if (targetPen != null)
                    {
                        Job job = MakeTakeToPenJob(handler, animal, targetPen, true, out jobFailReason);
                        if (job != null)
                        {
                            Utility_DebugManager.LogNormal($"{handler.LabelShort} created job to take {animal.LabelShort} to unenclosed pen {targetPen.parent.Label}");
                            return job;
                        }
                    }
                }

                // Check for unnecessary roping
                if (AnimalPenUtility.IsUnnecessarilyRoped(animal))
                {
                    if (AnimalPenUtility.RopeAttachmentInteractionCell(handler, animal) != IntVec3.Invalid)
                    {
                        Job unropeJob = JobMaker.MakeJob(JobDefOf.Unrope, animal);
                        Utility_DebugManager.LogNormal($"{handler.LabelShort} created job to unrope {animal.LabelShort}");
                        return unropeJob;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating take to pen job: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Update the balance calculators cache for a specific pawn
        /// </summary>
        protected void UpdateBalanceCalculators(Pawn pawn)
        {
            if (_balanceCalculatorsCachedTick != Find.TickManager.TicksGame || _balanceCalculatorsCachedForPawn != pawn)
            {
                foreach (var keyValuePair in _balanceCalculatorsCached)
                {
                    keyValuePair.Value.MarkDirty();
                }
                _balanceCalculatorsCachedTick = Find.TickManager.TicksGame;
                _balanceCalculatorsCachedForPawn = pawn;
            }
        }

        /// <summary>
        /// Get the balance calculator for a specific map
        /// </summary>
        protected AnimalPenBalanceCalculator GetBalanceCalculator(Map map)
        {
            if (_balanceCalculatorsCached.ContainsKey(map))
            {
                return _balanceCalculatorsCached[map];
            }
            else
            {
                AnimalPenBalanceCalculator calculator = new AnimalPenBalanceCalculator(map, true);
                _balanceCalculatorsCached.Add(map, calculator);
                return calculator;
            }
        }

        /// <summary>
        /// Create a job to take an animal to a pen
        /// </summary>
        private Job MakeTakeToPenJob(Pawn pawn, Pawn animal, CompAnimalPenMarker targetPenMarker,
            bool allowUnenclosed, out string jobFailReason)
        {
            jobFailReason = null;

            // Find a valid place to stand in the pen
            IntVec3 standPos = AnimalPenUtility.FindPlaceInPenToStand(targetPenMarker, pawn);
            if (!standPos.IsValid)
            {
                jobFailReason = CantRopeAnimalNoSpaceTrans;
                return null;
            }

            // Create appropriate job based on whether pen is enclosed
            JobDef jobDef = targetPenMarker.PenState.Enclosed
                ? JobDefOf.RopeToPen
                : JobDefOf.RopeRoamerToUnenclosedPen;

            Job job = JobMaker.MakeJob(jobDef, animal, standPos, targetPenMarker.parent);
            job.ropingPriority = ropingPriority;
            job.ropeToUnenclosedPens = allowUnenclosed;
            return job;
        }
    }
}