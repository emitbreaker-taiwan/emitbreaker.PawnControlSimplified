using RimWorld;
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
        protected override string WorkTag => "Hauling";

        /// <summary>
        /// Update cache every 5 seconds - campfire bills don't change often
        /// </summary>
        protected override int CacheUpdateInterval => 300;

        /// <summary>
        /// Campfires are usually in more centralized areas, use smaller distance thresholds
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

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

        #endregion

        #region Core flow

        public override float GetPriority(Pawn pawn)
        {
            // Working at campfire is moderately important
            return 5.4f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Use the common DoBill job logic from the base class
            return base.TryGiveJob(pawn);
        }

        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // Example implementation: Iterate through cached targets and try to create a job for the pawn
            foreach (var target in targets)
            {
                if (IsValidBillGiver(target, pawn, forced))
                {
                    var job = StartOrResumeBillJob(pawn, (IBillGiver)target, forced);
                    if (job != null)
                    {
                        return job;
                    }
                }
            }

            // Return null if no valid job could be created
            return null;
        }

        #endregion

        #region Target selection

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

        #endregion

        #region Utility

        public override string ToString()
        {
            return "JobGiver_Hauling_DoBillsHaulCampfire_PawnControl";
        }

        #endregion
    }
}