using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using RimWorld;

namespace emitbreaker.PawnControl
{
    public static class ManagedTags
    {        
        public const string ForceAnimal = "ForceAnimal"; // Mark pawn forcefully as an Animal
        public const string ForceMechanoid = "ForceMechanoid"; // Mark pawn forcefully as Mechanoid
        public const string ForceHumanlike = "ForceHumanlike"; // Mark pawn forcefully as Mechanoid
        public const string ForceDraftable = "ForceDraftable"; // Make pawn forcefully draftable
        public const string ForceEquipWeapon = "ForceEquipWeapon"; // Make pawn forcefully draftable
        public const string ForceWearApparel = "ForceWearApparel"; // Make pawn forcefully draftable
        public const string AllowAllWork = "AllowAllWork"; // Allow all works
        public const string AllowWorkPrefix = "AllowWork_"; //Allow specific works                                                          
        public const string BlockAllWork = "BlockAllWork"; // Block all works
        public const string BlockWorkPrefix = "BlockWork_"; //Block specific works
        public const string ForceVFECombatTree = "ForceVFECombatTree"; // For VFE Compatibility
        public const string VFECore_Combat = "VFECore_Combat"; // For VFE Compatibility
        public const string Mech_DefendBeacon = "Mech_DefendBeacon"; // For Mech_DefendBeacon
        public const string Mech_EscortCommander = "Mech_EscortCommander"; // For Mech_EscortCommander
        public const string VFESkipWorkJobGiver = "VFESkipWorkJobGiver"; // Tags for specific job control behavior
        public const string DisableVFEAIJobs = "DisableVFEAIJobs"; // Tags for specific job control behavior
    }
}

