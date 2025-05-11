using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to take beer out of fermenting barrels when ready.
    /// Uses the Hauling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Hauling_TakeBeerOutOfBarrel_PawnControl : JobGiver_Hauling_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging
        /// </summary>
        protected override string DebugName => "TakeBeerOutOfBarrel";

        /// <summary>
        /// Update cache every 5 seconds - barrels don't change state quickly
        /// </summary>
        protected override int CacheUpdateInterval => 300;

        /// <summary>
        /// Distance thresholds for brewery areas
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        #endregion

        #region Core flow

        public override float GetPriority(Pawn pawn)
        {
            // Taking beer out is moderately important
            return 5.3f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Hauling_TakeBeerOutOfBarrel_PawnControl>(
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

                    // Get barrels from shared cache
                    if (!_haulableCache.TryGetValue(mapId, out var haulables) || haulables.Count == 0)
                        return null;

                    // Filter only fermenting barrels that are ready
                    var barrels = haulables.OfType<Building_FermentingBarrel>().ToList();
                    if (barrels.Count == 0)
                        return null;

                    // Use the bucketing system to find the closest valid barrel
                    var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                        p,
                        barrels.Cast<Thing>().ToList(),
                        (thing) => (thing.Position - p.Position).LengthHorizontalSquared,
                        DistanceThresholds);

                    // Find the best barrel to take beer from
                    Thing targetThing = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                        buckets,
                        p,
                        (thing, worker) => IsValidFermentedBarrelTarget(thing, worker),
                        _reachabilityCache);

                    // Create job if target found
                    if (targetThing != null && targetThing is Building_FermentingBarrel barrel)
                    {
                        Job job = JobMaker.MakeJob(JobDefOf.TakeBeerOutOfFermentingBarrel, barrel);
                        Utility_DebugManager.LogNormal($"{p.LabelShort} created job to take beer out of fermenting barrel");
                        return job;
                    }

                    return null;
                },
                debugJobDesc: DebugName);
        }

        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // Filter only fermenting barrels that are ready
            var validBarrels = targets.OfType<Building_FermentingBarrel>().Where(b => IsValidFermentedBarrelTarget(b, pawn)).ToList();
            if (validBarrels.Count == 0)
                return null;

            // Use the bucketing system to find the closest valid barrel
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                validBarrels.Cast<Thing>().ToList(),
                (thing) => (thing.Position - pawn.Position).LengthHorizontalSquared,
                DistanceThresholds);

            // Find the best barrel to take beer from
            Thing targetThing = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (thing, worker) => IsValidFermentedBarrelTarget(thing, worker),
                _reachabilityCache);

            // Create job if target found
            if (targetThing != null && targetThing is Building_FermentingBarrel barrel)
            {
                Job job = JobMaker.MakeJob(JobDefOf.TakeBeerOutOfFermentingBarrel, barrel);
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to take beer out of fermenting barrel");
                return job;
            }

            return null;
        }

        #endregion

        #region Target selection

        /// <summary>
        /// Gets all fermenting barrels on the map that are ready
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null)
                yield break;

            // Find all fermenting barrels on the map that are ready
            foreach (Thing thing in map.listerThings.ThingsOfDef(ThingDefOf.FermentingBarrel))
            {
                Building_FermentingBarrel barrel = thing as Building_FermentingBarrel;
                if (barrel != null && barrel.Spawned && barrel.Fermented)
                {
                    yield return barrel;
                }
            }
        }

        /// <summary>
        /// Determines if a barrel is valid for taking beer out
        /// </summary>
        private bool IsValidFermentedBarrelTarget(Thing thing, Pawn pawn)
        {
            // Skip if not a fermenting barrel
            Building_FermentingBarrel barrel = thing as Building_FermentingBarrel;
            if (barrel == null)
                return false;

            // Skip if no longer valid
            if (barrel.Destroyed || !barrel.Spawned)
                return false;

            // Skip if not fermented, burning, or forbidden
            if (!barrel.Fermented || barrel.IsBurning() || barrel.IsForbidden(pawn))
                return false;

            // Skip if being deconstructed
            if (pawn.Map.designationManager.DesignationOn(barrel, DesignationDefOf.Deconstruct) != null)
                return false;

            // Skip if unreachable or can't be reserved
            if (!pawn.CanReserve(barrel) || !pawn.CanReserveAndReach(barrel, PathEndMode.Touch, pawn.NormalMaxDanger()))
                return false;

            return true;
        }

        #endregion
    }
}