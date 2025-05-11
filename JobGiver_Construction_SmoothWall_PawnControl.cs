using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that allows pawns to smooth walls in designated areas.
    /// Uses the Construction work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Construction_SmoothWall_PawnControl : JobGiver_Scan_PawnControl
    {
        #region Overrides

        protected override string WorkTag => "Construction";
        protected override int CacheUpdateInterval => 180; // Update every 3 seconds
        protected override string DebugName => "wall smoothing assignment";

        protected override bool ShouldExecuteNow(int mapId)
        {
            // Quick check if there are wall smoothing designations on the map
            var map = Find.Maps.FirstOrDefault(m => m.uniqueID == mapId);
            return map != null && map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.SmoothWall);
        }

        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null) return Enumerable.Empty<Thing>();

            var buildings = new List<Building>();

            // Find all designated walls for smoothing
            foreach (Designation designation in map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.SmoothWall))
            {
                // Get the building at this cell
                Building edifice = designation.target.Cell.GetEdifice(map);

                // Skip if there's no building or it's not smoothable
                if (edifice == null || !edifice.def.IsSmoothable)
                    continue;

                buildings.Add(edifice);
            }

            return buildings;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Use the StandardTryGiveJob pattern directly
            return Utility_JobGiverManager.StandardTryGiveJob<Building>(
                pawn,
                WorkTag,
                (p, forced) => {
                    // Get buildings that need smoothing from cache
                    int mapId = p.Map.uniqueID;
                    var targets = _cachedTargets.TryGetValue(mapId, out var list) ? list : null;
                    if (targets == null || targets.Count == 0) return null;

                    // Use distance bucketing for efficient target selection
                    float[] distanceThresholds = new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles
                    var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                        p,
                        targets.OfType<Building>(),
                        building => (building.Position - p.Position).LengthHorizontalSquared,
                        distanceThresholds
                    );

                    // Find the best wall to smooth
                    Building bestBuilding = Utility_JobGiverManager.FindFirstValidTargetInBuckets<Building>(
                        buckets,
                        p,
                        (building, builder) => ValidateBuildingTarget(building, builder, forced)
                    );

                    // Create job if target found
                    if (bestBuilding != null)
                    {
                        Job job = JobMaker.MakeJob(JobDefOf.SmoothWall, bestBuilding);
                        Utility_DebugManager.LogNormal($"{p.LabelShort} created job to smooth wall: {bestBuilding}");
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

            // Use distance bucketing for efficient target selection
            float[] distanceThresholds = new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                targets.OfType<Building>(),
                building => (building.Position - pawn.Position).LengthHorizontalSquared,
                distanceThresholds
            );

            // Find the best wall to smooth
            Building bestBuilding = Utility_JobGiverManager.FindFirstValidTargetInBuckets<Building>(
                buckets,
                pawn,
                (building, builder) => ValidateBuildingTarget(building, builder, forced)
            );

            // Create job if target found
            if (bestBuilding != null)
            {
                Job job = JobMaker.MakeJob(JobDefOf.SmoothWall, bestBuilding);
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to smooth wall: {bestBuilding}");
                return job;
            }

            return null;
        }

        #endregion

        #region Building-selection helpers

        private bool ValidateBuildingTarget(Building building, Pawn pawn, bool forced)
        {
            // Skip if no longer valid
            if (building == null || building.Destroyed || !building.Spawned)
                return false;

            // Skip if no longer designated
            if (pawn.Map.designationManager.DesignationAt(building.Position, DesignationDefOf.SmoothWall) == null)
                return false;

            // Skip if no longer smoothable
            if (!building.def.IsSmoothable)
                return false;

            // Skip if forbidden or unreachable
            if (building.IsForbidden(pawn) ||
                !pawn.CanReserve(building, 1, -1, null, forced) ||
                !pawn.CanReserve(building.Position, 1, -1, null, forced) ||
                !pawn.CanReach(building, PathEndMode.Touch, Danger.Some))
                return false;

            return true;
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return "JobGiver_SmoothWall_PawnControl";
        }

        #endregion
    }
}