using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for chatting with prisoners (recruitment/resistance reduction)
    /// </summary>
    public class JobModule_Warden_Chat : JobModule_Warden
    {
        public override string UniqueID => "ChatPrisoner";
        public override float Priority => 5.7f;
        public override string Category => "PrisonerCare"; // Added category for consistency

        // Static caches for map-specific data persistence
        private static readonly Dictionary<int, List<Pawn>> _interactionPrisonersCache = new Dictionary<int, List<Pawn>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _interactionCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static int _lastUpdateCacheTick = -999;

        public override bool ShouldProcessPrisoner(Pawn prisoner)
        {
            try
            {
                if (prisoner == null || prisoner.guest == null || prisoner.InMentalState)
                    return false;

                var mode = prisoner.guest.ExclusiveInteractionMode;
                if ((mode == PrisonerInteractionModeDefOf.AttemptRecruit ||
                     mode == PrisonerInteractionModeDefOf.ReduceResistance) &&
                    prisoner.guest.ScheduledForInteraction &&
                    !(mode == PrisonerInteractionModeDefOf.ReduceResistance && prisoner.guest.Resistance <= 0f))
                {
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in ShouldProcessPrisoner for chat interaction: {ex.Message}");
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
            if (!_interactionPrisonersCache.ContainsKey(mapId))
                _interactionPrisonersCache[mapId] = new List<Pawn>();

            if (!_reachabilityCache.ContainsKey(mapId))
                _reachabilityCache[mapId] = new Dictionary<Pawn, bool>();

            if (!_interactionCache.ContainsKey(mapId))
                _interactionCache[mapId] = new Dictionary<Pawn, bool>();

            // Only do a full update if needed
            if (currentTick > _lastUpdateCacheTick + CacheUpdateInterval ||
                !_interactionPrisonersCache.ContainsKey(mapId))
            {
                try
                {
                    // Clear existing caches for this map
                    _interactionPrisonersCache[mapId].Clear();
                    _reachabilityCache[mapId].Clear();
                    _interactionCache[mapId].Clear();

                    // Find all prisoners needing interaction
                    foreach (var prisoner in map.mapPawns.PrisonersOfColony)
                    {
                        if (ShouldProcessPrisoner(prisoner))
                        {
                            _interactionPrisonersCache[mapId].Add(prisoner);
                            targetCache.Add(prisoner);
                        }
                    }

                    if (_interactionPrisonersCache[mapId].Count > 0)
                    {
                        Utility_DebugManager.LogNormal(
                            $"Found {_interactionPrisonersCache[mapId].Count} prisoners requiring warden interaction on map {map.uniqueID}");
                    }

                    _lastUpdateCacheTick = currentTick;
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"Error updating prisoner interaction cache: {ex}");
                }
            }
            else
            {
                // Just add the cached prisoners to the target cache
                foreach (Pawn prisoner in _interactionPrisonersCache[mapId])
                {
                    // Skip prisoners who no longer need interaction
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

                // Check if prisoner still needs interaction
                if (!ShouldProcessPrisoner(prisoner))
                    return false;

                // Additional validation checks
                if (prisoner.Downed && !prisoner.InBed()) return false;
                if (!prisoner.Awake()) return false;

                // Check faction interaction
                if (!Utility_JobGiverManager.IsValidFactionInteraction(prisoner, warden, requiresDesignator: false))
                    return false;

                int mapId = warden.Map.uniqueID;

                // Check if warden can reach and reserve the prisoner
                if (prisoner.IsForbidden(warden) ||
                    !warden.CanReserveAndReach(prisoner, PathEndMode.Touch, warden.NormalMaxDanger()))
                    return false;

                return true;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error validating prisoner interaction job: {ex.Message}");
                return false;
            }
        }

        protected override Job CreateWardenJob(Pawn warden, Pawn prisoner)
        {
            try
            {
                // Choose the correct JobDef for the current interaction mode
                var mode = prisoner.guest.ExclusiveInteractionMode;
                JobDef def;
                if (mode == PrisonerInteractionModeDefOf.ReduceResistance)
                    def = DefDatabase<JobDef>.GetNamed("PrisonerAffectResistance");
                else
                    def = JobDefOf.PrisonerAttemptRecruit;

                var job = JobMaker.MakeJob(def, prisoner);

                string interactionType = mode == PrisonerInteractionModeDefOf.AttemptRecruit
                    ? "recruit"
                    : "reduce resistance of";

                Utility_DebugManager.LogNormal(
                    $"{warden.LabelShort} created job to {interactionType} prisoner {prisoner.LabelShort}");

                return job;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating prisoner interaction job: {ex.Message}");
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
            Utility_CacheManager.ResetJobGiverCache(_interactionPrisonersCache, _reachabilityCache);

            foreach (var interactionMap in _interactionCache.Values)
            {
                interactionMap.Clear();
            }
            _interactionCache.Clear();

            _lastUpdateCacheTick = -999;
        }
    }
}