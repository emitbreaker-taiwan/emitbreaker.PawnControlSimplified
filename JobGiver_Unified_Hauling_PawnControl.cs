using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Hauling-specific implementation of the unified job giver
    /// </summary>
    public class JobGiver_Unified_Hauling_PawnControl : JobGiver_Unified_PawnControl<JobModule_Hauling, Thing>
    {
        // Static initializer to register hauling modules
        static JobGiver_Unified_Hauling_PawnControl()
        {
            // Register all hauling modules in priority order
            RegisterModule(new JobModule_Hauling_HaulCorpses());
            RegisterModule(new JobModule_Hauling_Strip());
            RegisterModule(new JobModule_Hauling_UnloadCarriers());
            RegisterModule(new JobModule_Hauling_Cremate());
            RegisterModule(new JobModule_Hauling_EmptyEggBox());
            RegisterModule(new JobModule_Hauling_TakeBeerOutOfBarrel());
            RegisterModule(new JobModule_Hauling_FillFermentingBarrel());
            RegisterModule(new JobModule_Hauling_Merge());
            RegisterModule(new JobModule_Hauling_DeliverResourcesToBlueprints());
            RegisterModule(new JobModule_Hauling_DeliverResourcesToFrames());
            RegisterModule(new JobModule_Hauling_Refuel());
            RegisterModule(new JobModule_Hauling_RefuelTurret());
            RegisterModule(new JobModule_Hauling_LoadTransporters());
            RegisterModule(new JobModule_Hauling_HelpGatheringItemsForCaravan());
            RegisterModule(new JobModule_Hauling_HaulToPortal());
            RegisterModule(new JobModule_Hauling_HaulCampfire());
            RegisterModule(new JobModule_Hauling_HaulGeneral());
            // Add other hauling modules here

            // Register this specific JobGiver with the registry
            Type jobGiverType = typeof(JobGiver_Unified_Hauling_PawnControl);
            HashSet<string> workTypes = new HashSet<string> { "Hauling" };

            // Add work types from hauling modules
            foreach (var module in _jobModules)
            {
                if (!string.IsNullOrEmpty(module.WorkTypeName) && module.WorkTypeName != "Hauling")
                    workTypes.Add(module.WorkTypeName);
            }

            // Register with the appropriate priority for Hauling
            // Register this job giver with the appropriate priority
            RegisterDerivedJobGiver<JobGiver_Unified_Hauling_PawnControl>("Hauling", 3.9f);
        }

        protected override string WorkTypeName => "Hauling";

        public override string ToString()
        {
            return "JobGiver_Unified_Hauling_PawnControl";
        }
    }
}