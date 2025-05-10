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
    public class JobGiver_Hauling_HaulToPortal_PawnControl : ThinkNode_JobGiver
    {
        // Cache for portals that need items loaded
        private static readonly Dictionary<int, List<MapPortal>> _portalsCache = new Dictionary<int, List<MapPortal>>();
        private static readonly Dictionary<int, Dictionary<MapPortal, bool>> _reachabilityCache = new Dictionary<int, Dictionary<MapPortal, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 120; // Update every 2 seconds

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 100f, 400f, 900f }; // 10, 20, 30 tiles

        public override float GetPriority(Pawn pawn)
        {
            // Loading portals is important
            return 5.9f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManagerOld.StandardTryGiveJob<Plant>(
                pawn,
                "Hauling",
                (p, forced) => {
                    // Update plant cache
                    UpdatePortalsCache(p.Map);

                    // Find and create a job for cutting plants with VALID DESIGNATORS ONLY
                    return TryCreateHaulToPortalJob(p);
                },
                debugJobDesc: "haul to portal assignment");
        }

        /// <summary>
        /// Updates the cache of portals that need items loaded
        /// </summary>
        private void UpdatePortalsCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_portalsCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_portalsCache.ContainsKey(mapId))
                    _portalsCache[mapId].Clear();
                else
                    _portalsCache[mapId] = new List<MapPortal>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<MapPortal, bool>();

                // Find all portals on the map
                foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.MapPortal))
                {
                    MapPortal portal = thing as MapPortal;
                    if (portal != null)
                    {
                        _portalsCache[mapId].Add(portal);
                    }
                }

                _lastCacheUpdateTick = currentTick;
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
                DISTANCE_THRESHOLDS
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
                    if (_reachabilityCache[mapId].TryGetValue(portal, out isReachable))
                    {
                        if (!isReachable)
                            continue;
                    }
                    else
                    {
                        // Determine if portal is valid and reachable
                        if (!EnterPortalUtility.HasJobOnPortal(pawn, portal))
                        {
                            _reachabilityCache[mapId][portal] = false;
                            continue;
                        }
                        
                        _reachabilityCache[mapId][portal] = true;
                    }
                    
                    return portal;
                }
            }
            
            return null;
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_portalsCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_HaulToPortal_PawnControl";
        }
    }
}