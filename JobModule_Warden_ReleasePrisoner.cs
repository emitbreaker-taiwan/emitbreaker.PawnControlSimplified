using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for releasing prisoners
    /// </summary>
    public class JobModule_Warden_ReleasePrisoner : JobModule_Warden
    {
        public override string UniqueID => "ReleasePrisoner";
        public override float Priority => 5.5f;
        public override string Category => "PrisonerManagement"; // Added category for consistency

        // Static caches for map-specific data persistence
        private static readonly Dictionary<int, List<Pawn>> _prisonersToReleaseCache = new Dictionary<int, List<Pawn>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static int _lastUpdateCacheTick = -999;

        // Cache for release cell locations - map specific
        private static readonly Dictionary<int, Dictionary<Pawn, IntVec3>> _releaseCellCache = new Dictionary<int, Dictionary<Pawn, IntVec3>>();

        public override bool ShouldProcessPrisoner(Pawn prisoner)
        {
            try
            {
                return prisoner != null &&
                       prisoner.guest != null &&
                       prisoner.guest.IsInteractionEnabled(PrisonerInteractionModeDefOf.Release) &&
                       !prisoner.Downed &&
                       !prisoner.guest.Released &&
                       !prisoner.InMentalState;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in ShouldProcessPrisoner for release job: {ex.Message}");
                return false;
            }
        }

        public override void UpdateCache(Map map, List<Pawn> targetCache)
        {
            base.UpdateCache(map, targetCache);

            if (map == null) return;
            int mapId = map.uniqueID;
            bool hasTargets = false;

            int currentTick = Find.TickManager.TicksGame;

            // Initialize caches if needed
            if (!_prisonersToReleaseCache.ContainsKey(mapId))
                _prisonersToReleaseCache[mapId] = new List<Pawn>();

            if (!_reachabilityCache.ContainsKey(mapId))
                _reachabilityCache[mapId] = new Dictionary<Pawn, bool>();

            if (!_releaseCellCache.ContainsKey(mapId))
                _releaseCellCache[mapId] = new Dictionary<Pawn, IntVec3>();

            // Only do a full update if needed
            if (currentTick > _lastUpdateCacheTick + CacheUpdateInterval ||
                !_prisonersToReleaseCache.ContainsKey(mapId))
            {
                try
                {
                    // Clear existing caches for this map
                    _prisonersToReleaseCache[mapId].Clear();
                    _reachabilityCache[mapId].Clear();
                    _releaseCellCache[mapId].Clear();

                    // Find all prisoners that need to be released
                    foreach (Pawn prisoner in map.mapPawns.PrisonersOfColonySpawned)
                    {
                        if (ShouldProcessPrisoner(prisoner))
                        {
                            _prisonersToReleaseCache[mapId].Add(prisoner);
                            targetCache.Add(prisoner);

                            // Pre-cache release cells - this is an expensive operation
                            if (RCellFinder.TryFindPrisonerReleaseCell(prisoner, null, out IntVec3 releaseCell))
                            {
                                _releaseCellCache[mapId][prisoner] = releaseCell;
                            }
                        }
                    }

                    if (_prisonersToReleaseCache[mapId].Count > 0)
                    {
                        Utility_DebugManager.LogNormal(
                            $"Found {_prisonersToReleaseCache[mapId].Count} prisoners marked for release on map {map.uniqueID}");
                    }

                    _lastUpdateCacheTick = currentTick;
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"Error updating prisoner release cache: {ex}");
                }
            }
            else
            {
                // Just add the cached prisoners to the target cache
                foreach (Pawn prisoner in _prisonersToReleaseCache[mapId])
                {
                    targetCache.Add(prisoner);
                }
            }

            SetHasTargets(map, hasTargets || (targetCache != null && targetCache.Count > 0));
        }

        public override bool ValidateTarget(Pawn prisoner, Pawn warden)
        {
            try
            {
                if (prisoner == null || warden == null || !prisoner.Spawned || !warden.Spawned)
                    return false;

                // Check faction interaction
                if (!Utility_JobGiverManager.IsValidFactionInteraction(prisoner, warden, requiresDesignator: false))
                    return false;

                int mapId = warden.Map.uniqueID;

                // Check if prisoner still needs releasing
                if (!ShouldProcessPrisoner(prisoner))
                    return false;

                // Check if warden can reach the prisoner
                if (!warden.CanReserveAndReach(prisoner, PathEndMode.Touch, warden.NormalMaxDanger()))
                    return false;

                // Check for valid release cell - first from cache
                IntVec3 releaseCell;
                if (_releaseCellCache.ContainsKey(mapId) && _releaseCellCache[mapId].TryGetValue(prisoner, out releaseCell))
                {
                    // Validate the cached cell is still valid
                    if (releaseCell.IsValid && releaseCell.Walkable(warden.Map))
                    {
                        return true;
                    }
                }

                // If no valid cached cell or cache miss, try to find one now
                bool result = RCellFinder.TryFindPrisonerReleaseCell(prisoner, warden, out releaseCell);

                // Cache the result if successful
                if (result && _releaseCellCache.ContainsKey(mapId))
                {
                    _releaseCellCache[mapId][prisoner] = releaseCell;
                }

                return result;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error validating prisoner release job: {ex.Message}");
                return false;
            }
        }

        protected override Job CreateWardenJob(Pawn warden, Pawn prisoner)
        {
            try
            {
                int mapId = warden.Map.uniqueID;

                // Try to get release cell from cache first
                IntVec3 releaseCell;
                if (_releaseCellCache.ContainsKey(mapId) &&
                    _releaseCellCache[mapId].TryGetValue(prisoner, out releaseCell) &&
                    releaseCell.IsValid &&
                    releaseCell.Walkable(warden.Map))
                {
                    // Use cached release cell
                    Job job = JobMaker.MakeJob(JobDefOf.ReleasePrisoner, prisoner, releaseCell);
                    job.count = 1;
                    Utility_DebugManager.LogNormal(
                        $"{warden.LabelShort} created job to release prisoner {prisoner.LabelShort} (using cached location)");
                    return job;
                }

                // Otherwise find a new release cell
                if (RCellFinder.TryFindPrisonerReleaseCell(prisoner, warden, out releaseCell))
                {
                    // Cache the new release cell for future use
                    if (_releaseCellCache.ContainsKey(mapId))
                    {
                        _releaseCellCache[mapId][prisoner] = releaseCell;
                    }

                    Job job = JobMaker.MakeJob(JobDefOf.ReleasePrisoner, prisoner, releaseCell);
                    job.count = 1;
                    Utility_DebugManager.LogNormal(
                        $"{warden.LabelShort} created job to release prisoner {prisoner.LabelShort}");
                    return job;
                }

                return null;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating prisoner release job: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public override void ResetStaticData()
        {
            base.ResetStaticData();

            // Clear all specialized caches
            Utility_CacheManager.ResetJobGiverCache(_prisonersToReleaseCache, _reachabilityCache);

            foreach (var cellMap in _releaseCellCache.Values)
            {
                cellMap.Clear();
            }
            _releaseCellCache.Clear();

            _lastUpdateCacheTick = -999;
        }
    }
}