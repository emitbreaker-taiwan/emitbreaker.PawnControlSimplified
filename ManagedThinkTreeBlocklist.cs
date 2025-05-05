using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace emitbreaker.PawnControl
{
    public static class ManagedThinkTreeBlocklist
    {
        // These are node names that will be skipped if pawn lacks needs
        public static readonly HashSet<string> DangerousJobGiverNodeNames = new HashSet<string>
        {
            "JobGiver_SatisfyChemicalNeed",
            "JobGiver_SatifyChemicalDependency"
        };
    }
}
