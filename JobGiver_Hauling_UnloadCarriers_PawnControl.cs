using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to unload the inventory of carriers like pack animals or shuttles.
    /// Uses the Hauling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Hauling_UnloadCarriers_PawnControl : JobGiver_Hauling_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging
        /// </summary>
        protected override string DebugName => "UnloadCarriers";

        /// <summary>
        /// Update cache every 2 seconds - carriers need quick attention
        /// </summary>
        protected override int CacheUpdateInterval => 120;

        /// <summary>
        /// Smaller distance thresholds for carriers - typically centered in a base
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 100f, 400f, 900f }; // 10, 20, 30 tiles

        #endregion

        #region Core flow

        protected override float GetBasePriority(string workTag)
        {
            return 5.8f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Skip if there are no pawns with UnloadEverything flag on the map (quick optimization)
            if (ShouldSkip(pawn))
            {
                return null;
            }

            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Hauling_UnloadCarriers_PawnControl>(
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

                    // Get carriers from shared cache
                    if (!_haulableCache.TryGetValue(mapId, out var haulables) || haulables.Count == 0)
                        return null;

                    // Filter to pawns only
                    var unloadablePawns = haulables.OfType<Pawn>().ToList();
                    if (unloadablePawns.Count == 0)
                        return null;

                    // Use the bucketing system to find the closest valid carrier
                    var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                        p,
                        unloadablePawns.Cast<Thing>().ToList(),
                        (thing) => (thing.Position - p.Position).LengthHorizontalSquared,
                        DistanceThresholds);

                    // Find the best carrier to unload
                    Thing targetThing = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                        buckets,
                        p,
                        (thing, worker) => IsValidCarrierTarget(thing, worker),
                        _reachabilityCache);

                    // Create job if target found
                    if (targetThing != null && targetThing is Pawn targetPawn)
                    {
                        Job job = JobMaker.MakeJob(JobDefOf.UnloadInventory, targetPawn);
                        Utility_DebugManager.LogNormal($"{p.LabelShort} created job to unload inventory of {targetPawn.LabelCap}");
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

            // Filter to pawns only
            var unloadablePawns = targets.OfType<Pawn>().ToList();
            if (unloadablePawns.Count == 0)
                return null;

            // Use the bucketing system to find the closest valid carrier
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                unloadablePawns.Cast<Thing>().ToList(),
                (thing) => (thing.Position - pawn.Position).LengthHorizontalSquared,
                DistanceThresholds);

            // Find the best carrier to unload
            Thing targetThing = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (thing, worker) => IsValidCarrierTarget(thing, worker),
                _reachabilityCache);

            // Create job if target found
            if (targetThing != null && targetThing is Pawn targetPawn)
            {
                Job job = JobMaker.MakeJob(JobDefOf.UnloadInventory, targetPawn);
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to unload inventory of {targetPawn.LabelCap}");
                return job;
            }

            return null;
        }

        /// <summary>
        /// Check if we should skip this job giver entirely (optimization)
        /// </summary>
        public override bool ShouldSkip(Pawn pawn)
        {
            if (pawn?.Map == null)
                return true;

            IReadOnlyList<Pawn> allPawnsSpawned = pawn.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < allPawnsSpawned.Count; i++)
            {
                if (allPawnsSpawned[i].inventory.UnloadEverything)
                    return false;
            }
            return true;
        }

        #endregion

        #region Target selection

        /// <summary>
        /// Gets all pawns whose inventory should be unloaded
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null)
                yield break;

            // Use the map's existing list for better performance
            List<Pawn> unloadablePawns = map.mapPawns.SpawnedPawnsWhoShouldHaveInventoryUnloaded;
            if (unloadablePawns != null && unloadablePawns.Count > 0)
            {
                foreach (Pawn pawn in unloadablePawns)
                {
                    if (pawn != null && pawn.Spawned && !pawn.Dead &&
                        pawn.inventory.UnloadEverything && pawn.inventory.innerContainer.Count > 0)
                    {
                        yield return pawn;
                    }
                }
            }
        }

        /// <summary>
        /// Determines if a carrier is valid for unloading
        /// </summary>
        private bool IsValidCarrierTarget(Thing thing, Pawn pawn)
        {
            // Must be a pawn
            if (!(thing is Pawn carrier))
                return false;

            // Skip if no longer valid
            if (carrier.Dead || !carrier.Spawned || carrier == pawn)
                return false;

            // Skip if no longer needs unloading
            if (!carrier.inventory.UnloadEverything || carrier.inventory.innerContainer.Count == 0)
                return false;

            // Skip if forbidden or unreachable
            if (carrier.IsForbidden(pawn) || !pawn.CanReserveAndReach(carrier, PathEndMode.Touch, pawn.NormalMaxDanger()))
                return false;

            // Use the utility function if available, otherwise just return true
            if (UnloadCarriersJobGiverUtility.HasJobOnThing(pawn, carrier, false))
                return UnloadCarriersJobGiverUtility.HasJobOnThing(pawn, carrier, false);

            return true;
        }

        #endregion
    }
}