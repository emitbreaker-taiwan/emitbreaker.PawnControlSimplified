using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using RimWorld;

namespace emitbreaker.PawnControl
{
    public static class NonHumanlikePawnControlTags
    {
        public const string ForceAnimal = "ForceAnimal"; // Mark pawn forcefully as an Animal
        public const string ForceDraftable = "ForceDraftable"; // Make pawn forcefully draftable
        public const string ForceWork = "ForceWork"; // Allow pawn forcefully work
        public const string BlockWorkPrefix = "BlockWork_"; //Block specific works
        public const string AllowWorkPrefix = "AllowWork_"; //Allow specific works                                                          
        public const string BlockAllWork = "BlockAllWork"; // Block all works
        public const string AllowAllWork = "AllowAllWork"; // Allow all works
        public const string ForceTrainerTab = "ForceTrainerTab"; // Allow train tab
        public const string AutoDraftInjection = "AutoDraftInjection"; // Inject Draft function
        public const string ForceVFECombatTree = "ForceVFECombatTree"; // For VFE Compatibility
        public const string VFECore_Combat = "VFECore_Combat"; // For VFE Compatibility
    }
}

