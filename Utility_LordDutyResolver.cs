using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse.AI.Group;
using Verse.AI;
using Verse;
using static System.Net.Mime.MediaTypeNames;

namespace emitbreaker.PawnControl
{
    public static class Utility_LordDutyResolver
    {
        public static bool TryGetDutyOverride(Pawn pawn, LordToil lordToil, out PawnDuty duty)
        {
            duty = null;

            var physicalModExtension = pawn.def.GetModExtension<NonHumanlikePawnControlExtension>();
            var virtualModExtension = pawn.def.GetModExtension<VirtualNonHumanlikePawnControlExtension>();

            if (physicalModExtension != null)
            {
                if (physicalModExtension?.lordDutyMappings == null)
                {
                    return false;
                }

                string toilClassName = lordToil.GetType().Name;

                foreach (var mapping in physicalModExtension.lordDutyMappings)
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
            }
            else if (virtualModExtension != null)
            {
                if (virtualModExtension?.lordDutyMappings == null)
                {
                    return false;
                }

                string toilClassName = lordToil.GetType().Name;

                foreach (var mapping in virtualModExtension.lordDutyMappings)
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
            }

            return false;
        }


        public static bool TryGetDefaultDuty(Pawn pawn, out PawnDuty duty)
        {
            duty = null;
            var physicalModExtension = pawn.def.GetModExtension<NonHumanlikePawnControlExtension>();
            var virtualModExtension = pawn.def.GetModExtension<VirtualNonHumanlikePawnControlExtension>();

            if (physicalModExtension != null)
            {
                if (physicalModExtension?.defaultDutyDef == null)
                    return false;

                var def = Utility_CacheManager.GetDuty(physicalModExtension.defaultDutyDef);
                if (def != null)
                {
                    float radius = physicalModExtension.defaultDutyRadius >= 0
                        ? physicalModExtension.defaultDutyRadius
                        : Utility_CECompatibility.GetDefaultDutyRadius(pawn);

                    duty = new PawnDuty(def, pawn.Position, radius);
                    return true;
                }
            }
            else if (virtualModExtension != null)
            {
                if (virtualModExtension?.defaultDutyDef == null)
                    return false;

                var def = Utility_CacheManager.GetDuty(virtualModExtension.defaultDutyDef);
                if (def != null)
                {
                    float radius = virtualModExtension.defaultDutyRadius >= 0
                        ? virtualModExtension.defaultDutyRadius
                        : Utility_CECompatibility.GetDefaultDutyRadius(pawn);

                    duty = new PawnDuty(def, pawn.Position, radius);
                    return true;
                }
            }

            return false;
        }

        public static void TryAssignLordDuty(Pawn pawn, LordToil lordToil, DutyDef fallbackDuty, IntVec3 flagLoc, float fallbackRadius = -1f)
        {
            if (!Utility_NonHumanlikePawnControl.PawnChecker(pawn)) return;

            // Use enum-safe check for tag
            if (!Utility_CacheManager.Tags.HasEnumTag(pawn.def, PawnEnumTags.AutoDraftInjection)) return;

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
