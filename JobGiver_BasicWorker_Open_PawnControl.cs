using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Enhanced JobGiver that assigns container opening jobs to eligible pawns.
    /// </summary>
    public class JobGiver_BasicWorker_Open_PawnControl : JobGiver_BasicWorker_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Use Hauling work tag
        /// </summary>
        protected override string WorkTag => "Hauling";

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "Open";

        /// <summary>
        /// The designation this job giver targets
        /// </summary>
        protected override DesignationDef TargetDesignation => DesignationDefOf.Open;

        /// <summary>
        /// The job to create
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.Open;

        #endregion

        #region Core flow

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Quick early exit if there are no relevant designations
            if (!pawn.Map.designationManager.AnySpawnedDesignationOfDef(TargetDesignation))
            {
                return null;
            }

            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_BasicWorker_Open_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) =>
                {
                    int mapId = p.Map.uniqueID;

                    // Get the current last update time, or default if not set
                    if (!_lastDesignationCacheUpdate.TryGetValue(mapId, out int lastUpdateTick))
                    {
                        lastUpdateTick = -999;
                    }

                    // Update cache of items with designations
                    Utility_CacheManager.UpdateDesignationBasedCache(
                        p.Map,
                        ref lastUpdateTick,
                        CacheUpdateInterval,
                        _designationCache,
                        _reachabilityCache,
                        TargetDesignation,
                        (des) => des?.target.Thing,
                        100);

                    // Store the updated tick back in the dictionary
                    _lastDesignationCacheUpdate[mapId] = lastUpdateTick;

                    // Find a valid target and create a job
                    var targets = _designationCache.TryGetValue(mapId, out var list) ? list : null;
                    if (targets == null || targets.Count == 0)
                        return null;

                    // Use the bucketing system to find the closest valid target
                    var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                        p,
                        targets,
                        (thing) => (thing.Position - p.Position).LengthHorizontalSquared,
                        DistanceThresholds);

                    // Find the best target using the provided validation function 
                    Thing target = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                        buckets,
                        p,
                        (thing, worker) => IsValidTarget(thing, worker),
                        _reachabilityCache);

                    // Create and return job if we found a valid target
                    if (target != null)
                    {
                        return CreateJobForTarget(target);
                    }

                    return null;
                },
                debugJobDesc: DebugName);
        }

        /// <summary>
        /// Process the cached targets and create a job for the pawn
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // Skip if no targets
            if (targets.Count == 0)
                return null;

            // Use bucketing system to find closest valid target
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                targets,
                thing => (thing.Position - pawn.Position).LengthHorizontalSquared,
                DistanceThresholds);

            // Find the first valid target
            Thing bestTarget = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets, pawn, IsValidTarget, _reachabilityCache);

            // Create a job for the target if found
            if (bestTarget != null)
            {
                return CreateJobForTarget(bestTarget);
            }

            return null;
        }

        #endregion
    }
}