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
            return Utility_JobGiverManagerOld.StandardTryGiveJob<Pawn>(
                pawn,
                "Warden",
                (p, forced) => {
                    // Update prisoner cache with standardized method
                    Utility_JobGiverManagerOld.UpdatePrisonerCache(
                        p.Map,
                        ref _lastCacheUpdateTick,
                        CACHE_UPDATE_INTERVAL,
                        _interactablePrisonerCache,
                        _reachabilityCache,
                        FilterInteractablePrisoners);

                    // Create job using standardized method
                    return Utility_JobGiverManagerOld.TryCreatePrisonerInteractionJob(
                        p,
                        _interactablePrisonerCache,
                        _reachabilityCache,
                        ValidateCanChat,
                        CreateChatJob,
                        DISTANCE_THRESHOLDS);
                },
                debugJobDesc: "chat with prisoner assignment");
        }

        /// <summary>
        /// Filter function to identify prisoners ready for chat interaction
        /// </summary>
        private bool FilterInteractablePrisoners(Pawn prisoner)
        {
            // Skip prisoners in mental states
            if (prisoner.InMentalState)
                return false;

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
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Validates if a warden can chat with a specific prisoner
        /// </summary>
        private bool ValidateCanChat(Pawn prisoner, Pawn warden)
        {
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
            if (!prisoner.Spawned || prisoner.IsForbidden(warden) ||
                !warden.CanReserve(prisoner, 1, -1, null, false))
                return false;

            return true;
        }

        /// <summary>
        /// Creates the chat job for the warden
        /// </summary>
        private Job CreateChatJob(Pawn warden, Pawn prisoner)
        {
            Job job = JobMaker.MakeJob(JobDefOf.PrisonerAttemptRecruit, prisoner);

            if (Prefs.DevMode)
            {
                PrisonerInteractionModeDef interactionMode = prisoner.guest.ExclusiveInteractionMode;
                string interactionType = interactionMode == PrisonerInteractionModeDefOf.AttemptRecruit ?
                    "recruit" : "reduce resistance of";

                Utility_DebugManager.LogNormal($"{warden.LabelShort} created job to {interactionType} prisoner {prisoner.LabelShort}");
            }

            return job;
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