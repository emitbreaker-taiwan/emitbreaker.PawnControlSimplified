using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns turret refueling tasks to pawns with the Hauling work type.
    /// Optimized for large colonies with many turrets using distance-based bucketing.
    /// </summary>
    public class JobGiver_Refuel_Turret_PawnControl : ThinkNode_JobGiver
    {
        // Cache for turrets that need refueling
        private static readonly Dictionary<int, List<Building_Turret>> _turretCache = new Dictionary<int, List<Building_Turret>>();
        private static readonly Dictionary<int, Dictionary<Building_Turret, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Building_Turret, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 150; // Update every 2.5 seconds

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        public override float GetPriority(Pawn pawn)
        {
            // Refueling turrets is relatively important for defense
            return 5.9f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<Plant>(
                pawn,
                "Hauling",
                (p, forced) => {
                    // Update plant cache
                    UpdateTurretCache(p.Map);

                    // Find and create a job for cutting plants with VALID DESIGNATORS ONLY
                    return TryCreateRefuelTurretJob(p);
                },
                debugJobDesc: "refuel turret assignment");
        }

        /// <summary>
        /// Updates the cache of turrets that need refueling
        /// </summary>
        private void UpdateTurretCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_turretCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_turretCache.ContainsKey(mapId))
                    _turretCache[mapId].Clear();
                else
                    _turretCache[mapId] = new List<Building_Turret>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Building_Turret, bool>();

                // Find all colony turrets that need refueling
                foreach (Building building in map.listerBuildings.allBuildingsColonist)
                {
                    Building_Turret turret = building as Building_Turret;
                    if (turret != null)
                    {
                        CompRefuelable refuelable = turret.TryGetComp<CompRefuelable>();
                        if (refuelable != null && refuelable.ShouldAutoRefuelNow && !turret.IsBurning())
                        {
                            _turretCache[mapId].Add(turret);
                        }
                    }
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Create a job for refueling a turret using manager-driven bucket processing
        /// </summary>
        private Job TryCreateRefuelTurretJob(Pawn pawn)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_turretCache.ContainsKey(mapId) || _turretCache[mapId].Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing and target selection
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                _turretCache[mapId],
                (turret) => (turret.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Find the best turret to refuel
            Building_Turret targetTurret = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (turret, p) => {
                    // Skip if no longer valid
                    if (turret == null || turret.Destroyed || !turret.Spawned)
                        return false;

                    // Skip if burning
                    if (turret.IsBurning())
                        return false;

                    // Get refuelable component
                    CompRefuelable refuelable = turret.TryGetComp<CompRefuelable>();
                    if (refuelable == null || !refuelable.ShouldAutoRefuelNow)
                        return false;

                    // Skip if forbidden or unreachable
                    if (turret.IsForbidden(p) ||
                        !p.CanReserve(turret, 1, -1) ||
                        !p.CanReach(turret, PathEndMode.ClosestTouch, p.NormalMaxDanger()))
                        return false;

                    // Check if fuel is available
                    bool hasFuel = false;

                    // Check the resource counter for available fuel
                    foreach (var resourceCount in p.Map.resourceCounter.AllCountedAmounts)
                    {
                        if (refuelable.Props.fuelFilter.Allows(resourceCount.Key))
                        {
                            hasFuel = resourceCount.Value > 0;
                            if (hasFuel) break;
                        }
                    }

                    // If we found fuel, we can proceed
                    if (hasFuel)
                        return true;

                    // Otherwise, try to find fuel using a simpler approach
                    float fuelNeeded = refuelable.TargetFuelLevel - refuelable.Fuel;
                    if (fuelNeeded <= 0f)
                        return false;

                    // Check if there's any available fuel that can be reserved
                    foreach (Thing thing in p.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
                    {
                        if (refuelable.Props.fuelFilter.Allows(thing) &&
                            !thing.IsForbidden(p) &&
                            p.CanReserve(thing) &&
                            p.CanReach(thing, PathEndMode.ClosestTouch, p.NormalMaxDanger()))
                        {
                            return true;
                        }
                    }

                    return false;
                },
                _reachabilityCache
            );

            // Create job if target found
            if (targetTurret != null)
            {
                CompRefuelable refuelable = targetTurret.TryGetComp<CompRefuelable>();
                if (refuelable != null)
                {
                    // Determine job type based on atomicFueling flag
                    JobDef jobDef = refuelable.Props.atomicFueling ?
                        JobDefOf.RearmTurretAtomic : JobDefOf.RearmTurret;

                    Job job = JobMaker.MakeJob(jobDef, targetTurret);

                    if (Prefs.DevMode)
                    {
                        Log.Message($"[PawnControl] {pawn.LabelShort} created job to refuel turret {targetTurret.LabelCap}");
                    }

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
            Utility_CacheManager.ResetJobGiverCache(_turretCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_Refuel_Turret_PawnControl";
        }
    }
}