using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks for wardens to suppress activity in anomalous entities
    /// </summary>
    public class JobGiver_Warden_SuppressActivity_PawnControl : JobGiver_Warden_PawnControl
    {
        #region Configuration

        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.ActivitySuppression;

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "SuppressActivity";

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
            // Activity suppression has high priority
            return 6.5f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Warden_SuppressActivity_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) => {
                    // Process cached targets to create job
                    if (p?.Map == null) return null;

                    int mapId = p.Map.uniqueID;
                    List<Thing> targets;
                    
                    // Get cached targets
                    if (_cachedTargets.TryGetValue(mapId, out var suppressableList) && suppressableList != null)
                    {
                        targets = new List<Thing>(suppressableList);
                    }
                    else
                    {
                        targets = new List<Thing>();
                    }

                    return ProcessCachedTargets(p, targets, forced);
                },
                debugJobDesc: "suppress activity");
        }

        public override bool ShouldSkip(Pawn pawn)
        {
            // Check if anomaly mod is active
            if (!ModsConfig.AnomalyActive)
                return true;

            // Check if pawn can actually suppress activity
            if (StatDefOf.ActivitySuppressionRate.Worker.IsDisabledFor(pawn) || 
                StatDefOf.ActivitySuppressionRate.Worker.GetValue(pawn) <= 0f)
            {
                return true;
            }

            // Use base checks next
            return base.ShouldSkip(pawn);
        }

        #endregion

        #region Target processing

        /// <summary>
        /// Get all suppressable things on the map
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null) yield break;

            // Find all suppressable things on the map
            foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Suppressable))
            {
                if (thing != null && !thing.Destroyed && thing.Spawned)
                {
                    Thing thingToSuppress = GetThingToSuppress(thing, false);
                    if (thingToSuppress != null)
                    {
                        yield return thing;
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

            // Sort targets by activity level for prioritization
            targets = targets.OrderByDescending(t => {
                Thing thingToSuppress = GetThingToSuppress(t, forced);
                if (thingToSuppress != null)
                {
                    return thingToSuppress.TryGetComp<CompActivity>()?.ActivityLevel ?? 0f;
                }
                return 0f;
            }).ToList();

            // Create distance buckets for optimized searching
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets<Thing>(
                warden,
                targets,
                (thing) => (thing.Position - warden.Position).LengthHorizontalSquared,
                DistanceThresholds
            );

            // Find the first valid thing to suppress
            Thing targetThing = Utility_JobGiverManager.FindFirstValidTargetInBuckets<Thing>(
                buckets,
                warden,
                (thing, p) => IsValidSuppressionTarget(thing, p, forced),
                new Dictionary<int, Dictionary<Thing, bool>>()
            );

            if (targetThing == null)
                return null;

            // Create suppression job
            return CreateSuppressionJob(warden, targetThing, forced);
        }

        /// <summary>
        /// Check if this thing is a valid target for activity suppression
        /// </summary>
        private bool IsValidSuppressionTarget(Thing thing, Pawn warden, bool forced)
        {
            // Get the actual thing to suppress
            Thing thingToSuppress = GetThingToSuppress(thing, forced);
            if (thingToSuppress == null)
                return false;

            // Get the activity component
            CompActivity compActivity = thingToSuppress.TryGetComp<CompActivity>();
            if (compActivity == null)
                return false;

            // Check if suppression is possible
            if (!ActivitySuppressionUtility.CanBeSuppressed(thingToSuppress, true, forced))
                return false;

            // Check activity level threshold unless forced
            if (!forced && compActivity.ActivityLevel < compActivity.suppressIfAbove)
                return false;

            // Check if warden can reserve the target
            if (!warden.CanReserve(thingToSuppress, 1, -1, null, forced))
                return false;

            // Special case for holding platforms
            if (thingToSuppress.ParentHolder is Building_HoldingPlatform platform && 
                !warden.CanReserve(platform, 1, -1, null, forced))
                return false;

            // Check if warden can stand near the thing
            if (!InteractionUtility.TryGetAdjacentInteractionCell(warden, thingToSuppress, forced, out var _))
                return false;

            return true;
        }

        /// <summary>
        /// We don't need to use this method since we're dealing with general things, not prisoners
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
            // Check if there are any suppressable things on the map
            Map map = Find.Maps.Find(m => m.uniqueID == mapId);
            if (map == null || !map.listerThings.ThingsInGroup(ThingRequestGroup.Suppressable).Any(
                t => GetThingToSuppress(t, false) != null))
                return false;

            return !_lastWardenCacheUpdate.TryGetValue(mapId, out int lastUpdate) ||
                   Find.TickManager.TicksGame > lastUpdate + CacheUpdateInterval;
        }

        #endregion

        #region Job creation

        /// <summary>
        /// Creates the activity suppression job
        /// </summary>
        private Job CreateSuppressionJob(Pawn warden, Thing target, bool forced)
        {
            Job job = JobMaker.MakeJob(WorkJobDef, target);
            job.playerForced = forced;

            Thing thingToSuppress = GetThingToSuppress(target, forced);
            if (thingToSuppress != null)
            {
                Utility_DebugManager.LogNormal($"{warden.LabelShort} created job to suppress activity in {thingToSuppress.LabelShort}");
            }

            return job;
        }

        #endregion

        #region Utility

        /// <summary>
        /// Get the actual thing to suppress from a potential target
        /// </summary>
        private Thing GetThingToSuppress(Thing thing, bool playerForced)
        {
            Thing thingToSuppress = thing;
            
            // Handle holding platforms
            if (thing is Building_HoldingPlatform platform)
            {
                thingToSuppress = platform.HeldPawn;
            }

            // Check if the thing can be suppressed
            if (thingToSuppress == null || !ActivitySuppressionUtility.CanBeSuppressed(thingToSuppress, true, playerForced))
            {
                return null;
            }

            return thingToSuppress;
        }

        /// <summary>
        /// For debugging purposes
        /// </summary>
        public override string ToString()
        {
            return "JobGiver_Warden_SuppressActivity_PawnControl";
        }

        #endregion
    }
}