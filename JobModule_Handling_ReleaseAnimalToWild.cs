using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for releasing animals to the wild when marked with the appropriate designation.
    /// </summary>
    public class JobModule_Handling_ReleaseAnimalToWild : JobModule_Handling
    {
        public override string UniqueID => "ReleaseAnimalToWild";
        public override float Priority => 5.3f; // Slightly lower than slaughter (as in original JobGiver)
        public override string Category => "AnimalHandling";
        public override int CacheUpdateInterval => 250; // Every ~4 seconds

        // Static caches for map-specific data persistence
        private static readonly Dictionary<int, List<Pawn>> _releaseCache = new Dictionary<int, List<Pawn>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static int _lastUpdateCacheTick = -999;

        public override void ResetStaticData()
        {
            base.ResetStaticData();
            Utility_CacheManager.ResetJobGiverCache(_releaseCache, _reachabilityCache);
            _lastUpdateCacheTick = -999;
        }

        public override void UpdateCache(Map map, List<Pawn> targetCache)
        {
            base.UpdateCache(map, targetCache);

            if (map == null) return;
            int mapId = map.uniqueID;
            bool hasTargets = false;

            // Quick early exit if no animals are marked for release
            if (!map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.ReleaseAnimalToWild))
                return;

            int currentTick = Find.TickManager.TicksGame;

            // Initialize caches if needed
            if (!_releaseCache.ContainsKey(mapId))
                _releaseCache[mapId] = new List<Pawn>();

            if (!_reachabilityCache.ContainsKey(mapId))
                _reachabilityCache[mapId] = new Dictionary<Pawn, bool>();

            // Only do a full update if needed
            if (currentTick > _lastUpdateCacheTick + CacheUpdateInterval ||
                !_releaseCache.ContainsKey(mapId))
            {
                try
                {
                    // Clear existing caches for this map
                    _releaseCache[mapId].Clear();
                    _reachabilityCache[mapId].Clear();

                    // Add animals with release designations
                    foreach (Designation designation in map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.ReleaseAnimalToWild))
                    {
                        if (designation.target.Thing is Pawn animal && animal.IsNonMutantAnimal)
                        {
                            _releaseCache[mapId].Add(animal);
                            targetCache.Add(animal);
                            hasTargets = true;
                        }
                    }

                    if (_releaseCache[mapId].Count > 0)
                    {
                        Utility_DebugManager.LogNormal(
                            $"Found {_releaseCache[mapId].Count} animals marked for release to wild on map {map.uniqueID}");
                    }

                    _lastUpdateCacheTick = currentTick;
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"Error updating animal release cache: {ex}");
                }
            }
            else
            {
                // Just add the cached animals to the target cache
                foreach (Pawn animal in _releaseCache[mapId])
                {
                    // Skip animals that are no longer valid
                    if (!animal.Spawned || animal.Dead ||
                        map.designationManager.DesignationOn(animal, DesignationDefOf.ReleaseAnimalToWild) == null)
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

                // Check if the animal is designated for release
                return map.designationManager.DesignationOn(animal, DesignationDefOf.ReleaseAnimalToWild) != null;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in ShouldProcessAnimal for releasing: {ex}");
                return false;
            }
        }

        public override bool ValidateHandlingJob(Pawn animal, Pawn handler)
        {
            try
            {
                // CRITICAL: Don't release yourself!
                if (animal == handler)
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
                if (animal.IsForbidden(handler) || !handler.CanReserve(animal))
                    return false;

                // Check if there's a valid outside cell to release to
                IntVec3 outsideCell;
                if (!JobDriver_ReleaseAnimalToWild.TryFindClosestOutsideCell_NewTemp(
                    animal.Position, animal.Map, TraverseParms.For(handler), handler, out outsideCell))
                {
                    JobFailReason.Is("NoReleaseLocation".Translate());
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error validating release animal job: {ex}");
                return false;
            }
        }

        protected override Job CreateHandlingJob(Pawn handler, Pawn animal)
        {
            try
            {
                // Create the release job
                Job job = JobMaker.MakeJob(JobDefOf.ReleaseAnimalToWild, animal);
                job.count = 1;
                Utility_DebugManager.LogNormal($"{handler.LabelShort} created job to release {animal.LabelShort} to the wild");
                return job;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating release animal job: {ex}");
                return null;
            }
        }
    }
}