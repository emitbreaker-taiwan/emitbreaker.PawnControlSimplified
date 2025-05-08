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
    /// JobGiver that assigns tasks to clear snow from designated areas.
    /// Uses the Cleaning work tag for eligibility checking.
    /// </summary>
    public class JobGiver_ClearSnow_PawnControl : ThinkNode_JobGiver
    {
        // Cache for cells with snow that need clearing
        private static readonly Dictionary<int, List<IntVec3>> _snowCellCache = new Dictionary<int, List<IntVec3>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 600; // Update every 10 seconds (snow changes slowly)
        private const float MIN_SNOW_DEPTH = 0.2f; // Minimum snow depth to clear

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 100f, 400f, 1600f }; // 10, 20, 40 tiles

        public override float GetPriority(Pawn pawn)
        {
            // Snow clearing is lower priority than most tasks
            return 4.5f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // IMPORTANT: Only player pawns and slaves owned by player should clear snow
            if (pawn.Faction != Faction.OfPlayer &&
                !(pawn.IsSlave && pawn.HostFaction == Faction.OfPlayer))
                return null;

            // Check if map has any designated snow clear areas
            if (pawn.Map.areaManager.SnowClear.TrueCount == 0)
            {
                return null;
            }

            return Utility_JobGiverManager.StandardTryGiveJob<Plant>(
                pawn,
                "Cleaning",
                (p, forced) => {
                    // Update fire cache
                    UpdateSnowCellsCacheSafely(p.Map);

                    // Find and create a job for cutting plants with VALID DESIGNATORS ONLY
                    return TryCreateSnowClearingJob(pawn);
                },
                debugJobDesc: "snow cleaning assignment",
                skipEmergencyCheck: false);
        }

        /// <summary>
        /// Updates the cache of cells that have snow needing to be cleared
        /// </summary>
        private void UpdateSnowCellsCacheSafely(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
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
                    }
                }

                // Limit cache size for performance
                int maxCacheSize = 1000;
                if (_snowCellCache[mapId].Count > maxCacheSize)
                {
                    _snowCellCache[mapId] = _snowCellCache[mapId].Take(maxCacheSize).ToList();
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Creates a job for clearing snow
        /// </summary>
        private Job TryCreateSnowClearingJob(Pawn pawn)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_snowCellCache.ContainsKey(mapId) || _snowCellCache[mapId].Count == 0)
                return null;

            // Use distance bucketing for efficient cell selection
            List<IntVec3> cellsInReach = new List<IntVec3>();
            
            foreach (IntVec3 cell in _snowCellCache[mapId])
            {
                // Skip if the cell is forbidden
                if (cell.IsForbidden(pawn))
                    continue;
                
                // Skip if snow depth is now below threshold (might have melted)
                if (pawn.Map.snowGrid.GetDepth(cell) < MIN_SNOW_DEPTH)
                    continue;
                
                // Skip if we can't reserve the cell
                if (!pawn.CanReserve(cell))
                    continue;
                
                // Calculate distance
                int distanceSq = (cell - pawn.Position).LengthHorizontalSquared;
                
                // Only include cells within a reasonable range
                if (distanceSq <= DISTANCE_THRESHOLDS[DISTANCE_THRESHOLDS.Length - 1])
                {
                    cellsInReach.Add(cell);
                }
            }
            
            if (cellsInReach.Count == 0)
                return null;
            
            // Create distance buckets
            List<IntVec3>[] buckets = new List<IntVec3>[DISTANCE_THRESHOLDS.Length + 1];
            for (int i = 0; i < buckets.Length; i++)
            {
                buckets[i] = new List<IntVec3>();
            }
            
            // Sort cells into buckets by distance
            foreach (IntVec3 cell in cellsInReach)
            {
                float distanceSq = (cell - pawn.Position).LengthHorizontalSquared;
                
                int bucketIndex = 0;
                while (bucketIndex < DISTANCE_THRESHOLDS.Length && distanceSq > DISTANCE_THRESHOLDS[bucketIndex])
                {
                    bucketIndex++;
                }
                
                buckets[bucketIndex].Add(cell);
            }
            
            // Find the closest valid cell with sufficient snow
            IntVec3 targetCell = IntVec3.Invalid;
            
            for (int b = 0; b < buckets.Length; b++)
            {
                if (buckets[b].Count == 0)
                    continue;
                
                // Randomize within each bucket for even distribution
                buckets[b].Shuffle();
                
                foreach (IntVec3 cell in buckets[b])
                {
                    // Final validation - snow depth might have changed since we cached
                    if (pawn.Map.snowGrid.GetDepth(cell) >= MIN_SNOW_DEPTH &&
                        !cell.IsForbidden(pawn) && 
                        pawn.CanReserve(cell))
                    {
                        targetCell = cell;
                        break;
                    }
                }
                
                if (targetCell.IsValid)
                    break;
            }
            
            if (!targetCell.IsValid)
                return null;
            
            // Create the snow clearing job
            Job job = JobMaker.MakeJob(JobDefOf.ClearSnow, targetCell);

            Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to clear snow at {targetCell}");
            return job;
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            _snowCellCache.Clear();
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_ClearSnow_PawnControl";
        }
    }
}