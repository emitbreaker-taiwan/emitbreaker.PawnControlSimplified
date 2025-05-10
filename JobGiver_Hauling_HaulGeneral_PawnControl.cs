using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns general hauling tasks for loose items that need to be moved to storage.
    /// Uses the Hauling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Hauling_HaulGeneral_PawnControl : ThinkNode_JobGiver
    {
        // Cache for haulable things
        private static readonly Dictionary<int, List<Thing>> _haulableCache = new Dictionary<int, List<Thing>>();
        private static readonly Dictionary<int, Dictionary<Thing, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Thing, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 300; // Update every 5 seconds

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 2500f }; // 15, 25, 50 tiles

        public override float GetPriority(Pawn pawn)
        {
            // General hauling is less important than specialized hauling tasks
            return 4.9f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<Plant>(
                pawn,
                "Hauling",
                (p, forced) => {
                    // Update plant cache
                    UpdateHaulableCacheSafely(p.Map);

                    // Find and create a job for cutting plants with VALID DESIGNATORS ONLY
                    return TryCreateHaulJob(p);
                },
                debugJobDesc: "haul general assignment");
        }

        /// <summary>
        /// Updates the cache of haulable things
        /// </summary>
        private void UpdateHaulableCacheSafely(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_haulableCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_haulableCache.ContainsKey(mapId))
                    _haulableCache[mapId].Clear();
                else
                    _haulableCache[mapId] = new List<Thing>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Thing, bool>();

                // Find all haulable things on the map
                List<Thing> haulablesRaw = map.listerHaulables.ThingsPotentiallyNeedingHauling();
                int maxCacheSize = Math.Min(500, haulablesRaw.Count); // Limit cache size for performance

                // Filter out things we don't want to haul
                for (int i = 0; i < haulablesRaw.Count && _haulableCache[mapId].Count < maxCacheSize; i++)
                {
                    Thing thing = haulablesRaw[i];
                    if (IsValidHaulItem(thing))
                    {
                        _haulableCache[mapId].Add(thing);
                    }
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Determines if an item is valid for general hauling
        /// </summary>
        private bool IsValidHaulItem(Thing thing)
        {
            // Skip if null, destroyed, or not spawned
            if (thing == null || thing.Destroyed || !thing.Spawned)
                return false;

            // Skip corpses (handled by separate job giver)
            if (thing is Corpse)
                return false;

            // Skip items in good enough storage already (don't waste time moving them again)
            if (StoreUtility.IsInValidBestStorage(thing))
                return false;

            return true;
        }

        /// <summary>
        /// Creates a job for hauling an item to storage
        /// </summary>
        private Job TryCreateHaulJob(Pawn pawn)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_haulableCache.ContainsKey(mapId) || _haulableCache[mapId].Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                _haulableCache[mapId],
                (item) => (item.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Find the best thing to haul
            Thing targetThing = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (thing, p) => {
                    // Skip if no longer valid
                    if (thing == null || thing.Destroyed || !thing.Spawned)
                        return false;

                    // Skip if forbidden or unreachable
                    if (thing.IsForbidden(p) || !p.CanReserveAndReach(thing, PathEndMode.ClosestTouch, p.NormalMaxDanger()))
                        return false;

                    // Skip if already in valid storage
                    if (StoreUtility.IsInValidBestStorage(thing))
                        return false;

                    // Skip if we can't find a storage place
                    IntVec3 storeCell;
                    IHaulDestination haulDestination;
                    if (!StoreUtility.TryFindBestBetterStorageFor(thing, p, p.Map, StoreUtility.CurrentStoragePriorityOf(thing), p.Faction, out storeCell, out haulDestination))
                        return false;

                    // Skip if we can't reserve the store cell
                    if (!p.CanReserveAndReach(storeCell, PathEndMode.Touch, p.NormalMaxDanger()))
                        return false;

                    return true;
                },
                _reachabilityCache
            );

            // Create job if target found
            if (targetThing != null)
            {
                IntVec3 storeCell;
                IHaulDestination haulDestination;

                // Find storage location
                if (!StoreUtility.TryFindBestBetterStorageFor(targetThing, pawn, pawn.Map, StoreUtility.CurrentStoragePriorityOf(targetThing), pawn.Faction, out storeCell, out haulDestination))
                    return null;

                // Create haul job
                Job job = HaulAIUtility.HaulToCellStorageJob(pawn, targetThing, storeCell, false);

                if (job != null)
                {
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} created general hauling job for {targetThing.Label} to {storeCell}");
                }

                return job;
            }

            return null;
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_haulableCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_HaulGeneral_PawnControl";
        }
    }
}