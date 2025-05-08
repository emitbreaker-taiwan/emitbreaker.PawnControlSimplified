using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to unload the inventory of carriers like pack animals or shuttles.
    /// Uses the Hauling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_UnloadCarriers_PawnControl : ThinkNode_JobGiver
    {
        // Cache for pawns whose inventory should be unloaded
        private static readonly Dictionary<int, List<Pawn>> _unloadablePawnsCache = new Dictionary<int, List<Pawn>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 120; // Update every 2 seconds

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 100f, 400f, 900f }; // 10, 20, 30 tiles

        public override float GetPriority(Pawn pawn)
        {
            // Unloading carriers is important to get resources
            return 5.8f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Skip if there are no pawns with UnloadEverything flag on the map
            if (ShouldSkip(pawn))
            {
                return null;
            }

            return Utility_JobGiverManager.StandardTryGiveJob<Plant>(
                pawn,
                "Hauling",
                (p, forced) => {
                    UpdateUnloadablePawnsCache(p.Map);

                    return TryCreateUnloadInventoryJob(p);
                },
                debugJobDesc: "unload carriers assignment");
        }

        /// <summary>
        /// Check if we should skip this job giver entirely (optimization)
        /// </summary>
        private bool ShouldSkip(Pawn pawn)
        {
            if (pawn?.Map == null)
                return true;

            IReadOnlyList<Pawn> allPawnsSpawned = pawn.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < allPawnsSpawned.Count; i++)
            {
                if (allPawnsSpawned[i].inventory.UnloadEverything)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Updates the cache of pawns whose inventory should be unloaded
        /// </summary>
        private void UpdateUnloadablePawnsCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_unloadablePawnsCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_unloadablePawnsCache.ContainsKey(mapId))
                    _unloadablePawnsCache[mapId].Clear();
                else
                    _unloadablePawnsCache[mapId] = new List<Pawn>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Pawn, bool>();

                // Use the map's existing list for better performance
                List<Pawn> unloadablePawns = map.mapPawns.SpawnedPawnsWhoShouldHaveInventoryUnloaded;
                if (unloadablePawns != null && unloadablePawns.Count > 0)
                {
                    _unloadablePawnsCache[mapId].AddRange(unloadablePawns);
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Create a job for unloading inventory using manager-driven bucket processing
        /// </summary>
        private Job TryCreateUnloadInventoryJob(Pawn pawn)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_unloadablePawnsCache.ContainsKey(mapId) || _unloadablePawnsCache[mapId].Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing and target selection
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                _unloadablePawnsCache[mapId],
                (carrier) => (carrier.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Find the best carrier to unload
            Pawn targetCarrier = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (carrier, p) => {
                    // IMPORTANT: Check faction interaction validity first
                    if (!Utility_JobGiverManager.IsValidFactionInteraction(carrier, p, requiresDesignator: false))
                        return false;

                    // Skip if no longer valid
                    if (carrier == null || carrier.Dead || !carrier.Spawned || carrier == p)
                        return false;

                    // Skip if no longer needs unloading
                    if (!carrier.inventory.UnloadEverything || carrier.inventory.innerContainer.Count == 0)
                        return false;

                    // Skip if forbidden or unreachable
                    if (carrier.IsForbidden(p) || 
                        !p.CanReserve(carrier) || 
                        !p.CanReach(carrier, PathEndMode.Touch, p.NormalMaxDanger()))
                        return false;

                    // Use the utility function if available, otherwise just return true
                    if (UnloadCarriersJobGiverUtility.HasJobOnThing (pawn, carrier, false))
                        return UnloadCarriersJobGiverUtility.HasJobOnThing(p, carrier, false);

                    return true;
                },
                _reachabilityCache
            );

            // Create job if target found
            if (targetCarrier != null)
            {
                Job job = JobMaker.MakeJob(JobDefOf.UnloadInventory, targetCarrier);
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to unload inventory of {targetCarrier.LabelCap}");
                return job;
            }

            return null;
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_unloadablePawnsCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_UnloadCarriers_PawnControl";
        }
    }
}