using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Unified JobGiver that handles all basic worker tasks (flicking, opening containers, extracting skulls, etc.)
    /// Optimized for performance with large colonies and multiple designator types.
    /// </summary>
    public class JobGiver_Unified_BasicWorker_PawnControl : JobGiver_Unified_PawnControl<JobModule_BasicWorker, Thing>
    {
        // Static initializer to register basic worker modules
        static JobGiver_Unified_BasicWorker_PawnControl()
        {
            // Register all basic worker modules in priority order
            // Flicking has highest priority (6.2f)
            RegisterModule(new JobModule_BasicWorker_Flick());

            // Opening containers has medium priority (6.1f) 
            RegisterModule(new JobModule_BasicWorker_Open());

            // Extracting skulls has lowest priority (6.0f)
            RegisterModule(new JobModule_BasicWorker_ExtractSkull());

            // Add other basic worker modules here as needed

            // Register this JobGiver in the registry with appropriate work types
            Type jobGiverType = typeof(JobGiver_Unified_BasicWorker_PawnControl);
            HashSet<string> workTypes = new HashSet<string> { "BasicWorker" };

            // Add work types from modules
            foreach (var module in _jobModules)
            {
                if (!string.IsNullOrEmpty(module.WorkTypeName))
                    workTypes.Add(module.WorkTypeName);
            }

            // Register with appropriate priority (hardcoded for BasicWorker)
            // Register this job giver with the appropriate priority
            RegisterDerivedJobGiver<JobGiver_Unified_BasicWorker_PawnControl>("BasicWorker", 7.8f);
        }

        /// <summary>
        /// The work type name for basic worker tasks
        /// </summary>
        protected override string WorkTypeName => "BasicWorker";

        /// <summary>
        /// Override the default cache update interval - basic worker tasks can update less frequently
        /// </summary>
        protected new const int CACHE_UPDATE_INTERVAL = 300; // Update every 5 seconds

        /// <summary>
        /// Distance thresholds for bucketing - basic worker tasks typically don't need to be done urgently
        /// </summary>
        protected static new readonly float[] DISTANCE_THRESHOLDS = new float[] { 400f, 900f, 1600f }; // 20, 30, 40 tiles

        public override string ToString()
        {
            return "JobGiver_Unified_BasicWorker_PawnControl";
        }
    }
}