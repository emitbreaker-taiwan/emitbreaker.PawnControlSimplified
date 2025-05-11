using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to help gather items for forming caravans.
    /// Uses the Hauling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Hauling_HelpGatheringItemsForCaravan_PawnControl : JobGiver_Hauling_PawnControl
    {
        #region Configuration

        // Use higher priority since caravan forming is important
        protected override float GetBasePriority(string workTag)
        {
            return 6.0f;
        }

        // Use shorter cache update interval for more responsive caravan formation
        protected override int CacheUpdateInterval => 120;

        #endregion

        #region Caching

        // Cache for active caravan forming lords that are gathering items
        private static readonly Dictionary<int, List<Lord>> _caravanLordsCache = new Dictionary<int, List<Lord>>();
        private static readonly Dictionary<int, Dictionary<Thing, bool>> _thingToHaulCache = new Dictionary<int, Dictionary<Thing, bool>>();
        private static int _lastCacheUpdateTick = -999;

        #endregion

        #region Overrides

        /// <summary>
        /// Override TryGiveJob to implement caravan-specific logic
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            // IMPORTANT: Only player pawns and slaves owned by player should gather items for caravans
            if (pawn.Faction != Faction.OfPlayer &&
                !(pawn.IsSlave && pawn.HostFaction == Faction.OfPlayer))
                return null;

            // Don't assign caravan gathering jobs to pawns already forming caravans
            if (pawn.IsFormingCaravan())
            {
                return null;
            }

            return Utility_JobGiverManager.StandardTryGiveJob<Plant>(
                pawn,
                WorkTag,
                (p, forced) => {
                    // Update caravan lords cache
                    UpdateCaravanLordsCache(p.Map);

                    // Find and create a job for gathering items for caravans
                    return TryCreateGatherItemsJob(pawn);
                },
                debugJobDesc: "gather items for caravan assignment");
        }

        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // This won't be used since we're overriding TryGiveJob directly
            yield break;
        }

        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // This won't be used since we're overriding TryGiveJob directly
            return null;
        }

        #endregion

        #region Caravan-specific methods

        /// <summary>
        /// Updates the cache of lords that are currently forming caravans and gathering items
        /// </summary>
        private void UpdateCaravanLordsCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CacheUpdateInterval ||
                !_caravanLordsCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_caravanLordsCache.ContainsKey(mapId))
                    _caravanLordsCache[mapId].Clear();
                else
                    _caravanLordsCache[mapId] = new List<Lord>();

                // Clear thing to haul cache too
                if (_thingToHaulCache.ContainsKey(mapId))
                    _thingToHaulCache[mapId].Clear();
                else
                    _thingToHaulCache[mapId] = new Dictionary<Thing, bool>();

                // Find all lords that are forming caravans and gathering items
                List<Lord> activeCaravanLords = new List<Lord>();
                foreach (Lord lord in map.lordManager.lords)
                {
                    if (lord.LordJob is LordJob_FormAndSendCaravan caravanLordJob && caravanLordJob.GatheringItemsNow)
                    {
                        activeCaravanLords.Add(lord);
                    }
                }

                _caravanLordsCache[mapId] = activeCaravanLords;
                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Create a job for gathering items for a caravan
        /// </summary>
        private Job TryCreateGatherItemsJob(Pawn pawn)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_caravanLordsCache.ContainsKey(mapId) || _caravanLordsCache[mapId].Count == 0)
                return null;

            foreach (Lord lord in _caravanLordsCache[mapId])
            {
                // Find a thing to haul for this caravan
                Thing thingToHaul = FindThingToHaul(pawn, lord);
                if (thingToHaul != null)
                {
                    // Check if there's a reachable carrier or colonist
                    if (AnyReachableCarrierOrColonist(pawn, lord))
                    {
                        Job job = JobMaker.MakeJob(JobDefOf.PrepareCaravan_GatherItems, thingToHaul);
                        job.lord = lord;

                        Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to gather {thingToHaul.LabelCap} for caravan");
                        return job;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Find a thing to haul for the caravan
        /// </summary>
        private Thing FindThingToHaul(Pawn pawn, Lord lord)
        {
            // Use the utility method if available
            if (GatherItemsForCaravanUtility.FindThingToHaul(pawn, lord) != null)
            {
                try
                {
                    return GatherItemsForCaravanUtility.FindThingToHaul(pawn, lord);
                }
                catch (Exception ex)
                {
                    // Fallback to our own implementation if the utility method fails
                    Utility_DebugManager.LogWarning($"Error using GatherItemsForCaravanUtility.FindThingToHaul: {ex.Message}");
                }
            }

            // Fallback implementation
            LordJob_FormAndSendCaravan caravanLordJob = lord.LordJob as LordJob_FormAndSendCaravan;
            if (caravanLordJob == null) return null;

            // Look for things to haul among the transferables
            foreach (TransferableOneWay transferable in caravanLordJob.transferables)
            {
                if (transferable.CountToTransfer <= 0) continue;

                int leftToTransfer = transferable.CountToTransfer;
                foreach (Thing alreadyTransferred in lord.ownedPawns
                    .Where(p => p.inventory != null)
                    .SelectMany(p => p.inventory.innerContainer)
                    .Where(t => t.GetInnerIfMinified().def == transferable.ThingDef))
                {
                    leftToTransfer -= alreadyTransferred.stackCount;
                }

                if (leftToTransfer <= 0) continue;

                // Find a thing matching the transferable that can be hauled
                foreach (Thing thing in transferable.things)
                {
                    if (!thing.Spawned || thing.IsForbidden(pawn)) continue;

                    if (pawn.CanReserve(thing) && pawn.CanReach(thing, PathEndMode.Touch, Danger.Deadly))
                    {
                        return thing;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Check if there's any reachable carrier or colonist in the lord
        /// </summary>
        private bool AnyReachableCarrierOrColonist(Pawn forPawn, Lord lord)
        {
            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                Pawn ownedPawn = lord.ownedPawns[i];
                if (IsUsableCarrier(ownedPawn, forPawn, false) &&
                    !ownedPawn.IsForbidden(forPawn) &&
                    forPawn.CanReach(ownedPawn, PathEndMode.Touch, Danger.Deadly))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Check if a pawn can be used as a carrier
        /// </summary>
        private bool IsUsableCarrier(Pawn p, Pawn forPawn, bool allowColonists)
        {
            if (!p.IsFormingCaravan())
                return false;

            if (p == forPawn)
                return true;

            if (p.DestroyedOrNull() || !p.Spawned || p.inventory.UnloadEverything ||
                !forPawn.CanReach(p, PathEndMode.Touch, Danger.Deadly))
                return false;

            if (allowColonists && p.IsColonist)
                return true;

            return (p.RaceProps.packAnimal || p.HostFaction == Faction.OfPlayer) &&
                   !p.IsBurning() && !p.Downed && !MassUtility.IsOverEncumbered(p);
        }

        #endregion

        #region Cache management

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static new void ResetCache()
        {
            _caravanLordsCache.Clear();
            _thingToHaulCache.Clear();
            _lastCacheUpdateTick = -999;

            Utility_DebugManager.LogNormal("Reset JobGiver_HelpGatheringItemsForCaravan cache");
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return "JobGiver_Hauling_HelpGatheringItemsForCaravan_PawnControl";
        }

        #endregion
    }
}