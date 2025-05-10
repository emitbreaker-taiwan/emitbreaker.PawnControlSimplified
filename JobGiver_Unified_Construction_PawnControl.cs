using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Construction-specific implementation of the unified job giver
    /// </summary>
    public class JobGiver_Unified_Construction_PawnControl : JobGiver_Unified_PawnControl<JobModule_Construction, Thing>
    {
        // Static initializer to register construction modules
        static JobGiver_Unified_Construction_PawnControl()
        {
            // Register all construction modules in priority order
            RegisterModule(new JobModule_Construction_ConstructFinishFrames());
            RegisterModule(new JobModule_Construction_DeliverResourcesToBlueprints());
            RegisterModule(new JobModule_Construction_DeliverResourcesToFrames());
            RegisterModule(new JobModule_Construction_Deconstruct());
            RegisterModule(new JobModule_Construction_FixBrokenDown());
            RegisterModule(new JobModule_Construction_Uninstall());
            RegisterModule(new JobModule_Construction_Repair());
            RegisterModule(new JobModule_Construction_BuildRoof());
            RegisterModule(new JobModule_Construction_RemoveRoof());
            RegisterModule(new JobModule_Construction_SmoothWall());
            RegisterModule(new JobModule_Construction_SmoothFloor());
            RegisterModule(new JobModule_Construction_RemoveFloor());
            RegisterModule(new JobModule_Construction_FillIn());
            // Add other construction modules here

            // Register this specific JobGiver with the registry
            Type jobGiverType = typeof(JobGiver_Unified_Construction_PawnControl);
            HashSet<string> workTypes = new HashSet<string> { "Construction" };

            // Add work types from construction modules
            foreach (var module in _jobModules)
            {
                if (!string.IsNullOrEmpty(module.WorkTypeName) && module.WorkTypeName != "Construction")
                    workTypes.Add(module.WorkTypeName);
            }

            // Register with the appropriate priority for Construction
            // Register this job giver with the appropriate priority
            RegisterDerivedJobGiver<JobGiver_Unified_Construction_PawnControl>("Construction", 6.2f);
        }

        protected override string WorkTypeName => "Construction";

        public override string ToString()
        {
            return "JobGiver_Unified_Construction_PawnControl";
        }
    }
}