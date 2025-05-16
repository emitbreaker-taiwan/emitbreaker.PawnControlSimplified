using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns corpse hauling tasks to eligible pawns.
    /// Uses the Hauling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Hauling_HaulCorpses_PawnControl : JobGiver_Hauling_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        public override bool RequiresMapZoneorArea => false;

        /// <summary>
        /// Human-readable name for debug logging
        /// </summary>
        protected override string DebugName => "HaulCorpses";

        /// <summary>
        /// Update cache every 4 seconds
        /// </summary>
        protected override int CacheUpdateInterval => base.CacheUpdateInterval;

        /// <summary>
        /// Smaller distance thresholds for corpses - prioritize closer ones
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 100f, 400f, 1600f }; // 10, 20, 40 tiles

        #endregion

        #region Core flow

        /// <summary>
        /// The job to create when a valid target is found
        /// Custom implementation for corpse hauling is used instead
        /// </summary>
        protected override JobDef WorkJobDef => null;

        protected override float GetBasePriority(string workTag)
        {
            // Hauling corpses is more important than regular hauling
            return 4.2f;
        }

        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // Filter only corpses from the cached targets
            var corpses = targets.OfType<Corpse>().ToList();
            if (corpses.Count == 0)
                return null;

            // Use the bucketing system to find the closest valid corpse
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                corpses,
                (corpse) => (corpse.Position - pawn.Position).LengthHorizontalSquared,
                DistanceThresholds);

            // Use the centralized cache system with proper typing
            Corpse targetCorpse = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (corpse, worker) => IsValidCorpseTarget(corpse, worker),
                null); // Reachability cache handled by parent class

            // Create and return job if we found a valid target
            if (targetCorpse != null)
            {
                // Use the appropriate hauling utility method to create the job
                Job haulJob = HaulAIUtility.HaulToStorageJob(pawn, targetCorpse);

                if (haulJob != null)
                {
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to haul corpse: {targetCorpse.Label}");
                }

                return haulJob;
            }

            return null;
        }

        #endregion

        #region Target selection

        /// <summary>
        /// Gets all corpses that need hauling on the map
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map?.listerHaulables != null)
            {
                // Return all corpses that need hauling
                foreach (Thing thing in map.listerHaulables.ThingsPotentiallyNeedingHauling())
                {
                    if (thing is Corpse corpse && corpse.Spawned)
                    {
                        yield return corpse;
                    }
                }
            }
        }

        /// <summary>
        /// Determines if a corpse is valid for hauling
        /// </summary>
        private bool IsValidCorpseTarget(Corpse corpse, Pawn pawn)
        {
            // Skip invalid corpses
            if (corpse == null || corpse.Destroyed || !corpse.Spawned)
                return false;

            // Skip if corpse is forbidden or unreachable
            if (corpse.IsForbidden(pawn) || !pawn.CanReserveAndReach(corpse, PathEndMode.ClosestTouch, pawn.NormalMaxDanger()))
                return false;

            // Check if an animal is reserving the corpse (from original WorkGiver_HaulCorpses)
            Pawn reservingPawn = pawn.Map.physicalInteractionReservationManager.FirstReserverOf(corpse);
            if (reservingPawn != null && reservingPawn.RaceProps.Animal && reservingPawn.Faction != Faction.OfPlayer)
                return false;

            // Check if there's a storage location for this corpse
            if (!StoreUtility.TryFindBestBetterStoreCellFor(corpse, pawn, pawn.Map,
                StoragePriority.Unstored, pawn.Faction, out _, true))
            {
                return false;
            }

            return true;
        }

        #endregion
    }
}