using RimWorld;
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
    public class JobGiver_Cleaning_CleanFilth_PawnControl : JobGiver_Cleaning_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        public override bool RequiresMapZoneorArea => true;

        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.Clean;

        // Constants
        private const int MIN_TICKS_SINCE_THICKENED = 600;

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "FilthCleaning";

        /// <summary>
        /// Update cache every 7.5 seconds
        /// </summary>
        protected override int CacheUpdateInterval => base.CacheUpdateInterval;

        /// <summary>
        /// FilthCleaning is considered medium-low priority
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            return 4.7f;
        }

        /// <summary>
        /// Maximum filth per job
        /// </summary>
        protected override int MaxItemsPerJob => 15;

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_Cleaning_CleanFilth_PawnControl() : base()
        {
            // Base constructor already initializes the cache system
        }

        #endregion

        #region Target Selection

        /// <summary>
        /// Job-specific cache update method that implements specialized filth collection logic
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            if (map?.listerFilthInHomeArea == null)
                return Enumerable.Empty<Thing>();

            return map.listerFilthInHomeArea.FilthInHomeArea
                .Where(f => f != null && !f.Destroyed && f is Filth filth && filth.TicksSinceThickened >= MIN_TICKS_SINCE_THICKENED)
                .Take(1000);
        }

        /// <summary>
        /// Get all filth in the home area as targets
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // This is now primarily used by the cache update system
            return UpdateJobSpecificCache(map);
        }

        /// <summary>
        /// Check if the map has filth that needs cleaning
        /// </summary>
        protected override bool AreMapRequirementsMet(Pawn pawn)
        {
            // Make sure the map has filth in the home area
            return pawn?.Map?.listerFilthInHomeArea != null &&
                   pawn.Map.listerFilthInHomeArea.FilthInHomeArea.Count > 0;
        }

        /// <summary>
        /// Filth cleaning uses Thing targets
        /// </summary>
        protected override bool RequiresThingTargets()
        {
            return true;
        }

        #endregion

        #region Job Creation

        /// <summary>
        /// Creates a job for cleaning filth
        /// </summary>
        protected override Job CreateCleaningJob(Pawn pawn, bool forced)
        {
            if (pawn?.Map == null)
                return null;

            int mapId = pawn.Map.uniqueID;

            // Get targets from the centralized cache
            List<Thing> filthTargets = GetCachedTargets(mapId);

            // If cache is empty, try direct acquisition
            if (filthTargets == null || filthTargets.Count == 0)
            {
                // Update the cache if needed
                if (ShouldUpdateCache(mapId))
                {
                    UpdateCache(mapId, pawn.Map);
                    filthTargets = GetCachedTargets(mapId);
                }
                else
                {
                    // Direct acquisition as fallback
                    filthTargets = GetTargets(pawn.Map).ToList();
                }
            }

            if (filthTargets == null || filthTargets.Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                filthTargets,
                (filth) => (filth.Position - pawn.Position).LengthHorizontalSquared,
                DistanceThresholds
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
                    Job job = JobMaker.MakeJob(WorkJobDef);
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

        #endregion

        #region Validation

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

        #endregion

        #region Helper Methods

        /// <summary>
        /// Adds nearby filth to the job queue for batched cleaning
        /// </summary>
        private void AddNearbyFilthToQueue(Pawn pawn, Filth primaryFilth, Job job)
        {
            int queuedCount = 0;
            Map map = primaryFilth.Map;
            Room primaryRoom = primaryFilth.GetRoom();

            // Search in a radial pattern around the primary filth
            for (int i = 0; i < 100 && queuedCount < MaxItemsPerJob; i++)
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

                            if (queuedCount >= MaxItemsPerJob)
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

        #region Reset

        /// <summary>
        /// Reset the cache - implements IResettableCache
        /// </summary>
        public override void Reset()
        {
            // Use centralized cache reset
            base.Reset();
        }

        #endregion
    }
}