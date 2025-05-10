using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for slaughtering animals marked for slaughter.
    /// </summary>
    public class JobModule_Handling_Slaughter : JobModule_Handling
    {
        public override string UniqueID => "SlaughterAnimals";
        public override float Priority => 5.4f; // Same as original JobGiver
        public override string Category => "AnimalHandling";
        public override int CacheUpdateInterval => 250; // Every ~4 seconds

        // Static caches for map-specific data persistence
        private static readonly Dictionary<int, List<Pawn>> _slaughterCache = new Dictionary<int, List<Pawn>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static int _lastUpdateCacheTick = -999;

        public override void ResetStaticData()
        {
            base.ResetStaticData();
            Utility_CacheManager.ResetJobGiverCache(_slaughterCache, _reachabilityCache);
            _lastUpdateCacheTick = -999;
        }

        public override bool QuickFilterCheck(Pawn pawn)
        {
            // Must be able to perform violent actions
            return !pawn.WorkTagIsDisabled(WorkTags.Violent);
        }

        public override void UpdateCache(Map map, List<Pawn> targetCache)
        {
            base.UpdateCache(map, targetCache);

            if (map == null) return;
            int mapId = map.uniqueID;
            bool hasTargets = false;

            // Quick early exit if no animals are marked for slaughter
            if (!map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.Slaughter) &&
                map.autoSlaughterManager.AnimalsToSlaughter.Count == 0)
                return;

            int currentTick = Find.TickManager.TicksGame;

            // Initialize caches if needed
            if (!_slaughterCache.ContainsKey(mapId))
                _slaughterCache[mapId] = new List<Pawn>();

            if (!_reachabilityCache.ContainsKey(mapId))
                _reachabilityCache[mapId] = new Dictionary<Pawn, bool>();

            // Only do a full update if needed
            if (currentTick > _lastUpdateCacheTick + CacheUpdateInterval ||
                !_slaughterCache.ContainsKey(mapId))
            {
                try
                {
                    // Clear existing caches for this map
                    _slaughterCache[mapId].Clear();
                    _reachabilityCache[mapId].Clear();

                    // Add animals with slaughter designations
                    foreach (Designation designation in map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Slaughter))
                    {
                        if (designation.target.Thing is Pawn animal && animal.IsNonMutantAnimal)
                        {
                            _slaughterCache[mapId].Add(animal);
                            targetCache.Add(animal);
                            hasTargets = true;
                        }
                    }

                    // Add auto-slaughter manager animals
                    foreach (Pawn animal in map.autoSlaughterManager.AnimalsToSlaughter)
                    {
                        if (animal != null && animal.Spawned && animal.IsNonMutantAnimal && !_slaughterCache[mapId].Contains(animal))
                        {
                            _slaughterCache[mapId].Add(animal);
                            targetCache.Add(animal);
                            hasTargets = true;
                        }
                    }

                    if (_slaughterCache[mapId].Count > 0)
                    {
                        Utility_DebugManager.LogNormal(
                            $"Found {_slaughterCache[mapId].Count} animals marked for slaughter on map {map.uniqueID}");
                    }

                    _lastUpdateCacheTick = currentTick;
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"Error updating slaughter cache: {ex}");
                }
            }
            else
            {
                // Just add the cached animals to the target cache
                foreach (Pawn animal in _slaughterCache[mapId])
                {
                    // Skip animals that are no longer valid
                    if (!animal.Spawned || animal.Dead || !animal.ShouldBeSlaughtered())
                        continue;

                    targetCache.Add(animal);
                    hasTargets = true;
                }
            }

            SetHasTargets(map, hasTargets);
        }

        public override bool ShouldProcessAnimal(Pawn animal, Map map)
        {
            try
            {
                if (animal == null || !animal.Spawned || animal.Dead || !animal.IsNonMutantAnimal)
                    return false;

                // Check if this animal is marked for slaughter (either by designation or auto-slaughter)
                return animal.ShouldBeSlaughtered();
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in ShouldProcessAnimal for slaughtering: {ex}");
                return false;
            }
        }

        public override bool ValidateHandlingJob(Pawn animal, Pawn handler)
        {
            try
            {
                // CRITICAL: Don't slaughter yourself!
                if (animal == handler)
                    return false;

                // Check if handler can perform violent actions
                if (handler.WorkTagIsDisabled(WorkTags.Violent))
                {
                    JobFailReason.Is("IsIncapableOfViolenceShort".Translate(handler));
                    return false;
                }

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
                if (animal.IsForbidden(handler) || !handler.CanReserve(animal))
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
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error validating slaughter job: {ex}");
                return false;
            }
        }

        protected override Job CreateHandlingJob(Pawn handler, Pawn animal)
        {
            try
            {
                // Create the slaughter job
                Job job = JobMaker.MakeJob(JobDefOf.Slaughter, animal);
                Utility_DebugManager.LogNormal($"{handler.LabelShort} created job to slaughter {animal.LabelShort}");
                return job;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating slaughter job: {ex}");
                return null;
            }
        }
    }
}