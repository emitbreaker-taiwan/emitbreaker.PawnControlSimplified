using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Animal Handling-specific implementation of the unified job giver
    /// </summary>
    public class JobGiver_Unified_Handling_PawnControl : JobGiver_Unified_PawnControl<JobModule_Handling, Pawn>
    {
        // Static initializer to register handling modules
        static JobGiver_Unified_Handling_PawnControl()
        {
            // Register all handling modules in priority order
            RegisterModule(new JobModule_Handling_FeedAnimalPatient());
            RegisterModule(new JobModule_Handling_Tame());
            RegisterModule(new JobModule_Handling_Train());
            RegisterModule(new JobModule_Handling_TakeRoamingToPen()); // Higher priority pen tasks
            RegisterModule(new JobModule_Handling_Slaughter());
            RegisterModule(new JobModule_Handling_ReleaseAnimalToWild());
            RegisterModule(new JobModule_Handling_Milk()); // Add milk module
            RegisterModule(new JobModule_Handling_Shear()); // Add shear module
            RegisterModule(new JobModule_Handling_RebalanceAnimalsInPens());
            RegisterModule(new JobModule_Handling_TakeToPen()); // Lower priority pen tasks
            // Add other handling modules here

            // Register this specific JobGiver with the registry
            Type jobGiverType = typeof(JobGiver_Unified_Handling_PawnControl);
            HashSet<string> workTypes = new HashSet<string> { "Handling" };

            // Add work types from handling modules
            foreach (var module in _jobModules)
            {
                if (!string.IsNullOrEmpty(module.WorkTypeName) && module.WorkTypeName != "Handling")
                    workTypes.Add(module.WorkTypeName);
            }

            // Register with the appropriate priority for Handling
            // Register this job giver with the appropriate priority
            RegisterDerivedJobGiver<JobGiver_Unified_Handling_PawnControl>("Handling", 7.0f);
        }

        protected override string WorkTypeName => "Handling";

        public override string ToString()
        {
            return "JobGiver_Unified_Handling_PawnControl";
        }
    }
}