using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for hauling items to map portals
    /// </summary>
    public class JobModule_Hauling_HaulToPortal : JobModule_Hauling
    {
        public override string UniqueID => "HaulToPortal";
        public override float Priority => 5.9f; // Same as the original JobGiver
        public override string Category => "Logistics"; // Added category for consistency
        public override int CacheUpdateInterval => 180; // Update every 3 seconds

        // Cache for portals found during this update - converted to static for persistence
        private static readonly Dictionary<int, List<Thing>> _portalCache = new Dictionary<int, List<Thing>>();
        private static readonly Dictionary<int, Dictionary<Thing, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Thing, bool>>();
        private static int _lastCacheUpdateTick = -999;

        // Mapping to quickly look up MapPortal components for fast access
        private static readonly Dictionary<int, Dictionary<Thing, MapPortal>> _portalMap = new Dictionary<int, Dictionary<Thing, MapPortal>>();

        // These ThingRequestGroups are what this module cares about
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups =>
            new HashSet<ThingRequestGroup> { ThingRequestGroup.MapPortal };

        public override void UpdateCache(Map map, List<Thing> targetCache)
        {
            base.UpdateCache(map, targetCache);

            if (map == null) return;
            int mapId = map.uniqueID;
            bool hasTargets = false;

            // Initialize the portal map for this map if needed
            if (!_portalMap.ContainsKey(mapId))
                _portalMap[mapId] = new Dictionary<Thing, MapPortal>();

            // Use the base class's progressive cache update
            UpdateCacheProgressively(
                map,
                targetCache,
                ref _lastCacheUpdateTick,
                RelevantThingRequestGroups,
                thing => {
                    MapPortal portal = thing as MapPortal;
                    if (portal != null && portal.Spawned)
                    {
                        // Keep our lookup map updated
                        _portalMap[mapId][portal] = portal;
                        return true;
                    }
                    return false;
                },
                _portalCache,
                CacheUpdateInterval
            );

            SetHasTargets(map, hasTargets || (targetCache != null && targetCache.Count > 0));
        }

        public override bool ShouldHaulItem(Thing item, Map map)
        {
            if (map == null || item == null || !item.Spawned) return false;

            int mapId = map.uniqueID;

            // Check from our cached map first for better performance
            if (_portalMap.ContainsKey(mapId) && _portalMap[mapId].ContainsKey(item))
                return true;

            // If not in cache, check directly
            return item is MapPortal portal && portal.Spawned;
        }

        public override bool ValidateHaulingJob(Thing target, Pawn hauler)
        {
            if (target == null || hauler == null || !target.Spawned || !hauler.Spawned)
                return false;

            MapPortal portal = target as MapPortal;
            if (portal == null || portal.IsForbidden(hauler))
                return false;

            int mapId = hauler.Map.uniqueID;

            // Check faction interaction
            if (!Utility_JobGiverManager.IsValidFactionInteraction(target, hauler, requiresDesignator: false))
                return false;

            // Check reachability and reservation
            if (!hauler.CanReserve(target, 1, -1) ||
                !hauler.CanReach(target, PathEndMode.Touch, hauler.NormalMaxDanger()))
                return false;

            // Check reachability cache first - target-pawn pairs
            if (_reachabilityCache.ContainsKey(mapId) &&
                _reachabilityCache[mapId].TryGetValue(target, out bool isReachable))
            {
                return isReachable;
            }

            // Use the utility method to check if pawn has a job on this portal
            bool hasJob = EnterPortalUtility.HasJobOnPortal(hauler, portal);

            // Initialize map cache if needed
            if (!_reachabilityCache.ContainsKey(mapId))
                _reachabilityCache[mapId] = new Dictionary<Thing, bool>();

            // Cache the result for future lookups
            _reachabilityCache[mapId][target] = hasJob;

            return hasJob;
        }

        protected override Job CreateHaulingJob(Pawn hauler, Thing target)
        {
            try
            {
                if (target == null || hauler == null) return null;

                MapPortal portal = target as MapPortal;
                if (portal == null)
                    return null;

                // Use the utility method to create the job
                Job job = EnterPortalUtility.JobOnPortal(hauler, portal);

                if (job != null)
                {
                    Utility_DebugManager.LogNormal($"{hauler.LabelShort} created job to haul to portal {portal.LabelCap}");
                }

                return job;
            }
            catch (System.Exception ex)
            {
                Utility_DebugManager.LogError($"Error creating portal hauling job: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public override void ResetStaticData()
        {
            Utility_CacheManager.ResetJobGiverCache(_portalCache, _reachabilityCache);

            // Also clear the portal mapping
            foreach (var mapDict in _portalMap.Values)
            {
                mapDict.Clear();
            }
            _portalMap.Clear();

            _lastCacheUpdateTick = -999;
        }
    }
}