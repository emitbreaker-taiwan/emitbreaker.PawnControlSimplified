using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks for wardens to chat with prisoners for recruitment or resistance reduction.
    /// Optimized to use the PawnControl framework for better performance.
    /// </summary>
    public class JobGiver_Warden_Chat_PawnControl : JobGiver_Warden_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "Chat";

        /// <summary>
        /// Cache update interval in ticks (180 ticks = 3 seconds)
        /// </summary>
        protected override int CacheUpdateInterval => 180;

        /// <summary>
        /// Distance thresholds for bucketing (10, 20, 30 tiles)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 100f, 400f, 900f };

        #endregion

        #region Core flow

        protected override float GetBasePriority(string workTag)
        {
            // Chatting with prisoners for recruitment or resistance reduction is important
            return 5.7f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Warden_Chat_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) => {
                    // Process cached targets to create job
                    if (p?.Map == null) return null;

                    int mapId = p.Map.uniqueID;
                    List<Thing> targets;
                    if (_prisonerCache.TryGetValue(mapId, out var prisonerList) && prisonerList != null)
                    {
                        targets = new List<Thing>(prisonerList.Cast<Thing>());
                    }
                    else
                    {
                        targets = new List<Thing>();
                    }

                    return ProcessCachedTargets(p, targets, forced);
                },
                debugJobDesc: "chat with prisoner");
        }

        #endregion

        #region Target processing

        /// <summary>
        /// Get all prisoners eligible for chat interaction
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null) yield break;

            // Get all prisoner pawns on the map
            foreach (Pawn prisoner in map.mapPawns.PrisonersOfColonySpawned)
            {
                if (FilterInteractablePrisoners(prisoner))
                {
                    yield return prisoner;
                }
            }
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
        /// Process the cached targets to create jobs
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn warden, List<Thing> targets, bool forced)
        {
            if (warden?.Map == null || targets.Count == 0)
                return null;

            int mapId = warden.Map.uniqueID;

            // Create distance buckets for optimized searching
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets<Pawn>(
                warden,
                targets.ConvertAll(t => t as Pawn),
                (prisoner) => (prisoner.Position - warden.Position).LengthHorizontalSquared,
                DistanceThresholds
            );

            // Find the first valid prisoner to chat with
            Pawn targetPrisoner = Utility_JobGiverManager.FindFirstValidTargetInBuckets<Pawn>(
                buckets,
                warden,
                (prisoner, p) => IsValidPrisonerTarget(prisoner, p),
                _prisonerReachabilityCache.TryGetValue(mapId, out var cache) ?
                    new Dictionary<int, Dictionary<Pawn, bool>> { { mapId, cache } } :
                    new Dictionary<int, Dictionary<Pawn, bool>>()
            );

            if (targetPrisoner == null)
                return null;

            // Create chat job
            return CreateChatJob(warden, targetPrisoner);
        }

        /// <summary>
        /// Validates if a warden can chat with a specific prisoner
        /// </summary>
        protected override bool IsValidPrisonerTarget(Pawn prisoner, Pawn warden)
        {
            if (prisoner?.guest == null || warden?.Faction == null)
                return false;

            if (warden.Faction != prisoner.HostFaction)
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
            if (!prisoner.Spawned || prisoner.IsForbidden(warden) ||
                !warden.CanReserve(prisoner, 1, -1, null, false))
                return false;

            return true;
        }

        /// <summary>
        /// Determines if the job giver should execute based on cache status
        /// </summary>
        protected override bool ShouldExecuteNow(int mapId)
        {
            // Check if there's any prisoners of the colony on the map
            Map map = Find.Maps.Find(m => m.uniqueID == mapId);
            if (map == null || map.mapPawns.PrisonersOfColonySpawnedCount == 0)
                return false;

            return !_lastWardenCacheUpdate.TryGetValue(mapId, out int lastUpdate) ||
                   Find.TickManager.TicksGame > lastUpdate + CacheUpdateInterval;
        }

        #endregion

        #region Job creation

        /// <summary>
        /// Creates the chat job for the warden
        /// </summary>
        private Job CreateChatJob(Pawn warden, Pawn prisoner)
        {
            Job job = JobMaker.MakeJob(JobDefOf.PrisonerAttemptRecruit, prisoner);

            PrisonerInteractionModeDef interactionMode = prisoner.guest.ExclusiveInteractionMode;
            string interactionType = interactionMode == PrisonerInteractionModeDefOf.AttemptRecruit ?
                "recruit" : "reduce resistance of";

            Utility_DebugManager.LogNormal($"{warden.LabelShort} created job to {interactionType} prisoner {prisoner.LabelShort}");

            return job;
        }

        #endregion

        #region Utility

        /// <summary>
        /// For debugging purposes
        /// </summary>
        public override string ToString()
        {
            return "JobGiver_Warden_Chat_PawnControl";
        }

        #endregion
    }
}