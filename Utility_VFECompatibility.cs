using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace emitbreaker.PawnControl
{
    public static class Utility_VFECompatibility
    {
        public static readonly bool VFEActive = ModsConfig.ActiveModsInLoadOrder
            .Any(m => m.PackageId?.ToLowerInvariant().Contains("vfe") == true);
    }
}
