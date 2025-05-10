using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Abstract base class for JobGivers that affect floors (smooth, remove, etc.)
    /// Uses the Construction work tag for eligibility checking.
    /// </summary>
    public abstract class JobGiver_Common_ConstructAffectFloor_PawnControl : ThinkNode_JobGiver
    {
        // Cache for cells with designations
        protected static readonly Dictionary<int, Dictionary<DesignationDef, List<IntVec3>>> _designatedCellsCache = new Dictionary<int, Dictionary<DesignationDef, List<IntVec3>>>();
        protected static readonly Dictionary<int, Dictionary<IntVec3, bool>> _reachabilityCache = new Dictionary<int, Dictionary<IntVec3, bool>>();
        protected static int _lastCacheUpdateTick = -999;
        protected const int CACHE_UPDATE_INTERVAL = 120; // Update every 2 seconds

        // Distance thresholds for bucketing
        protected static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        // Must be implemented by subclasses to specify which designation to target
        protected abstract DesignationDef DesDef { get; }

        // Must be implemented by subclasses to specify which job to use
        protected abstract JobDef JobDef { get; }

        public override float GetPriority(Pawn pawn)
        {
            // Floor work is moderately important construction task
            return 5.3f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Quick early exit if there are no designations on the map
            if (pawn?.Map == null || !pawn.Map.designationManager.AnySpawnedDesignationOfDef(DesDef))
                return null;

            // Using StandardTryGiveJob to handle common validation checks - use Thing as the generic type
            return Utility_JobGiverManagerOld.StandardTryGiveJob<Thing>(
                pawn,
                "Construction",
                (p, forced) => {
                    // Update cache
                    UpdateDesignatedCellsCache(p.Map);

                    // Find and create job for affecting floor
                    return TryCreateAffectFloorJob(p, forced);
                },
                debugJobDesc: $"{DesDef.defName} assignment"); // Debug job description
        }

        /// <summary>
        /// Creates a job for affecting a floor cell
        /// </summary>
        protected Job TryCreateAffectFloorJob(Pawn pawn, bool forced)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_designatedCellsCache.ContainsKey(mapId) ||
                !_designatedCellsCache[mapId].ContainsKey(DesDef) ||
                _designatedCellsCache[mapId][DesDef].Count == 0)
                return null;

            // Create distance-based buckets manually since we're dealing with IntVec3
            List<IntVec3>[] buckets = new List<IntVec3>[DISTANCE_THRESHOLDS.Length + 1];
            for (int i = 0; i < buckets.Length; i++)
                buckets[i] = new List<IntVec3>();

            // Sort cells into buckets by distance
            foreach (IntVec3 cell in _designatedCellsCache[mapId][DesDef])
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

                // Randomize within bucket for even distribution
                buckets[i].Shuffle();

                foreach (IntVec3 cell in buckets[i])
                {
                    // Skip if cell is no longer designated
                    if (pawn.Map.designationManager.DesignationAt(cell, DesDef) == null)
                        continue;

                    // Skip if forbidden or unreachable
                    if (cell.IsForbidden(pawn) ||
                        !pawn.CanReserve(cell, 1, -1, null, forced) ||
                        !pawn.CanReach(cell, PathEndMode.Touch, Danger.Some))
                        continue;

                    // Create the job
                    Job job = JobMaker.MakeJob(JobDef, cell);

                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to affect floor at {cell} using {DesDef.defName}");
                    return job;
                }
            }

            return null;
        }

        /// <summary>
        /// Updates the cache of cells with floor designations
        /// </summary>
        protected void UpdateDesignatedCellsCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_designatedCellsCache.ContainsKey(mapId) ||
                !_designatedCellsCache[mapId].ContainsKey(DesDef))
            {
                // Initialize cache dictionaries if needed
                if (!_designatedCellsCache.ContainsKey(mapId))
                    _designatedCellsCache[mapId] = new Dictionary<DesignationDef, List<IntVec3>>();
                
                if (!_designatedCellsCache[mapId].ContainsKey(DesDef))
                    _designatedCellsCache[mapId][DesDef] = new List<IntVec3>();
                else
                    _designatedCellsCache[mapId][DesDef].Clear();

                // Clear reachability cache too
                if (!_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId] = new Dictionary<IntVec3, bool>();
                else
                    _reachabilityCache[mapId].Clear();

                // Find all cells with designations
                foreach (Designation designation in map.designationManager.SpawnedDesignationsOfDef(DesDef))
                {
                    _designatedCellsCache[mapId][DesDef].Add(designation.target.Cell);
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            _designatedCellsCache.Clear();
            _reachabilityCache.Clear();
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return $"JobGiver_ConstructAffectFloor_PawnControl({DesDef?.defName ?? "null"})";
        }
    }
}