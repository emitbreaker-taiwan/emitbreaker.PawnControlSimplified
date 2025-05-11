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

        #endregion

        #region Core flow

        protected override float GetBasePriority(string workTag)
        {
            // Chatting with prisoners for recruitment or resistance reduction is important
            return 5.5f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // IMPORTANT: Only player pawns and slaves owned by player should operate crematoriums
            if (pawn.Faction != Faction.OfPlayer &&
                !(pawn.IsSlave && pawn.HostFaction == Faction.OfPlayer))
                return null;

            // Use the base implementation which handles all bill processing
            return base.TryGiveJob(pawn);
        }

        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            foreach (var target in targets)
            {
                if (IsValidBillGiver(target, pawn, forced))
                {
                    Job job = TryGiveJob(pawn);
                    if (job != null)
                    {
                        return job;
                    }
                }
            }
            return null;
        }

        #endregion

        #region Target selection

        /// <summary>
        /// Checks if a bill giver is valid for cremation
        /// </summary>
        protected override bool IsValidBillGiver(Thing thing, Pawn pawn, bool forced = false)
        {
            // Make sure only player pawns and slaves owned by player operate crematoriums
            if (pawn.Faction != Faction.OfPlayer &&
                !(pawn.IsSlave && pawn.HostFaction == Faction.OfPlayer))
                return false;

            // Use the parent class's validation logic
            return base.IsValidBillGiver(thing, pawn, forced);
        }

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