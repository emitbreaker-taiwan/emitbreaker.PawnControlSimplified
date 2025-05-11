using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to merge partial stacks of the same item.
    /// Uses the Hauling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Hauling_HaulMerge_PawnControl : JobGiver_Hauling_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "Merge";

        /// <summary>
        /// Update cache every ~6.6 seconds
        /// </summary>
        protected override int CacheUpdateInterval => 400;

        /// <summary>
        /// Standard distance thresholds for bucketing (15, 25, 50 tiles)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 225f, 625f, 2500f };

        #endregion

        #region Cache Management

        // Merge-specific cache
        private static readonly Dictionary<int, List<Thing>> _mergeableCache = new Dictionary<int, List<Thing>>();
        private static readonly Dictionary<int, Dictionary<Thing, bool>> _mergeReachabilityCache = new Dictionary<int, Dictionary<Thing, bool>>();
        private static int _lastMergeCacheUpdateTick = -999;

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetMergeCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_mergeableCache, _mergeReachabilityCache);
            _lastMergeCacheUpdateTick = -999;
            ResetHaulingCache(); // Call base class reset too
        }

        #endregion

        #region Core flow

        protected override float GetBasePriority(string workTag)
        {
            // Merging is less important than specialized hauling jobs but more important than general hauling
            return 5.0f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Hauling_HaulMerge_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) => {
                    if (p?.Map == null)
                        return null;

                    int mapId = p.Map.uniqueID;
                    int now = Find.TickManager.TicksGame;

                    if (!ShouldExecuteNow(mapId))
                        return null;

                    // Update the cache if needed
                    if (now > _lastMergeCacheUpdateTick + CacheUpdateInterval ||
                        !_mergeableCache.ContainsKey(mapId))
                    {
                        UpdateMergeableCache(p.Map);
                    }

                    // Process cached targets
                    return TryCreateMergeJob(p);
                },
                debugJobDesc: DebugName);
        }

        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // Get all things potentially needing merging from the map's lister
            if (map?.listerMergeables != null)
            {
                List<Thing> mergeables = map.listerMergeables.ThingsPotentiallyNeedingMerging();
                if (mergeables != null)
                {
                    foreach (Thing thing in mergeables)
                    {
                        if (thing != null && thing.Spawned && !thing.Destroyed)
                        {
                            yield return thing;
                        }
                    }
                }
            }
        }

        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            if (targets == null || targets.Count == 0)
                return null;

            // Use the bucketing system to find the closest valid item
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                targets,
                (thing) => (thing.Position - pawn.Position).LengthHorizontalSquared,
                DistanceThresholds);

            // Process each bucket
            for (int b = 0; b < buckets.Length; b++)
            {
                if (buckets[b].Count == 0)
                    continue;

                // Randomize within each bucket for even distribution
                buckets[b].Shuffle();

                // Check each thing in this bucket
                foreach (Thing thing in buckets[b])
                {
                    // Skip if at stack limit
                    if (thing.stackCount >= thing.def.stackLimit)
                        continue;

                    // Skip if we can't haul this automatically
                    if (!HaulAIUtility.PawnCanAutomaticallyHaul(pawn, thing, false))
                        continue;

                    // Skip if we can't reserve the position
                    if (!pawn.CanReserve(thing.Position))
                        continue;

                    // Find a valid merge target and create a job
                    Job mergeJob = TryFindMergeTargetFor(thing, pawn);
                    if (mergeJob != null)
                    {
                        return mergeJob;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Determines if the job giver should execute based on cache status
        /// </summary>
        protected override bool ShouldExecuteNow(int mapId)
        {
            return Find.TickManager.TicksGame > _lastMergeCacheUpdateTick + CacheUpdateInterval ||
                  !_mergeableCache.ContainsKey(mapId);
        }

        #endregion

        #region Target processing

        /// <summary>
        /// Updates the cache of things potentially needing merging
        /// </summary>
        private void UpdateMergeableCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            // Clear outdated cache
            if (_mergeableCache.ContainsKey(mapId))
                _mergeableCache[mapId].Clear();
            else
                _mergeableCache[mapId] = new List<Thing>();

            // Clear reachability cache too
            if (_mergeReachabilityCache.ContainsKey(mapId))
                _mergeReachabilityCache[mapId].Clear();
            else
                _mergeReachabilityCache[mapId] = new Dictionary<Thing, bool>();

            // Find all things potentially needing merging
            List<Thing> mergeables = map.listerMergeables.ThingsPotentiallyNeedingMerging();
            if (mergeables != null && mergeables.Count > 0)
            {
                _mergeableCache[mapId].AddRange(mergeables);
            }

            _lastMergeCacheUpdateTick = currentTick;
        }

        /// <summary>
        /// Create a job for merging items
        /// </summary>
        private Job TryCreateMergeJob(Pawn pawn)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_mergeableCache.ContainsKey(mapId) || _mergeableCache[mapId].Count == 0)
                return null;

            return ProcessCachedTargets(pawn, _mergeableCache[mapId], false);
        }

        /// <summary>
        /// Finds a valid merge target for the given thing
        /// </summary>
        private Job TryFindMergeTargetFor(Thing thing, Pawn pawn)
        {
            // Get the slot group (storage)
            ISlotGroup slotGroup1 = thing.GetSlotGroup();
            if (slotGroup1 == null)
                return null;

            // Get the overall storage group if available
            ISlotGroup slotGroup2 = slotGroup1.StorageGroup ?? slotGroup1;

            // Find a valid target to merge with
            foreach (Thing heldThing in slotGroup2.HeldThings)
            {
                // Skip if this is the same thing or can't stack with our item
                if (heldThing == thing || !heldThing.CanStackWith(thing))
                    continue;

                // Prefer to merge smaller stacks into larger ones
                if (heldThing.stackCount < thing.stackCount)
                    continue;

                // Skip if target stack is already full
                if (heldThing.stackCount >= heldThing.def.stackLimit)
                    continue;

                // Skip if can't reserve both position and item
                if (!pawn.CanReserve(heldThing.Position) || !pawn.CanReserve(heldThing))
                    continue;

                // Skip if target cell isn't valid storage for the item
                if (!heldThing.Position.IsValidStorageFor(heldThing.Map, thing))
                    continue;

                // Skip if target cell has fire
                if (heldThing.Position.ContainsStaticFire(heldThing.Map))
                    continue;

                // Create the hauling job
                Job job = JobMaker.MakeJob(JobDefOf.HaulToCell, thing, heldThing.Position);
                job.count = Mathf.Min(heldThing.def.stackLimit - heldThing.stackCount, thing.stackCount);
                job.haulMode = HaulMode.ToCellStorage;
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to merge {thing.Label} ({thing.stackCount}) with {heldThing.Label} ({heldThing.stackCount})");
                return job;
            }

            return null;
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return "JobGiver_Merge_PawnControl";
        }

        #endregion
    }
}