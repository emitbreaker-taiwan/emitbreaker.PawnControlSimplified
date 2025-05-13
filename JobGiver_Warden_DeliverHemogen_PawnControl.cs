using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks for wardens to deliver hemogen packs to hemogenic prisoners.
    /// Optimized to use the PawnControl framework for better performance.
    /// </summary>
    public class JobGiver_Warden_DeliverHemogen_PawnControl : JobGiver_Warden_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "DeliverHemogen";

        /// <summary>
        /// Cache update interval in ticks (150 ticks = 2.5 seconds)
        /// </summary>
        protected override int CacheUpdateInterval => 150;

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
            // Delivering hemogen to prisoners is moderately important
            return 5.6f;
        }

        /// <summary>
        /// Creates a job for the warden to deliver hemogen to prisoners
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Warden_DeliverHemogen_PawnControl>(
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
                debugJobDesc: "deliver hemogen to prisoner");
        }

        /// <summary>
        /// Checks whether this job giver should be skipped for a pawn
        /// </summary>
        public override bool ShouldSkip(Pawn pawn)
        {
            // Skip if biotech is not active
            if (!ModsConfig.BiotechActive)
                return true;

            if (pawn == null || pawn.Dead || pawn.InMentalState)
                return true;

            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null)
                return true;

            // Skip if pawn is not a warden
            if (!Utility_TagManager.WorkEnabled(pawn.def, WorkTag))
                return true;

            return false;
        }

        #endregion

        #region Prisoner Selection

        /// <summary>
        /// Get prisoners that match the criteria for hemogen delivery
        /// </summary>
        protected override IEnumerable<Pawn> GetPrisonersMatchingCriteria(Map map)
        {
            if (map == null)
                yield break;

            // Skip if biotech is not active
            if (!ModsConfig.BiotechActive)
                yield break;

            // Get all prisoner pawns on the map
            foreach (Pawn prisoner in map.mapPawns.PrisonersOfColonySpawned)
            {
                if (FilterHemogenicPrisoners(prisoner))
                {
                    yield return prisoner;
                }
            }
        }

        /// <summary>
        /// Filter function to identify hemogenic prisoners who need hemogen packs
        /// </summary>
        private bool FilterHemogenicPrisoners(Pawn prisoner)
        {
            // Skip prisoners in mental states
            if (prisoner.InMentalState)
                return false;

            // Only include prisoners that should be fed
            if (!prisoner.guest?.CanBeBroughtFood == true)
                return false;

            // Make sure they're in a prison cell
            if (!prisoner.Position.IsInPrisonCell(prisoner.Map))
                return false;

            // Skip if the prisoner should be fed regular food
            if (WardenFeedUtility.ShouldBeFed(prisoner))
                return false;

            // Check if the prisoner is hemogenic
            if (!(prisoner.genes?.GetGene(GeneDefOf.Hemogenic) is Gene_Hemogen gene))
                return false;

            // Check if hemogen packs are allowed
            if (!gene.hemogenPacksAllowed)
                return false;

            // Check if they need to consume hemogen now
            if (!gene.ShouldConsumeHemogenNow())
                return false;

            // Check if there's already hemogen packs available
            if (HemogenPackAlreadyAvailableFor(prisoner))
                return false;

            return true;
        }

        /// <summary>
        /// Validates if a warden can deliver hemogen to a specific prisoner
        /// </summary>
        protected override bool IsValidPrisonerTarget(Pawn prisoner, Pawn warden)
        {
            // First check base class validation
            if (!base.IsValidPrisonerTarget(prisoner, warden))
                return false;

            if (prisoner?.guest == null)
                return false;

            // Skip if biotech is inactive
            if (!ModsConfig.BiotechActive)
                return false;

            // Only include prisoners that can be brought food
            if (!prisoner.guest.CanBeBroughtFood)
                return false;

            // Skip if the prisoner should be fed regular food
            if (WardenFeedUtility.ShouldBeFed(prisoner))
                return false;

            // Check if the prisoner is hemogenic and needs hemogen
            if (!(prisoner.genes?.GetGene(GeneDefOf.Hemogenic) is Gene_Hemogen gene))
                return false;

            // Check if hemogen packs are allowed and needed
            if (!gene.hemogenPacksAllowed || !gene.ShouldConsumeHemogenNow())
                return false;

            // Skip if already has hemogen packs
            if (HemogenPackAlreadyAvailableFor(prisoner))
                return false;

            // Find hemogen pack to deliver
            Thing hemogenPack = GenClosest.ClosestThingReachable(
                warden.Position,
                warden.Map,
                ThingRequest.ForDef(ThingDefOf.HemogenPack),
                PathEndMode.OnCell,
                TraverseParms.For(warden),
                9999f,
                (Thing pack) => !pack.IsForbidden(warden) && warden.CanReserve(pack) && pack.GetRoom() != prisoner.GetRoom());

            // Skip if no hemogen packs are available
            if (hemogenPack == null)
                return false;

            return true;
        }

        /// <summary>
        /// Create a job for the given prisoner
        /// </summary>
        protected override Job CreateJobForPrisoner(Pawn warden, Pawn prisoner, bool forced)
        {
            // Find hemogen pack to deliver
            Thing hemogenPack = GenClosest.ClosestThingReachable(
                warden.Position,
                warden.Map,
                ThingRequest.ForDef(ThingDefOf.HemogenPack),
                PathEndMode.OnCell,
                TraverseParms.For(warden),
                9999f,
                (Thing pack) => !pack.IsForbidden(warden) && warden.CanReserve(pack) && pack.GetRoom() != prisoner.GetRoom());

            if (hemogenPack == null)
                return null;

            return CreateDeliverHemogenJob(warden, prisoner, hemogenPack);
        }

        /// <summary>
        /// Determines if the job giver should execute based on cache status
        /// </summary>
        protected override bool ShouldExecuteNow(int mapId)
        {
            // Skip if biotech is inactive
            if (!ModsConfig.BiotechActive)
                return false;

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

        #region Job creation

        /// <summary>
        /// Creates the deliver hemogen job for the warden
        /// </summary>
        private Job CreateDeliverHemogenJob(Pawn warden, Pawn prisoner, Thing hemogenPack)
        {
            Job job = JobMaker.MakeJob(JobDefOf.DeliverFood, hemogenPack, prisoner);
            job.count = 1;
            job.targetC = RCellFinder.SpotToChewStandingNear(prisoner, hemogenPack);

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"{warden.LabelShort} created job to deliver hemogen pack to prisoner {prisoner.LabelShort}");
            }

            return job;
        }

        #endregion

        #region Utility methods

        /// <summary>
        /// Checks if a hemogen pack is already available for the prisoner
        /// </summary>
        private bool HemogenPackAlreadyAvailableFor(Pawn prisoner)
        {
            if (prisoner.carryTracker.CarriedCount(ThingDefOf.HemogenPack) > 0)
            {
                return true;
            }

            if (prisoner.inventory.Count(ThingDefOf.HemogenPack) > 0)
            {
                return true;
            }

            Room room = prisoner.GetRoom();
            if (room != null)
            {
                List<Region> regions = room.Regions;
                for (int i = 0; i < regions.Count; i++)
                {
                    if (regions[i].ListerThings.ThingsOfDef(ThingDefOf.HemogenPack).Count > 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        #endregion

        #region Debug

        /// <summary>
        /// For debugging purposes
        /// </summary>
        public override string ToString()
        {
            return "JobGiver_Warden_DeliverHemogen_PawnControl";
        }

        #endregion
    }
}