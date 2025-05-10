using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for cleaning filth in home areas
    /// </summary>
    public class JobModule_Cleaning_CleanFilth : JobModule_Cleaning
    {
        // Module metadata
        public override string UniqueID => "CleaningFilth";
        public override float Priority => 4.7f; // Same as original JobGiver
        public override string Category => "Cleaning";

        // Constants
        private const int MIN_TICKS_SINCE_THICKENED = 600;
        private const int MAX_FILTH_PER_JOB = 15;

        // Cache for filth that needs cleaning
        private static readonly Dictionary<int, List<Filth>> _filthCache = new Dictionary<int, List<Filth>>();
        private static readonly Dictionary<int, Dictionary<Filth, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Filth, bool>>();
        private static int _lastLocalUpdateTick = -999;

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 100f, 400f, 1600f }; // 10, 20, 40 tiles

        public override bool ShouldClean(Thing target, Map map)
        {
            // Only process Filth type targets
            if (!(target is Filth filth) || filth.Destroyed)
                return false;

            // Only clean filth in home area that's been around a bit
            return map.areaManager.Home[filth.Position] && 
                   filth.TicksSinceThickened >= MIN_TICKS_SINCE_THICKENED;
        }

        public override bool ValidateCleaningJob(Thing target, Pawn cleaner)
        {
            if (!(target is Filth filth) || filth.Destroyed)
                return false;

            // Skip if not in home area or can't be reserved
            if (!cleaner.Map.areaManager.Home[filth.Position] || !cleaner.CanReserve(filth))
                return false;

            return true;
        }

        protected override Job CreateCleaningJob(Pawn cleaner, Thing target)
        {
            if (!(target is Filth filth) || filth.Destroyed)
                return null;

            // Create a cleaning job with this filth as the primary target
            Job job = JobMaker.MakeJob(JobDefOf.Clean);
            job.AddQueuedTarget(TargetIndex.A, filth);
            
            // Find nearby filth to clean in the same job
            AddNearbyFilthToQueue(cleaner, filth, job);
            
            // If we have multiple filth targets, sort by distance from pawn
            if (job.targetQueueA != null && job.targetQueueA.Count >= 5)
            {
                job.targetQueueA.SortBy(targ => targ.Cell.DistanceToSquared(cleaner.Position));
            }

            Utility_DebugManager.LogNormal($"{cleaner.LabelShort} created job to clean {job.targetQueueA?.Count ?? 1} filth");
            return job;
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
                            ShouldClean(nearbyFilth, map) &&
                            pawn.CanReserve(nearbyFilth))
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
        /// Update the cache of filth that needs cleaning
        /// </summary>
        public override void UpdateCache(Map map, List<Thing> targetCache)
        {
            if (map == null) return;
            
            // Quick check if there's any filth in home area
            if (map.listerFilthInHomeArea.FilthInHomeArea.Count == 0)
            {
                SetHasTargets(map, false);
                return;
            }

            // Use progressive cache update approach
            UpdateCacheProgressively(
                map,
                targetCache,
                ref _lastLocalUpdateTick,
                RelevantThingRequestGroups,
                thing => ShouldProcessTarget(thing, map),
                null,
                CacheUpdateInterval
            );

            // Set whether we have any filth targets
            SetHasTargets(map, targetCache.Count > 0);
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public override void ResetStaticData()
        {
            base.ResetStaticData();
            Utility_CacheManager.ResetJobGiverCache(_filthCache, _reachabilityCache);
            _lastLocalUpdateTick = -999;
        }
    }
}