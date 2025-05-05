using RimWorld;
using UnityEngine;
using Verse;

namespace emitbreaker.PawnControl
{

    public class Mod_SimpleNonHumanlikePawnControl : Mod
    {
        public override string SettingsCategory()
        {
            return "PawnControl_ModName".Translate();
        }
        public override void DoSettingsWindowContents(Rect inRect)
        {
            base.DoSettingsWindowContents(inRect);

            // Add this line at the beginning of the method
            Utility_StatManager.CheckStatHediffDefExists();

            // Rest of your settings code...
        }

        public Mod_SimpleNonHumanlikePawnControl(ModContentPack content) : base(content)
        {
        }
    }
}
