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
    /// JobGiver that allows non-humanlike pawns to remove roofs in designated areas.
    /// Uses the Construction work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Construction_RemoveRoof_PawnControl : ThinkNode_JobGiver
    {
        // Cache for cells marked for roof removal - directly using IntVec3 like BuildRoof
        private static readonly Dictionary<int, List<IntVec3>> _roofRemovalCellsCache = new Dictionary<int, List<IntVec3>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 250; // Update every ~4 seconds

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 2500f }; // 15, 25, 50 tiles

        public override float GetPriority(Pawn pawn)
        {
            // Roof removal is slightly more important than building
            return 5.6f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Quick early exit if there are no roof removal areas on the map
            if (pawn?.Map == null || pawn.Map.areaManager.NoRoof == null || pawn.Map.areaManager.NoRoof.TrueCount == 0)
                return null;

            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null)
                return null;

            if (!Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_Construction.ToString()))
                return null;

            // Check work type
            if (pawn.WorkTypeIsDisabled(WorkTypeDefOf.Construction))
                return null;

            // Check for emergency states that would prevent this job
            if (pawn.Drafted || pawn.mindState.anyCloseHostilesRecently || pawn.InMentalState)
                return null;

            // Only player faction pawns can handle roof designation tasks
            if (pawn.Faction != Faction.OfPlayer)
                return null;

            // Update cache
            UpdateRoofRemovalCellsCache(pawn.Map);

            // Find and create job for removing roofs
            Job job = TryCreateRoofRemovalJob(pawn, false);

            if (job != null)
            {
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to remove roof");
            }

            return job;
        }

        /// <summary>
        /// Updates the cache of cells marked for roof removal
        /// </summary>
        private void UpdateRoofRemovalCellsCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_roofRemovalCellsCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_roofRemovalCellsCache.ContainsKey(mapId))
                    _roofRemovalCellsCache[mapId].Clear();
                else
                    _roofRemovalCellsCache[mapId] = new List<IntVec3>();

                // Find all cells marked for roof removal
                if (map.areaManager.NoRoof != null)
                {
                    // Create list of all active no-roof cells
                    foreach (IntVec3 cell in map.areaManager.NoRoof.ActiveCells)
                    {
                        // Skip if not roofed - only remove existing roofs
                        if (!cell.Roofed(map))
                            continue;

                        // Add to cache
                        _roofRemovalCellsCache[mapId].Add(cell);
                    }
                }

                // Limit cache size for performance
                int maxCacheSize = 500;
                if (_roofRemovalCellsCache[mapId].Count > maxCacheSize)
                {
                    _roofRemovalCellsCache[mapId] = _roofRemovalCellsCache[mapId].Take(maxCacheSize).ToList();
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Creates a job for removing roofs in the nearest valid location
        /// </summary>
        private Job TryCreateRoofRemovalJob(Pawn pawn, bool forced)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_roofRemovalCellsCache.ContainsKey(mapId) || _roofRemovalCellsCache[mapId].Count == 0)
                return null;

            // Create distance-based buckets manually since we can't use the generic utilities with IntVec3
            List<IntVec3>[] buckets = new List<IntVec3>[DISTANCE_THRESHOLDS.Length + 1];
            for (int i = 0; i < buckets.Length; i++)
                buckets[i] = new List<IntVec3>();

            // Sort cells into buckets by distance
            foreach (IntVec3 cell in _roofRemovalCellsCache[mapId])
            {
                float distSq = (cell - pawn.Position).LengthHorizontalSquared;
                int bucketIndex = buckets.Length - 1;

                for (int i = 0; i < DISTANCE_THRESHOLDS.Length; i++)
                {
                    if (distSq <= DISTANCE_THRESHOLDS[i])
                    {
                        bucketIndex = i;
                        break;
                    }
                }

                buckets[bucketIndex].Add(cell);
            }

            // Process each bucket in order
            for (int i = 0; i < buckets.Length; i++)
            {
                if (buckets[i].Count == 0)
                    continue;

                // Sort by priority within the bucket
                if (buckets[i].Count > 1)
                {
                    buckets[i].Sort((a, b) => GetCellPriority(a, pawn.Map).CompareTo(GetCellPriority(b, pawn.Map)));
                }

                // Randomize cells with same priority
                buckets[i].Shuffle();

                foreach (IntVec3 cell in buckets[i])
                {
                    // Filter out invalid cells immediately
                    if (!pawn.Map.areaManager.NoRoof[cell] || !cell.Roofed(pawn.Map))
                        continue;

                    // Skip if forbidden or unreachable
                    if (cell.IsForbidden(pawn) ||
                        !pawn.CanReserve(cell) ||
                        !pawn.CanReach(cell, PathEndMode.Touch, pawn.NormalMaxDanger()))
                        continue;

                    // Create the remove roof job
                    Job job = JobMaker.MakeJob(JobDefOf.RemoveRoof);
                    job.targetA = cell;
                    job.targetB = cell;
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to remove roof at {cell}");
                    return job;
                }
            }

            return null;
        }

        /// <summary>
        /// Calculates roof removal priority based on proximity to other roofed cells
        /// </summary>
        private float GetCellPriority(IntVec3 cell, Map map)
        {
            // Logic from vanilla WorkGiver_RemoveRoof:
            // - Prioritize cells NOT adjacent to support structures
            // - Prioritize cells with fewer adjacent roofed cells

            int adjacentRoofedCellsCount = 0;
            for (int i = 0; i < 8; ++i)
            {
                IntVec3 c = cell + GenAdj.AdjacentCells[i];
                if (c.InBounds(map))
                {
                    Building edifice = c.GetEdifice(map);
                    if (edifice != null && edifice.def.holdsRoof)
                        return -60f; // De-prioritize cells next to roof supports

                    if (c.Roofed(map))
                        ++adjacentRoofedCellsCount;
                }
            }

            // Prioritize cells with fewer adjacent roofed cells
            return -Mathf.Min(adjacentRoofedCellsCount, 3);
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            _roofRemovalCellsCache.Clear();
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_RemoveRoof_PawnControl";
        }
    }
}