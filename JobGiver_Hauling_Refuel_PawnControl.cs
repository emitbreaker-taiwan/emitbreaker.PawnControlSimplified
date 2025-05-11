using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns refueling tasks to pawns with the Hauling work type.
    /// Handles non-turret buildings that require fuel.
    /// Optimized for large colonies with many refuelable buildings using distance-based bucketing.
    /// </summary>
    public class JobGiver_Hauling_Refuel_PawnControl : JobGiver_Hauling_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging
        /// </summary>
        protected override string DebugName => "Refuel";

        /// <summary>
        /// Update cache every 3 seconds - fuel levels don't change rapidly
        /// </summary>
        protected override int CacheUpdateInterval => 180;

        /// <summary>
        /// Distance thresholds for fuel-requiring buildings
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        #endregion

        #region Core flow

        protected override float GetBasePriority(string workTag)
        {
            // Refueling is important to keep buildings running
            return 5.8f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Hauling_Refuel_PawnControl>(
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

                    // Get refuelable buildings from shared cache
                    if (!_haulableCache.TryGetValue(mapId, out var haulables) || haulables.Count == 0)
                        return null;

                    // Use the bucketing system to find the closest valid refuelable building
                    var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                        p,
                        haulables,
                        (thing) => (thing.Position - p.Position).LengthHorizontalSquared,
                        DistanceThresholds);

                    // Find the best building to refuel
                    Thing targetBuilding = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                        buckets,
                        p,
                        (thing, worker) => IsValidRefuelTarget(thing, worker),
                        _reachabilityCache);

                    // Create job if target found
                    if (targetBuilding != null)
                    {
                        CompRefuelable refuelable = targetBuilding.TryGetComp<CompRefuelable>();
                        if (refuelable != null)
                        {
                            // Determine job type based on atomicFueling flag
                            JobDef jobDef = refuelable.Props.atomicFueling ?
                                JobDefOf.RefuelAtomic : JobDefOf.Refuel;

                            Job job = JobMaker.MakeJob(jobDef, targetBuilding);
                            Utility_DebugManager.LogNormal($"{p.LabelShort} created job to refuel {targetBuilding.LabelCap}");
                            return job;
                        }
                    }

                    return null;
                },
                debugJobDesc: DebugName);
        }

        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // Iterate through the cached targets to find a valid refuelable building
            foreach (var target in targets)
            {
                if (IsValidRefuelTarget(target, pawn))
                {
                    CompRefuelable refuelable = target.TryGetComp<CompRefuelable>();
                    if (refuelable != null)
                    {
                        // Determine job type based on atomicFueling flag
                        JobDef jobDef = refuelable.Props.atomicFueling ? JobDefOf.RefuelAtomic : JobDefOf.Refuel;

                        Job job = JobMaker.MakeJob(jobDef, target);
                        Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to refuel {target.LabelCap}");
                        return job;
                    }
                }
            }

            // Return null if no valid target is found
            return null;
        }

        #endregion

        #region Target selection

        /// <summary>
        /// Gets all refuelable buildings on the map (excluding turrets)
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null)
                yield break;

            // Find all refuelable buildings (excluding turrets)
            foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Refuelable))
            {
                // Skip turrets (these are handled by JobGiver_Refuel_Turret_PawnControl)
                if (thing is Building_Turret)
                    continue;

                CompRefuelable refuelable = thing.TryGetComp<CompRefuelable>();
                if (refuelable != null && refuelable.ShouldAutoRefuelNow && !thing.IsBurning())
                {
                    yield return thing;
                }
            }
        }

        /// <summary>
        /// Determines if a building is valid for refueling
        /// </summary>
        protected virtual bool IsValidRefuelTarget(Thing thing, Pawn pawn)
        {
            // Skip if no longer valid
            if (thing == null || thing.Destroyed || !thing.Spawned)
                return false;

            // Skip if burning
            if (thing.IsBurning())
                return false;

            // Get refuelable component
            CompRefuelable refuelable = thing.TryGetComp<CompRefuelable>();
            if (refuelable == null || !refuelable.ShouldAutoRefuelNow)
                return false;

            // Skip if forbidden or unreachable
            if (thing.IsForbidden(pawn) || !pawn.CanReserveAndReach(thing, PathEndMode.Touch, pawn.NormalMaxDanger()))
                return false;

            // Check if fuel is available
            return IsFuelAvailable(pawn, refuelable);
        }

        /// <summary>
        /// Checks if fuel is available for the given refuelable component
        /// </summary>
        private bool IsFuelAvailable(Pawn pawn, CompRefuelable refuelable)
        {
            // Check the resource counter for available fuel
            foreach (var resourceCount in pawn.Map.resourceCounter.AllCountedAmounts)
            {
                if (refuelable.Props.fuelFilter.Allows(resourceCount.Key) && resourceCount.Value > 0)
                {
                    return true;
                }
            }

            // Check if there's any available fuel that can be reserved
            foreach (Thing fuel in pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
            {
                if (refuelable.Props.fuelFilter.Allows(fuel) &&
                    !fuel.IsForbidden(pawn) &&
                    pawn.CanReserve(fuel) &&
                    pawn.CanReach(fuel, PathEndMode.ClosestTouch, pawn.NormalMaxDanger()))
                {
                    return true;
                }
            }

            return false;
        }

        #endregion
    }
}