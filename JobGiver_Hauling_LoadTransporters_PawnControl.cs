using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to load items into transporters like shuttles.
    /// Uses the Hauling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Hauling_LoadTransporters_PawnControl : JobGiver_Hauling_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        protected override bool RequiresMapZoneorArea => false;

        /// <summary>
        /// Human-readable name for debug logging
        /// </summary>
        protected override string DebugName => "LoadTransporters";

        /// <summary>
        /// Update cache every 2 seconds - transporters are time-sensitive
        /// </summary>
        protected override int CacheUpdateInterval => 120;

        /// <summary>
        /// Smaller distance thresholds for transporters - typically centered in a base
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 100f, 400f, 900f }; // 10, 20, 30 tiles

        #endregion

        #region Core flow

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Hauling_LoadTransporters_PawnControl>(
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

                    // Get transporters from shared cache
                    if (!_haulableCache.TryGetValue(mapId, out var haulables) || haulables.Count == 0)
                        return null;

                    // Filter only things with CompTransporter component
                    var transporterThings = haulables.Where(t => t.TryGetComp<CompTransporter>() != null).ToList();
                    if (transporterThings.Count == 0)
                        return null;

                    // Extract CompTransporters from their parent things
                    var transporters = transporterThings
                        .Select(t => t.TryGetComp<CompTransporter>())
                        .Where(comp => comp != null)
                        .ToList();

                    // Use the bucketing system to find the closest valid transporter
                    var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                        p,
                        transporterThings,
                        (thing) => (thing.Position - p.Position).LengthHorizontalSquared,
                        DistanceThresholds);

                    // Find the best transporter to load
                    Thing targetThing = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                        buckets,
                        p,
                        (thing, worker) => IsValidTransporterTarget(thing, worker),
                        _reachabilityCache);

                    // Create job if target found
                    if (targetThing != null)
                    {
                        CompTransporter transporter = targetThing.TryGetComp<CompTransporter>();
                        if (transporter != null)
                        {
                            Job job = LoadTransportersJobUtility.JobOnTransporter(p, transporter);

                            if (job != null)
                            {
                                Utility_DebugManager.LogNormal($"{p.LabelShort} created job to load transporter {targetThing.LabelCap}");
                            }

                            return job;
                        }
                    }

                    return null;
                },
                debugJobDesc: DebugName);
        }

        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            if (targets == null || targets.Count == 0)
                return null;

            // Filter only things with CompTransporter component
            var transporterThings = targets.Where(t => t.TryGetComp<CompTransporter>() != null).ToList();
            if (transporterThings.Count == 0)
                return null;

            // Extract CompTransporters from their parent things
            var transporters = transporterThings
                .Select(t => t.TryGetComp<CompTransporter>())
                .Where(comp => comp != null)
                .ToList();

            // Use the bucketing system to find the closest valid transporter
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                transporterThings,
                (thing) => (thing.Position - pawn.Position).LengthHorizontalSquared,
                DistanceThresholds);

            // Find the best transporter to load
            Thing targetThing = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (thing, worker) => IsValidTransporterTarget(thing, worker),
                _reachabilityCache);

            // Create job if target found
            if (targetThing != null)
            {
                CompTransporter transporter = targetThing.TryGetComp<CompTransporter>();
                if (transporter != null)
                {
                    Job job = LoadTransportersJobUtility.JobOnTransporter(pawn, transporter);

                    if (job != null)
                    {
                        Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to load transporter {targetThing.LabelCap}");
                    }

                    return job;
                }
            }

            return null;
        }

        #endregion

        #region Target selection

        /// <summary>
        /// Gets all things with a CompTransporter component on the map
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null)
                yield break;

            // Find all transporters on the map
            foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Transporter))
            {
                CompTransporter transporter = thing.TryGetComp<CompTransporter>();
                if (transporter != null && thing.Faction == Faction.OfPlayer && thing.Spawned)
                {
                    yield return thing;
                }
            }
        }

        /// <summary>
        /// Determines if a transporter is valid for loading
        /// </summary>
        private bool IsValidTransporterTarget(Thing thing, Pawn pawn)
        {
            // Skip invalid transporters
            if (thing == null || thing.Destroyed || !thing.Spawned)
                return false;

            // Check if it has a transporter component
            CompTransporter transporter = thing.TryGetComp<CompTransporter>();
            if (transporter == null)
                return false;

            // Skip if forbidden or unreachable
            if (thing.IsForbidden(pawn) || !pawn.CanReserveAndReach(thing, PathEndMode.Touch, pawn.NormalMaxDanger()))
                return false;

            // Use the utility method to check if there's loading work to do
            return LoadTransportersJobUtility.HasJobOnTransporter(pawn, transporter);
        }

        #endregion
    }
}