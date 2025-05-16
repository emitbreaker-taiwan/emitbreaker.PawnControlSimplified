using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to fill fermenting barrels with wort.
    /// Uses the Hauling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Hauling_FillFermentingBarrel_PawnControl : JobGiver_Hauling_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging
        /// </summary>
        protected override string DebugName => "FillFermentingBarrel";

        /// <summary>
        /// Update cache every 5 seconds - barrels don't change state quickly
        /// </summary>
        protected override int CacheUpdateInterval => base.CacheUpdateInterval;

        /// <summary>
        /// Distance thresholds for brewery areas
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        // Cache for translation strings
        private static string TemperatureTrans;
        private static string NoWortTrans;

        #endregion

        #region Core flow

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        public override bool RequiresMapZoneorArea => true;

        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.FillFermentingBarrel;

        protected override float GetBasePriority(string workTag)
        {
            // Filling barrels is moderately important
            return 5.3f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // IMPORTANT: Only player pawns and slaves owned by player should fill fermenting barrels
            if (pawn.Faction != Faction.OfPlayer &&
                !(pawn.IsSlave && pawn.HostFaction == Faction.OfPlayer))
                return null;

            if (ShouldSkip(pawn))
            {
                return null;
            }

            return base.TryGiveJob(pawn);
        }

        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // Filter only fermenting barrels
            var barrels = targets.OfType<Building_FermentingBarrel>().ToList();
            if (barrels.Count == 0)
                return null;

            // Use the bucketing system to find the closest valid barrel
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                barrels.Cast<Thing>().ToList(),
                (thing) => (thing.Position - pawn.Position).LengthHorizontalSquared,
                DistanceThresholds);

            // Find the best barrel to fill using the centralized-but-keyed-by-giver cache system
            Thing targetThing = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (thing, worker) => IsValidBarrelTarget(thing, worker),
                WorkTag);

            // Create job if target found
            if (targetThing != null && targetThing is Building_FermentingBarrel barrel)
            {
                // Find wort to fill the barrel with
                Thing wort = FindWort(pawn, barrel);

                if (wort != null)
                {
                    Job job = JobMaker.MakeJob(WorkJobDef, barrel, wort);
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to fill fermenting barrel with wort");
                    return job;
                }
            }

            // Return null if no valid job is found
            return null;
        }

        #endregion

        #region Target selection

        /// <summary>
        /// Gets all fermenting barrels on the map that need filling
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null)
                yield break;

            // Find all fermenting barrels on the map that need filling
            foreach (Thing thing in map.listerThings.ThingsOfDef(ThingDefOf.FermentingBarrel))
            {
                Building_FermentingBarrel barrel = thing as Building_FermentingBarrel;
                if (barrel != null && barrel.Spawned && !barrel.Fermented && barrel.SpaceLeftForWort > 0)
                {
                    // Check if temperature is suitable for fermentation
                    float ambientTemperature = barrel.AmbientTemperature;
                    CompProperties_TemperatureRuinable compProperties = barrel.def.GetCompProperties<CompProperties_TemperatureRuinable>();
                    if ((double)ambientTemperature >= (double)compProperties.minSafeTemperature + 2.0 &&
                        (double)ambientTemperature <= (double)compProperties.maxSafeTemperature - 2.0)
                    {
                        yield return barrel;
                    }
                }
            }
        }

        /// <summary>
        /// Determines if a barrel is valid for filling
        /// </summary>
        private bool IsValidBarrelTarget(Thing thing, Pawn pawn)
        {
            // Skip if not a fermenting barrel
            Building_FermentingBarrel barrel = thing as Building_FermentingBarrel;
            if (barrel == null)
                return false;

            // Skip if no longer valid
            if (barrel.Destroyed || !barrel.Spawned)
                return false;

            // Skip if fermented, full, burning, or forbidden
            if (barrel.Fermented || barrel.SpaceLeftForWort <= 0 ||
                barrel.IsBurning() || barrel.IsForbidden(pawn))
                return false;

            // Skip if being deconstructed
            if (pawn.Map.designationManager.DesignationOn(barrel, DesignationDefOf.Deconstruct) != null)
                return false;

            // Skip if temperature is not suitable
            float ambientTemperature = barrel.AmbientTemperature;
            CompProperties_TemperatureRuinable compProperties = barrel.def.GetCompProperties<CompProperties_TemperatureRuinable>();
            if ((double)ambientTemperature < (double)compProperties.minSafeTemperature + 2.0 ||
                (double)ambientTemperature > (double)compProperties.maxSafeTemperature - 2.0)
            {
                return false;
            }

            // Skip if unreachable or can't be reserved
            if (!pawn.CanReserve(barrel) || !pawn.CanReserveAndReach(barrel, PathEndMode.Touch, pawn.NormalMaxDanger()))
                return false;

            // Skip if no wort is available
            if (FindWort(pawn, barrel) == null)
                return false;

            return true;
        }

        /// <summary>
        /// Finds wort that can be used to fill the barrel
        /// </summary>
        private Thing FindWort(Pawn pawn, Building_FermentingBarrel barrel)
        {
            Predicate<Thing> validator = (x => !x.IsForbidden(pawn) && pawn.CanReserve(x));
            return GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForDef(ThingDefOf.Wort),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn),
                validator: validator
            );
        }

        #endregion

        #region Static data initialization

        public static void ResetStaticData()
        {
            TemperatureTrans = "BadTemperature".Translate();
            NoWortTrans = "NoWort".Translate();
        }

        #endregion
    }
}