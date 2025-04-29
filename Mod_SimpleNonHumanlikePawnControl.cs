using RimWorld;
using Verse;

namespace emitbreaker.PawnControl
{

    public class Mod_SimpleNonHumanlikePawnControl : Mod
    {
        public override string SettingsCategory()
        {
            return "PawnControl_ModName".Translate();
        }

        public Mod_SimpleNonHumanlikePawnControl(ModContentPack content) : base(content)
        {
        }
    }
}
