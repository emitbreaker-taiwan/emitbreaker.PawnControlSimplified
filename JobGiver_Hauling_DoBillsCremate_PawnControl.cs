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
        protected override string WorkTag => "Hauling";

        /// <summary>
        /// Update cache every 5 seconds - cremation bills don't change often
        /// </summary>
        protected override int CacheUpdateInterval => 300;

        /// <summary>
        /// Distance thresholds for crematoriums (10, 20, 40 tiles)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 100f, 400f, 1600f };

        /// <summary>
        /// Cremation strictly requires player faction
        /// </summary>
        protected override bool RequiresPlayerFaction => true;

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
        protected override PawnEnumTags RequiredTag => PawnEnumTags.AllowWork_Hauling;

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
        protected override bool IsValidFactionForBillWork(Pawn pawn)
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
            // Example implementation: Iterate through targets and find a valid job.
            foreach (var target in targets)
            {
                if (IsValidBillGiver(target, pawn, forced))
                {
                    return StartOrResumeBillJob(pawn, (IBillGiver)target, forced);
                }
            }

            // Return null if no valid job is found.
            return null;
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

        #endregion
    }
}