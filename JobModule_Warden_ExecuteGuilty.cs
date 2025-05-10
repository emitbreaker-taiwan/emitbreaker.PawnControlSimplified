using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for executing guilty colonists
    /// </summary>
    public class JobModule_Warden_ExecuteGuilty : JobModule_Warden
    {
        public override string UniqueID => "GuiltyExecution";
        public override float Priority => 6.5f;
        public override bool RequiresViolence => true;
        public override bool HandlesColonists => true;
        public override string Category => "PrisonerManagement"; // Added category for consistency

        // Static caches for map-specific data persistence
        private static readonly Dictionary<int, List<Pawn>> _guiltyColonistsCache = new Dictionary<int, List<Pawn>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _historyEventCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static int _lastUpdateCacheTick = -999;

        public override bool ShouldProcessPrisoner(Pawn prisoner)
        {
            return false; // We handle free colonists instead
        }

        /// <summary>
        /// Check if a colonist is eligible for execution
        /// </summary>
        private bool ShouldProcessColonist(Pawn colonist)
        {
            try
            {
                return colonist != null &&
                       colonist.guilt != null &&
                       colonist.guilt.IsGuilty &&
                       colonist.guilt.awaitingExecution &&
                       !colonist.InAggroMentalState &&
                       !colonist.IsFormingCaravan();
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in ShouldProcessColonist for guilty execution: {ex.Message}");
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
            if (!_guiltyColonistsCache.ContainsKey(mapId))
                _guiltyColonistsCache[mapId] = new List<Pawn>();

            if (!_reachabilityCache.ContainsKey(mapId))
                _reachabilityCache[mapId] = new Dictionary<Pawn, bool>();

            if (!_historyEventCache.ContainsKey(mapId))
                _historyEventCache[mapId] = new Dictionary<Pawn, bool>();

            // Only do a full update if needed
            if (currentTick > _lastUpdateCacheTick + CacheUpdateInterval ||
                !_guiltyColonistsCache.ContainsKey(mapId))
            {
                try
                {
                    // Clear existing caches for this map
                    _guiltyColonistsCache[mapId].Clear();
                    _reachabilityCache[mapId].Clear();
                    _historyEventCache[mapId].Clear();

                    // Find all guilty colonists awaiting execution
                    foreach (var colonist in map.mapPawns.FreeColonistsSpawned)
                    {
                        if (ShouldProcessColonist(colonist))
                        {
                            _guiltyColonistsCache[mapId].Add(colonist);
                            targetCache.Add(colonist);
                        }
                    }

                    if (_guiltyColonistsCache[mapId].Count > 0)
                    {
                        Utility_DebugManager.LogNormal(
                            $"Found {_guiltyColonistsCache[mapId].Count} guilty colonists awaiting execution on map {map.uniqueID}");
                    }

                    _lastUpdateCacheTick = currentTick;
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"Error updating guilty colonist cache: {ex}");
                }
            }
            else
            {
                // Just add the cached colonists to the target cache
                foreach (Pawn colonist in _guiltyColonistsCache[mapId])
                {
                    // Skip colonists who are no longer guilty or awaiting execution
                    if (!ShouldProcessColonist(colonist))
                        continue;

                    targetCache.Add(colonist);
                }
            }

            SetHasTargets(map, hasTargets || (targetCache != null && targetCache.Count > 0));
        }

        public override bool ValidateTarget(Pawn target, Pawn executor)
        {
            try
            {
                if (target == null || executor == null || !target.Spawned || !executor.Spawned)
                    return false;

                // Check if colonist is still awaiting execution
                if (!ShouldProcessColonist(target))
                    return false;

                // Check faction interaction
                if (!Utility_JobGiverManager.IsValidFactionInteraction(target, executor, requiresDesignator: false))
                    return false;

                int mapId = executor.Map.uniqueID;

                // Check for reachability and reservation
                if (target.IsForbidden(executor) ||
                    !executor.CanReserveAndReach(target, PathEndMode.Touch, executor.NormalMaxDanger()))
                    return false;

                // Check if this execution is allowed by the history event system - first from cache
                if (_historyEventCache.ContainsKey(mapId) &&
                    _historyEventCache[mapId].TryGetValue(target, out bool isAllowed))
                {
                    return isAllowed;
                }

                // Otherwise check and cache the result
                bool result = new HistoryEvent(
                    HistoryEventDefOf.ExecutedColonist,
                    executor.Named(HistoryEventArgsNames.Doer)
                ).Notify_PawnAboutToDo_Job();

                if (_historyEventCache.ContainsKey(mapId))
                {
                    _historyEventCache[mapId][target] = result;
                }

                return result;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error validating guilty execution job: {ex.Message}");
                return false;
            }
        }

        protected override Job CreateWardenJob(Pawn executor, Pawn target)
        {
            try
            {
                Job job = JobMaker.MakeJob(JobDefOf.GuiltyColonistExecution, target);
                Utility_DebugManager.LogNormal(
                    $"{executor.LabelShort} created job to execute guilty colonist {target.LabelShort}");
                return job;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating guilty execution job: {ex.Message}");
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
            Utility_CacheManager.ResetJobGiverCache(_guiltyColonistsCache, _reachabilityCache);

            foreach (var historyMap in _historyEventCache.Values)
            {
                historyMap.Clear();
            }
            _historyEventCache.Clear();

            _lastUpdateCacheTick = -999;
        }
    }
}