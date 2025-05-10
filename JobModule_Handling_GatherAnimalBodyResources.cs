using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Abstract base class for modules that gather resources from animals (milk, wool, etc.)
    /// </summary>
    public abstract class JobModule_Handling_GatherAnimalBodyResources : JobModule_Handling
    {
        public override string Category => "AnimalHandling";
        public override int CacheUpdateInterval => 250; // Every ~4 seconds

        // Constants
        protected const int MAX_CACHE_SIZE = 1000;

        // Static caches for map-specific data persistence
        protected static readonly Dictionary<int, List<Pawn>> _gatherableAnimalsCache = new Dictionary<int, List<Pawn>>();
        protected static readonly Dictionary<int, Dictionary<Pawn, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        protected static int _lastCacheUpdateTick = -999;

        // Distance thresholds for bucketing (25, 50, 100 tiles squared)
        protected static readonly float[] DISTANCE_THRESHOLDS = new float[] { 625f, 2500f, 10000f };

        /// <summary>
        /// The JobDef to use when creating jobs
        /// </summary>
        protected abstract JobDef JobDef { get; }

        /// <summary>
        /// Gets the appropriate CompHasGatherableBodyResource component from the animal
        /// </summary>
        protected abstract CompHasGatherableBodyResource GetComp(Pawn animal);

        /// <summary>
        /// Reset static data when language changes or game reloads
        /// </summary>
        public override void ResetStaticData()
        {
            base.ResetStaticData();
            Utility_CacheManager.ResetJobGiverCache(_gatherableAnimalsCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        /// <summary>
        /// Quick filter for animals work tag
        /// </summary>
        public override bool QuickFilterCheck(Pawn pawn)
        {
            return !pawn.WorkTagIsDisabled(WorkTags.Animals);
        }

        /// <summary>
        /// Determine if an animal should be processed for resource gathering
        /// </summary>
        public override bool ShouldProcessAnimal(Pawn animal, Map map)
        {
            try
            {
                if (animal == null || !animal.Spawned || animal.Dead || !animal.IsNonMutantAnimal)
                    return false;

                // Must be player's animal
                if (animal.Faction != Faction.OfPlayer)
                    return false;

                // Check for gatherable resources
                CompHasGatherableBodyResource comp = GetComp(animal);
                if (comp == null || !comp.ActiveAndFull)
                    return false;

                // Skip downed animals
                if (animal.Downed)
                    return false;

                // Skip roped animals
                if (animal.roping != null && animal.roping.IsRopedByPawn)
                    return false;

                // Skip animals that can't interact now
                if (!animal.CanCasuallyInteractNow())
                    return false;

                return true;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in ShouldProcessAnimal for resource gathering: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Update the cache of animals that have gatherable resources
        /// </summary>
        public override void UpdateCache(Map map, List<Pawn> targetCache)
        {
            base.UpdateCache(map, targetCache);

            if (map == null) return;
            int mapId = map.uniqueID;
            bool hasTargets = false;

            int currentTick = Find.TickManager.TicksGame;

            // Initialize caches if needed
            if (!_gatherableAnimalsCache.ContainsKey(mapId))
                _gatherableAnimalsCache[mapId] = new List<Pawn>();

            if (!_reachabilityCache.ContainsKey(mapId))
                _reachabilityCache[mapId] = new Dictionary<Pawn, bool>();

            // Only do a full update if needed
            if (currentTick > _lastCacheUpdateTick + CacheUpdateInterval ||
                !_gatherableAnimalsCache.ContainsKey(mapId))
            {
                try
                {
                    // Clear existing caches for this map
                    _gatherableAnimalsCache[mapId].Clear();
                    _reachabilityCache[mapId].Clear();

                    // Get animals from the faction that have gatherable resources
                    foreach (Pawn animal in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
                    {
                        if (ShouldProcessAnimal(animal, map))
                        {
                            _gatherableAnimalsCache[mapId].Add(animal);
                            targetCache.Add(animal);
                            hasTargets = true;
                        }
                    }

                    // Limit cache size for performance
                    if (_gatherableAnimalsCache[mapId].Count > MAX_CACHE_SIZE)
                    {
                        _gatherableAnimalsCache[mapId] = _gatherableAnimalsCache[mapId].Take(MAX_CACHE_SIZE).ToList();
                    }

                    if (_gatherableAnimalsCache[mapId].Count > 0)
                    {
                        Utility_DebugManager.LogNormal(
                            $"Found {_gatherableAnimalsCache[mapId].Count} animals with gatherable resources on map {map.uniqueID}");
                    }

                    _lastCacheUpdateTick = currentTick;
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"Error updating gatherable animals cache: {ex}");
                }
            }
            else
            {
                // Just add the cached animals to the target cache
                foreach (Pawn animal in _gatherableAnimalsCache[mapId])
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
        /// Validate if this specific animal can have resources gathered by this handler
        /// </summary>
        public override bool ValidateHandlingJob(Pawn animal, Pawn handler)
        {
            try
            {
                // Skip if trying to gather from self
                if (animal == handler)
                    return false;

                // Skip if not a valid animal
                if (!animal.IsNonMutantAnimal)
                    return false;

                // Skip if not of same faction
                if (animal.Faction != handler.Faction)
                    return false;

                // Skip if doesn't have gatherable resources
                CompHasGatherableBodyResource comp = GetComp(animal);
                if (comp == null || !comp.ActiveAndFull)
                    return false;

                // Skip if downed, roped, or can't interact
                if (animal.Downed || !animal.CanCasuallyInteractNow() ||
                    (animal.roping != null && animal.roping.IsRopedByPawn))
                    return false;

                // Skip if forbidden or cannot reserve
                if (animal.IsForbidden(handler) || !handler.CanReserve(animal))
                    return false;

                return true;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error validating resource gathering job: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Create a job to gather resources from an animal
        /// </summary>
        protected override Job CreateHandlingJob(Pawn handler, Pawn animal)
        {
            try
            {
                // Create the appropriate gathering job
                Job job = JobMaker.MakeJob(JobDef, animal);

                // Get the resource type from derived class
                string resourceType = GetResourceName(animal);

                Utility_DebugManager.LogNormal($"{handler.LabelShort} created job to gather {resourceType} from {animal.LabelShort}");
                return job;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating resource gathering job: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Gets a human-readable name for the resource being gathered
        /// </summary>
        protected abstract string GetResourceName(Pawn animal);
    }
}