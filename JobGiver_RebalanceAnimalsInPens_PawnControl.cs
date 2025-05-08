using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver specialized for handling rebalance animals in pens specifically
    /// </summary>
    public class JobGiver_RebalanceAnimalsInPens_PawnControl : JobGiver_TakeToPen_PawnControl
    {
        public JobGiver_RebalanceAnimalsInPens_PawnControl()
        {
            this.ropingPriority = RopingPriority.Balanced;  // Use additional priority
        }

        public override float GetPriority(Pawn pawn)
        {
            // Slightly lower priority than regular animal handling
            return 5.5f;
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static new void ResetCache()
        {
            JobGiver_TakeToPen_PawnControl.ResetCache();
        }

        public override string ToString()
        {
            return "JobGiver_RebalanceAnimalsInPens_PawnControl";
        }
    }
}
