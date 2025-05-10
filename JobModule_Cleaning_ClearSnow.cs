using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for cleaning snow in designated areas
    /// </summary>
    public class JobModule_Cleaning_ClearSnow : JobModule_Cleaning
    {
        // Module metadata
        public override string UniqueID => "CleaningSnow";
        public override float Priority => 4.5f; // Same as original JobGiver, lower priority than filth cleaning
        public override string Category => "Cleaning";

        // Constants
        private const float MIN_SNOW_DEPTH = 0.2f; // Minimum snow depth to clear
        private const int MAX_SNOW_CELLS_PER_JOB = 15;

        // Cache for cells with snow that need clearing
        private static readonly Dictionary<int, List<IntVec3>> _snowCellCache = new Dictionary<int, List<IntVec3>>();
        private static int _lastLocalUpdateTick = -999;

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 100f, 400f, 1600f }; // 10, 20, 40 tiles

        public override bool ShouldClean(Thing target, Map map)
        {
            // Since we're dealing with snow, we need to convert the thing to its cell location
            if (target == null || map == null)
                return false;

            IntVec3 cell = target.Position;
            return ShouldCleanSnowAt(cell, map);
        }

        /// <summary>
        /// Checks if a cell has snow that needs to be cleared
        /// </summary>
        private bool ShouldCleanSnowAt(IntVec3 cell, Map map)
        {
            if (!cell.InBounds(map) || !map.areaManager.SnowClear[cell])
                return false;

            return map.snowGrid.GetDepth(cell) >= MIN_SNOW_DEPTH;
        }

        public override bool ValidateCleaningJob(Thing target, Pawn cleaner)
        {
            if (target == null || cleaner == null || cleaner.Map == null)
                return false;

            IntVec3 cell = target.Position;

            // Skip if not in snow clear area or can't be reserved
            if (!ShouldCleanSnowAt(cell, cleaner.Map) || !cleaner.CanReserve(cell))
                return false;

            return true;
        }

        protected override Job CreateCleaningJob(Pawn cleaner, Thing target)
        {
            if (target == null || cleaner == null || cleaner.Map == null)
                return null;

            IntVec3 targetCell = target.Position;

            // Create the snow clearing job
            Job job = JobMaker.MakeJob(JobDefOf.ClearSnow, targetCell);

            // Find nearby snow to clear in the same job
            AddNearbySnowToQueue(cleaner, targetCell, job);

            // If we have multiple snow cell targets, sort by distance from pawn
            if (job.targetQueueA != null && job.targetQueueA.Count >= 5)
            {
                job.targetQueueA.SortBy(targ => targ.Cell.DistanceToSquared(cleaner.Position));
            }

            Utility_DebugManager.LogNormal($"{cleaner.LabelShort} created job to clear snow at {targetCell} and {job.targetQueueA?.Count ?? 0} nearby cells");
            return job;
        }

        /// <summary>
        /// Adds nearby snow cells to the job queue for batched cleaning
        /// </summary>
        private void AddNearbySnowToQueue(Pawn pawn, IntVec3 primaryCell, Job job)
        {
            int queuedCount = 0;
            Map map = pawn.Map;

            // Search in a radial pattern around the primary cell
            for (int i = 0; i < 100 && queuedCount < MAX_SNOW_CELLS_PER_JOB; i++)
            {
                IntVec3 cell = primaryCell + GenRadial.RadialPattern[i];
                if (cell != primaryCell && ShouldCleanSnowAt(cell, map) && pawn.CanReserve(cell))
                {
                    job.AddQueuedTarget(TargetIndex.A, cell);
                    queuedCount++;

                    if (queuedCount >= MAX_SNOW_CELLS_PER_JOB)
                        break;
                }
            }
        }

        /// <summary>
        /// Update the cache of cells with snow that needs clearing
        /// </summary>
        public override void UpdateCache(Map map, List<Thing> targetCache)
        {
            if (map == null) return;

            // Quick check if there's any snow clear areas
            if (map.areaManager.SnowClear.TrueCount == 0)
            {
                SetHasTargets(map, false);
                return;
            }

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastLocalUpdateTick + CacheUpdateInterval ||
                !_snowCellCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_snowCellCache.ContainsKey(mapId))
                    _snowCellCache[mapId].Clear();
                else
                    _snowCellCache[mapId] = new List<IntVec3>();

                // Get cells from the snow clear area
                List<IntVec3> activeCells = map.areaManager.SnowClear.ActiveCells.ToList();

                // Only cache cells that actually have enough snow to clear
                foreach (IntVec3 cell in activeCells)
                {
                    if (map.snowGrid.GetDepth(cell) >= MIN_SNOW_DEPTH)
                    {
                        _snowCellCache[mapId].Add(cell);

                        // Add a "proxy thing" to the target cache for this cell
                        // We use a temporary ThingWithComps to represent the cell since JobModule works with Things
                        targetCache.Add(new SnowCellProxy(cell, map));
                    }
                }

                _lastLocalUpdateTick = currentTick;
            }
            else
            {
                // Just add the cached cells to the target cache
                foreach (IntVec3 cell in _snowCellCache[mapId])
                {
                    // Skip cells that no longer have enough snow
                    if (map.snowGrid.GetDepth(cell) < MIN_SNOW_DEPTH)
                        continue;

                    // Add a "proxy thing" to represent this cell
                    targetCache.Add(new SnowCellProxy(cell, map));
                }
            }

            // Set whether we have any snow targets
            SetHasTargets(map, targetCache.Count > 0);
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public override void ResetStaticData()
        {
            base.ResetStaticData();
            _snowCellCache.Clear();
            _lastLocalUpdateTick = -999;
        }

        /// <summary>
        /// Proxy class to represent a snow-covered cell as a Thing for the JobModule system
        /// </summary>
        private class SnowCellProxy : ThingWithComps
        {
            public SnowCellProxy(IntVec3 cell, Map map)
            {
                // Initialize with the cell position
                this.Position = cell;

                // Use SpawnSetup to properly initialize the map reference
                // instead of directly assigning to Map property
                this.def = ThingDefOf.Filth_Dirt; // Use any existing ThingDef as placeholder
                this.SpawnSetup(map, false);
            }

            // Prevent this proxy from being destroyed or showing up in lists
            public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish) { }
            public override void Destroy(DestroyMode mode = DestroyMode.Vanish) { }
        }
    }
}