using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to clean filth in the home area.
    /// Uses the Cleaning work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Cleaning_CleanFilth_PawnControl : JobGiver_Scan_PawnControl
    {
        #region Configuration

        // Constants
        private const int MIN_TICKS_SINCE_THICKENED = 600;
        private const int MAX_FILTH_PER_JOB = 15;
        private const int MAX_SEARCH_RADIUS = 10;

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 100f, 400f, 1600f }; // 10, 20, 40 tiles

        #endregion

        #region Overrides

        protected override string WorkTag => "Cleaning";

        protected override string DebugName => "FilthCleaning";

        protected override int CacheUpdateInterval => 450; // Update every 7.5 seconds

        public override float GetPriority(Pawn pawn)
        {
            // Cleaning is lower priority than most tasks but more important than snow clearing
            return 4.7f;
        }

        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map?.listerFilthInHomeArea == null)
                return Enumerable.Empty<Thing>();

            return map.listerFilthInHomeArea.FilthInHomeArea
                .Where(f => f != null && !f.Destroyed && f is Filth filth && filth.TicksSinceThickened >= MIN_TICKS_SINCE_THICKENED)
                .Take(1000);  // No need for Cast<Thing>() since FilthInHomeArea already contains Thing objects
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Use the StandardTryGiveJob pattern that works
            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Cleaning_CleanFilth_PawnControl>(
                pawn,
                WorkTag,  // Use WorkTag property for consistency
                (p, forced) => {
                    // IMPORTANT: Only player pawns and slaves owned by player should clean filth
                    if (p.Faction != Faction.OfPlayer &&
                        !(p.IsSlave && p.HostFaction == Faction.OfPlayer))
                        return null;

                    // Check if map has any filth in home area
                    if (p?.Map?.listerFilthInHomeArea == null ||
                        p.Map.listerFilthInHomeArea.FilthInHomeArea.Count == 0)
                        return null;

                    // Get targets from the base class cache
                    List<Thing> targets = GetTargets(p.Map).ToList();

                    // Return filth cleaning job
                    return TryCreateCleanFilthJob(p, targets);
                },
                debugJobDesc: "filth cleaning assignment");
        }

        /// <summary>
        /// Creates a job for the pawn using the cached targets
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // Skip if no targets
            if (targets == null || targets.Count == 0)
                return null;

            // Use the existing logic for cleaning filth
            return TryCreateCleanFilthJob(pawn, targets);
        }

        #endregion

        #region Filth-cleaning helpers

        /// <summary>
        /// Creates a job for cleaning filth
        /// </summary>
        private Job TryCreateCleanFilthJob(Pawn pawn, List<Thing> filthTargets)
        {
            if (pawn?.Map == null || filthTargets == null || filthTargets.Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                filthTargets,
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

                foreach (Thing t in buckets[b])
                {
                    Filth filth = t as Filth;
                    if (!ValidateFilthTarget(filth, pawn))
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

                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to clean {job.targetQueueA?.Count ?? 1} filth");
                    return job;
                }
            }

            return null;
        }

        /// <summary>
        /// Validates if a filth target is valid for cleaning
        /// </summary>
        private bool ValidateFilthTarget(Filth filth, Pawn pawn)
        {
            return filth != null &&
                   !filth.Destroyed &&
                   filth.Spawned &&
                   filth.TicksSinceThickened >= MIN_TICKS_SINCE_THICKENED &&
                   pawn.Map.areaManager.Home[filth.Position] &&
                   !filth.IsForbidden(pawn) &&
                   pawn.CanReserve(filth);
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
                        if (thing is Filth nearbyFilth && ValidateFilthTarget(nearbyFilth, pawn) && nearbyFilth != primaryFilth)
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

        #endregion

        #region Utility

        public override string ToString()
        {
            return "JobGiver_Cleaning_CleanFilth_PawnControl";
        }

        #endregion
    }
}