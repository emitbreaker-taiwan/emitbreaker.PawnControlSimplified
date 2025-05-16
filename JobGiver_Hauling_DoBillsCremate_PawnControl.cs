using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns cremation tasks to pawns at crematoriums with bills.
    /// Uses the Hauling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Hauling_DoBillsCremate_PawnControl : JobGiver_Common_DoBill_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging
        /// </summary>
        protected override string DebugName => "DoBillsCremate";

        /// <summary>
        /// Work tag for eligibility
        /// </summary>
        public override string WorkTag => "Hauling";

        /// <summary>
        /// Update cache every 5 seconds - cremation bills don't change often
        /// </summary>
        protected override int CacheUpdateInterval => base.CacheUpdateInterval;

        /// <summary>
        /// Distance thresholds for crematoriums (10, 20, 40 tiles)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 100f, 400f, 1600f };

        /// <summary>
        /// Cremation strictly requires player faction
        /// </summary>
        public override bool RequiresPlayerFaction => true;

        /// <summary>
        /// Fixed bill giver definitions for crematoriums
        /// </summary>
        protected override List<ThingDef> FixedBillGiverDefs
        {
            get
            {
                // Get the crematorium defs from the DoBillsCremate workgiver
                WorkGiverDef cremateWorkGiver = Utility_Common.WorkGiverDefNamed("DoBillsCremate");

                if (cremateWorkGiver != null && cremateWorkGiver.fixedBillGiverDefs != null && cremateWorkGiver.fixedBillGiverDefs.Count > 0)
                {
                    return cremateWorkGiver.fixedBillGiverDefs;
                }

                // Fallback to ElectricCrematorium if we can't find the workgiver
                ThingDef electricCrematorium = ThingDef.Named("ElectricCrematorium");
                if (electricCrematorium != null)
                {
                    return new List<ThingDef> { electricCrematorium };
                }

                Utility_DebugManager.LogWarning("Could not find DoBillsCremate WorkGiverDef, no crematoriums will be found");
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
        public JobGiver_Hauling_DoBillsCremate_PawnControl() : base()
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
            return 5.5f;
        }

        /// <summary>
        /// Override to strictly limit cremation to player pawns
        /// </summary>
        protected override bool IsValidFactionForCrafting(Pawn pawn)
        {
            // For cremation, strictly require player faction or player's slaves
            return pawn != null && (pawn.Faction == Faction.OfPlayer ||
                  (pawn.IsSlave && pawn.HostFaction == Faction.OfPlayer));
        }

        /// <summary>
        /// Cremation requires specific capabilities that non-humanlike pawns might not have
        /// </summary>
        protected override bool HasRequiredCapabilities(Pawn pawn)
        {
            // For cremation, require manipulation ability
            if (!pawn.RaceProps.Humanlike)
            {
                // Check if the pawn has manipulation capability
                return pawn.RaceProps.ToolUser;
            }
            return true;
        }

        /// <summary>
        /// Processes cached targets for the pawn.
        /// </summary>
        /// <param name="pawn">The pawn performing the job.</param>
        /// <param name="targets">The list of cached targets.</param>
        /// <param name="forced">Whether the job is forced.</param>
        /// <returns>A job for the pawn, or null if no job is found.</returns>
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

            // Find the best bill giver job using the parent class's method
            return FindBestBillGiverJob(pawn, validBillGivers, forced);
        }

        #endregion

        #region Target Selection

        /// <summary>
        /// Gets all crematoriums with bills on the map
        /// </summary>
        protected override IEnumerable<Thing> GetBillGivers(Map map)
        {
            if (map == null)
                yield break;

            // Get all crematoriums with active bills
            if (FixedBillGiverDefs != null)
            {
                foreach (ThingDef def in FixedBillGiverDefs)
                {
                    foreach (Building building in map.listerBuildings.AllBuildingsColonistOfDef(def))
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
        /// Job-specific cache update method that overrides the parent class method
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            // Use the GetBillGivers method to populate the cache
            return GetBillGivers(map);
        }

        /// <summary>
        /// Checks if a bill giver has valid work for a specific pawn
        /// Specialized for cremation tasks
        /// </summary>
        protected override bool HasWorkForPawn(Thing thing, Pawn pawn, bool forced = false)
        {
            // First perform the basic check from the parent class
            if (!base.HasWorkForPawn(thing, pawn, forced))
                return false;

            // Additional crematorium-specific validation could be added here
            // For example, checking if there are corpses available to be cremated

            return true;
        }

        /// <summary>
        /// Checks if a bill giver is valid for cremation work
        /// </summary>
        protected override bool IsValidBillGiver(Thing thing, Pawn pawn, bool forced = false)
        {
            // First perform the basic check from the parent class
            if (!base.IsValidBillGiver(thing, pawn, forced))
                return false;

            // Additional validation specific to cremation could be added here

            return true;
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

            // Any cremation-specific cache clearing could be added here
        }

        #endregion
    }
}