using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Plant Cutting-specific implementation of the unified job giver.
    /// Manages all plant cutting related jobs through a modular system.
    /// </summary>
    public class JobGiver_Unified_PlantCutting_PawnControl : JobGiver_Unified_PawnControl<JobModule_PlantCutting, Plant>
    {
        // Static initializer to register plant cutting modules
        static JobGiver_Unified_PlantCutting_PawnControl()
        {
            // Register all plant cutting modules in priority order
            RegisterModule(new JobModule_PlantCutting_ExtractTree());
            RegisterModule(new JobModule_PlantCutting_PlantsCut());

            // Additional modules can be added here as they are implemented
            // Examples:
            // RegisterModule(new JobModule_PlantCutting_CutBlighted());
            // RegisterModule(new JobModule_PlantCutting_ClearCrop());
            // Add other hauling modules here

            // Register this specific JobGiver with the registry
            Type jobGiverType = typeof(JobGiver_Unified_PlantCutting_PawnControl);
            HashSet<string> workTypes = new HashSet<string> { "PlantCutting" };

            // Add work types from hauling modules
            foreach (var module in _jobModules)
            {
                if (!string.IsNullOrEmpty(module.WorkTypeName) && module.WorkTypeName != "PlantCutting")
                    workTypes.Add(module.WorkTypeName);
            }

            // Register with the appropriate priority for Hauling
            // Register this job giver with the appropriate priority
            RegisterDerivedJobGiver<JobGiver_Unified_PlantCutting_PawnControl>("PlantCutting", 5.2f);
        }

        /// <summary>
        /// The work type name for this job giver
        /// </summary>
        protected override string WorkTypeName => "PlantCutting";

        public override string ToString()
        {
            return "JobGiver_Unified_PlantCutting_PawnControl";
        }
        
        /// <summary>
        /// Reset all cache data when game is loaded
        /// </summary>
        public new static void ResetCache()
        {
            JobGiver_Unified_PawnControl<JobModule_PlantCutting, Plant>.ResetCache();
        }
    }
}