using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to empty egg boxes.
    /// Uses the Hauling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Hauling_EmptyEggBox_PawnControl : JobGiver_Hauling_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        public override bool RequiresMapZoneorArea => false;

        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.EmptyThingContainer;

        /// <summary>
        /// Human-readable name for debug logging
        /// </summary>
        protected override string DebugName => "EmptyEggBox";

        /// <summary>
        /// Update cache every 5 seconds - egg boxes don't fill that quickly
        /// </summary>
        protected override int CacheUpdateInterval => base.CacheUpdateInterval;

        /// <summary>
        /// Distance thresholds for egg boxes
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 225f, 625f, 2500f }; // 15, 25, 50 tiles

        #endregion

        #region Core flow

        protected override float GetBasePriority(string workTag)
        {
            // Emptying egg boxes is moderately important
            return 5.2f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // IMPORTANT: Only player pawns and slaves owned by player should empty egg boxes
            if (pawn.Faction != Faction.OfPlayer &&
                !(pawn.IsSlave && pawn.HostFaction == Faction.OfPlayer))
                return null;

            if (ShouldSkip(pawn))
            {
                return null;
            }

            return base.TryGiveJob(pawn);
        }

        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // Use bucketing system to find the closest valid egg box
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                targets,
                (thing) => (thing.Position - pawn.Position).LengthHorizontalSquared,
                DistanceThresholds);

            // Use the centralized-but-keyed-by-giver cache system by using the job giver's type to store/retrieve reachability results
            Thing targetThing = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (thing, worker) => IsValidEggBoxTarget(thing, worker)
            );

            // Create job if target found
            if (targetThing != null)
            {
                CompEggContainer comp = targetThing.TryGetComp<CompEggContainer>();
                if (comp?.ContainedThing != null)
                {
                    // Find storage for the eggs
                    if (!StoreUtility.TryFindBestBetterStorageFor(comp.ContainedThing,
                                                                 pawn,
                                                                 pawn.Map,
                                                                 StoragePriority.Unstored,
                                                                 pawn.Faction,
                                                                 out IntVec3 foundCell,
                                                                 out _))
                        return null;

                    // Create the job
                    Job job = JobMaker.MakeJob(WorkJobDef, targetThing, comp.ContainedThing, foundCell);
                    job.count = comp.ContainedThing.stackCount;

                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to empty egg box containing {comp.ContainedThing.Label} ({comp.ContainedThing.stackCount})");
                    return job;
                }
            }

            // Return null if no valid job is found
            return null;
        }

        #endregion

        #region Target selection

        /// <summary>
        /// Gets all egg boxes on the map that need emptying
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null)
                yield break;

            // Find all egg boxes on the map
            foreach (Thing thing in map.listerThings.ThingsOfDef(ThingDefOf.EggBox))
            {
                if (thing != null && thing.Spawned && !thing.Destroyed)
                {
                    CompEggContainer comp = thing.TryGetComp<CompEggContainer>();
                    if (comp?.ContainedThing != null)
                    {
                        yield return thing;
                    }
                }
            }
        }

        /// <summary>
        /// Determines if an egg box is valid for emptying
        /// </summary>
        private bool IsValidEggBoxTarget(Thing thing, Pawn pawn)
        {
            // Skip if not an egg box
            if (thing == null || thing.def != ThingDefOf.EggBox || thing.Destroyed || !thing.Spawned)
                return false;

            // Skip if forbidden
            if (thing.IsForbidden(pawn))
                return false;

            // Skip if unreachable or can't be reserved
            if (!pawn.CanReserveAndReach(thing, PathEndMode.Touch, pawn.NormalMaxDanger()))
                return false;

            // Check egg container component
            CompEggContainer comp = thing.TryGetComp<CompEggContainer>();
            if (comp?.ContainedThing == null)
                return false;

            // Check if we can empty the box
            if (!comp.CanEmpty && !pawn.WorkTagIsDisabled(WorkTags.Violent))
                return false;

            // Check if we can find a place to store eggs
            return StoreUtility.TryFindBestBetterStorageFor(comp.ContainedThing,
                                                           pawn,
                                                           pawn.Map,
                                                           StoragePriority.Unstored,
                                                           pawn.Faction,
                                                           out _,
                                                           out _);
        }

        #endregion
    }
}