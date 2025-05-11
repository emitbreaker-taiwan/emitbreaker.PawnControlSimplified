using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to tame animals with tame designations.
    /// Uses the Handler work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Handling_Tame_PawnControl : JobGiver_Handling_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging
        /// </summary>
        protected override string DebugName => "Tame";

        /// <summary>
        /// Work tag for eligibility checking
        /// </summary>
        protected override string WorkTag => "Handling";

        /// <summary>
        /// Maximum cache size to control memory usage
        /// </summary>
        private const int MAX_CACHE_SIZE = 1000;

        /// <summary>
        /// Cache update interval in ticks (180 ticks = 3 seconds)
        /// </summary>
        protected override int CacheUpdateInterval => 180;

        /// <summary>
        /// Distance thresholds for bucketing (25, 50, 100 tiles squared)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 625f, 2500f, 10000f };

        #endregion

        #region Cache Management

        // Cache for target animals with tame designation
        private static readonly Dictionary<int, List<Pawn>> _tameDesignationCache = new Dictionary<int, List<Pawn>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static int _lastCacheUpdateTick = -999;

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetTameCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_tameDesignationCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        /// <summary>
        /// Updates the cache of animals that need taming
        /// </summary>
        private void UpdateTameableCacheSafely(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CacheUpdateInterval ||
                !_tameDesignationCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_tameDesignationCache.ContainsKey(mapId))
                    _tameDesignationCache[mapId].Clear();
                else
                    _tameDesignationCache[mapId] = new List<Pawn>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Pawn, bool>();

                // Get animals with tame designations
                foreach (Designation designation in map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Tame))
                {
                    if (designation.target.Thing is Pawn animal && TameUtility.CanTame(animal))
                    {
                        _tameDesignationCache[mapId].Add(animal);
                    }
                }

                // Limit cache size for performance
                if (_tameDesignationCache[mapId].Count > MAX_CACHE_SIZE)
                {
                    _tameDesignationCache[mapId] = _tameDesignationCache[mapId].Take(MAX_CACHE_SIZE).ToList();
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Determines if the job giver should execute based on cache status
        /// </summary>
        protected override bool ShouldExecuteNow(int mapId)
        {
            return Find.TickManager.TicksGame > _lastCacheUpdateTick + CacheUpdateInterval ||
                  !_tameDesignationCache.ContainsKey(mapId);
        }

        #endregion

        #region Core flow

        protected override float GetBasePriority(string workTag)
        {
            // Taming has moderate priority among work tasks
            return 5.1f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Skip if no tame designations exist on map
            if (pawn?.Map == null || !pawn.Map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.Tame))
                return null;

            // IMPORTANT: Only player faction pawns or pawns slaved to player faction can perform designation jobs
            if (pawn.Faction != Faction.OfPlayer &&
                !(pawn.IsSlave && pawn.HostFaction == Faction.OfPlayer))
                return null;

            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Handling_Tame_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) => {
                    // Update taming cache
                    UpdateTameableCacheSafely(p.Map);

                    // Create taming job
                    return TryCreateTameJob(p);
                },
                (p, setFailReason) => {
                    // Additional check for animals work tag
                    if (p.WorkTagIsDisabled(WorkTags.Animals))
                    {
                        if (setFailReason)
                            JobFailReason.Is("CannotPrioritizeWorkTypeDisabled".Translate(WorkTypeDefOf.Handling.gerundLabel).CapitalizeFirst());
                        return false;
                    }
                    return true;
                },
                debugJobDesc: "animal taming",
                skipEmergencyCheck: false);
        }

        #endregion

        #region Target processing

        /// <summary>
        /// Get all animals eligible for taming
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null) yield break;

            // Return animals with tame designations
            foreach (Designation designation in map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Tame))
            {
                if (designation.target.Thing is Pawn animal && TameUtility.CanTame(animal))
                {
                    yield return animal;
                }
            }
        }

        /// <summary>
        /// Process the cached targets for taming
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            return TryCreateTameJob(pawn);
        }

        /// <summary>
        /// Creates a job for taming an animal with a tame designation
        /// </summary>
        private Job TryCreateTameJob(Pawn pawn)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_tameDesignationCache.ContainsKey(mapId) || _tameDesignationCache[mapId].Count == 0)
                return null;

            // Create distance buckets for optimized searching
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                _tameDesignationCache[mapId],
                (animal) => (animal.Position - pawn.Position).LengthHorizontalSquared,
                DistanceThresholds
            );

            // Find first valid target
            Pawn targetAnimal = Utility_JobGiverManager.FindFirstValidTargetInBuckets<Pawn>(
                buckets,
                pawn,
                (animal, p) => {
                    // IMPORTANT: Check faction interaction validity first
                    if (!Utility_JobGiverManager.IsValidFactionInteraction(animal, p, requiresDesignator: true))
                        return false;

                    // Skip if no longer valid
                    if (animal == p || animal.Destroyed || !animal.Spawned || animal.Map != p.Map)
                        return false;

                    // Skip if no longer designated for taming
                    if (p.Map.designationManager.DesignationOn(animal, DesignationDefOf.Tame) == null)
                        return false;

                    // Skip if cannot be tamed
                    if (!TameUtility.CanTame(animal) || TameUtility.TriedToTameTooRecently(animal))
                        return false;

                    // Skip if in aggressive mental state
                    if (animal.InAggroMentalState)
                        return false;

                    // Skip if not an animal (redundant with CanTame but added for clarity)
                    if (!animal.RaceProps.Animal)
                        return false;

                    // Skip if forbidden or unreachable
                    if (animal.IsForbidden(p) ||
                        !p.CanReserve((LocalTargetInfo)animal) ||
                        !p.CanReach((LocalTargetInfo)animal, PathEndMode.Touch, Danger.Some))
                        return false;

                    return true;
                },
                _reachabilityCache
            );

            if (targetAnimal == null)
                return null;

            // Handle food for taming
            Thing foodSource = null;
            int foodCount = -1;

            if (targetAnimal.RaceProps.EatsFood && targetAnimal.needs?.food != null &&
                !HasFoodToInteractAnimal(pawn, targetAnimal))
            {
                ThingDef foodDef;
                foodSource = FoodUtility.BestFoodSourceOnMap(
                    pawn, targetAnimal, false, out foodDef, FoodPreferability.RawTasty,
                    false, false, false, false, false,
                    minNutrition: new float?((float)((double)JobDriver_InteractAnimal.RequiredNutritionPerFeed(targetAnimal) * 2.0 * 4.0))
                );

                if (foodSource == null)
                {
                    JobFailReason.Is("NoFood".Translate());
                    return null;
                }

                foodCount = Mathf.CeilToInt((float)((double)JobDriver_InteractAnimal.RequiredNutritionPerFeed(targetAnimal) * 2.0 * 4.0) /
                    FoodUtility.GetNutrition(targetAnimal, foodSource, foodDef));
            }

            // Create job if target found
            Job job = JobMaker.MakeJob(JobDefOf.Tame, targetAnimal, null, foodSource);
            job.count = foodCount;
            Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to tame {targetAnimal.LabelShort}");
            return job;
        }

        /// <summary>
        /// Implements the animal-specific validity check
        /// </summary>
        protected override bool IsValidAnimalTarget(Pawn animal, Pawn handler)
        {
            // Skip if no tame designation
            if (handler?.Map == null || animal?.Map == null ||
                handler.Map.designationManager.DesignationOn(animal, DesignationDefOf.Tame) == null)
                return false;

            // Skip if invalid tame target
            if (!TameUtility.CanTame(animal) || TameUtility.TriedToTameTooRecently(animal))
                return false;

            // Skip if in aggressive mental state
            if (animal.InAggroMentalState)
                return false;

            return true;
        }

        #endregion

        #region Animal interaction helpers

        /// <summary>
        /// Checks if the pawn has appropriate food for interacting with the animal
        /// </summary>
        protected virtual bool HasFoodToInteractAnimal(Pawn pawn, Pawn animal)
        {
            return pawn.inventory.innerContainer.Contains(ThingDefOf.Kibble) ||
                   (animal.RaceProps.foodType & (FoodTypeFlags.Plant | FoodTypeFlags.VegetableOrFruit)) != FoodTypeFlags.None &&
                   pawn.inventory.innerContainer.Any(t => t.def.IsNutritionGivingIngestible &&
                   t.def.ingestible.preferability >= FoodPreferability.RawBad &&
                   (t.def.ingestible.foodType & (FoodTypeFlags.Plant | FoodTypeFlags.VegetableOrFruit)) != FoodTypeFlags.None);
        }

        #endregion

        #region Utility

        /// <summary>
        /// For debugging purposes
        /// </summary>
        public override string ToString()
        {
            return "JobGiver_Tame_PawnControl";
        }

        #endregion
    }
}