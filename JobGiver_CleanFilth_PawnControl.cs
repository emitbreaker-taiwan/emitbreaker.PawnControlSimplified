using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to clean filth in the home area.
    /// Uses the Cleaning work tag for eligibility checking.
    /// </summary>
    public class JobGiver_CleanFilth_PawnControl : ThinkNode_JobGiver
    {
        // Constants
        private const int MIN_TICKS_SINCE_THICKENED = 600;
        private const int MAX_FILTH_PER_JOB = 15;
        private const int MAX_SEARCH_RADIUS = 10;

        // Cache for filth that needs cleaning
        private static readonly Dictionary<int, List<Filth>> _filthCache = new Dictionary<int, List<Filth>>();
        private static readonly Dictionary<int, Dictionary<Filth, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Filth, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 450; // Update every 7.5 seconds

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 100f, 400f, 1600f }; // 10, 20, 40 tiles

        public override float GetPriority(Pawn pawn)
        {
            // Cleaning is lower priority than most tasks but more important than snow clearing
            return 4.7f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // IMPORTANT: Only player pawns and slaves owned by player should clear filth
            if (pawn.Faction != Faction.OfPlayer &&
                !(pawn.IsSlave && pawn.HostFaction == Faction.OfPlayer))
                return null;

            // Check if map has any filth in home area
            if (pawn.Map.listerFilthInHomeArea.FilthInHomeArea.Count == 0)
            {
                return null;
            }

            return Utility_JobGiverManager.StandardTryGiveJob<Plant>(
                pawn,
                "Cleaning",
                (p, forced) => {
                    // Update fire cache
                    UpdateFilthCacheSafely(p.Map);

                    // Find and create a job for cutting plants with VALID DESIGNATORS ONLY
                    return TryCreateCleanFilthJob(pawn);
                },
                debugJobDesc: "filth cleaning assignment",
                skipEmergencyCheck: false);
        }

        /// <summary>
        /// Updates the cache of filth that needs cleaning
        /// </summary>
        private void UpdateFilthCacheSafely(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_filthCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_filthCache.ContainsKey(mapId))
                    _filthCache[mapId].Clear();
                else
                    _filthCache[mapId] = new List<Filth>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Filth, bool>();

                // Get filth from the home area
                foreach (Filth filth in map.listerFilthInHomeArea.FilthInHomeArea)
                {
                    if (filth != null && !filth.Destroyed && filth.TicksSinceThickened >= MIN_TICKS_SINCE_THICKENED)
                    {
                        _filthCache[mapId].Add(filth);
                    }
                }

                // Limit cache size for performance
                int maxCacheSize = 1000;
                if (_filthCache[mapId].Count > maxCacheSize)
                {
                    _filthCache[mapId] = _filthCache[mapId].Take(maxCacheSize).ToList();
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Creates a job for cleaning filth
        /// </summary>
        private Job TryCreateCleanFilthJob(Pawn pawn)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_filthCache.ContainsKey(mapId) || _filthCache[mapId].Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                _filthCache[mapId],
                (filth) => (filth.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Find a valid filth to clean
            for (int b = 0; b < buckets.Length; b++)
            {
                if (buckets[b].Count == 0)
                    continue;
                
                // Randomize within each bucket for even distribution
                buckets[b].Shuffle();
                
                foreach (Filth filth in buckets[b])
                {
                    // Skip if filth is invalid, not in home area, or can't be reserved
                    if (filth == null || filth.Destroyed || !pawn.Map.areaManager.Home[filth.Position] || 
                        !pawn.CanReserve(filth))
                        continue;

                    // Create a cleaning job with this filth as the primary target
                    Job job = JobMaker.MakeJob(JobDefOf.Clean);
                    job.AddQueuedTarget(TargetIndex.A, filth);
                    
                    // Find nearby filth to clean in the same job
                    AddNearbyFilthToQueue(pawn, filth, job);
                    
                    // If we have multiple filth targets, sort by distance from pawn
                    if (job.targetQueueA != null && job.targetQueueA.Count >= 5)
                    {
                        job.targetQueueA.SortBy(targ => targ.Cell.DistanceToSquared(pawn.Position));
                    }
                    
                    if (Prefs.DevMode)
                    {
                        Log.Message($"[PawnControl] {pawn.LabelShort} created job to clean {job.targetQueueA?.Count ?? 1} filth");
                    }
                    
                    return job;
                }
            }

            return null;
        }

        /// <summary>
        /// Adds nearby filth to the job queue for batched cleaning
        /// </summary>
        private void AddNearbyFilthToQueue(Pawn pawn, Filth primaryFilth, Job job)
        {
            int queuedCount = 0;
            Map map = primaryFilth.Map;
            Room primaryRoom = primaryFilth.GetRoom();
            
            // Search in a radial pattern around the primary filth
            for (int i = 0; i < 100 && queuedCount < MAX_FILTH_PER_JOB; i++)
            {
                IntVec3 cell = primaryFilth.Position + GenRadial.RadialPattern[i];
                if (ShouldCleanCell(cell, map, primaryRoom))
                {
                    List<Thing> thingsInCell = cell.GetThingList(map);
                    foreach (Thing thing in thingsInCell)
                    {
                        if (thing is Filth nearbyFilth && 
                            nearbyFilth != primaryFilth && 
                            map.areaManager.Home[nearbyFilth.Position] &&
                            pawn.CanReserve(nearbyFilth) &&
                            nearbyFilth.TicksSinceThickened >= MIN_TICKS_SINCE_THICKENED)
                        {
                            job.AddQueuedTarget(TargetIndex.A, nearbyFilth);
                            queuedCount++;
                            
                            if (queuedCount >= MAX_FILTH_PER_JOB)
                                break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Determines if a cell should be cleaned as part of the job
        /// </summary>
        private bool ShouldCleanCell(IntVec3 cell, Map map, Room primaryRoom)
        {
            if (!cell.InBounds(map))
                return false;
                
            // If the cell is in the same room, it's valid
            Room cellRoom = cell.GetRoom(map);
            if (cellRoom == primaryRoom)
                return true;
                
            // Check if the cell is a door that connects to the primary room
            Building_Door door = cell.GetDoor(map);
            if (door != null)
            {
                Region portalRegion = door.GetRegion(RegionType.Portal);
                if (portalRegion != null && !portalRegion.links.NullOrEmpty())
                {
                    foreach (RegionLink link in portalRegion.links)
                    {
                        for (int i = 0; i < 2; i++)
                        {
                            if (link.regions[i] != null && 
                                link.regions[i] != portalRegion &&
                                link.regions[i].valid && 
                                link.regions[i].Room == primaryRoom)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            
            return false;
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_filthCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_CleanFilth_PawnControl";
        }
    }
}