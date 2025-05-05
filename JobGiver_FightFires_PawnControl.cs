using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Enhanced JobGiver that assigns firefighting jobs to eligible pawns.
    /// Optimized for large colonies with 1000+ pawns and frequent fire events.
    /// </summary>
    public class JobGiver_FightFires_PawnControl : ThinkNode_JobGiver
    {
        // Cached fires to improve performance with large maps
        private static readonly Dictionary<int, List<Fire>> _fireCache = new Dictionary<int, List<Fire>>();
        private static readonly Dictionary<int, Dictionary<Fire, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Fire, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 60; // Update every second (more frequent for fires)

        // Local optimization parameters
        private const int MAX_CACHE_ENTRIES = 500;       // Cap cache size to avoid memory issues
        private const int NEARBY_PAWN_RADIUS = 15;       // Same as original WorkGiver
        private const float HANDLED_DISTANCE = 5f;       // Same as original WorkGiver

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 100f, 400f, 625f }; // 10, 20, 25 tiles

        public override float GetPriority(Pawn pawn)
        {
            // Higher priority than plant cutting - fires are emergencies
            return 9.0f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<Plant>(
                pawn,
                "Firefighter",
                (p, forced) => {
                    // Update fire cache
                    UpdateFireCache(p.Map);

                    // Find and create a job for cutting plants with VALID DESIGNATORS ONLY
                    return TryCreateFirefightingJob(p);
                },
                debugJobDesc: "firefighting assignment",
                skipEmergencyCheck: true);
        }

        /// <summary>
        /// Update the fire cache periodically for improved performance
        /// </summary>
        private void UpdateFireCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_fireCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_fireCache.ContainsKey(mapId))
                    _fireCache[mapId].Clear();
                else
                    _fireCache[mapId] = new List<Fire>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Fire, bool>();

                // Find all fires on map
                var fires = map.listerThings.ThingsOfDef(ThingDefOf.Fire);
                foreach (Thing thing in fires)
                {
                    if (thing is Fire fire)
                    {
                        // Only add home area fires unless on a pawn
                        if (fire.parent is Pawn || map.areaManager.Home[fire.Position])
                        {
                            _fireCache[mapId].Add(fire);
                        }
                    }
                }

                // Limit cache size for memory efficiency
                if (_fireCache[mapId].Count > MAX_CACHE_ENTRIES)
                {
                    _fireCache[mapId].RemoveRange(MAX_CACHE_ENTRIES, _fireCache[mapId].Count - MAX_CACHE_ENTRIES);
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Create a firefighting job using manager-driven bucket processing
        /// </summary>
        private Job TryCreateFirefightingJob(Pawn pawn)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_fireCache.ContainsKey(mapId) || _fireCache[mapId].Count == 0)
                return null;

            // Step 1: Pre-filter valid fires (this is special handling specific to fires)
            List<Fire> validFires = new List<Fire>();
            foreach (Fire fire in _fireCache[mapId])
            {
                // Skip invalid fires
                if (fire == null || fire.Destroyed || !fire.Spawned)
                    continue;

                // Special handling for fires on pawns
                if (fire.parent is Pawn parentPawn)
                {
                    // Skip if the burning pawn is the firefighter
                    if (parentPawn == pawn)
                        continue;

                    // Skip if the burning pawn is an enemy
                    if ((parentPawn.Faction == null || parentPawn.Faction != pawn.Faction) &&
                        (parentPawn.HostFaction == null || (parentPawn.HostFaction != pawn.Faction && parentPawn.HostFaction != pawn.HostFaction)))
                        continue;

                    // Skip distant burning pawns outside home area
                    if (!pawn.Map.areaManager.Home[fire.Position] &&
                        IntVec3Utility.ManhattanDistanceFlat(pawn.Position, parentPawn.Position) > NEARBY_PAWN_RADIUS)
                        continue;

                    // Skip unreachable pawn fires
                    if (!pawn.CanReach(parentPawn, PathEndMode.Touch, Danger.Deadly))
                        continue;
                }
                else
                {
                    // Skip non-pawn fires outside home area
                    if (!pawn.Map.areaManager.Home[fire.Position])
                        continue;
                }

                // Skip fires being handled by others
                if (FireIsBeingHandled(fire, pawn))
                    continue;

                validFires.Add(fire);
            }

            // Step 2: Use JobGiverManager for distance bucketing and target selection
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                validFires,
                (fire) => (fire.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Step 3: Find the best fire using the manager
            Fire targetFire = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (fire, p) => !fire.IsForbidden(p) && p.CanReserveAndReach(fire, PathEndMode.Touch, p.NormalMaxDanger()),
                _reachabilityCache
            );

            // Step 4: Create job if we found a target
            if (targetFire != null)
            {
                Job job = JobMaker.MakeJob(JobDefOf.BeatFire, targetFire);

                if (Prefs.DevMode)
                    Log.Message($"[PawnControl] {pawn.LabelShort} created job for fighting fire at {targetFire.Position}");

                return job;
            }

            return null;
        }

        /// <summary>
        /// Check if a fire is already being handled by another pawn nearby
        /// Logic adapted from the original WorkGiver_FightFires
        /// </summary>
        private bool FireIsBeingHandled(Fire fire, Pawn potentialHandler)
        {
            if (!fire.Spawned)
                return false;

            Pawn pawn = fire.Map.reservationManager.FirstRespectedReserver(fire, potentialHandler);
            return pawn != null && pawn.Position.InHorDistOf(fire.Position, HANDLED_DISTANCE);
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_fireCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_FightFires_PawnControl";
        }
    }
}