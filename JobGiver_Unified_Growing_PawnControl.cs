using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Growing-specific implementation of the unified job giver.
    /// Manages all plant growing related jobs through a modular system.
    /// </summary>
    public class JobGiver_Unified_Growing_PawnControl : JobGiver_Unified_PawnControl<JobModule_Growing, Plant>
    {
        // Static initializer to register growing modules
        static JobGiver_Unified_Growing_PawnControl()
        {
            // Register all growing modules in priority order
            RegisterModule(new JobModule_Growing_GrowerHarvest());  // Assume this exists or will be implemented
            RegisterModule(new JobModule_Growing_GrowerSow());      // Assume this exists or will be implemented
            RegisterModule(new JobModule_Growing_Replant());  // Our new replant module

            // Additional modules can be added here as they are implemented

            // Register this specific JobGiver with the registry
            Type jobGiverType = typeof(JobGiver_Unified_Growing_PawnControl);
            HashSet<string> workTypes = new HashSet<string> { "Growing" };

            // Add work types from growing modules
            foreach (var module in _jobModules)
            {
                if (!string.IsNullOrEmpty(module.WorkTypeName) && module.WorkTypeName != "Growing")
                    workTypes.Add(module.WorkTypeName);
            }

            // Register with the appropriate priority for Growing
            // Register this job giver with the appropriate priority
            RegisterDerivedJobGiver<JobGiver_Unified_Growing_PawnControl>("Growing", 5.8f);
        }

        /// <summary>
        /// The work type name for this job giver
        /// </summary>
        protected override string WorkTypeName => "Growing";

        public override string ToString()
        {
            return "JobGiver_Unified_Growing_PawnControl";
        }

        /// <summary>
        /// Reset all cache data when game is loaded
        /// </summary>
        public new static void ResetCache()
        {
            JobGiver_Unified_PawnControl<JobModule_Growing, Plant>.ResetCache();
        }
    }
}