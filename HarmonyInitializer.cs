using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace emitbreaker.PawnControl
{
    [StaticConstructorOnStartup]
    public static class HarmonyInitializer
    {
        static HarmonyInitializer()
        {
            var harmony = new Harmony("com.emitbreaker.PawnControl");

            // Automatically applies all Harmony patches in the project
            harmony.PatchAll(); // This will apply all patches within the HarmonyPatches.cs file.

            //Apply additional patches that need custom logic(like LordToil or VFE)
            ApplyApparelPatch(harmony);
        }

        // Apparel patch applied after all def databases are loaded
        private static void ApplyApparelPatch(Harmony harmony)
        {
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                HarmonyPatches.Patch_LordToil_Additional.TryPatchApparelFilter(harmony);
            });
        }
    }
}
