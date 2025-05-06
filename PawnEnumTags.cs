using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace emitbreaker.PawnControl
{
    public enum PawnEnumTags
    {
        ForceAnimal,
        ForceDraftable,
        ForceWork,
        AllowAllWork,
        AllowWork_Firefighter,
        AllowWork_Patient,
        AllowWork_Doctor, // Available
        AllowWork_PatientBedRest,
        AllowWork_BasicWorker, // Available
        AllowWork_Warden, // Available
        AllowWork_Handling, // Available
        AllowWork_Cooking,
        AllowWork_Hunting,
        AllowWork_Construction,
        AllowWork_Growing, // Available
        AllowWork_Mining,
        AllowWork_PlantCutting, // Available
        AllowWork_Smithing,
        AllowWork_Tailoring,
        AllowWork_Art,
        AllowWork_Crafting,
        AllowWork_Hauling, // Available
        AllowWork_Cleaning, // Available
        AllowWork_Research,
        AllowWork_Childcare, // 🔒 Biotech-only
        AllowWork_DarkStudy, // 🔒 Anomaly-only
        BlockAllWork,
        BlockWork_Firefighter,
        BlockWork_Patient,
        BlockWork_Doctor,
        BlockWork_PatientBedRest,
        BlockWork_BasicWorker,
        BlockWork_Warden,
        BlockWork_Handling,
        BlockWork_Cooking,
        BlockWork_Hunting,
        BlockWork_Construction,
        BlockWork_Growing,
        BlockWork_Mining,
        BlockWork_PlantCutting,
        BlockWork_Smithing,
        BlockWork_Tailoring,
        BlockWork_Art,
        BlockWork_Crafting,
        BlockWork_Hauling,
        BlockWork_Cleaning,
        BlockWork_Research,
        BlockWork_Childcare, // 🔒 Biotech-only
        BlockWork_DarkStudy, // 🔒 Anomaly-only
        ForceTrainerTab,
        AutoDraftInjection,

        ForceVFECombatTree,
        VFECore_Combat,

        Mech_DefendBeacon,
        Mech_EscortCommander,

        Unknown
    }
}
