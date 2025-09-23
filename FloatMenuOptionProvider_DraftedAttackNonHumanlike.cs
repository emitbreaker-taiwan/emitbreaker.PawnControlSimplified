using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace emitbreaker.PawnControl
{
    public class FloatMenuOptionProvider_DraftedAttackNonHumanlike : FloatMenuOptionProvider_DraftedAttack
    {
        protected override bool MechanoidCanDo => true;

        public override bool SelectedPawnValid(Pawn pawn, FloatMenuContext context)
        {
            if (pawn.RaceProps.Humanlike) return false;
            if (pawn.Faction != Faction.OfPlayer) return false;

            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null) return false;

            return Utility_TagManager.ForceDraftable(pawn);
        }
    }
}
