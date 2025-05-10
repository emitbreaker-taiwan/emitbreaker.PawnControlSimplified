using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for handling gathering items for caravans - implements the functionality of WorkGiver_HelpGatheringItemsForCaravan
    /// </summary>
    public class JobModule_Hauling_HelpGatheringItemsForCaravan : JobModule_Hauling
    {
        public override string UniqueID => "ItemsForCaravan";
        public override float Priority => 6.0f; // Same as original JobGiver
        public override string Category => "Logistics";
        public override int CacheUpdateInterval => 180; // Update every 3 seconds

        // Static caches for map-specific data persistence
        private static readonly Dictionary<int, List<Thing>> _caravanItemsCache = new Dictionary<int, List<Thing>>();
        private static readonly Dictionary<int, Dictionary<Thing, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Thing, bool>>();
        private static int _lastCacheUpdateTick = -999;

        // Cache for caravan lords - map specific
        private static readonly Dictionary<int, List<Lord>> _activeCaravanLords = new Dictionary<int, List<Lord>>();
        private static readonly Dictionary<int, Dictionary<Lord, List<Thing>>> _caravanItems = new Dictionary<int, Dictionary<Lord, List<Thing>>>();

        // Cached caravan forming spot def
        private static ThingDef _caravanFormingSpotDef;

        // These ThingRequestGroups are what this module cares about
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups =>
            new HashSet<ThingRequestGroup> { ThingRequestGroup.HaulableEver };

        public override void UpdateCache(Map map, List<Thing> targetCache)
        {
            base.UpdateCache(map, targetCache);

            if (map == null) return;
            int mapId = map.uniqueID;
            bool hasTargets = false;

            int currentTick = Find.TickManager.TicksGame;

            // Initialize caches if needed
            if (!_caravanItemsCache.ContainsKey(mapId))
                _caravanItemsCache[mapId] = new List<Thing>();

            if (!_reachabilityCache.ContainsKey(mapId))
                _reachabilityCache[mapId] = new Dictionary<Thing, bool>();

            if (!_activeCaravanLords.ContainsKey(mapId))
                _activeCaravanLords[mapId] = new List<Lord>();

            if (!_caravanItems.ContainsKey(mapId))
                _caravanItems[mapId] = new Dictionary<Lord, List<Thing>>();

            // Only do a full update if needed
            if (currentTick > _lastCacheUpdateTick + CacheUpdateInterval ||
                !_caravanItemsCache.ContainsKey(mapId))
            {
                // Clear existing caches for this map
                _caravanItemsCache[mapId].Clear();
                _reachabilityCache[mapId].Clear();
                _activeCaravanLords[mapId].Clear();

                foreach (var items in _caravanItems[mapId].Values)
                    items.Clear();
                _caravanItems[mapId].Clear();

                try
                {
                    // Find all lords that are forming caravans and gathering items
                    foreach (Lord lord in map.lordManager.lords)
                    {
                        if (lord.LordJob is LordJob_FormAndSendCaravan caravanLordJob && caravanLordJob.GatheringItemsNow)
                        {
                            _activeCaravanLords[mapId].Add(lord);

                            // Initialize item list for this lord
                            if (!_caravanItems[mapId].ContainsKey(lord))
                                _caravanItems[mapId][lord] = new List<Thing>();

                            // Find all valid transferable things for this lord
                            foreach (TransferableOneWay transferable in caravanLordJob.transferables)
                            {
                                if (transferable.CountToTransfer <= 0)
                                    continue;

                                // Calculate how many of this item have already been transferred
                                int leftToTransfer = GatherItemsForCaravanUtility.CountLeftToTransfer(null, transferable, lord);
                                if (leftToTransfer <= 0)
                                    continue;

                                // Add all valid things to transfer
                                foreach (Thing thing in transferable.things)
                                {
                                    if (thing.Spawned && !thing.IsForbidden(Faction.OfPlayer))
                                    {
                                        _caravanItems[mapId][lord].Add(thing);
                                        _caravanItemsCache[mapId].Add(thing);
                                        targetCache.Add(thing);
                                    }
                                }
                            }
                        }
                    }

                    // Find caravan forming spots and add them as well
                    if (_caravanFormingSpotDef == null)
                        _caravanFormingSpotDef = DefDatabase<ThingDef>.GetNamed("CaravanFormingSpot", false);

                    if (_caravanFormingSpotDef != null)
                    {
                        foreach (Thing spot in map.listerThings.ThingsOfDef(_caravanFormingSpotDef))
                        {
                            _caravanItemsCache[mapId].Add(spot);
                            targetCache.Add(spot);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogWarning($"Error updating caravan items cache: {ex}");
                }

                if (_activeCaravanLords[mapId].Count > 0)
                {
                    int totalItems = _caravanItems[mapId].Sum(kvp => kvp.Value.Count);
                    Utility_DebugManager.LogNormal($"Found {_activeCaravanLords[mapId].Count} active caravan lords with {totalItems} items to transfer");
                }

                _lastCacheUpdateTick = currentTick;
            }
            else
            {
                // Just add the cached items to the target cache
                foreach (var item in _caravanItemsCache[mapId])
                {
                    targetCache.Add(item);
                }
            }

            SetHasTargets(map, hasTargets || (targetCache != null && targetCache.Count > 0));
        }

        public override bool ShouldHaulItem(Thing item, Map map)
        {
            if (item == null || map == null || !item.Spawned) return false;

            int mapId = map.uniqueID;

            // Check if it's in our cache first
            if (_caravanItemsCache.ContainsKey(mapId) && _caravanItemsCache[mapId].Contains(item))
                return true;

            try
            {
                // Check if it's a caravan forming spot
                if (_caravanFormingSpotDef != null && item.def == _caravanFormingSpotDef)
                    return true;

                // Check if it's a transferable item for any active caravan
                if (_caravanItems.ContainsKey(mapId))
                {
                    foreach (var itemList in _caravanItems[mapId].Values)
                    {
                        if (itemList.Contains(item))
                            return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in ShouldHaulItem for caravan item: {ex}");
            }

            return false;
        }

        public override bool ValidateHaulingJob(Thing thing, Pawn hauler)
        {
            try
            {
                if (thing == null || hauler == null || !thing.Spawned || !hauler.Spawned)
                    return false;

                // Check faction interaction
                if (!Utility_JobGiverManager.IsValidFactionInteraction(thing, hauler, requiresDesignator: false))
                    return false;

                int mapId = hauler.Map.uniqueID;

                // IMPORTANT: Only player pawns and slaves owned by player should gather items for caravans
                if (hauler.Faction != Faction.OfPlayer &&
                    !(hauler.IsSlave && hauler.HostFaction == Faction.OfPlayer))
                    return false;

                // Don't assign caravan gathering jobs to pawns already forming caravans
                if (hauler.IsFormingCaravan())
                    return false;

                // Find thing to haul for a caravan
                Lord caravanLord = null;
                Thing thingToHaul = null;

                // Try each active caravan lord
                if (_activeCaravanLords.ContainsKey(mapId))
                {
                    foreach (Lord lord in _activeCaravanLords[mapId])
                    {
                        // Check if there's any reachable carrier in this lord
                        if (!AnyReachableCarrierOrColonist(hauler, lord))
                            continue;

                        // Find a thing to haul for this lord
                        Thing haulThing = FindThingToHaul(hauler, lord);
                        if (haulThing != null)
                        {
                            caravanLord = lord;
                            thingToHaul = haulThing;
                            break;
                        }
                    }
                }

                // No valid hauling found
                if (caravanLord == null || thingToHaul == null)
                    return false;

                return true;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error validating caravan hauling job: {ex}");
                return false;
            }
        }

        protected override Job CreateHaulingJob(Pawn hauler, Thing thing)
        {
            if (hauler == null) return null;

            try
            {
                // Find all lords that are forming caravans
                foreach (Lord lord in hauler.Map.lordManager.lords)
                {
                    if (lord.LordJob is LordJob_FormAndSendCaravan caravanLordJob &&
                        caravanLordJob.GatheringItemsNow &&
                        AnyReachableCarrierOrColonist(hauler, lord))
                    {
                        // Find a thing to haul
                        Thing thingToHaul = FindThingToHaul(hauler, lord);
                        if (thingToHaul != null)
                        {
                            // Create job to gather items
                            Job job = JobMaker.MakeJob(JobDefOf.PrepareCaravan_GatherItems, thingToHaul);
                            job.lord = lord;

                            Utility_DebugManager.LogNormal($"{hauler.LabelShort} created job to gather {thingToHaul.LabelCap} for caravan");
                            return job;
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating caravan item gathering job: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Find a thing to haul for the caravan
        /// </summary>
        private Thing FindThingToHaul(Pawn pawn, Lord lord)
        {
            try
            {
                // Use the vanilla utility method
                return GatherItemsForCaravanUtility.FindThingToHaul(pawn, lord);
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error using GatherItemsForCaravanUtility.FindThingToHaul: {ex.Message}");

                // Fallback implementation - use our cached items
                int mapId = pawn.Map.uniqueID;
                if (_caravanItems.ContainsKey(mapId) &&
                    _caravanItems[mapId].TryGetValue(lord, out List<Thing> transferableItems))
                {
                    foreach (Thing thing in transferableItems)
                    {
                        if (!thing.Spawned || thing.IsForbidden(pawn))
                            continue;

                        if (pawn.CanReserve(thing) &&
                            pawn.CanReach(thing, PathEndMode.Touch, Danger.Deadly))
                        {
                            return thing;
                        }
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Check if there's any reachable carrier or colonist in the lord
        /// </summary>
        private bool AnyReachableCarrierOrColonist(Pawn forPawn, Lord lord)
        {
            try
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
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error checking for reachable carriers: {ex}");
            }

            return false;
        }

        /// <summary>
        /// Check if a pawn can be used as a carrier - matches vanilla code exactly
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

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public override void ResetStaticData()
        {
            Utility_CacheManager.ResetJobGiverCache(_caravanItemsCache, _reachabilityCache);

            // Clear all specialized caches
            foreach (var lordItems in _caravanItems.Values)
            {
                foreach (var itemList in lordItems.Values)
                {
                    itemList.Clear();
                }
                lordItems.Clear();
            }
            _caravanItems.Clear();

            foreach (var lordList in _activeCaravanLords.Values)
            {
                lordList.Clear();
            }
            _activeCaravanLords.Clear();

            _lastCacheUpdateTick = -999;
            _caravanFormingSpotDef = null;
        }
    }
}