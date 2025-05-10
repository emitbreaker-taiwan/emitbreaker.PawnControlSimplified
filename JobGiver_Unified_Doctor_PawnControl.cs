using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Doctor-specific implementation of the unified job giver
    /// </summary>
    public class JobGiver_Unified_Doctor_PawnControl : JobGiver_Unified_PawnControl<JobModule_Doctor, Pawn>
    {
        // Static initializer to register doctor modules
        static JobGiver_Unified_Doctor_PawnControl()
        {
            // Register all doctor modules in priority order
            RegisterModule(new JobModule_Doctor_FeedPatient());
            RegisterModule(new JobModule_Doctor_FeedPrisonerPatient());
            RegisterModule(new JobModule_Doctor_FeedAnimalPatient());

            // Add additional doctor modules as they are implemented
            // Examples:
            // RegisterModule(new JobModule_Doctor_TendPatient());
            // RegisterModule(new JobModule_Doctor_TendPrisonerPatient());
            // RegisterModule(new JobModule_Doctor_TendAnimalPatient());
            // RegisterModule(new JobModule_Doctor_TendSelf());
            // RegisterModule(new JobModule_Doctor_TakeToSickbed());
            // RegisterModule(new JobModule_Doctor_Rescue());
            // RegisterModule(new JobModule_Doctor_Anesthetize());
            // RegisterModule(new JobModule_Doctor_Surgery());

            // Register this specific JobGiver with the registry
            Type jobGiverType = typeof(JobGiver_Unified_Doctor_PawnControl);
            HashSet<string> workTypes = new HashSet<string> { "Doctor" };

            // Add work types from doctor modules
            foreach (var module in _jobModules)
            {
                if (!string.IsNullOrEmpty(module.WorkTypeName) && module.WorkTypeName != "Doctor")
                    workTypes.Add(module.WorkTypeName);
            }

            // Register with the appropriate priority for Doctor
            // Register this job giver with the appropriate priority
            RegisterDerivedJobGiver<JobGiver_Unified_Doctor_PawnControl>("Doctor", 8.5f);
        }

        protected override string WorkTypeName => "Doctor";

        public override string ToString()
        {
            return "JobGiver_Unified_Doctor_PawnControl";
        }
    }
}