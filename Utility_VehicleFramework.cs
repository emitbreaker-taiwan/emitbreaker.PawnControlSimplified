using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace emitbreaker.PawnControl
{
    public static class Utility_VehicleFramework
    {
        private static readonly Type vehiclePawnType = Type.GetType("Vehicles.VehiclePawn, Vehicles");

        public static bool IsVehiclePawn(Pawn pawn)
        {
            return pawn != null && vehiclePawnType != null && vehiclePawnType.IsInstanceOfType(pawn);
        }
    }
}
