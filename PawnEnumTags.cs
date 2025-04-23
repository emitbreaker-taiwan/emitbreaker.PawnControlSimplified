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
        AllowWork_Mining,
        AllowWork_Growing,
        AllowWork_Construction,
        AllowWork_Warden,
        AllowWork_Doctor,
        AllowWork_Firefighter,
        AllowWork_Hunting,
        AllowWork_Handling,
        AllowWork_Crafting,
        AllowWork_Hauling,
        AllowWork_Cleaning,
        AllowWork_Research,
        AllowWork_PlantCutting,
        AllowWork_Smithing,
        AllowWork_Childcare, // 🔒 Biotech-only
        AllowWork_DarkStudy, // 🔒 Royalty-only
        BlockAllWork,
        BlockWork_Mining,
        BlockWork_Growing,
        BlockWork_Construction,
        BlockWork_Warden,
        BlockWork_Doctor,
        BlockWork_Firefighter,
        BlockWork_Hunting,
        BlockWork_Handling,
        BlockWork_Crafting,
        BlockWork_Hauling,
        BlockWork_Cleaning,
        BlockWork_Research,
        BlockWork_PlantCutting,
        BlockWork_Smithing,
        BlockWork_Childcare, // 🔒 Biotech-only
        BlockWork_DarkStudy, // 🔒 Royalty-only
        ForceTrainerTab,
        AutoDraftInjection,

        ForceVFECombatTree,
        VFECore_Combat,

        Mech_DefendBeacon,
        Mech_EscortCommander,

        Unknown
    }
}
