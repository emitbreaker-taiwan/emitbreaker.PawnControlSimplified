using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Cleaning-specific implementation of the unified job giver
    /// </summary>
    public class JobGiver_Unified_Cleaning_PawnControl : JobGiver_Unified_PawnControl<JobModule_Cleaning, Thing>
    {
        // Static initializer to register cleaning modules
        static JobGiver_Unified_Cleaning_PawnControl()
        {
            // Register all cleaning modules in priority order
            RegisterModule(new JobModule_Cleaning_CleanFilth());
            RegisterModule(new JobModule_Cleaning_ClearSnow());
            RegisterModule(new JobModule_Cleaning_ClearPollution());
            // Add other cleaning modules here as needed

            // Register this specific JobGiver with the registry
            Type jobGiverType = typeof(JobGiver_Unified_Cleaning_PawnControl);
            HashSet<string> workTypes = new HashSet<string> { "Cleaning" };

            // Add work types from cleaning modules
            foreach (var module in _jobModules)
            {
                if (!string.IsNullOrEmpty(module.WorkTypeName) && module.WorkTypeName != "Cleaning")
                    workTypes.Add(module.WorkTypeName);
            }

            // Register with the appropriate priority for Cleaning
            // Register this job giver with the appropriate priority
            RegisterDerivedJobGiver<JobGiver_Unified_Cleaning_PawnControl>("Cleaning", 3.5f);
        }

        /// <summary>
        /// The work type name for cleaning tasks
        /// </summary>
        protected override string WorkTypeName => "Cleaning";

        public override string ToString()
        {
            return "JobGiver_Unified_Cleaning_PawnControl";
        }
    }
}