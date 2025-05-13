using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to work at campfires with bills.
    /// Uses the Hauling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Hauling_DoBillsHaulCampfire_PawnControl : JobGiver_Common_DoBill_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging
        /// </summary>
        protected override string DebugName => "HaulCampfire";

        /// <summary>
        /// WorkTag used for eligibility checks
        /// </summary>
        public override string WorkTag => "Hauling";

        /// <summary>
        /// Update cache every 5 seconds - campfire bills don't change often
        /// </summary>
        protected override int CacheUpdateInterval => 300;

        /// <summary>
        /// Campfires are usually in more centralized areas, use smaller distance thresholds
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        /// <summary>
        /// Campfire cooking requires player faction
        /// </summary>
        protected override bool RequiresPlayerFaction => true;

        /// <summary>
        /// Fixed bill giver definitions for campfires
        /// </summary>
        protected override List<ThingDef> FixedBillGiverDefs
        {
            get
            {
                // Get the campfire defs from the DoBillsHaulCampfire workgiver
                WorkGiverDef campfireWorkGiver = Utility_Common.WorkGiverDefNamed("DoBillsHaulCampfire");

                if (campfireWorkGiver != null && campfireWorkGiver.fixedBillGiverDefs != null && campfireWorkGiver.fixedBillGiverDefs.Count > 0)
                {
                    return campfireWorkGiver.fixedBillGiverDefs;
                }

                // Fallback to Campfire if we can't find the workgiver
                ThingDef fallbackDef = ThingDef.Named("Campfire");
                if (fallbackDef != null)
                {
                    return new List<ThingDef> { fallbackDef };
                }

                Utility_DebugManager.LogWarning("Could not find DoBillsHaulCampfire WorkGiverDef, no campfires will be found");
                return new List<ThingDef>();
            }
        }

        /// <summary>
        /// Whether this construction job requires specific tag for non-humanlike pawns
        /// </summary>
        public override PawnEnumTags RequiredTag => PawnEnumTags.AllowWork_Hauling;

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_Hauling_DoBillsHaulCampfire_PawnControl() : base()
        {
            // Base constructor already initializes the cache
        }

        #endregion

        #region Core Flow

        /// <summary>
        /// Sets the base priority for this job giver
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            return 5.4f;
        }

        /// <summary>
        /// Override to limit campfire work to player pawns
        /// </summary>
        protected override bool IsValidFactionForCrafting(Pawn pawn)
        {
            // For campfire work, only player pawns or slaves should do it
            return pawn != null && (pawn.Faction == Faction.OfPlayer ||
                  (pawn.IsSlave && pawn.HostFaction == Faction.OfPlayer));
        }

        /// <summary>
        /// Campfire cooking requires specific capabilities that non-humanlike pawns might not have
        /// </summary>
        protected override bool HasRequiredCapabilities(Pawn pawn)
        {
            // For campfire cooking, require manipulation ability
            if (!pawn.RaceProps.Humanlike)
            {
                // Check for extension that might override capability checks
                NonHumanlikePawnControlExtension modExtension =
                    pawn.def.GetModExtension<NonHumanlikePawnControlExtension>();

                if (modExtension != null && modExtension.ignoreCapability)
                    return true;

                // Default to requiring tool user capability
                return pawn.RaceProps.ToolUser;
            }
            return true;
        }

        /// <summary>
        /// Processes cached targets for campfire-related jobs.
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            if (pawn == null || targets == null || targets.Count == 0)
                return null;

            // Filter bill givers to those that are valid for this pawn
            var validBillGivers = targets
                .Where(thing => IsValidBillGiver(thing, pawn, forced))
                .ToList();

            if (validBillGivers.Count == 0)
                return null;

            // Find the best bill giver job
            return FindBestBillGiverJob(pawn, validBillGivers, forced);
        }

        /// <summary>
        /// Checks if a bill giver has valid work for a specific pawn
        /// </summary>
        protected override bool HasWorkForPawn(Thing thing, Pawn pawn, bool forced = false)
        {
            if (!(thing is IBillGiver billGiver))
                return false;

            // Remove bills that can't be completed
            billGiver.BillStack.RemoveIncompletableBills();

            // Check if there's a valid job to do
            return StartOrResumeBillJob(pawn, billGiver, forced) != null;
        }

        /// <summary>
        /// Checks if a bill giver is valid for a specific pawn
        /// </summary>
        protected override bool IsValidBillGiver(Thing thing, Pawn pawn, bool forced = false)
        {
            // Basic validity checks from parent class
            if (!(thing is IBillGiver billGiver) ||
                !ThingIsUsableBillGiver(thing) ||
                !billGiver.BillStack.AnyShouldDoNow ||
                !billGiver.UsableForBillsAfterFueling() ||
                !pawn.CanReserve(thing, 1, -1, null, forced) ||
                thing.IsBurning() ||
                thing.IsForbidden(pawn))
            {
                return false;
            }

            // Check interaction cell
            if (thing.def.hasInteractionCell && !pawn.CanReserveSittableOrSpot_NewTemp(thing.InteractionCell, thing, forced))
            {
                return false;
            }

            // Campfire-specific checks for fuel
            CompRefuelable refuelable = thing.TryGetComp<CompRefuelable>();
            if (refuelable != null && !refuelable.HasFuel)
            {
                return RefuelWorkGiverUtility.CanRefuel(pawn, thing, forced);
            }

            return true;
        }

        #endregion

        #region Target Selection

        /// <summary>
        /// Gets the campfires with bills that need doing
        /// </summary>
        protected override IEnumerable<Thing> GetBillGivers(Map map)
        {
            if (map == null)
                yield break;

            // Get all campfires with active bills
            if (FixedBillGiverDefs != null)
            {
                foreach (ThingDef campfireDef in FixedBillGiverDefs)
                {
                    foreach (Building building in map.listerBuildings.AllBuildingsColonistOfDef(campfireDef))
                    {
                        // Cast to Building_WorkTable which implements IBillGiver
                        if (building is Building_WorkTable workTable && workTable.Spawned &&
                            workTable.BillStack != null && workTable.BillStack.AnyShouldDoNow)
                        {
                            yield return workTable;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Job-specific cache update method that implements the parent class method
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            // Use the GetBillGivers method to populate the cache
            return GetBillGivers(map);
        }

        #endregion

        #region Cache Management

        /// <summary>
        /// Reset the cache - overrides the Reset method from the parent class
        /// </summary>
        public override void Reset()
        {
            // Call the base class Reset method to use the centralized cache system
            base.Reset();
        }

        #endregion
    }
}