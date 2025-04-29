using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using static emitbreaker.PawnControl.Utility_ThinkTreeManager;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    [StaticConstructorOnStartup]
    public static class Startup_ThinkTreeInjector
    {
        static Startup_ThinkTreeInjector()
        {
            LongEventHandler.ExecuteWhenFinished(InjectPawnControlThinkTreesStaticRaceLevel);
        }
    }
}
