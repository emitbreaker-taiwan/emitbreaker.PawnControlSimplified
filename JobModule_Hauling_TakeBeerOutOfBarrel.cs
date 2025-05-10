using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for taking beer out of fermenting barrels when ready
    /// </summary>
    public class JobModule_Hauling_TakeBeerOutOfBarrel : JobModule_Hauling
    {
        public override string UniqueID => "TakeBeerOutOfBarrel";
        public override float Priority => 5.3f; // Same as original JobGiver
        public override string Category => "Production";
        public override int CacheUpdateInterval => 300; // Update every 5 seconds

        // Static caches for map-specific data persistence
        private static readonly Dictionary<int, List<Building_FermentingBarrel>> _fermentedBarrelCache = new Dictionary<int, List<Building_FermentingBarrel>>();
        private static readonly Dictionary<int, Dictionary<Building_FermentingBarrel, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Building_FermentingBarrel, bool>>();
        private static int _lastCacheUpdateTick = -999;

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        // These ThingRequestGroups are what this module cares about
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups =>
            new HashSet<ThingRequestGroup> { ThingRequestGroup.BuildingArtificial };

        public override void ResetStaticData()
        {
            Utility_CacheManager.ResetJobGiverCache(_fermentedBarrelCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override void UpdateCache(Map map, List<Thing> targetCache)
        {
            base.UpdateCache(map, targetCache);

            if (map == null) return;
            int mapId = map.uniqueID;
            bool hasTargets = false;

            int currentTick = Find.TickManager.TicksGame;

            // Initialize caches if needed
            if (!_fermentedBarrelCache.ContainsKey(mapId))
                _fermentedBarrelCache[mapId] = new List<Building_FermentingBarrel>();

            if (!_reachabilityCache.ContainsKey(mapId))
                _reachabilityCache[mapId] = new Dictionary<Building_FermentingBarrel, bool>();

            // Only do a full update if needed
            if (currentTick > _lastCacheUpdateTick + CacheUpdateInterval ||
                !_fermentedBarrelCache.ContainsKey(mapId))
            {
                try
                {
                    // Clear existing caches for this map
                    _fermentedBarrelCache[mapId].Clear();
                    _reachabilityCache[mapId].Clear();

                    // Find all fermenting barrels on the map that are ready
                    foreach (Thing thing in map.listerThings.ThingsOfDef(ThingDefOf.FermentingBarrel))
                    {
                        Building_FermentingBarrel barrel = thing as Building_FermentingBarrel;
                        if (barrel != null && barrel.Spawned && barrel.Fermented)
                        {
                            _fermentedBarrelCache[mapId].Add(barrel);

                            // Also add to the target cache provided by the job giver
                            targetCache.Add(barrel);
                        }
                    }

                    if (_fermentedBarrelCache[mapId].Count > 0)
                    {
                        Utility_DebugManager.LogNormal(
                            $"Found {_fermentedBarrelCache[mapId].Count} fermented barrels ready for beer extraction on map {map.uniqueID}");
                    }

                    _lastCacheUpdateTick = currentTick;
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"Error updating fermented barrel cache: {ex}");
                }
            }
            else
            {
                // Just add the cached targets to the target cache
                foreach (Building_FermentingBarrel barrel in _fermentedBarrelCache[mapId])
                {
                    // Skip if no longer valid
                    if (!barrel.Spawned || barrel.Destroyed || !barrel.Fermented)
                        continue;

                    targetCache.Add(barrel);
                }
            }

            SetHasTargets(map, hasTargets || (targetCache != null && targetCache.Count > 0));
        }

        public override bool ShouldHaulItem(Thing thing, Map map)
        {
            try
            {
                if (thing == null || map == null || !thing.Spawned) return false;

                // Check if it's a fermenting barrel
                Building_FermentingBarrel barrel = thing as Building_FermentingBarrel;
                if (barrel == null)
                    return false;

                int mapId = map.uniqueID;

                // Check if it's in our cache first
                if (_fermentedBarrelCache.ContainsKey(mapId) && _fermentedBarrelCache[mapId].Contains(barrel))
                    return true;

                // If not in cache, check if barrel is fermented
                return barrel.Fermented;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in ShouldHaulItem for fermented barrel: {ex}");
                return false;
            }
        }

        public override bool ValidateHaulingJob(Thing thing, Pawn hauler)
        {
            try
            {
                if (thing == null || hauler == null || !thing.Spawned || !hauler.Spawned)
                    return false;

                Building_FermentingBarrel barrel = thing as Building_FermentingBarrel;
                if (barrel == null)
                    return false;

                // IMPORTANT: Check faction interaction validity first
                if (!Utility_JobGiverManager.IsValidFactionInteraction(barrel, hauler, requiresDesignator: false))
                    return false;

                // Skip if not fermented, burning, or forbidden
                if (!barrel.Fermented || barrel.IsBurning() || barrel.IsForbidden(hauler))
                    return false;

                // Skip if unreachable or can't be reserved
                if (!hauler.CanReserve(barrel))
                    return false;

                return true;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error validating take beer job: {ex}");
                return false;
            }
        }

        protected override Job CreateHaulingJob(Pawn hauler, Thing thing)
        {
            try
            {
                if (hauler == null || thing == null)
                    return null;

                Building_FermentingBarrel barrel = thing as Building_FermentingBarrel;
                if (barrel == null || !barrel.Fermented)
                    return null;

                Job job = JobMaker.MakeJob(JobDefOf.TakeBeerOutOfFermentingBarrel, barrel);
                Utility_DebugManager.LogNormal($"{hauler.LabelShort} created job to take beer out of fermenting barrel");
                return job;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating take beer job: {ex}");
                return null;
            }
        }
    }
}