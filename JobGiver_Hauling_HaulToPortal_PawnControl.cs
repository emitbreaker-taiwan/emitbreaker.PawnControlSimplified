using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to haul items to map portals.
    /// Uses the Hauling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Hauling_HaulToPortal_PawnControl : JobGiver_Hauling_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "HaulToPortal";

        /// <summary>
        /// Update cache every 2 seconds
        /// </summary>
        protected override int CacheUpdateInterval => 120;

        /// <summary>
        /// Distance thresholds for bucketing (10, 20, 30 tiles)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 100f, 400f, 900f };

        #endregion

        #region Cache Management

        // Cache for portals that need items loaded
        private static readonly Dictionary<int, List<MapPortal>> _portalsCache = new Dictionary<int, List<MapPortal>>();
        private static readonly Dictionary<int, Dictionary<MapPortal, bool>> _portalReachabilityCache = new Dictionary<int, Dictionary<MapPortal, bool>>();
        private static int _lastPortalCacheUpdateTick = -999;

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetHaulToPortalCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_portalsCache, _portalReachabilityCache);
            _lastPortalCacheUpdateTick = -999;
            ResetHaulingCache(); // Call base class reset too
        }

        #endregion

        #region Core flow

        protected override float GetBasePriority(string workTag)
        {
            // Loading portals is important
            return 5.9f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Hauling_HaulToPortal_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) => {
                    if (p?.Map == null)
                        return null;

                    int mapId = p.Map.uniqueID;
                    int now = Find.TickManager.TicksGame;

                    if (!ShouldExecuteNow(mapId))
                        return null;

                    // Update portals cache
                    UpdatePortalsCache(p.Map);

                    // Create job for hauling to portal
                    return TryCreateHaulToPortalJob(p);
                },
                debugJobDesc: DebugName);
        }

        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // We're using our custom portal cache instead of Thing targets
            yield break;
        }

        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // We're using portal-specific logic instead of the standard target processing
            // This method is required by the base class but not used for portal jobs
            return null;
        }

        /// <summary>
        /// Determines if the job giver should execute now based on cache status
        /// </summary>
        protected override bool ShouldExecuteNow(int mapId)
        {
            int now = Find.TickManager.TicksGame;
            return now > _lastPortalCacheUpdateTick + CacheUpdateInterval ||
                  !_portalsCache.ContainsKey(mapId);
        }

        #endregion

        #region Portal-specific processing

        /// <summary>
        /// Updates the cache of portals that need items loaded
        /// </summary>
        private void UpdatePortalsCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastPortalCacheUpdateTick + CacheUpdateInterval ||
                !_portalsCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_portalsCache.ContainsKey(mapId))
                    _portalsCache[mapId].Clear();
                else
                    _portalsCache[mapId] = new List<MapPortal>();

                // Clear reachability cache too
                if (_portalReachabilityCache.ContainsKey(mapId))
                    _portalReachabilityCache[mapId].Clear();
                else
                    _portalReachabilityCache[mapId] = new Dictionary<MapPortal, bool>();

                // Find all portals on the map
                foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.MapPortal))
                {
                    MapPortal portal = thing as MapPortal;
                    if (portal != null)
                    {
                        _portalsCache[mapId].Add(portal);
                    }
                }

                _lastPortalCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Create a job for hauling to a portal using manager-driven bucket processing
        /// </summary>
        private Job TryCreateHaulToPortalJob(Pawn pawn)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_portalsCache.ContainsKey(mapId) || _portalsCache[mapId].Count == 0)
                return null;

            // Create custom buckets for MapPortals
            List<MapPortal>[] buckets = CreateDistanceBucketsForPortals(
                pawn,
                _portalsCache[mapId],
                DistanceThresholds
            );

            // Find the best portal to load
            MapPortal targetPortal = FindFirstValidPortal(
                buckets,
                pawn
            );

            // Create job if target found
            if (targetPortal != null)
            {
                // Use the utility method if available
                Job job = EnterPortalUtility.JobOnPortal(pawn, targetPortal);

                if (job != null)
                {
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to haul to portal {targetPortal.LabelCap}");
                }

                return job;
            }

            return null;
        }

        /// <summary>
        /// Create distance-based buckets for MapPortals
        /// </summary>
        private List<MapPortal>[] CreateDistanceBucketsForPortals(Pawn pawn, List<MapPortal> portals, float[] distanceThresholds)
        {
            if (pawn == null || portals == null || distanceThresholds == null)
                return null;

            // Initialize buckets
            List<MapPortal>[] buckets = new List<MapPortal>[distanceThresholds.Length + 1];
            for (int i = 0; i < buckets.Length; i++)
                buckets[i] = new List<MapPortal>();

            foreach (MapPortal portal in portals)
            {
                // Get distance squared between pawn and portal
                float distSq = (portal.Position - pawn.Position).LengthHorizontalSquared;

                // Assign to appropriate bucket
                int bucketIndex = distanceThresholds.Length; // Default to last bucket (furthest)
                for (int i = 0; i < distanceThresholds.Length; i++)
                {
                    if (distSq < distanceThresholds[i])
                    {
                        bucketIndex = i;
                        break;
                    }
                }

                buckets[bucketIndex].Add(portal);
            }

            return buckets;
        }

        /// <summary>
        /// Find the first valid portal from bucketed lists
        /// </summary>
        private MapPortal FindFirstValidPortal(List<MapPortal>[] buckets, Pawn pawn)
        {
            if (buckets == null || pawn == null) return null;

            // Get map ID for caching
            int mapId = pawn.Map.uniqueID;

            // Process buckets from closest to furthest
            for (int b = 0; b < buckets.Length; b++)
            {
                if (buckets[b] == null || buckets[b].Count == 0)
                    continue;

                // Randomize within each distance band for better distribution
                buckets[b].Shuffle();

                // Check each portal in this distance band
                foreach (MapPortal portal in buckets[b])
                {
                    // Skip if portal is null or destroyed
                    if (portal == null || portal.Destroyed || !portal.Spawned)
                        continue;

                    // Check if we've already determined this portal is reachable
                    bool isReachable;
                    if (_portalReachabilityCache[mapId].TryGetValue(portal, out isReachable))
                    {
                        if (!isReachable)
                            continue;
                    }
                    else
                    {
                        // Determine if portal is valid and reachable
                        if (!EnterPortalUtility.HasJobOnPortal(pawn, portal))
                        {
                            _portalReachabilityCache[mapId][portal] = false;
                            continue;
                        }

                        _portalReachabilityCache[mapId][portal] = true;
                    }

                    return portal;
                }
            }

            return null;
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return "JobGiver_Hauling_HaulToPortal_PawnControl";
        }

        #endregion
    }
}