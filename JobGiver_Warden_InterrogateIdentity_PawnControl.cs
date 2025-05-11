using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks for wardens to interrogate prisoners about their identity
    /// </summary>
    public class JobGiver_Warden_InterrogateIdentity_PawnControl : JobGiver_Warden_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "InterrogateIdentity";

        /// <summary>
        /// Cache update interval in ticks (180 ticks = 3 seconds)
        /// </summary>
        protected override int CacheUpdateInterval => 180;

        /// <summary>
        /// Distance thresholds for bucketing (10, 15, 25 tiles)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 100f, 225f, 625f };

        #endregion

        #region Core flow

        protected override float GetBasePriority(string workTag)
        {
            // Interrogation has medium priority
            return 5.0f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Warden_InterrogateIdentity_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) => {
                    // Process cached targets to create job
                    if (p?.Map == null) return null;

                    int mapId = p.Map.uniqueID;
                    List<Pawn> targets;
                    if (_prisonerCache.TryGetValue(mapId, out var prisonerList) && prisonerList != null)
                    {
                        targets = new List<Pawn>(prisonerList);
                    }
                    else
                    {
                        targets = new List<Pawn>();
                    }

                    return ProcessCachedTargets(p, targets.Cast<Thing>().ToList(), forced);
                },
                debugJobDesc: "interrogate identity");
        }

        public override bool ShouldSkip(Pawn pawn)
        {
            // Check if anomaly mod is active
            if (!ModsConfig.AnomalyActive)
                return true;

            // Check if pawn is capable of talking
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Talking))
            {
                return true;
            }

            // Use base checks next
            return base.ShouldSkip(pawn);
        }

        #endregion

        #region Target processing

        /// <summary>
        /// Get all prisoners eligible for identity interrogation
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null) yield break;

            // Find all prisoners on the map
            foreach (Pawn prisoner in map.mapPawns.PrisonersOfColonySpawned)
            {
                if (prisoner != null && !prisoner.Destroyed && prisoner.Spawned)
                {
                    if (CanBeInterrogated(prisoner))
                    {
                        yield return prisoner;
                    }
                }
            }
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
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets<Thing>(
                warden,
                targets,
                (thing) => (thing.Position - warden.Position).LengthHorizontalSquared,
                DistanceThresholds
            );

            // Find the first valid prisoner to interrogate
            Thing targetThing = Utility_JobGiverManager.FindFirstValidTargetInBuckets<Thing>(
                buckets,
                warden,
                (thing, p) => IsValidPrisonerTarget(thing as Pawn, p),
                _prisonerReachabilityCache.TryGetValue(mapId, out var cache) ?
                    new Dictionary<int, Dictionary<Thing, bool>> { { mapId, cache.ToDictionary(kvp => (Thing)kvp.Key, kvp => kvp.Value) } } :
                    new Dictionary<int, Dictionary<Thing, bool>>()
            );

            if (targetThing == null || !(targetThing is Pawn prisoner))
                return null;

            // Create interrogation job
            return CreateInterrogationJob(warden, prisoner);
        }

        /// <summary>
        /// Check if prisoner can be interrogated about identity
        /// </summary>
        private bool CanBeInterrogated(Pawn prisoner)
        {
            // Check if has valid guest data
            if (prisoner?.guest == null)
                return false;

            // Skip prisoners in mental states
            if (prisoner.InMentalState)
                return false;

            // Check if prisoner is set for interrogation mode
            if (!prisoner.guest.IsInteractionEnabled(PrisonerInteractionModeDefOf.Interrogate))
                return false;

            // Check if prisoner is scheduled for interaction
            if (!prisoner.guest.ScheduledForInteraction)
                return false;

            // Prisoner must be either in a bed or not downed
            if (prisoner.Downed && !prisoner.InBed())
                return false;

            // Prisoner must be awake
            if (!prisoner.Awake())
                return false;

            return true;
        }

        /// <summary>
        /// Check if this prisoner is a valid target for interrogation
        /// </summary>
        protected override bool IsValidPrisonerTarget(Pawn prisoner, Pawn warden)
        {
            // Check if the prisoner exists
            if (prisoner == null || !prisoner.Spawned)
                return false;

            // Check if prisoner can be interrogated
            if (!CanBeInterrogated(prisoner))
                return false;

            // Check if warden can talk
            if (!warden.health.capacities.CapableOf(PawnCapacityDefOf.Talking))
                return false;

            // Check if warden can reserve the prisoner
            if (!warden.CanReserve(prisoner))
                return false;

            return true;
        }

        /// <summary>
        /// Determines if the job giver should execute based on cache status
        /// </summary>
        protected override bool ShouldExecuteNow(int mapId)
        {
            // Check if there are any prisoners on the map
            Map map = Find.Maps.Find(m => m.uniqueID == mapId);
            if (map == null || !map.mapPawns.PrisonersOfColonySpawned.Any(p => 
                p.guest?.IsInteractionEnabled(PrisonerInteractionModeDefOf.Interrogate) == true && 
                p.guest.ScheduledForInteraction))
                return false;

            return !_lastWardenCacheUpdate.TryGetValue(mapId, out int lastUpdate) ||
                   Find.TickManager.TicksGame > lastUpdate + CacheUpdateInterval;
        }

        #endregion

        #region Job creation

        /// <summary>
        /// Creates the interrogation job for the warden
        /// </summary>
        private Job CreateInterrogationJob(Pawn warden, Pawn prisoner)
        {
            Job job = JobMaker.MakeJob(JobDefOf.PrisonerInterrogateIdentity, prisoner);

            Utility_DebugManager.LogNormal($"{warden.LabelShort} created job to interrogate prisoner {prisoner.LabelShort} about identity");

            return job;
        }

        #endregion

        #region Utility

        /// <summary>
        /// For debugging purposes
        /// </summary>
        public override string ToString()
        {
            return "JobGiver_Warden_InterrogateIdentity_PawnControl";
        }

        #endregion
    }
}