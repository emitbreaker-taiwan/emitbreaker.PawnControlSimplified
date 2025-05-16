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
        protected override int CacheUpdateInterval => base.CacheUpdateInterval;

        /// <summary>
        /// Same distance thresholds as regular refueling
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        #endregion

        #region Core flow

        protected override float GetBasePriority(string workTag)
        {
            // Slightly higher priority than regular refueling since turrets are defensive structures
            return 5.9f;
        }

        /// <summary>
        /// Process cached targets to find a valid job for refueling turrets
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            if (targets == null || targets.Count == 0)
                return null;

            // Filter to just turrets
            var turrets = targets.OfType<Building_Turret>().ToList();
            if (turrets.Count == 0)
                return null;

            // Use the bucketing system to find the closest valid turret
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                turrets.Cast<Thing>().ToList(),
                (thing) => (thing.Position - pawn.Position).LengthHorizontalSquared,
                DistanceThresholds);

            // Find the best turret to refuel using the centralized cache system
            Thing targetTurret = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (thing, worker) => IsValidTurretTarget(thing, worker),
                null); // Let the parent class handle reachability caching

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
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to refuel turret {turret.LabelCap}");
                    return job;
                }
            }

            return null;
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