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
        protected override bool RequiresMapZoneorArea => false;

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
        protected override int CacheUpdateInterval => 250;

        /// <summary>
        /// Distance thresholds for bucketing (15, 25, 40 tiles)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 225f, 625f, 1600f };

        #endregion

        #region Cache Management

        // Slaughter-specific cache
        private static readonly Dictionary<int, List<Pawn>> _slaughterCache = new Dictionary<int, List<Pawn>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _slaughterReachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static int _lastSlaughterCacheUpdateTick = -999;

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetSlaughterCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_slaughterCache, _slaughterReachabilityCache);
            _lastSlaughterCacheUpdateTick = -999;
            ResetHandlingCache(); // Call base class reset too
        }

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
            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
                return null;

            // Quick early exit if no animals are marked for slaughter
            if (!pawn.Map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.Slaughter) &&
                pawn.Map.autoSlaughterManager.AnimalsToSlaughter.Count == 0)
                return null;

            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Handling_Slaughter_PawnControl>(
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
                    if (now > _lastSlaughterCacheUpdateTick + CacheUpdateInterval ||
                        !_slaughterCache.ContainsKey(mapId))
                    {
                        UpdateSlaughterCache(p.Map);
                    }

                    // Process cached targets
                    return TryCreateSlaughterJob(p, forced);
                },
                debugJobDesc: DebugName);
        }

        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // Get all slaughter-designated animals and auto-slaughter animals
            if (map?.designationManager != null)
            {
                // Regular slaughter designations
                foreach (Designation designation in map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Slaughter))
                {
                    if (designation.target.HasThing && designation.target.Thing is Pawn animal && animal.IsNonMutantAnimal)
                    {
                        yield return animal;
                    }
                }

                // Auto-slaughter manager animals
                foreach (Pawn animal in map.autoSlaughterManager.AnimalsToSlaughter)
                {
                    if (animal != null && animal.Spawned && animal.IsNonMutantAnimal)
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

            // Find the best animal to slaughter
            Pawn targetAnimal = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (animal, p) => IsValidSlaughterTarget(animal, p, forced),
                _slaughterReachabilityCache);

            // Create job if target found
            if (targetAnimal != null)
            {
                Job job = JobMaker.MakeJob(WorkJobDef, targetAnimal);
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to slaughter {targetAnimal.LabelShort}");
                return job;
            }

            return null;
        }

        /// <summary>
        /// Determines if the job giver should execute based on cache status
        /// </summary>
        protected override bool ShouldExecuteNow(int mapId)
        {
            return Find.TickManager.TicksGame > _lastSlaughterCacheUpdateTick + CacheUpdateInterval ||
                  !_slaughterCache.ContainsKey(mapId);
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

        #endregion

        #region Target processing

        /// <summary>
        /// Updates the cache of animals that need to be slaughtered
        /// </summary>
        private void UpdateSlaughterCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            // Clear outdated cache
            if (_slaughterCache.ContainsKey(mapId))
                _slaughterCache[mapId].Clear();
            else
                _slaughterCache[mapId] = new List<Pawn>();

            // Clear reachability cache too
            if (_slaughterReachabilityCache.ContainsKey(mapId))
                _slaughterReachabilityCache[mapId].Clear();
            else
                _slaughterReachabilityCache[mapId] = new Dictionary<Pawn, bool>();

            // Add animals with slaughter designations
            foreach (Designation designation in map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Slaughter))
            {
                if (designation.target.Thing is Pawn animal && animal.IsNonMutantAnimal)
                {
                    _slaughterCache[mapId].Add(animal);
                }
            }

            // Add auto-slaughter manager animals
            foreach (Pawn animal in map.autoSlaughterManager.AnimalsToSlaughter)
            {
                if (animal != null && animal.Spawned && animal.IsNonMutantAnimal && !_slaughterCache[mapId].Contains(animal))
                {
                    _slaughterCache[mapId].Add(animal);
                }
            }

            _lastSlaughterCacheUpdateTick = currentTick;
        }

        /// <summary>
        /// Create a job to slaughter an animal
        /// </summary>
        private Job TryCreateSlaughterJob(Pawn pawn, bool forced = false)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_slaughterCache.ContainsKey(mapId) || _slaughterCache[mapId].Count == 0)
                return null;

            return ProcessCachedTargets(pawn, _slaughterCache[mapId].Cast<Thing>().ToList(), forced);
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

        #region Utility

        public override string ToString()
        {
            return "JobGiver_Slaughter_PawnControl";
        }

        #endregion
    }
}