using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace emitbreaker.PawnControl
{
    [StaticConstructorOnStartup]
    public static class Startup_IdentityPreloader
    {
        static Startup_IdentityPreloader()
        {
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                // ✅ Clear old mod extension cache
                Utility_CacheManager.ClearModExtensionCache();

                // ✅ Preload mod extensions into runtime cache
                Utility_CacheManager.PreloadModExtensions();

                // ✅ Build identity flag cache based on injected extensions
                Utility_IdentityManager.BuildIdentityFlagCache(false);

                // ✅ Attach skill trackers to eligible pawns
                Utility_SkillManager.AttachSkillTrackersToPawnsSafely();
            });
        }
    }
}
