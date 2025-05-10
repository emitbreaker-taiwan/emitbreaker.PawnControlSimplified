using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Defines the scope of cache reset operations
    /// </summary>
    public enum JobModuleCacheResetScope
    {
        All,               // Reset all caches completely
        WorkSettings,      // Reset due to work settings changes
        SpawnSetup,        // Reset due to pawn spawn/despawn
        MapChange          // Reset due to map change
    }
}
