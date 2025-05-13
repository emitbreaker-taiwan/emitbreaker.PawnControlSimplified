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
        /// Distance thresholds for bucketing (10, 20, 30 tiles squared)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 100f, 400f, 900f };

        #endregion

        #region Core flow

        /// <summary>
        /// Gets base priority for the job giver
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            // Chatting with prisoners for recruitment or resistance reduction is important
            return 5.7f;
        }

        /// <summary>
        /// Creates a job for the warden to interact with a prisoner
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Warden_Chat_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) => {
                    // Process cached targets to create job
                    if (p?.Map == null) return null;

                    // Get prisoners from centralized cache system
                    var prisoners = GetOrCreatePrisonerCache(p.Map);

                    // Convert to Thing list for processing
                    List<Thing> targets = new List<Thing>();
                    foreach (Pawn prisoner in prisoners)
                    {
                        if (prisoner != null && !prisoner.Dead && prisoner.Spawned)
                            targets.Add(prisoner);
                    }

                    return ProcessCachedTargets(p, targets, forced);
                },
                debugJobDesc: "chat with prisoner");
        }

        #endregion

        #region Prisoner Selection

        /// <summary>
        /// Get prisoners that match the criteria for chat interactions
        /// </summary>
        protected override IEnumerable<Pawn> GetPrisonersMatchingCriteria(Map map)
        {
            if (map == null)
                yield break;

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
        /// Validates if a warden can chat with a specific prisoner
        /// </summary>
        protected override bool IsValidPrisonerTarget(Pawn prisoner, Pawn warden)
        {
            // First check base class validation
            if (!base.IsValidPrisonerTarget(prisoner, warden))
                return false;

            if (prisoner?.guest == null)
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

            // Check if warden can reserve prisoner
            if (!warden.CanReserve(prisoner, 1, -1, null, false))
                return false;

            return true;
        }

        /// <summary>
        /// Create a job for the given prisoner
        /// </summary>
        protected override Job CreateJobForPrisoner(Pawn warden, Pawn prisoner, bool forced)
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

        #endregion

        #region Cache Management

        /// <summary>
        /// Determines if the job giver should execute based on cache status
        /// </summary>
        protected override bool ShouldExecuteNow(int mapId)
        {
            // Check if there's any prisoners of the colony on the map
            Map map = Find.Maps.Find(m => m.uniqueID == mapId);
            if (map == null || map.mapPawns.PrisonersOfColonySpawnedCount == 0)
                return false;

            // Check if cache needs updating
            string cacheKey = this.GetType().Name + PRISONERS_CACHE_SUFFIX;
            int lastUpdateTick = Utility_MapCacheManager.GetLastCacheUpdateTick(mapId, cacheKey);

            return Find.TickManager.TicksGame > lastUpdateTick + CacheUpdateInterval;
        }

        #endregion

        #region Debug

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