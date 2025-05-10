using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for cleaning pollution in designated areas
    /// Requires Biotech DLC
    /// </summary>
    public class JobModule_Cleaning_ClearPollution : JobModule_Cleaning
    {
        // Module metadata
        public override string UniqueID => "CleaningPollution";
        public override float Priority => 4.3f; // Lower priority than filth and snow cleaning
        public override string Category => "Cleaning";

        // Cache for cells with pollution that need cleaning
        private static readonly Dictionary<int, List<IntVec3>> _pollutionCellCache = new Dictionary<int, List<IntVec3>>();
        private static int _lastLocalUpdateTick = -999;

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 100f, 400f, 1600f }; // 10, 20, 40 tiles
        
        // Maximum number of pollution cells to process in a single job
        private const int MAX_POLLUTION_CELLS_PER_JOB = 15;

        /// <summary>
        /// Very early check if this module should even be considered
        /// </summary>
        public override bool ShouldSkipModule(Map map)
        {
            // Skip immediately if Biotech DLC isn't active - earliest possible exit
            if (!ModLister.CheckBiotech("Clear pollution"))
                return true;
            
            // Also skip if there are no pollution clear areas
            return map?.areaManager?.PollutionClear == null || 
                   map.areaManager.PollutionClear.TrueCount == 0;
        }

        public override bool ShouldClean(Thing target, Map map)
        {
            // Very early DLC check
            if (!ModLister.CheckBiotech("Clear pollution"))
                return false;
                
            // Since we're dealing with pollution, we need to check the cell
            if (target == null || map == null)
                return false;

            IntVec3 cell = target.Position;
            return ShouldCleanPollutionAt(cell, map);
        }

        /// <summary>
        /// Checks if a cell has pollution that needs to be cleared
        /// </summary>
        private bool ShouldCleanPollutionAt(IntVec3 cell, Map map)
        {
            if (!ModLister.CheckBiotech("Clear pollution"))
                return false;
                
            if (!cell.InBounds(map) || !map.areaManager.PollutionClear[cell])
                return false;

            return map.pollutionGrid.IsPolluted(cell);
        }

        public override bool ValidateCleaningJob(Thing target, Pawn cleaner)
        {
            // Very early DLC check
            if (!ModLister.CheckBiotech("Clear pollution"))
                return false;
                
            if (target == null || cleaner == null || cleaner.Map == null)
                return false;

            IntVec3 cell = target.Position;
            
            // Skip if cell is forbidden or not polluted
            if (cell.IsForbidden(cleaner) || !cleaner.Map.pollutionGrid.IsPolluted(cell))
                return false;
                
            // Skip if not in pollution clear area
            if (!cleaner.Map.areaManager.PollutionClear[cell])
                return false;

            // Skip if can't be reserved
            if (!cleaner.CanReserve(cell, 1, -1))
                return false;

            return true;
        }

        protected override Job CreateCleaningJob(Pawn cleaner, Thing target)
        {
            // Very early DLC check
            if (!ModLister.CheckBiotech("Clear pollution"))
                return null;
                
            if (target == null || cleaner == null || cleaner.Map == null)
                return null;

            IntVec3 targetCell = target.Position;
            
            // Create the pollution clearing job
            Job job = JobMaker.MakeJob(JobDefOf.ClearPollution, targetCell);
            
            // Find nearby pollution to clear in the same job
            AddNearbyPollutionToQueue(cleaner, targetCell, job);
            
            // If we have multiple pollution cell targets, sort by distance from pawn
            if (job.targetQueueA != null && job.targetQueueA.Count >= 5)
            {
                job.targetQueueA.SortBy(targ => targ.Cell.DistanceToSquared(cleaner.Position));
            }

            Utility_DebugManager.LogNormal($"{cleaner.LabelShort} created job to clear pollution at {targetCell} and {job.targetQueueA?.Count ?? 0} nearby cells");
            return job;
        }

        /// <summary>
        /// Adds nearby pollution cells to the job queue for batched cleaning
        /// </summary>
        private void AddNearbyPollutionToQueue(Pawn pawn, IntVec3 primaryCell, Job job)
        {
            // Very early DLC check
            if (!ModLister.CheckBiotech("Clear pollution"))
                return;
                
            int queuedCount = 0;
            Map map = pawn.Map;
            
            // Search in a radial pattern around the primary cell
            for (int i = 0; i < 100 && queuedCount < MAX_POLLUTION_CELLS_PER_JOB; i++)
            {
                IntVec3 cell = primaryCell + GenRadial.RadialPattern[i];
                if (cell != primaryCell && 
                    ShouldCleanPollutionAt(cell, map) && 
                    !cell.IsForbidden(pawn) && 
                    pawn.CanReserve(cell, 1, -1))
                {
                    job.AddQueuedTarget(TargetIndex.A, cell);
                    queuedCount++;
                    
                    if (queuedCount >= MAX_POLLUTION_CELLS_PER_JOB)
                        break;
                }
            }
        }

        /// <summary>
        /// Update the cache of cells with pollution that needs clearing
        /// </summary>
        public override void UpdateCache(Map map, List<Thing> targetCache)
        {
            if (map == null) return;

            // Very early DLC check - skip all processing if the DLC isn't present
            if (!ModLister.CheckBiotech("Clear pollution"))
            {
                SetHasTargets(map, false);
                return;
            }

            // Quick check if there's any pollution clear areas
            if (map.areaManager.PollutionClear == null || map.areaManager.PollutionClear.TrueCount == 0)
            {
                SetHasTargets(map, false);
                return;
            }

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastLocalUpdateTick + CacheUpdateInterval ||
                !_pollutionCellCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_pollutionCellCache.ContainsKey(mapId))
                    _pollutionCellCache[mapId].Clear();
                else
                    _pollutionCellCache[mapId] = new List<IntVec3>();

                // Get cells from the pollution clear area
                List<IntVec3> activeCells = map.areaManager.PollutionClear.ActiveCells.ToList();
                
                // Only cache cells that actually have pollution
                foreach (IntVec3 cell in activeCells)
                {
                    if (map.pollutionGrid.IsPolluted(cell))
                    {
                        _pollutionCellCache[mapId].Add(cell);
                        
                        // Add a "proxy thing" to the target cache for this cell
                        targetCache.Add(new PollutionCellProxy(cell, map));
                    }
                }

                _lastLocalUpdateTick = currentTick;
            }
            else
            {
                // Just add the cached cells to the target cache
                foreach (IntVec3 cell in _pollutionCellCache[mapId])
                {
                    // Skip cells that no longer have pollution
                    if (!map.pollutionGrid.IsPolluted(cell))
                        continue;

                    // Add a "proxy thing" to represent this cell
                    targetCache.Add(new PollutionCellProxy(cell, map));
                }
            }

            // Set whether we have any pollution targets
            SetHasTargets(map, targetCache.Count > 0);
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public override void ResetStaticData()
        {
            base.ResetStaticData();
            _pollutionCellCache.Clear();
            _lastLocalUpdateTick = -999;
        }

        /// <summary>
        /// Proxy class to represent a polluted cell as a Thing for the JobModule system
        /// </summary>
        private class PollutionCellProxy : ThingWithComps
        {
            public PollutionCellProxy(IntVec3 cell, Map map)
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