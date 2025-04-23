using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace emitbreaker.PawnControl
{
    [StaticConstructorOnStartup]
    public static class CacheInvalidationHandler
    {
        static CacheInvalidationHandler()
        {
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                Utility_CacheManager.ClearModExtensionCache();
            });
        }
    }
}
