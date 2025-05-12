using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns prisoner release jobs to eligible wardens.
    /// Optimized to use the PawnControl framework for better performance.
    /// </summary>
    public class JobGiver_Warden_ReleasePrisoner_PawnControl : JobGiver_Warden_PawnControl
    {
        #region Configuration

        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.ReleasePrisoner;

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "ReleasePrisoner";

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
            // Standard priority for prisoner handling
            return 5.5f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Warden_ReleasePrisoner_PawnControl>(
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
                debugJobDesc: "release prisoner");
        }

        #endregion

        #region Target processing

        /// <summary>
        /// Get all prisoners eligible for release
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null) yield break;

            // Get all prisoner pawns on the map
            foreach (Pawn prisoner in map.mapPawns.PrisonersOfColonySpawned)
            {
                if (FilterReleasablePrisoners(prisoner))
                {
                    yield return prisoner;
                }
            }
        }

        /// <summary>
        /// Filter function to identify prisoners that can be released
        /// </summary>
        private bool FilterReleasablePrisoners(Pawn prisoner)
        {
            return prisoner?.guest != null &&
                   prisoner.guest.IsInteractionEnabled(PrisonerInteractionModeDefOf.Release) &&
                   !prisoner.Downed &&
                   !prisoner.guest.Released &&
                   !prisoner.InMentalState;
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

            // Find the first valid prisoner to release
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

            // Create release job
            return CreateReleasePrisonerJob(warden, targetPrisoner);
        }

        /// <summary>
        /// Validates if a warden can release a specific prisoner
        /// </summary>
        protected override bool IsValidPrisonerTarget(Pawn prisoner, Pawn warden)
        {
            // Skip if not actually a valid prisoner for release anymore
            if (prisoner?.guest == null ||
                !prisoner.IsPrisoner ||
                prisoner.Downed ||
                prisoner.InMentalState ||
                prisoner.guest.Released ||
                !prisoner.guest.IsInteractionEnabled(PrisonerInteractionModeDefOf.Release))
                return false;

            // Check if warden can reach prisoner
            if (!prisoner.Spawned || prisoner.IsForbidden(warden) ||
                !warden.CanReserveAndReach(prisoner, PathEndMode.Touch, warden.NormalMaxDanger()))
                return false;

            // Check for valid release cell
            IntVec3 releaseCell;
            return RCellFinder.TryFindPrisonerReleaseCell(prisoner, warden, out releaseCell);
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

            // Check for releasable prisoners to avoid unnecessary cache updates
            bool hasReleasablePrisoners = map.mapPawns.PrisonersOfColonySpawned.Any(p =>
                p?.guest != null && p.guest.IsInteractionEnabled(PrisonerInteractionModeDefOf.Release) &&
                !p.Downed && !p.guest.Released && !p.InMentalState);

            if (!hasReleasablePrisoners)
                return false;

            return !_lastWardenCacheUpdate.TryGetValue(mapId, out int lastUpdate) ||
                   Find.TickManager.TicksGame > lastUpdate + CacheUpdateInterval;
        }

        #endregion

        #region Job creation

        /// <summary>
        /// Creates a job to release a prisoner
        /// </summary>
        private Job CreateReleasePrisonerJob(Pawn warden, Pawn prisoner)
        {
            // Find release cell
            IntVec3 releaseCell;
            if (RCellFinder.TryFindPrisonerReleaseCell(prisoner, warden, out releaseCell))
            {
                Job job = JobMaker.MakeJob(WorkJobDef, prisoner, releaseCell);
                job.count = 1;

                Utility_DebugManager.LogNormal($"{warden.LabelShort} created job to release prisoner {prisoner.LabelShort}");

                return job;
            }

            return null;
        }

        #endregion

        #region Utility

        /// <summary>
        /// For debugging purposes
        /// </summary>
        public override string ToString()
        {
            return "JobGiver_Warden_ReleasePrisoner_PawnControl";
        }

        #endregion
    }
}