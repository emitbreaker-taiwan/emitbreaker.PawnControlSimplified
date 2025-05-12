using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to strip downed pawns or corpses.
    /// Uses the Hauling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Hauling_Strip_PawnControl : JobGiver_Hauling_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        protected override bool RequiresMapZoneorArea => false;

        /// <summary>
        /// The designation type this job giver handles
        /// </summary>
        protected override DesignationDef TargetDesignation => DesignationDefOf.Strip;

        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.Strip;

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "Strip";

        /// <summary>
        /// Update cache every 1.5 seconds
        /// </summary>
        protected override int CacheUpdateInterval => 90;

        /// <summary>
        /// Distance thresholds for bucketing (10, 20, 30 tiles)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 100f, 400f, 900f };

        #endregion

        #region Cache Management

        // Strip-specific cache
        private static readonly Dictionary<int, List<Thing>> _strippableThingsCache = new Dictionary<int, List<Thing>>();
        private static readonly Dictionary<int, Dictionary<Thing, bool>> _stripReachabilityCache = new Dictionary<int, Dictionary<Thing, bool>>();
        private static int _lastStripCacheUpdateTick = -999;

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetStripCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_strippableThingsCache, _stripReachabilityCache);
            _lastStripCacheUpdateTick = -999;
            ResetHaulingCache(); // Call base class reset too
        }

        #endregion

        #region Core flow

        protected override float GetBasePriority(string workTag)
        {
            // Stripping is moderately important
            return 5.7f;
        }

        public override bool ShouldSkip(Pawn pawn)
        {
            base.ShouldSkip(pawn);

            if (Utility_Common.PawnIsNotPlayerFaction(pawn))
                return true;

            return false;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Quick early exit if there are no strip designations
            if (!pawn.Map.designationManager.AnySpawnedDesignationOfDef(TargetDesignation))
            {
                return null;
            }

            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Hauling_Strip_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) => {
                    if (p?.Map == null)
                        return null;

                    int mapId = p.Map.uniqueID;
                    int now = Find.TickManager.TicksGame;

                    if (!ShouldExecuteNow(mapId))
                        return null;

                    // Update the cache if needed
                    if (now > _lastStripCacheUpdateTick + CacheUpdateInterval ||
                        !_strippableThingsCache.ContainsKey(mapId))
                    {
                        UpdateStrippableThingsCache(p.Map);
                    }

                    // Process cached targets
                    return TryCreateStripJob(p);
                },
                debugJobDesc: DebugName);
        }

        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // We're using our custom strippable cache instead of the standard Thing targets
            if (map?.designationManager != null)
            {
                foreach (Designation designation in map.designationManager.SpawnedDesignationsOfDef(TargetDesignation))
                {
                    if (designation.target.HasThing)
                    {
                        Thing thing = designation.target.Thing;
                        if (thing != null && StrippableUtility.CanBeStrippedByColony(thing))
                        {
                            yield return thing;
                        }
                    }
                }
            }
        }

        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            if (targets == null || targets.Count == 0)
                return null;

            // Use the bucketing system to find the closest valid item
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                targets,
                (thing) => (thing.Position - pawn.Position).LengthHorizontalSquared,
                DistanceThresholds);

            // Find the best target to strip
            Thing targetThing = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (thing, p) => IsValidStripTarget(thing, p),
                _stripReachabilityCache);

            // Create job if target found
            if (targetThing != null)
            {
                Job job = JobMaker.MakeJob(WorkJobDef, targetThing);

                if (Prefs.DevMode)
                {
                    string targetDesc = targetThing is Corpse corpse ? $"corpse of {corpse.InnerPawn.LabelCap}" : targetThing.LabelCap;
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to strip {targetDesc}");
                }

                return job;
            }

            return null;
        }

        /// <summary>
        /// Determines if the job giver should execute based on cache status
        /// </summary>
        protected override bool ShouldExecuteNow(int mapId)
        {
            return Find.TickManager.TicksGame > _lastStripCacheUpdateTick + CacheUpdateInterval ||
                  !_strippableThingsCache.ContainsKey(mapId);
        }

        #endregion

        #region Target processing

        /// <summary>
        /// Updates the cache of things designated for stripping
        /// </summary>
        private void UpdateStrippableThingsCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            // Clear outdated cache
            if (_strippableThingsCache.ContainsKey(mapId))
                _strippableThingsCache[mapId].Clear();
            else
                _strippableThingsCache[mapId] = new List<Thing>();

            // Clear reachability cache too
            if (_stripReachabilityCache.ContainsKey(mapId))
                _stripReachabilityCache[mapId].Clear();
            else
                _stripReachabilityCache[mapId] = new Dictionary<Thing, bool>();

            // Find all things designated for stripping
            foreach (Designation designation in map.designationManager.SpawnedDesignationsOfDef(TargetDesignation))
            {
                if (designation.target.HasThing)
                {
                    Thing thing = designation.target.Thing;
                    if (thing != null && StrippableUtility.CanBeStrippedByColony(thing))
                    {
                        _strippableThingsCache[mapId].Add(thing);
                    }
                }
            }

            _lastStripCacheUpdateTick = currentTick;
        }

        /// <summary>
        /// Create a job for stripping a target using manager-driven bucket processing
        /// </summary>
        private Job TryCreateStripJob(Pawn pawn)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_strippableThingsCache.ContainsKey(mapId) || _strippableThingsCache[mapId].Count == 0)
                return null;

            return ProcessCachedTargets(pawn, _strippableThingsCache[mapId], false);
        }

        /// <summary>
        /// Determines if a thing is valid for stripping
        /// </summary>
        private bool IsValidStripTarget(Thing thing, Pawn pawn)
        {
            // IMPORTANT: Check faction interaction validity first
            if (!Utility_JobGiverManager.IsValidFactionInteraction(thing, pawn, requiresDesignator: true))
                return false;

            // Skip if no longer valid
            if (thing == null || thing.Destroyed || !thing.Spawned)
                return false;

            // Skip if no longer designated for stripping
            if (thing.Map.designationManager.DesignationOn(thing, TargetDesignation) == null)
                return false;

            // Skip if cannot be stripped by colony
            if (!StrippableUtility.CanBeStrippedByColony(thing))
                return false;

            // Skip if in mental state (for pawns)
            Pawn targetPawn = thing as Pawn;
            if (targetPawn != null && targetPawn.InAggroMentalState)
                return false;

            // Skip if forbidden or unreachable
            if (thing.IsForbidden(pawn) ||
                !pawn.CanReserve(thing) ||
                !pawn.CanReach(thing, PathEndMode.ClosestTouch, Danger.Deadly))
                return false;

            return true;
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return "JobGiver_Hauling_Strip_PawnControl";
        }

        #endregion
    }
}