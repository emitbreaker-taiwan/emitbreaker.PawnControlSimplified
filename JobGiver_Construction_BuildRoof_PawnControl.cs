using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that allows non-humanlike pawns to build roofs in designated areas.
    /// Uses the Construction work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Construction_BuildRoof_PawnControl : ThinkNode_JobGiver
    {
        // Cache for cells marked for roof building
        private static readonly Dictionary<int, List<IntVec3>> _roofBuildCellsCache = new Dictionary<int, List<IntVec3>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 250; // Update every ~4 seconds

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 2500f }; // 15, 25, 50 tiles

        public override float GetPriority(Pawn pawn)
        {
            // Roofing is moderately important construction task
            return 5.5f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Quick early exit if there are no roof areas on the map
            if (pawn?.Map == null || pawn.Map.areaManager.BuildRoof == null || pawn.Map.areaManager.BuildRoof.TrueCount == 0)
                return null;

            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null)
                return null;

            if (!Utility_TagManager.WorkEnabled(pawn.def, PawnEnumTags.AllowWork_Construction.ToString()))
                return null;

            // Check work type - follow standard pattern even though we can't use StandardTryGiveJob
            if (pawn.WorkTypeIsDisabled(WorkTypeDefOf.Construction))
                return null;
            
            // Check for emergency states that would prevent this job
            if (pawn.Drafted || pawn.mindState.anyCloseHostilesRecently || pawn.InMentalState)
                return null;

            // Only player faction pawns can handle roof designation tasks
            if (pawn.Faction != Faction.OfPlayer)
                return null;

            // Update cache
            UpdateRoofBuildCellsCache(pawn.Map);

            // Find and create job for building roofs
            Job job = TryCreateRoofBuildJob(pawn, false);

            if (job != null)
            {
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to build roof");
            }

            return job;
        }

        /// <summary>
        /// Updates the cache of cells marked for roof building
        /// </summary>
        private void UpdateRoofBuildCellsCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_roofBuildCellsCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_roofBuildCellsCache.ContainsKey(mapId))
                    _roofBuildCellsCache[mapId].Clear();
                else
                    _roofBuildCellsCache[mapId] = new List<IntVec3>();

                // Find all cells marked for roof building
                if (map.areaManager.BuildRoof != null)
                {
                    // Create list of all active roof building cells
                    foreach (IntVec3 cell in map.areaManager.BuildRoof.ActiveCells)
                    {
                        // Skip if already roofed
                        if (cell.Roofed(map))
                            continue;

                        // Skip if this would create an unsupported roof
                        if (!RoofCollapseUtility.WithinRangeOfRoofHolder(cell, map))
                            continue;

                        // Skip if not connected to existing roof/support
                        if (!RoofCollapseUtility.ConnectedToRoofHolder(cell, map, assumeRoofAtRoot: true))
                            continue;

                        // Add to cache
                        _roofBuildCellsCache[mapId].Add(cell);
                    }
                }

                // Limit cache size for performance
                int maxCacheSize = 500;
                if (_roofBuildCellsCache[mapId].Count > maxCacheSize)
                {
                    _roofBuildCellsCache[mapId] = _roofBuildCellsCache[mapId].Take(maxCacheSize).ToList();
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Creates a job for building roofs in the nearest valid location
        /// </summary>
        private Job TryCreateRoofBuildJob(Pawn pawn, bool forced)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_roofBuildCellsCache.ContainsKey(mapId) || _roofBuildCellsCache[mapId].Count == 0)
                return null;

            // Create distance-based buckets manually since we can't use the generic utilities with IntVec3
            List<IntVec3>[] buckets = new List<IntVec3>[DISTANCE_THRESHOLDS.Length + 1];
            for (int i = 0; i < buckets.Length; i++)
                buckets[i] = new List<IntVec3>();

            // Sort cells into buckets by distance
            foreach (IntVec3 cell in _roofBuildCellsCache[mapId])
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

            // Process each bucket to first check for blocking jobs
            for (int i = 0; i < buckets.Length; i++)
            {
                // Randomize within bucket for even distribution
                buckets[i].Shuffle();

                foreach (IntVec3 cell in buckets[i])
                {
                    // Filter out invalid cells immediately
                    if (!pawn.Map.areaManager.BuildRoof[cell] || cell.Roofed(pawn.Map))
                        continue;

                    // Check for blocking things first
                    Thing blocker = RoofUtility.FirstBlockingThing(cell, pawn.Map);
                    if (blocker != null)
                    {
                        Job blockingJob = RoofUtility.HandleBlockingThingJob(blocker, pawn, forced);
                        if (blockingJob != null)
                        {
                            Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to handle {blocker.LabelCap} blocking roof construction");
                            return blockingJob;
                        }
                        // If we can't handle the blocker, skip this cell
                        continue;
                    }

                    // Skip if forbidden or unreachable
                    if (cell.IsForbidden(pawn) ||
                        !pawn.CanReserve(cell) ||
                        !pawn.CanReach(cell, PathEndMode.Touch, pawn.NormalMaxDanger()))
                        continue;

                    // Check for roof stability
                    if (!RoofCollapseUtility.WithinRangeOfRoofHolder(cell, pawn.Map) ||
                        !RoofCollapseUtility.ConnectedToRoofHolder(cell, pawn.Map, assumeRoofAtRoot: true))
                        continue;

                    // Create roof building job
                    Job job = JobMaker.MakeJob(JobDefOf.BuildRoof);
                    job.targetA = cell;
                    job.targetB = cell; // Some jobs need a backup target
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to build roof at {cell}");
                    return job;
                }
            }

            return null;
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            _roofBuildCellsCache.Clear();
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_BuildRoof_PawnControl";
        }
    }
}