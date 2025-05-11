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
    public class JobGiver_Firefighter_FightFires_PawnControl : JobGiver_Scan_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Use the Firefighter work tag
        /// </summary>
        protected override string WorkTag => "Firefighter";

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "FightFires";

        /// <summary>
        /// Update cache more frequently for fires (every second)
        /// </summary>
        protected override int CacheUpdateInterval => 60;

        /// <summary>
        /// Local optimization parameters
        /// </summary>
        private const int MAX_CACHE_ENTRIES = 500;       // Cap cache size to avoid memory issues
        private const int NEARBY_PAWN_RADIUS = 15;       // Same as original WorkGiver
        private const float HANDLED_DISTANCE = 5f;       // Same as original WorkGiver

        /// <summary>
        /// Distance thresholds for bucketing - smaller for fires (10, 20, 25 tiles)
        /// </summary>
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 100f, 400f, 625f };

        #endregion

        #region Caching

        /// <summary>
        /// Specialized cache for Fire objects rather than generic Things
        /// </summary>
        private static readonly Dictionary<int, List<Fire>> _fireCache = new Dictionary<int, List<Fire>>();

        /// <summary>
        /// Cache for reachability checks
        /// </summary>
        private static readonly Dictionary<int, Dictionary<Fire, bool>> _fireReachabilityCache = new Dictionary<int, Dictionary<Fire, bool>>();

        /// <summary>
        /// Track when the fire cache was last updated
        /// </summary>
        private static readonly Dictionary<int, int> _lastFireCacheUpdate = new Dictionary<int, int>();

        #endregion

        #region Core flow

        /// <summary>
        /// Execute more frequently for fires - check every tick
        /// </summary>
        protected override bool ShouldExecuteNow(int mapId)
        {
            // Fires are emergencies - check every tick
            return true;
        }

        /// <summary>
        /// Override the job creation to handle the specialized fire case
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            // Standard JobGiver pattern but with skipEmergencyCheck = true for fires
            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Firefighter_FightFires_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) =>
                {
                    if (p?.Map == null)
                        return null;

                    int mapId = p.Map.uniqueID;

                    // Update the fire cache
                    UpdateFireCache(p.Map);

                    // Early exit if no fires to handle
                    if (!_fireCache.TryGetValue(mapId, out var fires) || fires.Count == 0)
                        return null;

                    // Create firefighting job
                    return TryCreateFirefightingJob(p);
                },
                debugJobDesc: DebugName,
                skipEmergencyCheck: true, // Fires ARE the emergency, don't skip for them
                jobGiverType: GetType()
            );
        }

        /// <summary>
        /// Processes cached targets to create a job for the pawn
        /// Implementation of the required abstract method from JobGiver_Scan_PawnControl
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            if (pawn?.Map == null || targets == null || targets.Count == 0)
                return null;

            int mapId = pawn.Map.uniqueID;

            // Update the fire cache - ensure we're working with the latest data
            UpdateFireCache(pawn.Map);

            // Ensure we have fires to handle
            if (!_fireCache.TryGetValue(mapId, out var fires) || fires.Count == 0)
                return null;

            // Delegate to our specialized fire handling method
            return TryCreateFirefightingJob(pawn);
        }

        #endregion

        #region Target selection

        /// <summary>
        /// Get all fires on the map
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // Return all fires that should be extinguished
            if (map?.listerThings != null)
            {
                foreach (Thing thing in map.listerThings.ThingsOfDef(ThingDefOf.Fire))
                {
                    if (thing is Fire fire)
                    {
                        // Only return home area fires unless on a pawn
                        if (fire.parent is Pawn || map.areaManager.Home[fire.Position])
                        {
                            yield return fire;
                        }
                    }
                }
            }
        }

        #endregion

        #region Fire-specific methods

        /// <summary>
        /// Update the fire cache periodically for improved performance
        /// </summary>
        private void UpdateFireCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            // Get the current last update time, or default if not set
            if (!_lastFireCacheUpdate.TryGetValue(mapId, out int lastUpdateTick))
            {
                lastUpdateTick = -999;
            }

            if (currentTick > lastUpdateTick + CacheUpdateInterval ||
                !_fireCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_fireCache.ContainsKey(mapId))
                    _fireCache[mapId].Clear();
                else
                    _fireCache[mapId] = new List<Fire>();

                // Clear reachability cache too
                if (_fireReachabilityCache.ContainsKey(mapId))
                    _fireReachabilityCache[mapId].Clear();
                else
                    _fireReachabilityCache[mapId] = new Dictionary<Fire, bool>();

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

                // Store the updated tick
                _lastFireCacheUpdate[mapId] = currentTick;
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

            // No valid fires found
            if (validFires.Count == 0)
                return null;

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
                _fireReachabilityCache
            );

            // Step 4: Create job if we found a target
            if (targetFire != null)
            {
                Job job = JobMaker.MakeJob(JobDefOf.BeatFire, targetFire);
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job for fighting fire at {targetFire.Position}");
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

        #endregion

        #region Cache management

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetFightFiresCache()
        {
            _fireCache.Clear();
            _fireReachabilityCache.Clear();
            _lastFireCacheUpdate.Clear();
        }

        #endregion
    }
}