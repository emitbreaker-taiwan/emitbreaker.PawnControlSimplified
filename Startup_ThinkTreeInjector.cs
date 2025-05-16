using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    [StaticConstructorOnStartup]
    public static class Startup_ThinkTreeInjector
    {
        static Startup_ThinkTreeInjector()
        {
            try
            {
                // Call ApplyThinkTreeToRaceDefs directly since it doesn't require game state
                Utility_ThinkTreeManager.ApplyThinkTreeToRaceDefs();
            }
            catch (Exception ex)
            {
                Log.Error($"[PawnControl] Error in ThinkTree initialization: {ex}");
            }
        }
    }
}
