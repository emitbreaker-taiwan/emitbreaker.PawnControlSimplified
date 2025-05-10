using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that allows non-humanlike pawns to fix broken down buildings.
    /// </summary>
    public class JobGiver_Construction_FixBrokenDownBuilding_PawnControl : ThinkNode_JobGiver
    {
        // Cache for buildings that need repairing
        private static readonly Dictionary<int, List<Building>> _brokenBuildingsCache = new Dictionary<int, List<Building>>();
        private static readonly Dictionary<Building, Thing> _componentCache = new Dictionary<Building, Thing>();
        // Change the reachability cache to use Building instead of Thing to match the target type
        private static readonly Dictionary<int, Dictionary<Building, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Building, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 300; // Update every 5 seconds (broken buildings don't change often)

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 100f, 400f, 900f }; // 10, 20, 30 tiles

        // Static translation strings
        private static string NotInHomeAreaTrans;
        private static string NoComponentsToRepairTrans;

        public static void ResetStaticData()
        {
            NotInHomeAreaTrans = "NotInHomeArea".Translate();
            NoComponentsToRepairTrans = "NoComponentsToRepair".Translate();
        }

        public override float GetPriority(Pawn pawn)
        {
            // Repairing is quite important as it prevents loss of capabilities
            return 6.5f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<Building>(
                pawn,
                "Construction", // This uses the Construction work type
                (p, forced) => {
                    // Update cache first
                    UpdateBrokenBuildingsCache(p.Map);

                    // Try to create a job to repair a broken building
                    return TryCreateRepairJob(p, forced);
                },
                debugJobDesc: "broken building repair assignment",
                skipEmergencyCheck: true);
        }

        /// <summary>
        /// Updates the cache of broken buildings that need repair
        /// </summary>
        private void UpdateBrokenBuildingsCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_brokenBuildingsCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_brokenBuildingsCache.ContainsKey(mapId))
                    _brokenBuildingsCache[mapId].Clear();
                else
                    _brokenBuildingsCache[mapId] = new List<Building>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Building, bool>();

                // Use the BreakdownManager to get all broken buildings
                var brokenDownThings = map.GetComponent<BreakdownManager>()?.brokenDownThings;
                if (brokenDownThings != null)
                {
                    foreach (Building building in brokenDownThings)
                    {
                        if (building != null && building.Spawned && building.def.building.repairable)
                        {
                            _brokenBuildingsCache[mapId].Add(building);
                        }
                    }
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Create a job for repairing a broken building
        /// </summary>
        private Job TryCreateRepairJob(Pawn pawn, bool forced)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_brokenBuildingsCache.ContainsKey(mapId) || _brokenBuildingsCache[mapId].Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing and target selection
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                _brokenBuildingsCache[mapId],
                (thing) => (thing.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Find the best broken building to repair
            Building bestTarget = Utility_JobGiverManager.FindFirstValidTargetInBuckets<Building>(
                buckets,
                pawn,
                (thing, p) => {
                    Building building = thing;
                    if (building == null) return false;

                    // Basic validation checks
                    if (!building.Spawned || !building.IsBrokenDown() || building.IsForbidden(p))
                        return false;

                    // Check building faction - must be same as pawn's
                    if (building.Faction != p.Faction)
                        return false;

                    // Check if in home area (for player faction)
                    if (p.Faction == Faction.OfPlayer && !p.Map.areaManager.Home[building.Position])
                    {
                        JobFailReason.Is(NotInHomeAreaTrans);
                        return false;
                    }

                    // Check existing designations
                    if (p.Map.designationManager.DesignationOn(building, DesignationDefOf.Deconstruct) != null)
                        return false;

                    // Check if building is on fire
                    if (building.IsBurning())
                        return false;

                    // Check if pawn can reserve the building
                    if (!p.CanReserve(building, 1, -1, null, forced))
                        return false;

                    // Check if reachable
                    if (!p.CanReach(building, PathEndMode.Touch, Danger.Deadly))
                        return false;

                    // Check for components to repair with
                    Thing component = FindClosestComponent(p);
                    if (component == null)
                    {
                        JobFailReason.Is(NoComponentsToRepairTrans);
                        return false;
                    }

                    // Store the actual component for later use
                    _componentCache[building] = component;

                    return true;
                },
                _reachabilityCache
            );

            // Create job if building found
            if (bestTarget != null)
            {
                // Use the cached component
                Thing component = null;
                if (_componentCache.TryGetValue(bestTarget, out var cachedComponent))
                {
                    component = cachedComponent;
                }
                else
                {
                    component = FindClosestComponent(pawn);
                }

                if (component != null)
                {
                    Job job = JobMaker.MakeJob(JobDefOf.FixBrokenDownBuilding, bestTarget, component);
                    job.count = 1;
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to repair broken {bestTarget.LabelCap}");
                    return job;
                }
            }

            return null;
        }

        /// <summary>
        /// Find the closest component available for repairs
        /// </summary>
        private Thing FindClosestComponent(Pawn pawn)
        {
            return GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForDef(ThingDefOf.ComponentIndustrial),
                PathEndMode.InteractionCell,
                TraverseParms.For(pawn, pawn.NormalMaxDanger()),
                validator: x => !x.IsForbidden(pawn) && pawn.CanReserve(x)
            );
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            // Changed generic type parameter to match the cache types
            Utility_CacheManager.ResetJobGiverCache<Building>(_brokenBuildingsCache, _reachabilityCache);
            _componentCache.Clear(); // Clear component cache too
            _lastCacheUpdateTick = -999;
            ResetStaticData();
        }

        public override string ToString()
        {
            return "JobGiver_FixBrokenDownBuilding_PawnControl";
        }
    }
}