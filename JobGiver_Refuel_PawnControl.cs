using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns refueling tasks to pawns with the Hauling work type.
    /// Handles non-turret buildings that require fuel.
    /// Optimized for large colonies with many refuelable buildings using distance-based bucketing.
    /// </summary>
    public class JobGiver_Refuel_PawnControl : ThinkNode_JobGiver
    {
        // Cache for buildings that need refueling
        private static readonly Dictionary<int, List<Thing>> _refuelableBuildingsCache = new Dictionary<int, List<Thing>>();
        private static readonly Dictionary<int, Dictionary<Thing, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Thing, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 180; // Update every 3 seconds

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        public override float GetPriority(Pawn pawn)
        {
            // Refueling is important to keep buildings running
            return 5.8f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<Plant>(
                pawn,
                "Hauling",
                (p, forced) => {
                    // Update plant cache
                    UpdateRefuelableBuildingsCache(p.Map);

                    // Find and create a job for cutting plants with VALID DESIGNATORS ONLY
                    return TryCreateRefuelJob(p);
                },
                debugJobDesc: "refuel assignment");
        }

        /// <summary>
        /// Updates the cache of buildings that need refueling (excluding turrets)
        /// </summary>
        private void UpdateRefuelableBuildingsCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_refuelableBuildingsCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_refuelableBuildingsCache.ContainsKey(mapId))
                    _refuelableBuildingsCache[mapId].Clear();
                else
                    _refuelableBuildingsCache[mapId] = new List<Thing>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Thing, bool>();

                // Find all refuelable buildings (excluding turrets)
                foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Refuelable))
                {
                    // Skip turrets (these are handled by JobGiver_Refuel_Turret_PawnControl)
                    if (thing is Building_Turret)
                        continue;

                    CompRefuelable refuelable = thing.TryGetComp<CompRefuelable>();
                    if (refuelable != null && refuelable.ShouldAutoRefuelNow && !thing.IsBurning())
                    {
                        _refuelableBuildingsCache[mapId].Add(thing);
                    }
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Create a job for refueling a building using manager-driven bucket processing
        /// </summary>
        private Job TryCreateRefuelJob(Pawn pawn)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_refuelableBuildingsCache.ContainsKey(mapId) || _refuelableBuildingsCache[mapId].Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing and target selection
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                _refuelableBuildingsCache[mapId],
                (building) => (building.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Find the best building to refuel
            Thing targetBuilding = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (building, p) => {
                    // Skip if no longer valid
                    if (building == null || building.Destroyed || !building.Spawned)
                        return false;

                    // Skip if burning
                    if (building.IsBurning())
                        return false;

                    // Get refuelable component
                    CompRefuelable refuelable = building.TryGetComp<CompRefuelable>();
                    if (refuelable == null || !refuelable.ShouldAutoRefuelNow)
                        return false;

                    // Skip if forbidden or unreachable
                    if (building.IsForbidden(p) ||
                        !p.CanReserve(building, 1, -1) ||
                        !p.CanReach(building, PathEndMode.Touch, p.NormalMaxDanger()))
                        return false;

                    // Check if fuel is available
                    bool hasFuel = false;

                    // Check the resource counter for available fuel
                    foreach (var resourceCount in p.Map.resourceCounter.AllCountedAmounts)
                    {
                        if (refuelable.Props.fuelFilter.Allows(resourceCount.Key) && resourceCount.Value > 0)
                        {
                            hasFuel = true;
                            break;
                        }
                    }

                    if (!hasFuel)
                    {
                        // Check if there's any available fuel that can be reserved
                        foreach (Thing fuel in p.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
                        {
                            if (refuelable.Props.fuelFilter.Allows(fuel) &&
                                !fuel.IsForbidden(p) &&
                                p.CanReserve(fuel) &&
                                p.CanReach(fuel, PathEndMode.ClosestTouch, p.NormalMaxDanger()))
                            {
                                hasFuel = true;
                                break;
                            }
                        }
                    }

                    return hasFuel;
                },
                _reachabilityCache
            );

            // Create job if target found
            if (targetBuilding != null)
            {
                CompRefuelable refuelable = targetBuilding.TryGetComp<CompRefuelable>();
                if (refuelable != null)
                {
                    // Determine job type based on atomicFueling flag
                    JobDef jobDef = refuelable.Props.atomicFueling ?
                        JobDefOf.RefuelAtomic : JobDefOf.Refuel;

                    Job job = JobMaker.MakeJob(jobDef, targetBuilding);

                    if (Prefs.DevMode)
                    {
                        Log.Message($"[PawnControl] {pawn.LabelShort} created job to refuel {targetBuilding.LabelCap}");
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
            Utility_CacheManager.ResetJobGiverCache(_refuelableBuildingsCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_Refuel_PawnControl";
        }
    }
}