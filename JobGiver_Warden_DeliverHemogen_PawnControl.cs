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
        /// Distance thresholds for bucketing (10, 20, 30 tiles)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 100f, 400f, 900f };

        #endregion

        #region Core flow

        protected override float GetBasePriority(string workTag)
        {
            return 5.6f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Warden_DeliverHemogen_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) => {
                    // Process cached targets to create job
                    if (p?.Map == null) return null;

                    int mapId = p.Map.uniqueID;
                    List<Thing> targets;
                    if (_prisonerCache.TryGetValue(mapId, out var prisonerList) && prisonerList != null)
                    {
                        targets = prisonerList.Cast<Thing>().ToList();
                    }
                    else
                    {
                        targets = new List<Thing>();
                    }

                    return ProcessCachedTargets(p, targets, forced);
                },
                debugJobDesc: "deliver hemogen to prisoner");
        }

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

        #region Target processing

        /// <summary>
        /// Get all prisoners eligible for hemogen delivery
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null) yield break;

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

            // Find the first valid prisoner to feed hemogen to
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

            // Find hemogen pack to deliver
            Thing hemogenPack = GenClosest.ClosestThingReachable(
                warden.Position, 
                warden.Map, 
                ThingRequest.ForDef(ThingDefOf.HemogenPack), 
                PathEndMode.OnCell, 
                TraverseParms.For(warden), 
                9999f, 
                (Thing pack) => !pack.IsForbidden(warden) && warden.CanReserve(pack) && pack.GetRoom() != targetPrisoner.GetRoom());

            if (hemogenPack == null)
                return null;

            // Create job to deliver hemogen pack
            return CreateDeliverHemogenJob(warden, targetPrisoner, hemogenPack);
        }

        /// <summary>
        /// Validates if a warden can deliver hemogen to a specific prisoner
        /// </summary>
        protected override bool IsValidPrisonerTarget(Pawn prisoner, Pawn warden)
        {
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

            // Basic reachability checks
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
            // Skip if biotech is inactive
            if (!ModsConfig.BiotechActive)
                return false;

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
        /// Creates the deliver hemogen job for the warden
        /// </summary>
        private Job CreateDeliverHemogenJob(Pawn warden, Pawn prisoner, Thing hemogenPack)
        {
            Job job = JobMaker.MakeJob(JobDefOf.DeliverFood, hemogenPack, prisoner);
            job.count = 1;
            job.targetC = RCellFinder.SpotToChewStandingNear(prisoner, hemogenPack);
            
            Utility_DebugManager.LogNormal($"{warden.LabelShort} created job to deliver hemogen pack to prisoner {prisoner.LabelShort}");
            
            return job;
        }

        #endregion

        #region Utility

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