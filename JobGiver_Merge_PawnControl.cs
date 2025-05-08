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
    public class JobGiver_Merge_PawnControl : ThinkNode_JobGiver
    {
        // Cache for mergeable things
        private static readonly Dictionary<int, List<Thing>> _mergeableCache = new Dictionary<int, List<Thing>>();
        private static readonly Dictionary<int, Dictionary<Thing, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Thing, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 400; // Update every ~6.6 seconds

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 2500f }; // 15, 25, 50 tiles

        public override float GetPriority(Pawn pawn)
        {
            // Merging is less important than specialized hauling jobs but more important than general hauling
            return 5.0f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<Plant>(
                pawn,
                "Hauling",
                (p, forced) => {
                    // Update plant cache
                    UpdateMergeableCacheSafely(p.Map);

                    // Find and create a job for cutting plants with VALID DESIGNATORS ONLY
                    return TryCreateMergeJob(p);
                },
                debugJobDesc: "merge assignment");
        }

        /// <summary>
        /// Updates the cache of things potentially needing merging
        /// </summary>
        private void UpdateMergeableCacheSafely(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_mergeableCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_mergeableCache.ContainsKey(mapId))
                    _mergeableCache[mapId].Clear();
                else
                    _mergeableCache[mapId] = new List<Thing>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Thing, bool>();

                // Find all things potentially needing merging
                List<Thing> mergeables = map.listerMergeables.ThingsPotentiallyNeedingMerging();
                if (mergeables != null && mergeables.Count > 0)
                {
                    _mergeableCache[mapId].AddRange(mergeables);
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Creates a job for merging items
        /// </summary>
        private Job TryCreateMergeJob(Pawn pawn)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_mergeableCache.ContainsKey(mapId) || _mergeableCache[mapId].Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                _mergeableCache[mapId],
                (item) => (item.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Find a valid mergeable item
            for (int b = 0; b < buckets.Length; b++)
            {
                if (buckets[b].Count == 0)
                    continue;

                // Randomize within each bucket for even distribution
                buckets[b].Shuffle();

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

                    // Get the slot group (storage)
                    ISlotGroup slotGroup1 = thing.GetSlotGroup();
                    if (slotGroup1 == null)
                        continue;

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
                }
            }

            return null;
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_mergeableCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_Merge_PawnControl";
        }
    }
}