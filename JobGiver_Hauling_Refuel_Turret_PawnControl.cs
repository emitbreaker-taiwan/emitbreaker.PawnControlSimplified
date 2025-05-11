using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns turret refueling tasks to pawns with the Hauling work type.
    /// Optimized for large colonies with many turrets using distance-based bucketing.
    /// </summary>
    public class JobGiver_Hauling_Refuel_Turret_PawnControl : JobGiver_Hauling_Refuel_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging
        /// </summary>
        protected override string DebugName => "RefuelTurret";

        /// <summary>
        /// Update cache more frequently for turrets - they're critical defense structures
        /// </summary>
        protected override int CacheUpdateInterval => 150; // Every 2.5 seconds

        /// <summary>
        /// Same distance thresholds as regular refueling
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        #endregion

        #region Core flow

        protected override float GetBasePriority(string workTag)
        {
            return 5.9f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Hauling_Refuel_Turret_PawnControl>(
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

                    // Get turrets from shared cache
                    if (!_haulableCache.TryGetValue(mapId, out var haulables) || haulables.Count == 0)
                        return null;

                    // Filter to just turrets
                    var turrets = haulables.OfType<Building_Turret>().ToList();
                    if (turrets.Count == 0)
                        return null;

                    // Use the bucketing system to find the closest valid turret
                    var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                        p,
                        turrets.Cast<Thing>().ToList(),
                        (thing) => (thing.Position - p.Position).LengthHorizontalSquared,
                        DistanceThresholds);

                    // Find the best turret to refuel
                    Thing targetTurret = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                        buckets,
                        p,
                        (thing, worker) => IsValidTurretTarget(thing, worker),
                        _reachabilityCache);

                    // Create job if target found
                    if (targetTurret != null && targetTurret is Building_Turret turret)
                    {
                        CompRefuelable refuelable = turret.TryGetComp<CompRefuelable>();
                        if (refuelable != null)
                        {
                            // Use RearmTurret instead of Refuel for turrets
                            JobDef jobDef = refuelable.Props.atomicFueling ?
                                JobDefOf.RearmTurretAtomic : JobDefOf.RearmTurret;

                            Job job = JobMaker.MakeJob(jobDef, turret);
                            Utility_DebugManager.LogNormal($"{p.LabelShort} created job to refuel turret {turret.LabelCap}");
                            return job;
                        }
                    }

                    return null;
                },
                debugJobDesc: DebugName);
        }

        #endregion

        #region Target selection

        /// <summary>
        /// Gets all turrets on the map that need refueling
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null)
                yield break;

            // Find all colony turrets that need refueling
            foreach (Building building in map.listerBuildings.allBuildingsColonist)
            {
                if (building is Building_Turret turret)
                {
                    CompRefuelable refuelable = turret.TryGetComp<CompRefuelable>();
                    if (refuelable != null && refuelable.ShouldAutoRefuelNow && !turret.IsBurning())
                    {
                        yield return turret;
                    }
                }
            }
        }

        /// <summary>
        /// Determines if a turret is valid for refueling
        /// </summary>
        private bool IsValidTurretTarget(Thing thing, Pawn pawn)
        {
            // Ensure it's a turret
            if (!(thing is Building_Turret turret))
                return false;

            // Use the base validation logic for general checks
            if (!IsValidRefuelTarget(thing, pawn))
                return false;

            // Any turret-specific checks could go here

            return true;
        }

        #endregion
    }
}