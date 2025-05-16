using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns general hauling tasks for loose items that need to be moved to storage.
    /// Uses the Hauling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Hauling_HaulGeneral_PawnControl : JobGiver_Hauling_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Whether this job giver requires a designator to operate
        /// </summary>
        public override bool RequiresMapZoneorArea => false;

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "HaulGeneral";

        /// <summary>
        /// Standard distance thresholds for bucketing (15, 25, 50 tiles)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 225f, 625f, 2500f };

        #endregion

        #region Core flow

        /// <summary>
        /// Use sequential job processing for better performance
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            // Skip if pawn or map is null
            if (pawn?.Map == null)
            {
                return null;
            }

            if (ShouldSkip(pawn))
            {
                return null;
            }

            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Hauling_HaulGeneral_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) => CreateHaulingJob(p, forced),
                debugJobDesc: DebugName,
                skipEmergencyCheck: false,
                jobGiverType: GetType()
            );
        }

        public override bool ShouldSkip(Pawn pawn)
        {
            // Quick null checks first (fastest)
            if (pawn?.Map == null)
            {
                if (pawn.Faction == Faction.OfPlayer)
                    Utility_DebugManager.LogNormal("ShouldSkip is true for " + pawn.LabelShort + ": nullmap");
                return true;
            }

            // Check tick interval first (very fast check)
            if (!ShouldExecuteNow(pawn.Map.uniqueID))
            {
                if (pawn.Faction == Faction.OfPlayer)
                    Utility_DebugManager.LogNormal("ShouldSkip is true for " + pawn.LabelShort + ": ShouldExecuteNow is false");
                return true;
            }

            // Then check work type settings (still quite fast)
            if (!Utility_TagManager.WorkTypeSettingEnabled(pawn, WorkTag))
            {
                if (pawn.Faction == Faction.OfPlayer)
                    Utility_DebugManager.LogNormal("ShouldSkip is true for " + pawn.LabelShort + ": WorkTypeSettingEnabled is false");
                return true;
            }

            // Then faction-related checks
            if (((pawn.Faction != Faction.OfPlayer) ||
                (pawn.IsSlave && pawn.HostFaction != Faction.OfPlayer)) &&
                !Utility_WorkPermissionManager.CanNonPlayerPawnDoWorkType(this, WorkTag))
            {
                if (pawn.Faction == Faction.OfPlayer)
                    Utility_DebugManager.LogNormal("ShouldSkip is true for " + pawn.LabelShort + ": CanNonPlayerPawnDoWorkType is false");
                return true;
            }

            // More expensive checks last
            if (!Utility_JobGiverManager.IsEligibleForSpecializedJobGiver(pawn, WorkTag))
            {
                if (pawn.Faction == Faction.OfPlayer)
                    Utility_DebugManager.LogNormal("ShouldSkip is true for " + pawn.LabelShort + ": IsEligibleForSpecializedJobGiver is false");
                return true;
            }

            // More expensive checks for required capabilities (Optional)
            if (!HasRequiredCapabilities(pawn))
            {
                if (pawn.Faction == Faction.OfPlayer)
                    Utility_DebugManager.LogNormal("ShouldSkip is true for " + pawn.LabelShort + ": HasRequiredCapabilities is false");
                return true;
            }

            // Faction-specific expensive checks last
            if (!NonPlayerFactionCheck(pawn))
            {
                if (pawn.Faction == Faction.OfPlayer)
                    Utility_DebugManager.LogNormal("ShouldSkip is true for " + pawn.LabelShort + ": NonPlayerFactionCheck is false");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Creates a hauling job for the pawn
        /// </summary>
        private Job CreateHaulingJob(Pawn pawn, bool forced)
        {
            // Get all haulable items from cache
            List<Thing> targets = GetHaulableItems(pawn.Map);
            if (targets.Count == 0)
            {
                if (Prefs.DevMode)
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort}: No haulable items found");
                return null;
            }

            // Process targets using existing method
            return ProcessCachedTargets(pawn, targets, forced);
        }

        /// <summary>
        /// Get a filtered list of all haulable items on the map
        /// </summary>
        private List<Thing> GetHaulableItems(Map map)
        {
            List<Thing> targets = GetTargets(map).ToList();

            if (Prefs.DevMode && targets.Count > 0)
                Utility_DebugManager.LogNormal($"Found {targets.Count} haulable items on map {map.uniqueID}");

            return targets;
        }

        #endregion

        #region Target selection

        /// <summary>
        /// Gets all haulable items (excluding corpses) on the map
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map?.listerHaulables == null)
                yield break;

            // Get all potential haulables, similar to vanilla WorkGiver_Haul.PotentialWorkThingsGlobal
            List<Thing> haulablesRaw = map.listerHaulables.ThingsPotentiallyNeedingHauling();

            // Limit search to closest items first (for performance)
            int maxItems = 200;
            int count = 0;

            foreach (Thing thing in haulablesRaw)
            {
                if (thing != null && thing.Spawned && !(thing is Corpse) &&
                    !thing.Destroyed && !StoreUtility.IsInValidBestStorage(thing))
                {
                    yield return thing;

                    count++;
                    if (count >= maxItems)
                        break;
                }
            }
        }

        // Override to disable cache updates
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            // Return empty collection - we're not using caching
            yield break;
        }

        /// <summary>
        /// Process targets to find valid hauling job
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // Only allow hauling if pawn is the player or a slave of the player
            if (pawn.Faction != Faction.OfPlayer && !(pawn.IsSlave && pawn.HostFaction == Faction.OfPlayer))
                return null;

            if (targets == null || targets.Count == 0)
                return null;

            if (Prefs.DevMode)
                Utility_DebugManager.LogNormal($"{pawn.LabelShort}: Processing {targets.Count} potential haul items");

            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                targets,
                item => (item.Position - pawn.Position).LengthHorizontalSquared,
                DistanceThresholds);

            int validItemsCount = 0;
            int itemsChecked = 0;

            Thing targetThing = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (thing, worker) =>
                {
                    itemsChecked++;
                    bool isValid = IsValidHaulItem(thing, worker, forced);
                    if (isValid) validItemsCount++;
                    return isValid;
                },
                null);

            if (Prefs.DevMode)
                Utility_DebugManager.LogNormal($"{pawn.LabelShort}: Checked {itemsChecked} items, found {validItemsCount} valid targets");

            if (targetThing == null)
                return null;

            IntVec3 storeCell;
            IHaulDestination haulDestination;
            if (!StoreUtility.TryFindBestBetterStorageFor(
                    targetThing,
                    pawn,
                    pawn.Map,
                    StoreUtility.CurrentStoragePriorityOf(targetThing),
                    pawn.Faction,
                    out storeCell,
                    out haulDestination))
            {
                if (Prefs.DevMode)
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort}: Failed to find storage for {targetThing.Label}");
                return null;
            }

            Job job = HaulAIUtility.HaulToCellStorageJob(pawn, targetThing, storeCell, false);

            if (job != null)
            {
                if (Prefs.DevMode)
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort}: Created hauling job for {targetThing.Label} to {storeCell}");

                if (!job.TryMakePreToilReservations(pawn, forced))
                {
                    if (Prefs.DevMode)
                        Utility_DebugManager.LogNormal($"{pawn.LabelShort}: Couldn't reserve hauling job for {targetThing.Label}");
                    return null;
                }
            }

            return job;
        }

        /// <summary>
        /// Determines if an item is valid for general hauling
        /// </summary>
        private bool IsValidHaulItem(Thing thing, Pawn pawn, bool forced = false)
        {
            // First check base class hauling validation
            if (!CanHaulThing(thing, pawn))
                return false;

            // Similar to vanilla's WorkGiver_HaulGeneral - filter out corpses
            if (thing is Corpse)
                return false;

            // Similar to vanilla's WorkGiver_Haul.JobOnThing - check automatic hauling eligibility
            if (!HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, thing, forced))
                return false;

            // Check if we can find and reserve a storage location
            IntVec3 storeCell;
            IHaulDestination haulDestination;
            return StoreUtility.TryFindBestBetterStorageFor(thing, pawn, pawn.Map,
                StoreUtility.CurrentStoragePriorityOf(thing), pawn.Faction, out storeCell, out haulDestination) &&
                pawn.CanReserve(storeCell, 1, -1) &&
                pawn.CanReach(storeCell, PathEndMode.Touch, Danger.Deadly);
        }

        #endregion
    }
}