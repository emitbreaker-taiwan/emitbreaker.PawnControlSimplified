using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks for wardens to enslave prisoners or reduce their will.
    /// Optimized to use the PawnControl framework for better performance.
    /// </summary>
    public class JobGiver_Warden_Enslave_PawnControl : JobGiver_Warden_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "Enslave";

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

        /// <summary>
        /// Enslave has high priority as it changes prisoner status
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            // Enslaving is a higher priority task than regular warden duties
            return 6.5f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Check for Ideology DLC first
            if (!ModLister.CheckIdeology("WorkGiver_Warden_Enslave"))
            {
                return null;
            }

            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Warden_Enslave_PawnControl>(
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
                debugJobDesc: "enslave prisoner or reduce will");
        }

        public override bool ShouldSkip(Pawn pawn)
        {
            // Use base checks first
            if (base.ShouldSkip(pawn))
                return true;

            // Ideology required
            if (!ModLister.CheckIdeology("WorkGiver_Warden_Enslave"))
                return true;
                
            // Check pawn capable of talking
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Talking))
                return true;

            return false;
        }

        #endregion

        #region Target processing

        /// <summary>
        /// Get all prisoners eligible for enslavement interaction
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null) yield break;

            // Get all prisoner pawns on the map
            foreach (Pawn prisoner in map.mapPawns.PrisonersOfColonySpawned)
            {
                if (FilterEnslavablePrisoners(prisoner))
                {
                    yield return prisoner;
                }
            }
        }

        /// <summary>
        /// Filter function to identify prisoners ready for enslavement interaction
        /// </summary>
        private bool FilterEnslavablePrisoners(Pawn prisoner)
        {
            // Skip prisoners in mental states
            if (prisoner.InMentalState)
                return false;

            // Check if either Enslave or ReduceWill interaction is enabled
            bool canEnslave = prisoner.guest.IsInteractionEnabled(PrisonerInteractionModeDefOf.Enslave);
            bool canReduceWill = prisoner.guest.IsInteractionEnabled(PrisonerInteractionModeDefOf.ReduceWill);

            if (!canEnslave && !canReduceWill)
                return false;

            // Check if scheduled for interaction
            if (!prisoner.guest.ScheduledForInteraction)
                return false;
                
            // Skip if attempting to reduce will but will is already 0
            if (canReduceWill && !canEnslave && prisoner.guest.will <= 0f)
                return false;

            // Check if downed but not in bed
            if (prisoner.Downed && !prisoner.InBed())
                return false;

            // Check if awake
            if (!prisoner.Awake())
                return false;

            return true;
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

            // Find the first valid prisoner to enslave or reduce will
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

            // Create enslave job
            return CreateEnslaveJob(warden, targetPrisoner);
        }

        /// <summary>
        /// Validates if a warden can enslave a specific prisoner
        /// </summary>
        protected override bool IsValidPrisonerTarget(Pawn prisoner, Pawn warden)
        {
            if (prisoner?.guest == null)
                return false;

            // Check if either Enslave or ReduceWill interaction is enabled
            bool canEnslave = prisoner.guest.IsInteractionEnabled(PrisonerInteractionModeDefOf.Enslave);
            bool canReduceWill = prisoner.guest.IsInteractionEnabled(PrisonerInteractionModeDefOf.ReduceWill);

            if (!canEnslave && !canReduceWill)
                return false;

            // Check if scheduled for interaction
            if (!prisoner.guest.ScheduledForInteraction)
                return false;
                
            // Check for reduce will when will is at 0
            if (canReduceWill && !canEnslave && prisoner.guest.will <= 0f)
                return false;

            // Check if prisoner is downed but not in bed
            if (prisoner.Downed && !prisoner.InBed())
                return false;

            // Check if prisoner is awake
            if (!prisoner.Awake())
                return false;

            // Check if warden can talk
            if (!warden.health.capacities.CapableOf(PawnCapacityDefOf.Talking))
                return false;

            // Check basic reachability
            if (!prisoner.Spawned || prisoner.IsForbidden(warden) ||
                !warden.CanReserve(prisoner, 1, -1, null, false))
                return false;

            // Check history event notification for enslaving
            if (!new HistoryEvent(HistoryEventDefOf.EnslavedPrisoner, warden.Named(HistoryEventArgsNames.Doer)).Notify_PawnAboutToDo_Job())
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
        /// Creates the enslave job for the warden
        /// </summary>
        private Job CreateEnslaveJob(Pawn warden, Pawn prisoner)
        {
            bool canEnslave = prisoner.guest.IsInteractionEnabled(PrisonerInteractionModeDefOf.Enslave);
            bool canReduceWill = prisoner.guest.IsInteractionEnabled(PrisonerInteractionModeDefOf.ReduceWill);
            
            Job job;
            
            if (canEnslave)
            {
                job = JobMaker.MakeJob(JobDefOf.PrisonerEnslave, prisoner);
                Utility_DebugManager.LogNormal($"{warden.LabelShort} created job to enslave prisoner {prisoner.LabelShort}");
            }
            else
            {
                job = JobMaker.MakeJob(JobDefOf.PrisonerReduceWill, prisoner);
                Utility_DebugManager.LogNormal($"{warden.LabelShort} created job to reduce will of prisoner {prisoner.LabelShort}");
            }

            return job;
        }

        #endregion

        #region Debug support

        /// <summary>
        /// For debugging purposes
        /// </summary>
        public override string ToString()
        {
            return "JobGiver_Warden_Enslave_PawnControl";
        }

        #endregion
    }
}