using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver specialized for handling rebalance animals in pens specifically.
    /// Uses the Handling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Handling_RebalanceAnimalsInPens_PawnControl : JobGiver_Handling_TakeToPen_PawnControl
    {
        #region Configuration

        public JobGiver_Handling_RebalanceAnimalsInPens_PawnControl()
        {
            // Use balanced priority for pen distribution
            this.ropingPriority = RopingPriority.Balanced;
        }

        protected override float GetBasePriority(string workTag)
        {
            // Slightly lower priority than regular animal handling
            return 5.5f;
        }

        #endregion

        #region Overrides

        /// <summary>
        /// Override TryGiveJob to implement specialized balanced animal distribution
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<Pawn>(
                pawn,
                WorkTag,
                (p, forced) => {
                    // Use base class implementation but directly return the Job object
                    return base.TryGiveJob(p);
                },
                debugJobDesc: "rebalance animals in pens");
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return "JobGiver_Handling_RebalanceAnimalsInPens_PawnControl";
        }

        #endregion
    }
}