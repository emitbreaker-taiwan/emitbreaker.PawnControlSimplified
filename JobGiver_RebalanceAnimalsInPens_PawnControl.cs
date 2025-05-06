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

        public override string ToString()
        {
            return "JobGiver_RebalanceAnimalsInPens_PawnControl";
        }
    }
}
