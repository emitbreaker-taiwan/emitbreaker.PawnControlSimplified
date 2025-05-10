using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Base abstract class for all cleaning job modules
    /// </summary>
    public abstract class JobModule_Cleaning : JobModule<Thing>
    {
        // Simple fixed priority (higher number = higher priority)
        public override float Priority => 3.5f;

        /// <summary>
        /// Gets the work type this module is associated with
        /// </summary>
        public override string WorkTypeName => "Cleaning";

        /// <summary>
        /// Fast filter check for cleaners
        /// </summary>
        public override bool QuickFilterCheck(Pawn pawn)
        {
            return !pawn.WorkTagIsDisabled(WorkTags.Cleaning);
        }

        public override bool WorkTypeApplies(Pawn pawn)
        {
            // Quick check based on work settings
            return pawn?.workSettings?.WorkIsActive(WorkTypeDefOf.Cleaning) == true;
        }

        /// <summary>
        /// Default cache update interval - 7 seconds for cleaning jobs
        /// </summary>
        public override int CacheUpdateInterval => 420; // Update every 7 seconds

        /// <summary>
        /// Filter function to identify targets for cleaning (specifically named for cleaning jobs)
        /// </summary>
        public abstract bool ShouldClean(Thing target, Map map);

        /// <summary>
        /// Filter function implementation that calls the cleaning-specific method
        /// </summary>
        public override bool ShouldProcessTarget(Thing target, Map map)
            => ShouldClean(target, map);

        /// <summary>
        /// Validates if the pawn can perform this cleaning job on the target
        /// </summary>
        public abstract bool ValidateCleaningJob(Thing target, Pawn cleaner);

        /// <summary>
        /// Validates job implementation that calls the cleaning-specific method
        /// </summary>
        public override bool ValidateJob(Thing target, Pawn actor)
            => ValidateCleaningJob(target, actor);

        /// <summary>
        /// Creates the job for the cleaner to perform on the target
        /// </summary>
        public override Job CreateJob(Pawn actor, Thing target)
            => CreateCleaningJob(actor, target);

        /// <summary>
        /// Cleaning-specific implementation of job creation
        /// </summary>
        protected abstract Job CreateCleaningJob(Pawn cleaner, Thing target);

        /// <summary>
        /// Helper method to check if a cell is in home area and needs cleaning
        /// </summary>
        protected bool CellNeedsCleaning(IntVec3 cell, Map map)
        {
            if (!cell.InBounds(map) || !map.areaManager.Home[cell])
                return false;

            // Check for filth in the cell
            List<Thing> things = cell.GetThingList(map);
            foreach (Thing thing in things)
            {
                if (thing is Filth filth && !filth.Destroyed)
                {
                    return true;
                }
            }

            // Check for snow in the cell if it's in snow clear area
            if (map.areaManager.SnowClear[cell] && map.snowGrid.GetDepth(cell) >= 0.2f)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Helper method to check if pawn can perform cleaning work
        /// </summary>
        protected bool CanCleanAt(IntVec3 cell, Pawn cleaner)
        {
            if (!cell.InBounds(cleaner.Map) || cell.IsForbidden(cleaner))
                return false;

            // Check if cell can be reserved
            if (!cleaner.CanReserve(cell))
                return false;

            // Check if pawn can reach the cell
            return cleaner.CanReach(cell, PathEndMode.Touch, cleaner.NormalMaxDanger());
        }

        /// <summary>
        /// Helper method to find nearby cleaning targets for batching
        /// </summary>
        protected void FindNearbyCleaningTargets(Thing primaryTarget, Pawn cleaner, Job job, int maxTargets = 15)
        {
            // Skip if we don't have a queue system or already reached max targets
            if (job.targetQueueA == null || maxTargets <= 0)
                return;

            int queuedCount = 0;
            Room primaryRoom = primaryTarget.GetRoom();
            IntVec3 primaryPos = primaryTarget.Position;
            Map map = primaryTarget.Map;
            bool isFilthJob = primaryTarget is Filth;
            bool isSnowJob = job.def == JobDefOf.ClearSnow;

            // Search in a radial pattern around the primary target
            for (int i = 0; i < 100 && queuedCount < maxTargets; i++)
            {
                IntVec3 cell = primaryPos + GenRadial.RadialPattern[i];
                if (!cell.InBounds(map) || cell.IsForbidden(cleaner))
                    continue;

                // For filth jobs, stay in the same room or connected rooms
                if (isFilthJob)
                {
                    Room cellRoom = cell.GetRoom(map);
                    if (cellRoom != primaryRoom && !IsConnectedRoom(cellRoom, primaryRoom, map))
                        continue;
                }
                // For snow jobs, stay within the snow clear area
                else if (isSnowJob)
                {
                    if (!map.areaManager.SnowClear[cell] || map.snowGrid.GetDepth(cell) < 0.2f)
                        continue;
                }

                // Find additional targets in the cell
                List<Thing> thingsInCell = cell.GetThingList(map);
                foreach (Thing thing in thingsInCell)
                {
                    if ((isFilthJob && thing is Filth) || 
                        (isSnowJob && map.snowGrid.GetDepth(cell) >= 0.2f))
                    {
                        if (thing != primaryTarget &&
                            ShouldClean(thing, map) &&
                            cleaner.CanReserve(thing))
                        {
                            job.AddQueuedTarget(TargetIndex.A, thing);
                            queuedCount++;

                            if (queuedCount >= maxTargets)
                                break;
                        }
                    }
                }

                // For snow jobs, add the cell itself
                if (isSnowJob && queuedCount < maxTargets && 
                    ShouldCleanCell(cell, map) && 
                    cleaner.CanReserve(cell))
                {
                    LocalTargetInfo targetInfo = new LocalTargetInfo(cell);
                    job.AddQueuedTarget(TargetIndex.A, targetInfo);
                    queuedCount++;
                }
            }

            // Sort targets by distance from pawn for efficiency
            if (job.targetQueueA != null && job.targetQueueA.Count >= 5)
            {
                job.targetQueueA.SortBy(targ => targ.Cell.DistanceToSquared(cleaner.Position));
            }
        }

        /// <summary>
        /// Check if a cell should be cleaned
        /// </summary>
        protected virtual bool ShouldCleanCell(IntVec3 cell, Map map)
        {
            // Base implementation - override in specific modules
            return cell.InBounds(map) && map.areaManager.Home[cell];
        }

        /// <summary>
        /// Helper method to determine if two rooms are connected
        /// </summary>
        private bool IsConnectedRoom(Room roomA, Room roomB, Map map)
        {
            if (roomA == null || roomB == null)
                return false;

            if (roomA == roomB)
                return true;

            // Check if the room is connected via a door
            foreach (IntVec3 doorCell in roomA.Cells.Where(c => c.GetDoor(map) != null))
            {
                Building_Door door = doorCell.GetDoor(map);
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
                                    link.regions[i].Room == roomB)
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Default cache update for cleaning targets
        /// </summary>
        public override void UpdateCache(Map map, List<Thing> targetCache)
        {
            if (map == null) return;

            // Use progressive cache update with the appropriate filter
            UpdateCacheProgressively(
                map,
                targetCache,
                ref _lastUpdateTick,
                RelevantThingRequestGroups,
                thing => ShouldProcessTarget(thing, map),
                null,
                CacheUpdateInterval
            );
        }

        // Track last update tick for progressive updates
        private static int _lastUpdateTick = -999;

        /// <summary>
        /// Reset any static data when language changes or game reloads
        /// </summary>
        public override void ResetStaticData()
        {
            _lastUpdateTick = -999;
        }

        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups { get; } =
            new HashSet<ThingRequestGroup> {
                ThingRequestGroup.Filth,  // For clean filth jobs
                ThingRequestGroup.BuildingArtificial // For snow cleaning (roofs)
            };
    }
}