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
    public class JobGiver_Hauling_HaulGeneral_PawnControl : JobGiver_Hauling_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "HaulGeneral";

        /// <summary>
        /// Update cache every 5 seconds
        /// </summary>
        protected override int CacheUpdateInterval => 300;

        /// <summary>
        /// Standard distance thresholds for bucketing (15, 25, 50 tiles)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 225f, 625f, 2500f };

        /// <summary>
        /// Maximum cache size to control memory usage
        /// </summary>
        private const int MAX_CACHE_ENTRIES = 500;

        #endregion

        #region Core flow

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Hauling_HaulGeneral_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) =>
                {
                    if (p?.Map == null)
                        return null;

                    int mapId = p.Map.uniqueID;
                    int now = Find.TickManager.TicksGame;

                    if (!ShouldExecuteNow(mapId))
                        return null;

                    // Use the shared cache updating logic from base class
                    if (!_lastHaulingCacheUpdate.TryGetValue(mapId, out int last)
                        || now - last >= CacheUpdateInterval)
                    {
                        _lastHaulingCacheUpdate[mapId] = now;
                        _haulableCache[mapId] = new List<Thing>(GetTargets(p.Map));
                    }

                    // Get general items from shared cache
                    if (!_haulableCache.TryGetValue(mapId, out var haulables) || haulables.Count == 0)
                        return null;

                    // Filter only non-corpse items from the shared cache
                    var generalItems = haulables.Where(t => !(t is Corpse)).ToList();
                    if (generalItems.Count == 0)
                        return null;

                    // Use the bucketing system to find the closest valid item
                    var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                        p,
                        generalItems,
                        (item) => (item.Position - p.Position).LengthHorizontalSquared,
                        DistanceThresholds);

                    // Find the best item to haul
                    Thing targetThing = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                        buckets,
                        p,
                        (thing, worker) => IsValidHaulItem(thing, worker),
                        _reachabilityCache);

                    // Create and return job if we found a valid target
                    if (targetThing != null)
                    {
                        // Find storage location
                        IntVec3 storeCell;
                        IHaulDestination haulDestination;
                        if (!StoreUtility.TryFindBestBetterStorageFor(targetThing, p, p.Map,
                            StoreUtility.CurrentStoragePriorityOf(targetThing), p.Faction, out storeCell, out haulDestination))
                            return null;

                        // Create haul job
                        Job job = HaulAIUtility.HaulToCellStorageJob(p, targetThing, storeCell, false);

                        if (job != null)
                        {
                            Utility_DebugManager.LogNormal($"{p.LabelShort} created general hauling job for {targetThing.Label} to {storeCell}");
                        }

                        return job;
                    }

                    return null;
                },
                debugJobDesc: DebugName);
        }

        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            if (targets == null || targets.Count == 0)
                return null;

            // Use the bucketing system to find the closest valid item
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                targets,
                (item) => (item.Position - pawn.Position).LengthHorizontalSquared,
                DistanceThresholds);

            // Find the best item to haul
            Thing targetThing = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (thing, worker) => IsValidHaulItem(thing, worker),
                _reachabilityCache);

            if (targetThing != null)
            {
                // Find storage location
                IntVec3 storeCell;
                IHaulDestination haulDestination;
                if (!StoreUtility.TryFindBestBetterStorageFor(targetThing, pawn, pawn.Map,
                    StoreUtility.CurrentStoragePriorityOf(targetThing), pawn.Faction, out storeCell, out haulDestination))
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

        #endregion

        #region Target selection

        /// <summary>
        /// Gets all haulable items (excluding corpses) on the map
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map?.listerHaulables != null)
            {
                // Get all potential haulables
                List<Thing> haulablesRaw = map.listerHaulables.ThingsPotentiallyNeedingHauling();
                int count = 0;

                // Return valid haulable items
                foreach (Thing thing in haulablesRaw)
                {
                    // Apply basic filters during target collection
                    if (thing != null && thing.Spawned && !(thing is Corpse) &&
                        !thing.Destroyed && !StoreUtility.IsInValidBestStorage(thing))
                    {
                        yield return thing;

                        // Limit cache size
                        count++;
                        if (count >= MAX_CACHE_ENTRIES)
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// Determines if an item is valid for general hauling
        /// </summary>
        private bool IsValidHaulItem(Thing thing, Pawn pawn)
        {
            // Skip if null, destroyed, or not spawned
            if (thing == null || thing.Destroyed || !thing.Spawned)
                return false;

            // Skip corpses (handled by separate job giver)
            if (thing is Corpse)
                return false;

            // Skip if forbidden or unreachable
            if (thing.IsForbidden(pawn) || !pawn.CanReserveAndReach(thing, PathEndMode.ClosestTouch, pawn.NormalMaxDanger()))
                return false;

            // Skip if already in valid storage
            if (StoreUtility.IsInValidBestStorage(thing))
                return false;

            // Skip if we can't find a storage place
            IntVec3 storeCell;
            IHaulDestination haulDestination;
            if (!StoreUtility.TryFindBestBetterStorageFor(thing, pawn, pawn.Map,
                StoreUtility.CurrentStoragePriorityOf(thing), pawn.Faction, out storeCell, out haulDestination))
                return false;

            // Skip if we can't reserve the store cell
            if (!pawn.CanReserveAndReach(storeCell, PathEndMode.Touch, pawn.NormalMaxDanger()))
                return false;

            return true;
        }

        #endregion
    }
}