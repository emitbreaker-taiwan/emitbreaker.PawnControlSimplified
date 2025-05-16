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
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        public override bool RequiresMapZoneorArea => false;

        /// <summary>
        /// Whether this job giver requires player faction specifically (for jobs like deconstruct)
        /// </summary>
        public override bool RequiresPlayerFaction => true;

        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.HaulToCell;

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "Merge";

        /// <summary>
        /// Update cache every ~6.6 seconds
        /// </summary>
        protected override int CacheUpdateInterval => base.CacheUpdateInterval;

        /// <summary>
        /// Standard distance thresholds for bucketing (15, 25, 50 tiles)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 225f, 625f, 2500f };

        #endregion

        #region Core flow

        protected override float GetBasePriority(string workTag)
        {
            // Merging is less important than specialized hauling jobs but more important than general hauling
            return 5.0f;
        }

        /// <summary>
        /// Process cached targets to find valid merge jobs
        /// </summary>
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

        #endregion

        #region Target selection

        /// <summary>
        /// Gets all things potentially needing merging from the map
        /// </summary>
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
                Job job = JobMaker.MakeJob(WorkJobDef, thing, heldThing.Position);
                job.count = Mathf.Min(heldThing.def.stackLimit - heldThing.stackCount, thing.stackCount);
                job.haulMode = HaulMode.ToCellStorage;
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to merge {thing.Label} ({thing.stackCount}) with {heldThing.Label} ({heldThing.stackCount})");
                return job;
            }

            return null;
        }

        #endregion

        #region Reset

        /// <summary>
        /// Reset the cache - implements IResettableCache
        /// </summary>
        public override void Reset()
        {
            base.Reset();
        }

        #endregion
    }
}