using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobModule that assigns turret refueling tasks to pawns with the Hauling work type.
    /// Optimized for large colonies with many turrets using distance-based bucketing.
    /// </summary>
    public class JobModule_Hauling_RefuelTurret : JobModule_Hauling
    {
        public override string UniqueID => "RefuelTurret";
        public override float Priority => 5.9f; // Same priority as the original JobGiver
        public override string Category => "Maintenance"; // Added category for consistency
        public override int CacheUpdateInterval => 150; // Update every 2.5 seconds

        // Cache for turrets that need refueling
        private static readonly Dictionary<int, List<Thing>> _turretCache = new Dictionary<int, List<Thing>>();
        private static readonly Dictionary<int, Dictionary<Thing, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Thing, bool>>();
        private static int _lastCacheUpdateTick = -999;

        // These ThingRequestGroups are what this module cares about
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups =>
            new HashSet<ThingRequestGroup> { ThingRequestGroup.Refuelable };

        public override void UpdateCache(Map map, List<Thing> targetCache)
        {
            base.UpdateCache(map, targetCache);

            if (map == null) return;
            int mapId = map.uniqueID;
            bool hasTargets = false;

            // Use the base class's progressive cache update
            UpdateCacheProgressively(
                map,
                targetCache,
                ref _lastCacheUpdateTick,
                RelevantThingRequestGroups,
                thing => {
                    // Only include turrets
                    Building_Turret turret = thing as Building_Turret;
                    if (turret == null)
                        return false;

                    CompRefuelable refuelable = turret.TryGetComp<CompRefuelable>();
                    return refuelable != null && refuelable.ShouldAutoRefuelNow && !turret.IsBurning();
                },
                _turretCache,
                CacheUpdateInterval
            );

            SetHasTargets(map, hasTargets || (targetCache != null && targetCache.Count > 0));
        }

        public override bool ShouldHaulItem(Thing item, Map map)
        {
            Building_Turret turret = item as Building_Turret;
            if (turret == null || !turret.Spawned)
                return false;

            CompRefuelable refuelable = turret.TryGetComp<CompRefuelable>();
            return refuelable != null && refuelable.ShouldAutoRefuelNow && !turret.IsBurning();
        }

        public override bool ValidateHaulingJob(Thing target, Pawn hauler)
        {
            Building_Turret turret = target as Building_Turret;
            if (turret == null || turret.Destroyed || !turret.Spawned || turret.IsBurning())
                return false;

            // Get refuelable component
            CompRefuelable refuelable = turret.TryGetComp<CompRefuelable>();
            if (refuelable == null || !refuelable.ShouldAutoRefuelNow)
                return false;

            // Check faction interaction
            if (!Utility_JobGiverManager.IsValidFactionInteraction(turret, hauler, requiresDesignator: false))
                return false;

            // Skip if forbidden or unreachable
            if (turret.IsForbidden(hauler) ||
                !hauler.CanReserve(turret, 1, -1) ||
                !hauler.CanReach(turret, PathEndMode.ClosestTouch, hauler.NormalMaxDanger()))
                return false;

            // Use base class helper for fuel check
            return CheckFuelAvailability(refuelable, hauler);
        }

        protected override Job CreateHaulingJob(Pawn hauler, Thing target)
        {
            Building_Turret turret = target as Building_Turret;
            if (turret == null)
                return null;

            CompRefuelable refuelable = turret.TryGetComp<CompRefuelable>();
            if (refuelable != null)
            {
                // Determine job type based on atomicFueling flag
                JobDef jobDef = refuelable.Props.atomicFueling ?
                    JobDefOf.RearmTurretAtomic : JobDefOf.RearmTurret;

                Job job = JobMaker.MakeJob(jobDef, turret);
                Utility_DebugManager.LogNormal($"{hauler.LabelShort} created job to refuel turret {turret.LabelCap}");
                return job;
            }

            return null;
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public override void ResetStaticData()
        {
            Utility_CacheManager.ResetJobGiverCache(_turretCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }
    }
}