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
    public static class Utility_DrafterManager
    {
        public static bool ShouldInjectDrafter(Pawn pawn)
        {
            if (pawn == null || pawn.def == null || pawn.RaceProps == null || pawn.drafter != null)
            {
                return false; // Invalid pawn or missing definitions
            }

            // Drafter should be injected if the pawn is valid, has no drafter, and has the AutoDraftInjection tag
            return Utility_Common.PawnChecker(pawn) && Utility_TagManager.HasTag(pawn.def, ManagedTags.AutoDraftInjection);
        }

        public static DutyDef ResolveSiegeDuty(Pawn p)
        {
            if (Utility_TagManager.HasTag(p.def, "Siege_HoldFire"))
            {
                var holdFire = Utility_CacheManager.GetDuty("HoldFire");
                if (holdFire != null)
                {
                    return holdFire;
                }
            }

            if (Utility_TagManager.HasTag(p.def, "Siege_ManTurret"))
            {
                var manTurrets = Utility_CacheManager.GetDuty("ManTurrets");
                if (manTurrets != null)
                {
                    return manTurrets;
                }
            }

            return DutyDefOf.Defend;
        }
    }
}
