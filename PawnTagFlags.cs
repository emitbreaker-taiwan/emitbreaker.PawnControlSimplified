using System;

namespace emitbreaker.PawnControl
{
    [Flags]
    public enum PawnTagFlags : long
    {
        None = 0,

        // Force type tags
        ForceAnimal = 1 << 0,
        ForceMechanoid = 1 << 1,
        ForceHumanlike = 1 << 2,

        // Behavior flags
        ForceDraftable = 1 << 3,
        ForceEquipWeapon = 1 << 4,
        ForceWearApparel = 1 << 5,
        ForceWork = 1 << 6,
        ForceTrainerTab = 1 << 7,
        AutoDraftInjection = 1 << 8,

        // Work related flags - general
        AllowAllWork = 1 << 9,
        BlockAllWork = 1 << 10,

        // Allow specific works
        AllowWork_Firefighter = 1 << 11,
        AllowWork_Patient = 1 << 12,
        AllowWork_Doctor = 1 << 13,
        AllowWork_PatientBedRest = 1 << 14,
        AllowWork_BasicWorker = 1 << 15,
        AllowWork_Warden = 1 << 16,
        AllowWork_Handling = 1 << 17,
        AllowWork_Cooking = 1 << 18,
        AllowWork_Hunting = 1 << 19,
        AllowWork_Construction = 1 << 20,
        AllowWork_Growing = 1 << 21,
        AllowWork_Mining = 1 << 22,
        AllowWork_PlantCutting = 1 << 23,
        AllowWork_Smithing = 1 << 24,
        AllowWork_Tailoring = 1 << 25,
        AllowWork_Art = 1 << 26,
        AllowWork_Crafting = 1 << 27,
        AllowWork_Hauling = 1 << 28,
        AllowWork_Cleaning = 1 << 29,
        AllowWork_Research = 1 << 30,
        AllowWork_Childcare = 1L << 31,  // Using long (1L) for bits beyond 31
        AllowWork_DarkStudy = 1L << 32,

        // Block specific works
        BlockWork_Firefighter = 1L << 33,
        BlockWork_Patient = 1L << 34,
        BlockWork_Doctor = 1L << 35,
        BlockWork_PatientBedRest = 1L << 36,
        BlockWork_BasicWorker = 1L << 37,
        BlockWork_Warden = 1L << 38,
        BlockWork_Handling = 1L << 39,
        BlockWork_Cooking = 1L << 40,
        BlockWork_Hunting = 1L << 41,
        BlockWork_Construction = 1L << 42,
        BlockWork_Growing = 1L << 43,
        BlockWork_Mining = 1L << 44,
        BlockWork_PlantCutting = 1L << 45,
        BlockWork_Smithing = 1L << 46,
        BlockWork_Tailoring = 1L << 47,
        BlockWork_Art = 1L << 48,
        BlockWork_Crafting = 1L << 49,
        BlockWork_Hauling = 1L << 50,
        BlockWork_Cleaning = 1L << 51,
        BlockWork_Research = 1L << 52,
        BlockWork_Childcare = 1L << 53,
        BlockWork_DarkStudy = 1L << 54,

        // VFE and mechanic compatibility
        ForceVFECombatTree = 1L << 55,
        VFECore_Combat = 1L << 56,
        Mech_DefendBeacon = 1L << 57,
        Mech_EscortCommander = 1L << 58,
        VFESkipWorkJobGiver = 1L << 59,
        DisableVFEAIJobs = 1L << 60,

        // Composite flags for convenience
        AllowMedicalWorks = AllowWork_Patient | AllowWork_Doctor | AllowWork_PatientBedRest,
        AllowCraftingWorks = AllowWork_Smithing | AllowWork_Tailoring | AllowWork_Art | AllowWork_Crafting,
        AllowResourceWorks = AllowWork_Mining | AllowWork_Growing | AllowWork_PlantCutting,

        BlockBasicWorks = BlockWork_BasicWorker,
        BlockMedicalWorks = BlockWork_Patient | BlockWork_Doctor | BlockWork_PatientBedRest,
        BlockCraftingWorks = BlockWork_Smithing | BlockWork_Tailoring | BlockWork_Art | BlockWork_Crafting,
        BlockResourceWorks = BlockWork_Mining | BlockWork_Growing | BlockWork_PlantCutting,
    }
}