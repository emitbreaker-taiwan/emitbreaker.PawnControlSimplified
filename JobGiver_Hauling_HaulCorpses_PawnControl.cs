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
        /// Human-readable name for debug logging
        /// </summary>
        protected override string DebugName => "HaulCorpses";

        /// <summary>
        /// Update cache every 4 seconds
        /// </summary>
        protected override int CacheUpdateInterval => 240;

        /// <summary>
        /// Smaller distance thresholds for corpses - prioritize closer ones
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 100f, 400f, 1600f }; // 10, 20, 40 tiles

        #endregion

        #region Core flow

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Hauling_HaulCorpses_PawnControl>(
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

                    // Get corpses from shared cache
                    if (!_haulableCache.TryGetValue(mapId, out var haulables) || haulables.Count == 0)
                        return null;

                    // Filter only corpses from the shared cache
                    var corpses = haulables.OfType<Corpse>().ToList();
                    if (corpses.Count == 0)
                        return null;

                    // Use the bucketing system to find the closest valid corpse
                    var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                        p,
                        corpses,
                        (corpse) => (corpse.Position - p.Position).LengthHorizontalSquared,
                        DistanceThresholds);

                    // Create a custom validator that uses our Thing-based cache but works with Corpses
                    Func<Corpse, Pawn, bool> corpseValidator = (corpse, worker) => {
                        // Use our standard validation but cast the corpse to Thing for reachability cache
                        return IsValidCorpseTarget(corpse, worker);
                    };

                    // Use the regular Thing-based FindFirstValidTargetInBuckets
                    Corpse targetCorpse = Utility_JobGiverManager.FindFirstValidTargetInBuckets<Corpse>(
                        buckets,
                        p,
                        corpseValidator,
                        null); // Don't use reachability cache to avoid type mismatch

                    // Create and return job if we found a valid target
                    if (targetCorpse != null)
                    {
                        return HaulAIUtility.HaulToStorageJob(p, targetCorpse);
                    }

                    return null;
                },
                debugJobDesc: DebugName);
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

            // Create a custom validator that uses our Thing-based cache but works with Corpses
            Func<Corpse, Pawn, bool> corpseValidator = (corpse, worker) => {
                return IsValidCorpseTarget(corpse, worker);
            };

            // Use the regular Thing-based FindFirstValidTargetInBuckets
            Corpse targetCorpse = Utility_JobGiverManager.FindFirstValidTargetInBuckets<Corpse>(
                buckets,
                pawn,
                corpseValidator,
                null); // Don't use reachability cache to avoid type mismatch

            // Create and return job if we found a valid target
            if (targetCorpse != null)
            {
                return HaulAIUtility.HaulToStorageJob(pawn, targetCorpse);
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
            // Using the correct method StoreUtility.TryFindBestBetterStoreCellFor
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