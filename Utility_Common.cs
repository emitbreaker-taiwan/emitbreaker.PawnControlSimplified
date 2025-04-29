using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimWorld;
using Verse;

namespace emitbreaker.PawnControl
{
    public static class Utility_Common
    {
        public static bool PawnChecker(Pawn pawn)
        {
            if (pawn == null || pawn.RaceProps.Humanlike || pawn.Dead || !pawn.Spawned || pawn.IsDessicated())
            {
                return false;
            }

            if (Utility_VehicleFramework.IsVehiclePawn(pawn))
            {
                return false;
            }

            return true;
        }

        public static bool RaceDefChecker(ThingDef def)
        {
            if (def == null || def.race == null)
            {
                return false;
            }

            return true;
        }

        public static JobDef JobDefNamed(string defName)
        {
            return DefDatabase<JobDef>.GetNamed(defName);
        }

        public static NeedDef NeedDefNamed(string defName)
        {
            return DefDatabase<NeedDef>.GetNamed(defName);
        }
    }
}
