using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobModule that assigns refueling tasks to pawns with the Hauling work type.
    /// Handles non-turret buildings that require fuel.
    /// Optimized for large colonies with many refuelable buildings.
    /// </summary>
    public class JobModule_Hauling_Refuel : JobModule_Hauling
    {
        public override string UniqueID => "Refuel";
        public override float Priority => 5.8f;
        public override string Category => "Maintenance";
        public override int CacheUpdateInterval => 180; // Update every 3 seconds

        // Cache for buildings that need refueling
        private static readonly Dictionary<int, List<Thing>> _refuelableBuildingsCache = new Dictionary<int, List<Thing>>();
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
                    // Skip turrets
                    if (thing is Building_Turret)
                        return false;

                    CompRefuelable refuelable = thing.TryGetComp<CompRefuelable>();
                    return refuelable != null && refuelable.ShouldAutoRefuelNow && !thing.IsBurning();
                },
                _refuelableBuildingsCache,
                CacheUpdateInterval
            );

            SetHasTargets(map, hasTargets || (targetCache != null && targetCache.Count > 0));
        }

        public override bool ShouldHaulItem(Thing item, Map map)
        {
            // Check if this item is a refuelable building (not a turret)
            if (item is Building_Turret)
                return false;

            CompRefuelable refuelable = item.TryGetComp<CompRefuelable>();
            return refuelable != null && refuelable.ShouldAutoRefuelNow && !item.IsBurning();
        }

        public override bool ValidateHaulingJob(Thing target, Pawn hauler)
        {
            // Skip if no longer valid
            if (target == null || target.Destroyed || !target.Spawned || target.IsBurning())
                return false;

            // Get refuelable component
            CompRefuelable refuelable = target.TryGetComp<CompRefuelable>();
            if (refuelable == null || !refuelable.ShouldAutoRefuelNow)
                return false;

            // Check faction interaction
            if (!Utility_JobGiverManager.IsValidFactionInteraction(target, hauler, requiresDesignator: false))
                return false;

            // Skip if forbidden or unreachable
            if (target.IsForbidden(hauler) ||
                !hauler.CanReserve(target, 1, -1) ||
                !hauler.CanReach(target, PathEndMode.Touch, hauler.NormalMaxDanger()))
                return false;

            // Use base class helper for fuel check
            return CheckFuelAvailability(refuelable, hauler);
        }

        protected override Job CreateHaulingJob(Pawn hauler, Thing target)
        {
            CompRefuelable refuelable = target.TryGetComp<CompRefuelable>();
            if (refuelable != null)
            {
                // Determine job type based on atomicFueling flag
                JobDef jobDef = refuelable.Props.atomicFueling ?
                    JobDefOf.RefuelAtomic : JobDefOf.Refuel;

                Job job = JobMaker.MakeJob(jobDef, target);
                Utility_DebugManager.LogNormal($"{hauler.LabelShort} created job to refuel {target.LabelCap}");
                return job;
            }
            return null;
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public override void ResetStaticData()
        {
            Utility_CacheManager.ResetJobGiverCache(_refuelableBuildingsCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }
    }
}