using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for executing prisoners
    /// </summary>
    public class JobModule_Warden_Execution : JobModule_Warden
    {
        public override string UniqueID => "PrisonerExecution";
        public override float Priority => 7.0f;
        public override bool RequiresViolence => true;
        public override string Category => "PrisonerManagement"; // Added category for consistency

        // Static caches for map-specific data persistence
        private static readonly Dictionary<int, List<Pawn>> _prisonersToExecuteCache = new Dictionary<int, List<Pawn>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _ideologyAllowedCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static int _lastUpdateCacheTick = -999;

        // These precepts are cached to avoid repeated lookups
        private static PreceptDef _preceptColonistExecution;
        private static PreceptDef _preceptPrisonerExecution;

        public override bool ShouldProcessPrisoner(Pawn prisoner)
        {
            try
            {
                return prisoner != null &&
                       prisoner.guest != null &&
                       !prisoner.Dead &&
                       prisoner.Spawned &&
                       !prisoner.guest.IsInteractionDisabled(PrisonerInteractionModeDefOf.Execution) &&
                       prisoner.guest.ExclusiveInteractionMode == PrisonerInteractionModeDefOf.Execution;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in ShouldProcessPrisoner for execution job: {ex.Message}");
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
            if (!_prisonersToExecuteCache.ContainsKey(mapId))
                _prisonersToExecuteCache[mapId] = new List<Pawn>();

            if (!_reachabilityCache.ContainsKey(mapId))
                _reachabilityCache[mapId] = new Dictionary<Pawn, bool>();

            if (!_ideologyAllowedCache.ContainsKey(mapId))
                _ideologyAllowedCache[mapId] = new Dictionary<Pawn, bool>();

            // Cache ideology precepts
            if (ModsConfig.IdeologyActive)
            {
                if (_preceptColonistExecution == null)
                    _preceptColonistExecution = Utility_Common.PreceptDefNamed("Execution_Colonists_Forbidden");

                if (_preceptPrisonerExecution == null)
                    _preceptPrisonerExecution = Utility_Common.PreceptDefNamed("Execution_Prisoners_Forbidden");
            }

            // Only do a full update if needed
            if (currentTick > _lastUpdateCacheTick + CacheUpdateInterval ||
                !_prisonersToExecuteCache.ContainsKey(mapId))
            {
                try
                {
                    // Clear existing caches for this map
                    _prisonersToExecuteCache[mapId].Clear();
                    _reachabilityCache[mapId].Clear();
                    _ideologyAllowedCache[mapId].Clear();

                    // Find all prisoners that need to be executed
                    foreach (Pawn prisoner in map.mapPawns.PrisonersOfColonySpawned)
                    {
                        if (ShouldProcessPrisoner(prisoner))
                        {
                            _prisonersToExecuteCache[mapId].Add(prisoner);
                            targetCache.Add(prisoner);

                            // No additional caching needed for executions - they're fairly simple
                        }
                    }

                    if (_prisonersToExecuteCache[mapId].Count > 0)
                    {
                        Utility_DebugManager.LogNormal(
                            $"Found {_prisonersToExecuteCache[mapId].Count} prisoners marked for execution on map {map.uniqueID}");
                    }

                    _lastUpdateCacheTick = currentTick;
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"Error updating prisoner execution cache: {ex}");
                }
            }
            else
            {
                // Just add the cached prisoners to the target cache
                foreach (Pawn prisoner in _prisonersToExecuteCache[mapId])
                {
                    // Skip prisoners who are no longer eligible for execution
                    if (!ShouldProcessPrisoner(prisoner))
                        continue;

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

                // Check if prisoner is still marked for execution
                if (!ShouldProcessPrisoner(prisoner))
                    return false;

                // Check if the prisoner needs general care
                if (!ShouldTakeCareOfPrisoner(warden, prisoner))
                    return false;

                // Check ideology restrictions - first from cache
                if (_ideologyAllowedCache.ContainsKey(mapId) &&
                    _ideologyAllowedCache[mapId].TryGetValue(prisoner, out bool isAllowed))
                {
                    if (!isAllowed)
                        return false;
                }
                else
                {
                    // Check ideology allowance and cache result
                    isAllowed = IsExecutionIdeoAllowed(warden, prisoner);
                    if (_ideologyAllowedCache.ContainsKey(mapId))
                        _ideologyAllowedCache[mapId][prisoner] = isAllowed;

                    if (!isAllowed)
                        return false;
                }

                // Check if warden can reach the prisoner
                if (!warden.CanReserveAndReach(prisoner, PathEndMode.OnCell, Danger.Deadly))
                    return false;

                return true;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error validating prisoner execution job: {ex.Message}");
                return false;
            }
        }

        protected override Job CreateWardenJob(Pawn warden, Pawn prisoner)
        {
            try
            {
                Job job = JobMaker.MakeJob(JobDefOf.PrisonerExecution, prisoner);
                Utility_DebugManager.LogNormal(
                    $"{warden.LabelShort} created job for executing prisoner {prisoner.LabelShort}");
                return job;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating prisoner execution job: {ex.Message}");
                return null;
            }
        }

        private bool ShouldTakeCareOfPrisoner(Pawn warden, Thing prisoner)
        {
            try
            {
                if (!(prisoner is Pawn p))
                    return false;

                return p.IsPrisoner &&
                       p.Spawned &&
                       !p.Dead &&
                       !p.InAggroMentalState &&
                       p.guest?.PrisonerIsSecure == true &&
                       warden.CanReach(p, PathEndMode.OnCell, Danger.Some);
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in ShouldTakeCareOfPrisoner: {ex.Message}");
                return false;
            }
        }

        private bool IsExecutionIdeoAllowed(Pawn executioner, Pawn victim)
        {
            try
            {
                if (!executioner.RaceProps.Humanlike) return true;
                if (!ModsConfig.IdeologyActive) return true;

                var ideo = executioner.Ideo;
                if (ideo != null)
                {
                    if (victim.IsColonist &&
                        _preceptColonistExecution != null &&
                        ideo.HasPrecept(_preceptColonistExecution))
                        return false;

                    if (victim.IsPrisonerOfColony &&
                        _preceptPrisonerExecution != null &&
                        ideo.HasPrecept(_preceptPrisonerExecution))
                        return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error checking if execution is allowed by ideology: {ex.Message}");
                return true; // Default to allowing execution if there's an error
            }
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public override void ResetStaticData()
        {
            base.ResetStaticData();

            // Clear all specialized caches
            Utility_CacheManager.ResetJobGiverCache(_prisonersToExecuteCache, _reachabilityCache);

            foreach (var allowedMap in _ideologyAllowedCache.Values)
            {
                allowedMap.Clear();
            }
            _ideologyAllowedCache.Clear();

            _lastUpdateCacheTick = -999;

            // Reset cached precepts
            _preceptColonistExecution = null;
            _preceptPrisonerExecution = null;
        }
    }
}