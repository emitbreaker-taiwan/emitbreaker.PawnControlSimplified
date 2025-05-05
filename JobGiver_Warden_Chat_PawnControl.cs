using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks for wardens to chat with prisoners for recruitment or resistance reduction.
    /// Optimized to use the PawnControl framework for better performance.
    /// </summary>
    public class JobGiver_Warden_Chat_PawnControl : ThinkNode_JobGiver
    {
        // Cached prisoners that are eligible for interaction
        private static readonly Dictionary<int, List<Pawn>> _interactablePrisonerCache = new Dictionary<int, List<Pawn>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 180; // Update every 3 seconds

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 100f, 400f, 900f }; // 10, 20, 30 tiles

        public override float GetPriority(Pawn pawn)
        {
            // Chatting with prisoners for recruitment or resistance reduction is important
            return 5.7f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<Plant>(
                pawn,
                "Warden",
                (p, forced) => {
                    // Update plant cache
                    UpdatePrisonerCache(p.Map);

                    // Find and create a job for cutting plants with VALID DESIGNATORS ONLY
                    return TryCreateChatWithPrisonerJob(p);
                },
                debugJobDesc: "chat with prisoner assignment");
        }

        /// <summary>
        /// Updates the cache of prisoners that are ready for interaction
        /// </summary>
        private void UpdatePrisonerCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_interactablePrisonerCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_interactablePrisonerCache.ContainsKey(mapId))
                    _interactablePrisonerCache[mapId].Clear();
                else
                    _interactablePrisonerCache[mapId] = new List<Pawn>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Pawn, bool>();

                // Find all prisoners who can be interacted with
                foreach (Pawn prisoner in map.mapPawns.PrisonersOfColonySpawned)
                {
                    if (prisoner == null || prisoner.Destroyed || !prisoner.Spawned)
                        continue;

                    // Skip prisoners in mental states
                    if (prisoner.InMentalState)
                        continue;

                    PrisonerInteractionModeDef interactionMode = prisoner.guest?.ExclusiveInteractionMode;

                    // Only include prisoners set for AttemptRecruit or ReduceResistance
                    if (interactionMode == PrisonerInteractionModeDefOf.AttemptRecruit ||
                        interactionMode == PrisonerInteractionModeDefOf.ReduceResistance)
                    {
                        // Only include prisoners scheduled for interaction
                        if (prisoner.guest.ScheduledForInteraction)
                        {
                            // Skip if resistance is already 0 and we're trying to reduce resistance
                            if (!(interactionMode == PrisonerInteractionModeDefOf.ReduceResistance &&
                                  prisoner.guest.Resistance <= 0f))
                            {
                                _interactablePrisonerCache[mapId].Add(prisoner);
                            }
                        }
                    }
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Create a job to chat with a prisoner using manager-driven bucket processing
        /// </summary>
        private Job TryCreateChatWithPrisonerJob(Pawn pawn)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_interactablePrisonerCache.ContainsKey(mapId) || _interactablePrisonerCache[mapId].Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing and target selection
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                _interactablePrisonerCache[mapId],
                (prisoner) => (prisoner.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Find the best prisoner to chat with
            Pawn targetPrisoner = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (prisoner, p) => {
                    // Skip if no longer a valid prisoner for chatting
                    if (prisoner?.guest == null || !prisoner.IsPrisoner || prisoner.InMentalState)
                        return false;

                    PrisonerInteractionModeDef interactionMode = prisoner.guest.ExclusiveInteractionMode;

                    // Check for valid interaction mode and scheduling
                    if ((interactionMode != PrisonerInteractionModeDefOf.AttemptRecruit &&
                         interactionMode != PrisonerInteractionModeDefOf.ReduceResistance) ||
                        !prisoner.guest.ScheduledForInteraction)
                        return false;

                    // Skip resistance reduction if already at 0
                    if (interactionMode == PrisonerInteractionModeDefOf.ReduceResistance &&
                        prisoner.guest.Resistance <= 0f)
                        return false;

                    // Check if prisoner is downed but not in bed
                    if (prisoner.Downed && !prisoner.InBed())
                        return false;

                    // Check if prisoner is awake
                    if (!prisoner.Awake())
                        return false;

                    // Check basic reachability
                    if (!prisoner.Spawned || prisoner.IsForbidden(p) ||
                        !p.CanReserve(prisoner, 1, -1, null, false))
                        return false;

                    return true;
                },
                _reachabilityCache
            );

            // Create job if target found
            if (targetPrisoner != null)
            {
                Job job = JobMaker.MakeJob(JobDefOf.PrisonerAttemptRecruit, targetPrisoner);

                if (Prefs.DevMode)
                {
                    PrisonerInteractionModeDef interactionMode = targetPrisoner.guest.ExclusiveInteractionMode;
                    string interactionType = interactionMode == PrisonerInteractionModeDefOf.AttemptRecruit ?
                        "recruit" : "reduce resistance of";

                    Log.Message($"[PawnControl] {pawn.LabelShort} created job to {interactionType} prisoner {targetPrisoner.LabelShort}");
                }

                return job;
            }

            return null;
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_interactablePrisonerCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_Warden_Chat_PawnControl";
        }
    }
}