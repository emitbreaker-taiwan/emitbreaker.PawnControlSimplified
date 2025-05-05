using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to haul corpses to graves, stockpiles, etc.
    /// Uses the Hauling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_HaulCorpses_PawnControl : ThinkNode_JobGiver
    {
        // Cache for corpses that need hauling
        private static readonly Dictionary<int, List<Corpse>> _haulableCorpsesCache = new Dictionary<int, List<Corpse>>();
        private static readonly Dictionary<int, Dictionary<Corpse, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Corpse, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 120; // Update every 2 seconds

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        public override float GetPriority(Pawn pawn)
        {
            // Hauling corpses is moderately important
            return 5.6f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<Plant>(
                pawn,
                "Hauling",
                (p, forced) => {
                    // Update plant cache
                    UpdateHaulableCorpsesCache(p.Map);

                    // Find and create a job for cutting plants with VALID DESIGNATORS ONLY
                    return TryCreateHaulCorpseJob(p);
                },
                debugJobDesc: "haul corpse assignment");
        }

        /// <summary>
        /// Updates the cache of corpses that need hauling
        /// </summary>
        private void UpdateHaulableCorpsesCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_haulableCorpsesCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_haulableCorpsesCache.ContainsKey(mapId))
                    _haulableCorpsesCache[mapId].Clear();
                else
                    _haulableCorpsesCache[mapId] = new List<Corpse>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Corpse, bool>();

                // Find all haulable corpses on the map
                foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse))
                {
                    if (!(thing is Corpse corpse) || !corpse.Spawned || HaulAIUtility.IsInHaulableInventory(corpse))
                        continue;

                    try
                    {
                        // CRITICAL FIX: Check forbiddenness safely without relying on a specific pawn
                        // This avoids null reference exceptions in ForbidUtility
                        bool isForbidden = false;
                        try
                        {
                            isForbidden = corpse.IsForbidden(Faction.OfPlayer);
                        }
                        catch
                        {
                            // If IsForbidden throws, assume it's not forbidden
                            isForbidden = false;

                            if (Prefs.DevMode)
                                Log.Warning($"[PawnControl] Error checking if corpse {corpse.InnerPawn?.LabelShort ?? "unknown"} is forbidden. Assuming not forbidden.");
                        }

                        if (isForbidden)
                            continue;

                        // CRITICAL FIX: Use a safe check for haulability that doesn't require a specific pawn
                        bool canBeHauled = false;
                        try
                        {
                            // Check if the corpse meets basic hauling requirements
                            canBeHauled = corpse.def.EverHaulable &&
                                          corpse.stackCount > 0 &&
                                          corpse.GetInnerIfMinified() == corpse;
                        }
                        catch (System.Exception ex)
                        {
                            if (Prefs.DevMode)
                                Log.Warning($"[PawnControl] Error checking if corpse {corpse.InnerPawn?.LabelShort ?? "unknown"} can be hauled: {ex.Message}");
                            canBeHauled = false;
                        }

                        if (!canBeHauled)
                            continue;

                        // For a real corpse, check if it's already being hauled by an animal
                        Pawn firstReserver = map.physicalInteractionReservationManager.FirstReserverOf(corpse);
                        if (firstReserver != null &&
                            firstReserver.RaceProps.Animal &&
                            firstReserver.Faction != Faction.OfPlayer)
                        {
                            continue;
                        }

                        // If we got this far, add the corpse to the haulable cache
                        _haulableCorpsesCache[mapId].Add(corpse);
                    }
                    catch (System.Exception ex)
                    {
                        // Catch any other exceptions to prevent crashes
                        if (Prefs.DevMode)
                            Log.Error($"[PawnControl] Error processing corpse for hauling: {ex}");
                    }
                }

                if (Prefs.DevMode && _haulableCorpsesCache[mapId].Count > 0)
                    Log.Message($"[PawnControl] Found {_haulableCorpsesCache[mapId].Count} haulable corpses on map {mapId}");

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Create a job for hauling a corpse using manager-driven bucket processing
        /// </summary>
        private Job TryCreateHaulCorpseJob(Pawn pawn)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_haulableCorpsesCache.ContainsKey(mapId) || _haulableCorpsesCache[mapId].Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                _haulableCorpsesCache[mapId],
                (corpse) => (corpse.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Find the best corpse to haul
            Corpse targetCorpse = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (corpse, p) => {
                    // Skip if no longer valid
                    if (corpse == null || corpse.Destroyed || !corpse.Spawned || HaulAIUtility.IsInHaulableInventory(corpse))
                        return false;

                    // Skip if no longer haulable
                    if (!HaulAIUtility.PawnCanAutomaticallyHaul(p, corpse, false) ||
                        corpse.IsForbidden(p) ||
                        !p.CanReserve(corpse))
                        return false;

                    // Skip if already being hauled by an animal
                    Pawn firstReserver = p.Map.physicalInteractionReservationManager.FirstReserverOf(corpse);
                    if (firstReserver != null && firstReserver.RaceProps.Animal && firstReserver.Faction != Faction.OfPlayer)
                        return false;

                    // Check if there's a storage destination for this corpse
                    IntVec3 storeCell;
                    if (!StoreUtility.TryFindBestBetterStoreCellFor(corpse, p, p.Map, StoragePriority.Unstored,
                                                                   p.Faction, out storeCell))
                        return false;

                    // Check if pawn can reach both the corpse and the storage location
                    if (!p.CanReserve(storeCell) ||
                        !p.CanReach(corpse, PathEndMode.ClosestTouch, p.NormalMaxDanger()) ||
                        !p.CanReach(storeCell, PathEndMode.OnCell, p.NormalMaxDanger()))
                        return false;

                    return true;
                },
                _reachabilityCache
            );

            // Create job if target found
            if (targetCorpse != null)
            {
                // Find storage cell for this corpse
                IntVec3 storeCell;
                bool foundCell = StoreUtility.TryFindBestBetterStoreCellFor(targetCorpse, pawn, pawn.Map,
                                                                           StoragePriority.Unstored,
                                                                           pawn.Faction, out storeCell);

                if (foundCell)
                {
                    Job job = null;

                    // Check if the store location is a grave or other container
                    Building_Grave grave = null;
                    IHaulDestination container = null;

                    SlotGroup slotGroup = storeCell.GetSlotGroup(pawn.Map);
                    if (slotGroup != null)
                    {
                        container = slotGroup.parent as IHaulDestination;
                    }

                    Thing edifice = storeCell.GetEdifice(pawn.Map);
                    if (edifice is Building_Grave buildingGrave)
                    {
                        grave = buildingGrave;
                        container = grave;
                    }

                    // Create appropriate job based on destination type
                    if (grave != null)
                    {
                        job = JobMaker.MakeJob(JobDefOf.HaulToContainer, targetCorpse, grave);
                        job.count = 1;

                        if (Prefs.DevMode)
                        {
                            Log.Message($"[PawnControl] {pawn.LabelShort} created job to bury {targetCorpse.InnerPawn.LabelShort}");
                        }
                    }
                    else if (container != null && container is Thing containerThing)
                    {
                        job = JobMaker.MakeJob(JobDefOf.HaulToContainer, targetCorpse, containerThing);
                        job.count = 1;

                        if (Prefs.DevMode)
                        {
                            Log.Message($"[PawnControl] {pawn.LabelShort} created job to haul {targetCorpse.InnerPawn.LabelShort} to container");
                        }
                    }
                    else
                    {
                        job = HaulAIUtility.HaulToCellStorageJob(pawn, targetCorpse, storeCell, false);

                        if (Prefs.DevMode && job != null)
                        {
                            Log.Message($"[PawnControl] {pawn.LabelShort} created job to haul {targetCorpse.InnerPawn.LabelShort} to stockpile");
                        }
                    }

                    return job;
                }
            }

            return null;
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_haulableCorpsesCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_HaulCorpses_PawnControl";
        }
    }
}