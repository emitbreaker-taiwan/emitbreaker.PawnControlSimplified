using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to slaughter animals marked for slaughter.
    /// Uses the Handling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Slaughter_PawnControl : ThinkNode_JobGiver
    {
        // Cache for animals marked for slaughter
        private static readonly Dictionary<int, List<Pawn>> _slaughterCache = new Dictionary<int, List<Pawn>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 250; // Update every ~4 seconds

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        public override float GetPriority(Pawn pawn)
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

            return Utility_JobGiverManager.StandardTryGiveJob<Pawn>(
                pawn,
                "Handling",
                (p, forced) => {
                    // Update slaughter cache
                    UpdateSlaughterCache(p.Map);

                    // Find and create a job for slaughtering animals
                    return TryCreateSlaughterJob(p, forced);
                },
                debugJobDesc: "slaughter animal");
        }

        /// <summary>
        /// Updates the cache of animals that need to be slaughtered
        /// </summary>
        private void UpdateSlaughterCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_slaughterCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_slaughterCache.ContainsKey(mapId))
                    _slaughterCache[mapId].Clear();
                else
                    _slaughterCache[mapId] = new List<Pawn>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Pawn, bool>();

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

                _lastCacheUpdateTick = currentTick;
            }
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

            // Use JobGiverManager for distance bucketing
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                _slaughterCache[mapId],
                (animal) => (animal.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Find the best animal to slaughter
            Pawn targetAnimal = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (animal, p) => {
                    // CRITICAL: Don't slaughter yourself!
                    if (animal == p)
                        return false;

                    // IMPORTANT: Check faction interaction validity first
                    if (!Utility_JobGiverManager.IsValidFactionInteraction(animal, p, requiresDesignator: true))
                        return false;

                    // Skip if no longer a valid slaughter target
                    if (!animal.IsNonMutantAnimal || !animal.ShouldBeSlaughtered())
                        return false;

                    // Skip if wrong faction
                    if (p.Faction != animal.Faction)
                        return false;

                    // Skip if in mental state
                    if (animal.InAggroMentalState)
                        return false;

                    // Skip if forbidden or cannot reserve
                    if (animal.IsForbidden(p) || !p.CanReserve(animal, 1, -1, null, forced))
                        return false;

                    // Check ideological restrictions
                    if (ModsConfig.IdeologyActive)
                    {
                        if (!new HistoryEvent(HistoryEventDefOf.SlaughteredAnimal, p.Named(HistoryEventArgsNames.Doer))
                            .Notify_PawnAboutToDo_Job())
                            return false;

                        if (HistoryEventUtility.IsKillingInnocentAnimal(p, animal) &&
                            !new HistoryEvent(HistoryEventDefOf.KilledInnocentAnimal, p.Named(HistoryEventArgsNames.Doer))
                            .Notify_PawnAboutToDo_Job())
                            return false;

                        if (p.Ideo != null && p.Ideo.IsVeneratedAnimal(animal) &&
                            !new HistoryEvent(HistoryEventDefOf.SlaughteredVeneratedAnimal, p.Named(HistoryEventArgsNames.Doer))
                            .Notify_PawnAboutToDo_Job())
                            return false;
                    }

                    return true;
                },
                _reachabilityCache
            );

            // Create job if target found
            if (targetAnimal != null)
            {
                Job job = JobMaker.MakeJob(JobDefOf.Slaughter, targetAnimal);
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to slaughter {targetAnimal.LabelShort}");
                return job;
            }

            return null;
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_slaughterCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_Slaughter_PawnControl";
        }
    }
}