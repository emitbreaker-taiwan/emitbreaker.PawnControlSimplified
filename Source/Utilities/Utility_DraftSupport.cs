using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace emitbreaker.PawnControl
{
    public static class Utility_DraftSupport
    {
        public static bool ShouldInjectDrafter(Pawn pawn)
        {
            return Utility_NonHumanlikePawnControl.PawnChecker(pawn) &&
                   pawn.drafter == null &&
                   Utility_NonHumanlikePawnControl.HasTag(pawn.def, NonHumanlikePawnControlTags.AutoDraftInjection);
        }

        public static DutyDef ResolveSiegeDuty(Pawn p)
        {
            if (Utility_CacheManager.HasTag(p.def, "Siege_HoldFire"))
            {
                var holdFire = Utility_CacheManager.GetDuty("HoldFire");
                if (holdFire != null)
                    return holdFire;
            }

            if (Utility_CacheManager.HasTag(p.def, "Siege_ManTurret"))
            {
                var manTurrets = Utility_CacheManager.GetDuty("ManTurrets");
                if (manTurrets != null)
                    return manTurrets;
            }

            return DutyDefOf.Defend;
        }
    }
}
