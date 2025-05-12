using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks for wardens to release entities from holding platforms
    /// </summary>
    public class JobGiver_Warden_ReleaseEntity_PawnControl : JobGiver_Warden_PawnControl
    {
        #region Configuration

        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.ReleaseEntity;

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "ReleaseEntity";

        /// <summary>
        /// Cache update interval in ticks (180 ticks = 3 seconds)
        /// </summary>
        protected override int CacheUpdateInterval => 180;

        /// <summary>
        /// Distance thresholds for bucketing (10, 15, 25 tiles)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 100f, 225f, 625f };

        #endregion

        #region Core flow

        protected override float GetBasePriority(string workTag)
        {
            // Releasing entities has medium-high priority
            return 5.5f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Warden_ReleaseEntity_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) => {
                    // Process cached targets to create job
                    if (p?.Map == null) return null;

                    int mapId = p.Map.uniqueID;
                    List<Thing> targets;
                    
                    // Since we're dealing with buildings rather than prisoners directly,
                    // we'll use a cached list of holding platforms
                    if (_cachedTargets.TryGetValue(mapId, out var platformList) && platformList != null)
                    {
                        targets = new List<Thing>(platformList);
                    }
                    else
                    {
                        targets = new List<Thing>();
                    }

                    return ProcessCachedTargets(p, targets, forced);
                },
                debugJobDesc: "release entity");
        }

        public override bool ShouldSkip(Pawn pawn)
        {
            // Check if anomaly mod is active
            if (!ModsConfig.AnomalyActive)
                return true;

            // Use base checks next
            return base.ShouldSkip(pawn);
        }

        #endregion

        #region Target processing

        /// <summary>
        /// Get all holding platforms with entities ready to be released
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null) yield break;

            // Find all holding platforms on the map
            foreach (Building_HoldingPlatform platform in map.listerBuildings.AllBuildingsColonistOfClass<Building_HoldingPlatform>())
            {
                if (platform != null && !platform.Destroyed && platform.Spawned)
                {
                    Pawn entity = GetEntity(platform);
                    if (entity != null)
                    {
                        yield return platform;
                    }
                }
            }
        }

        /// <summary>
        /// Process the cached targets to create jobs
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn warden, List<Thing> targets, bool forced)
        {
            if (warden?.Map == null || targets.Count == 0)
                return null;

            int mapId = warden.Map.uniqueID;

            // Create distance buckets for optimized searching
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets<Thing>(
                warden,
                targets,
                (thing) => (thing.Position - warden.Position).LengthHorizontalSquared,
                DistanceThresholds
            );

            // Find the first valid platform to work with
            Thing targetThing = Utility_JobGiverManager.FindFirstValidTargetInBuckets<Thing>(
                buckets,
                warden,
                (thing, p) => IsValidPlatformTarget(thing, p, forced),
                new Dictionary<int, Dictionary<Thing, bool>>()
            );

            if (targetThing == null || !(targetThing is Building_HoldingPlatform platform))
                return null;

            // Get the entity to release
            Pawn entity = GetEntity(platform);
            if (entity == null)
                return null;

            // Create release job
            return CreateReleaseJob(warden, platform, entity);
        }

        /// <summary>
        /// Check if this platform is a valid target for releasing an entity
        /// </summary>
        private bool IsValidPlatformTarget(Thing platform, Pawn warden, bool forced)
        {
            // Check if the platform exists and is valid
            if (platform == null || !platform.Spawned || platform.Destroyed)
                return false;

            // Check if warden can access the platform
            if (!warden.CanReach(platform, PathEndMode.Touch, Danger.Deadly))
                return false;

            // Check if warden can reserve the platform
            if (!warden.CanReserve(platform, 1, -1, null, forced))
                return false;

            // Check if the platform has a releasable entity
            Pawn entity = GetEntity(platform);
            if (entity == null)
                return false;

            return true;
        }

        /// <summary>
        /// We don't need to use this method since we're dealing with platforms, not prisoners
        /// But we need to implement it since it's abstract in the base class
        /// </summary>
        protected override bool IsValidPrisonerTarget(Pawn prisoner, Pawn warden)
        {
            return false;
        }

        /// <summary>
        /// Determines if the job giver should execute based on cache status
        /// </summary>
        protected override bool ShouldExecuteNow(int mapId)
        {
            // Check if there are any holding platforms on the map
            Map map = Find.Maps.Find(m => m.uniqueID == mapId);
            if (map == null || !map.listerBuildings.AllBuildingsColonistOfClass<Building_HoldingPlatform>().Any())
                return false;

            return !_lastWardenCacheUpdate.TryGetValue(mapId, out int lastUpdate) ||
                   Find.TickManager.TicksGame > lastUpdate + CacheUpdateInterval;
        }

        #endregion

        #region Job creation

        /// <summary>
        /// Creates the release job for the warden
        /// </summary>
        private Job CreateReleaseJob(Pawn warden, Building_HoldingPlatform platform, Pawn entity)
        {
            Job job = JobMaker.MakeJob(WorkJobDef, platform, entity);
            job.count = 1;

            Utility_DebugManager.LogNormal($"{warden.LabelShort} created job to release entity {entity.LabelShort}");

            return job;
        }

        #endregion

        #region Utility

        /// <summary>
        /// Get the entity from a holding platform if it's ready to be released
        /// </summary>
        private Pawn GetEntity(Thing thing)
        {
            if (thing is Building_HoldingPlatform platform && platform.HeldPawn != null)
            {
                Pawn heldPawn = platform.HeldPawn;
                
                // Check if the entity can be released
                CompHoldingPlatformTarget compHoldingPlatformTarget = heldPawn.TryGetComp<CompHoldingPlatformTarget>();
                if (compHoldingPlatformTarget != null && compHoldingPlatformTarget.containmentMode == EntityContainmentMode.Release)
                {
                    return heldPawn;
                }
            }

            return null;
        }

        /// <summary>
        /// For debugging purposes
        /// </summary>
        public override string ToString()
        {
            return "JobGiver_Warden_ReleaseEntity_PawnControl";
        }

        #endregion
    }
}