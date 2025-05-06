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
        }

        public Mod_SimpleNonHumanlikePawnControl(ModContentPack content) : base(content)
        {
        }
    }
}
