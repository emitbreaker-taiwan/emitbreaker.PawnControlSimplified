using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that allows pawns to smooth walls in designated areas.
    /// Uses the Construction work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Construction_SmoothWall_PawnControl : ThinkNode_JobGiver
    {
        // Cache for buildings designated for smoothing
        private static readonly Dictionary<int, List<Building>> _smoothBuildingsCache = new Dictionary<int, List<Building>>();
        private static readonly Dictionary<int, Dictionary<Building, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Building, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 180; // Update every 3 seconds

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        public override float GetPriority(Pawn pawn)
        {
            // Wall smoothing is similar priority to floor smoothing
            return 5.4f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Quick early exit if there are no wall smoothing designations
            if (pawn?.Map == null || !pawn.Map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.SmoothWall))
                return null;

            // Using StandardTryGiveJob to handle common validation checks
            return Utility_JobGiverManagerOld.StandardTryGiveJob<Thing>(
                pawn,
                "Construction",
                (p, forced) => {
                    // Update cache
                    UpdateSmoothBuildingsCache(p.Map);

                    // Find and create job for smoothing walls
                    return TryCreateSmoothWallJob(p, forced);
                },
                debugJobDesc: "wall smoothing assignment");
        }

        /// <summary>
        /// Updates the cache of buildings designated for smoothing
        /// </summary>
        private void UpdateSmoothBuildingsCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_smoothBuildingsCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_smoothBuildingsCache.ContainsKey(mapId))
                    _smoothBuildingsCache[mapId].Clear();
                else
                    _smoothBuildingsCache[mapId] = new List<Building>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Building, bool>();

                // Find all designated walls for smoothing
                foreach (Designation designation in map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.SmoothWall))
                {
                    // Get the building at this cell
                    Building edifice = designation.target.Cell.GetEdifice(map);

                    // Skip if there's no building or it's not smoothable
                    if (edifice == null || !edifice.def.IsSmoothable)
                        continue;

                    _smoothBuildingsCache[mapId].Add(edifice);
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Creates a job for smoothing walls
        /// </summary>
        private Job TryCreateSmoothWallJob(Pawn pawn, bool forced)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_smoothBuildingsCache.ContainsKey(mapId) || _smoothBuildingsCache[mapId].Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing and target selection
            var buckets = Utility_JobGiverManagerOld.CreateDistanceBuckets(
                pawn,
                _smoothBuildingsCache[mapId],
                (building) => (building.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Find the best wall to smooth - explicitly specify Building as the type parameter
            Building bestBuilding = Utility_JobGiverManagerOld.FindFirstValidTargetInBuckets<Building>(
                buckets,
                pawn,
                (building, p) => {
                    // Skip if no longer valid
                    if (building == null || building.Destroyed || !building.Spawned)
                        return false;

                    // Skip if no longer designated
                    if (p.Map.designationManager.DesignationAt(building.Position, DesignationDefOf.SmoothWall) == null)
                        return false;

                    // Skip if no longer smoothable
                    if (!building.def.IsSmoothable)
                        return false;

                    // Skip if forbidden or unreachable
                    if (building.IsForbidden(p) ||
                        !p.CanReserve(building, 1, -1, null, forced) ||
                        !p.CanReserve(building.Position, 1, -1, null, forced) ||
                        !p.CanReach(building, PathEndMode.Touch, Danger.Some))
                        return false;

                    return true;
                },
                _reachabilityCache
            );

            // Create job if target found
            if (bestBuilding != null)
            {
                Job job = JobMaker.MakeJob(JobDefOf.SmoothWall, bestBuilding);
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to smooth wall: {bestBuilding}");
                return job;
            }

            return null;
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            _smoothBuildingsCache.Clear();
            _reachabilityCache.Clear();
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_SmoothWall_PawnControl";
        }
    }
}