using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that allows non-humanlike pawns to deconstruct buildings with the Deconstruct designation.
    /// </summary>
    public class JobGiver_Deconstruct_PawnControl : JobGiver_RemoveBuilding_PawnControl
    {
        protected override DesignationDef Designation => DesignationDefOf.Deconstruct;

        protected override JobDef RemoveBuildingJob => JobDefOf.Deconstruct;

        public override float GetPriority(Pawn pawn)
        {
            // Higher priority than extract tree but lower than most urgent tasks
            return 5.9f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // This approach properly overrides the base class implementation
            // and handles the additional building-specific validation
            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Deconstruct_PawnControl>(
                pawn,
                "Construction", // This uses the Construction work type
                (p, forced) => {
                    // First call base implementation to update caches
                    UpdateTargetCache(p.Map);

                    // Get cached targets
                    int mapId = p.Map.uniqueID;
                    if (!_targetCache.ContainsKey(mapId) || _targetCache[mapId].Count == 0)
                        return null;

                    // Use the same bucketing and target selection as base class
                    var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                        p,
                        _targetCache[mapId],
                        (thing) => (thing.Position - p.Position).LengthHorizontalSquared,
                        DISTANCE_THRESHOLDS
                    );

                    // Find best target with additional building-specific validation
                    Thing bestTarget = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                        buckets,
                        p,
                        (thing, pn) => {
                            // Basic validation from base class
                            if (!Utility_JobGiverManager.IsValidFactionInteraction(thing, pn, requiresDesignator: true))
                                return false;

                            if (thing == null || thing.Destroyed || !thing.Spawned)
                                return false;

                            if (thing.Map.designationManager.DesignationOn(thing, Designation) == null)
                                return false;

                            CompExplosive explosive = thing.TryGetComp<CompExplosive>();
                            if (explosive != null && explosive.wickStarted)
                                return false;

                            if (thing.IsForbidden(pn) ||
                                !pn.CanReserve(thing, 1, -1, null, forced) ||
                                !pn.CanReach(thing, PathEndMode.Touch, Danger.Some))
                                return false;

                            // Special building checks for deconstruction
                            Building building = thing.GetInnerIfMinified() as Building;
                            if (building == null)
                                return false;

                            if (!building.DeconstructibleBy(pn.Faction))
                                return false;

                            return true;
                        },
                        _reachabilityCache // Pass the entire dictionary, not just mapId entry
                    );

                    // Create job if target found
                    if (bestTarget != null)
                    {
                        Job job = JobMaker.MakeJob(RemoveBuildingJob, bestTarget);
                        Utility_DebugManager.LogNormal($"{p.LabelShort} created job to deconstruct {bestTarget.LabelCap}");
                        return job;
                    }

                    return null;
                },
                debugJobDesc: "deconstruction assignment",
                skipEmergencyCheck: true);
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static new void ResetCache()
        {
            JobGiver_RemoveBuilding_PawnControl.ResetCache();
        }

        public override string ToString()
        {
            return "JobGiver_Deconstruct_PawnControl";
        }
    }
}