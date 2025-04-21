using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse.AI.Group;
using Verse.AI;
using Verse;

namespace emitbreaker.PawnControl
{
    public static class Utility_LordDutyResolver
    {
        public static bool TryGetDutyOverride(Pawn pawn, LordToil lordToil, out PawnDuty duty)
        {
            duty = null;
            var ext = Utility_NonHumanlikePawnControl.GetExtension(pawn.def);
            if (ext?.lordDutyMappings == null)
                return false;

            string toilClassName = lordToil.GetType().Name;

            foreach (var mapping in ext.lordDutyMappings)
            {
                if (mapping.lordToilClass == toilClassName)
                {
                    var def = Utility_CacheManager.GetDuty(mapping.dutyDef);
                    if (def != null)
                    {
                        duty = new PawnDuty(def, lordToil.FlagLoc, mapping.radius);
                        return true;
                    }
                }
            }

            return false;
        }

        public static bool TryGetDefaultDuty(Pawn pawn, out PawnDuty duty)
        {
            duty = null;
            var ext = Utility_NonHumanlikePawnControl.GetExtension(pawn.def);
            if (ext?.defaultDutyDef == null)
                return false;

            var def = Utility_CacheManager.GetDuty(ext.defaultDutyDef);
            if (def != null)
            {
                float radius = ext.defaultDutyRadius >= 0
                    ? ext.defaultDutyRadius
                    : Utility_CECompatibility.GetDefaultDutyRadius(pawn);

                duty = new PawnDuty(def, pawn.Position, radius);
                return true;
            }

            return false;
        }

        public static void TryAssignLordDuty(Pawn pawn, LordToil lordToil, DutyDef fallbackDuty, IntVec3 flagLoc, float fallbackRadius = -1f)
        {
            if (!Utility_NonHumanlikePawnControl.PawnChecker(pawn)) return;

            // Use enum-safe check for tag
            if (!Utility_CacheManager.HasTag(pawn.def, PawnTag.AutoDraftInjection)) return;

            // Prefer XML override
            if (TryGetDutyOverride(pawn, lordToil, out var dutyFromXml))
            {
                pawn.mindState.duty = dutyFromXml;
                return;
            }

            // Fallback
            float radius = fallbackRadius >= 0f ? fallbackRadius : Utility_CECompatibility.GetDefaultDutyRadius(pawn);
            pawn.mindState.duty = new PawnDuty(fallbackDuty, flagLoc, radius);
        }
    }
}
