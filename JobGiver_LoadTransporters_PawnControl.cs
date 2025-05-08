using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to load items into transporters like shuttles.
    /// Uses the Hauling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_LoadTransporters_PawnControl : ThinkNode_JobGiver
    {
        // Cache for transporters that need loading
        private static readonly Dictionary<int, List<CompTransporter>> _transportersCache = new Dictionary<int, List<CompTransporter>>();
        private static readonly Dictionary<int, Dictionary<CompTransporter, bool>> _reachabilityCache = new Dictionary<int, Dictionary<CompTransporter, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 120; // Update every 2 seconds

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 100f, 400f, 900f }; // 10, 20, 30 tiles

        public override float GetPriority(Pawn pawn)
        {
            // Loading transporters is important
            return 5.9f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<Plant>(
                pawn,
                "Hauling",
                (p, forced) => {
                    // Update plant cache
                    UpdateTransportersCache(p.Map);

                    // Find and create a job for cutting plants with VALID DESIGNATORS ONLY
                    return TryCreateLoadTransporterJob(p);
                },
                debugJobDesc: "load transporters assignment");
        }

        /// <summary>
        /// Updates the cache of transporters that need loading
        /// </summary>
        private void UpdateTransportersCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_transportersCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_transportersCache.ContainsKey(mapId))
                    _transportersCache[mapId].Clear();
                else
                    _transportersCache[mapId] = new List<CompTransporter>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<CompTransporter, bool>();

                // Find all transporters that need loading
                foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Transporter))
                {
                    CompTransporter transporter = thing.TryGetComp<CompTransporter>();
                    if (transporter != null && transporter.parent.Faction == Faction.OfPlayer)
                    {
                        _transportersCache[mapId].Add(transporter);
                    }
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Create a job for loading a transporter using manager-driven bucket processing
        /// </summary>
        private Job TryCreateLoadTransporterJob(Pawn pawn)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_transportersCache.ContainsKey(mapId) || _transportersCache[mapId].Count == 0)
                return null;

            // Create custom buckets for CompTransporters
            List<CompTransporter>[] buckets = CreateDistanceBucketsForTransporters(
                pawn,
                _transportersCache[mapId],
                DISTANCE_THRESHOLDS
            );

            // Find the best transporter to load
            CompTransporter targetTransporter = FindFirstValidTransporter(
                buckets,
                pawn
            );

            // Create job if target found
            if (targetTransporter != null)
            {
                // Use the utility method if available
                Job job = LoadTransportersJobUtility.JobOnTransporter(pawn, targetTransporter);
                
                if (job != null)
                {
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to load transporter {targetTransporter.parent.LabelCap}");
                }
                
                return job;
            }

            return null;
        }

        /// <summary>
        /// Create distance-based buckets for CompTransporters
        /// </summary>
        private List<CompTransporter>[] CreateDistanceBucketsForTransporters(Pawn pawn, List<CompTransporter> transporters, float[] distanceThresholds)
        {
            if (pawn == null || transporters == null || distanceThresholds == null)
                return null;
                
            // Initialize buckets
            List<CompTransporter>[] buckets = new List<CompTransporter>[distanceThresholds.Length + 1];
            for (int i = 0; i < buckets.Length; i++)
                buckets[i] = new List<CompTransporter>();
                
            foreach (CompTransporter transporter in transporters)
            {
                // Get distance squared between pawn and transporter
                float distSq = (transporter.parent.Position - pawn.Position).LengthHorizontalSquared;
                
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
                
                buckets[bucketIndex].Add(transporter);
            }
            
            return buckets;
        }

        /// <summary>
        /// Find the first valid transporter from bucketed lists
        /// </summary>
        private CompTransporter FindFirstValidTransporter(List<CompTransporter>[] buckets, Pawn pawn)
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
                
                // Check each transporter in this distance band
                foreach (CompTransporter transporter in buckets[b])
                {
                    // Check if we've already determined this transporter is reachable
                    bool isReachable;
                    if (_reachabilityCache[mapId].TryGetValue(transporter, out isReachable))
                    {
                        if (!isReachable)
                            continue;
                    }
                    else
                    {
                        // Determine if transporter is valid and reachable
                        if (!LoadTransportersJobUtility.HasJobOnTransporter(pawn, transporter))
                        {
                            _reachabilityCache[mapId][transporter] = false;
                            continue;
                        }
                        
                        _reachabilityCache[mapId][transporter] = true;
                    }
                    
                    return transporter;
                }
            }
            
            return null;
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_transportersCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_LoadTransporters_PawnControl";
        }
    }
}