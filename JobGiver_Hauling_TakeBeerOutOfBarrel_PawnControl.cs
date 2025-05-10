using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to take beer out of fermenting barrels when ready.
    /// Uses the Hauling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Hauling_TakeBeerOutOfBarrel_PawnControl : ThinkNode_JobGiver
    {
        // Cache for fermenting barrels that are ready
        private static readonly Dictionary<int, List<Building_FermentingBarrel>> _fermentedBarrelCache = new Dictionary<int, List<Building_FermentingBarrel>>();
        private static readonly Dictionary<int, Dictionary<Building_FermentingBarrel, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Building_FermentingBarrel, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 300; // Update every 5 seconds

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        public override float GetPriority(Pawn pawn)
        {
            // Taking beer out is moderately important
            return 5.3f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<Plant>(
                pawn,
                "Hauling",
                (p, forced) => {
                    // Update plant cache
                    UpdateFermentedBarrelsCacheSafely(p.Map);

                    // Find and create a job for cutting plants with VALID DESIGNATORS ONLY
                    return TryCreateTakeBeerJob(p);
                },
                debugJobDesc: "take beer out of fermenting barrels assignment");
        }

        /// <summary>
        /// Updates the cache of fermenting barrels that are ready
        /// </summary>
        private void UpdateFermentedBarrelsCacheSafely(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_fermentedBarrelCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_fermentedBarrelCache.ContainsKey(mapId))
                    _fermentedBarrelCache[mapId].Clear();
                else
                    _fermentedBarrelCache[mapId] = new List<Building_FermentingBarrel>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Building_FermentingBarrel, bool>();

                // Find all fermenting barrels on the map that are ready
                foreach (Thing thing in map.listerThings.ThingsOfDef(ThingDefOf.FermentingBarrel))
                {
                    Building_FermentingBarrel barrel = thing as Building_FermentingBarrel;
                    if (barrel != null && barrel.Spawned && barrel.Fermented)
                    {
                        _fermentedBarrelCache[mapId].Add(barrel);
                    }
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Creates a job for taking beer out of a fermenting barrel
        /// </summary>
        private Job TryCreateTakeBeerJob(Pawn pawn)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_fermentedBarrelCache.ContainsKey(mapId) || _fermentedBarrelCache[mapId].Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                _fermentedBarrelCache[mapId],
                (barrel) => (barrel.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Find the best barrel to use
            Building_FermentingBarrel targetBarrel = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (barrel, p) => {
                    // IMPORTANT: Check faction interaction validity first
                    if (!Utility_JobGiverManager.IsValidFactionInteraction(barrel, p, requiresDesignator: false))
                        return false;

                    // Skip if no longer valid
                    if (barrel == null || barrel.Destroyed || !barrel.Spawned)
                        return false;
                    
                    // Skip if not fermented, burning or forbidden
                    if (!barrel.Fermented || barrel.IsBurning() || barrel.IsForbidden(p))
                        return false;
                        
                    // Skip if unreachable or can't be reserved
                    if (!p.CanReserve(barrel))
                        return false;
                    
                    return true;
                },
                _reachabilityCache
            );

            // Create job if target found
            if (targetBarrel != null)
            {
                Job job = JobMaker.MakeJob(JobDefOf.TakeBeerOutOfFermentingBarrel, targetBarrel);
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to take beer out of fermenting barrel");
                return job;
            }

            return null;
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_fermentedBarrelCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_TakeBeerOutOfBarrel_PawnControl";
        }
    }
}