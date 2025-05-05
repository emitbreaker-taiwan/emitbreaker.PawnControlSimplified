using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace emitbreaker.PawnControl
{
    public static class Utility_Compatibility
    {
        public static object TryGetModExtensionNoDependency(ThingDef def, string defModExtensionTypeOf)
        {
            if (def == null || def.modExtensions == null)
            {
                return null;
            }

            foreach (var modExtension in def.modExtensions)
            {
                var modExtensionType = modExtension?.GetType();
                if (modExtensionType == null)
                {
                    continue;
                }

                if (modExtensionType.FullName == defModExtensionTypeOf || modExtensionType.Name == defModExtensionTypeOf)
                {
                    return modExtension;
                }
            }

            return null;
        }
    }
}
