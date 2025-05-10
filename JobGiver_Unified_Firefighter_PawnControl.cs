using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Unified JobGiver that handles all firefighting tasks
    /// Optimized for performance with large colonies and frequent fire events.
    /// </summary>
    public class JobGiver_Unified_Firefighter_PawnControl : JobGiver_Unified_PawnControl<JobModule_Firefighter, Fire>
    {
        // Static initializer to register firefighting modules
        static JobGiver_Unified_Firefighter_PawnControl()
        {
            // Register firefighting module
            RegisterModule(new JobModule_Firefighter_FightFires());

            // Additional firefighting modules could be registered here if needed
            // For example, specialized modules for different types of fires or situations
            // RegisterModule(new JobModule_Firefighter_PreventFlammables());
            // RegisterModule(new JobModule_Firefighter_FightForestFires());

            // Register this specific JobGiver with the registry
            Type jobGiverType = typeof(JobGiver_Unified_Firefighter_PawnControl);
            HashSet<string> workTypes = new HashSet<string> { "Firefighter" };

            // Add work types from firefighting modules
            foreach (var module in _jobModules)
            {
                if (!string.IsNullOrEmpty(module.WorkTypeName) && module.WorkTypeName != "Firefighter")
                    workTypes.Add(module.WorkTypeName);
            }

            // Register with the appropriate priority for Firefighter
            // Register this job giver with the appropriate priority
            RegisterDerivedJobGiver<JobGiver_Unified_Firefighter_PawnControl>("Firefighter", 9.0f);
        }

        /// <summary>
        /// The work type name for firefighting tasks
        /// </summary>
        protected override string WorkTypeName => "Firefighter";

        /// <summary>
        /// Override the default cache update interval - fires need frequent updates
        /// </summary>
        protected new const int CACHE_UPDATE_INTERVAL = 60; // Update every 1 second (more frequent due to urgency)

        /// <summary>
        /// Distance thresholds for bucketing - fires are emergencies so use smaller distances
        /// </summary>
        protected static new readonly float[] DISTANCE_THRESHOLDS = new float[] { 100f, 400f, 625f }; // 10, 20, 25 tiles

        /// <summary>
        /// Override TryGiveJob to skip usual worktype checks since firefighting is an emergency response
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (pawn?.Map == null) return null;

            // Fast early exit - if we know this map has no fires at all
            if (!MapHasAnyTargets(pawn.Map))
                return null;

            // Check if pawn is eligible for firefighting (enabled and not incapable)
            if (pawn.workSettings == null || !pawn.workSettings.WorkIsActive(WorkTypeDefOf.Firefighter) ||
                pawn.WorkTagIsDisabled(WorkTags.Firefighting))
                return null;

            // Add detailed diagnostic logging
            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogJobAssignmentDiagnostic(pawn, this.GetType().Name);
            }

            // Update cache if needed
            UpdateTargetCache(pawn.Map);

            // Get the map ID to access the correct reachability cache dictionary
            int mapId = pawn.Map.uniqueID;

            // Create a dictionary that matches the expected type for the TryGetJobFromModules method
            Dictionary<int, Dictionary<Fire, bool>> reachabilityDict = new Dictionary<int, Dictionary<Fire, bool>>
            {
                { mapId, _reachabilityCache.GetMapCache(mapId) }
            };

            // Use the optimized method to find a job from any module
            // This handles last successful module prioritization and early filtering
            Job job = Utility_JobGiverManager.TryGetJobFromModules(
                pawn,
                _jobModules,
                _targetsByTypeCache,
                reachabilityDict,
                DISTANCE_THRESHOLDS);

            return job;
        }

        public override string ToString()
        {
            return "JobGiver_Unified_Firefighter_PawnControl";
        }
    }
}