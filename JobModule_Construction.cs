using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Base abstract class for all construction job modules
    /// </summary>
    public abstract class JobModule_Construction : JobModule<Thing>
    {
        // Simple fixed priority (higher number = higher priority)
        public override float Priority => 6.2f;

        /// <summary>
        /// Gets the work type this module is associated with
        /// </summary>
        public override string WorkTypeName => "Construction";

        /// <summary>
        /// Fast filter check for constructors
        /// </summary>
        public override bool QuickFilterCheck(Pawn pawn)
        {
            return !pawn.WorkTagIsDisabled(WorkTags.Constructing);
        }

        public override bool WorkTypeApplies(Pawn pawn)
        {
            // Quick check based on work settings
            return pawn?.workSettings?.WorkIsActive(WorkTypeDefOf.Construction) == true;
        }

        /// <summary>
        /// Default cache update interval - 4 seconds for construction jobs
        /// </summary>
        public override int CacheUpdateInterval => 240; // Update every 4 seconds

        /// <summary>
        /// Relevant ThingRequestGroups for construction jobs
        /// </summary>
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups { get; } =
            new HashSet<ThingRequestGroup> { 
                ThingRequestGroup.BuildingFrame, 
                ThingRequestGroup.Blueprint 
            };

        /// <summary>
        /// Filter function to identify targets for this job (specifically named for construction jobs)
        /// </summary>
        public abstract bool ShouldProcessBuildable(Thing constructible, Map map);

        /// <summary>
        /// Filter function implementation that calls the construction-specific method
        /// </summary>
        public override bool ShouldProcessTarget(Thing target, Map map) 
            => ShouldProcessBuildable(target, map);

        /// <summary>
        /// Validates if the constructor can perform this job on the target
        /// </summary>
        public abstract bool ValidateConstructionJob(Thing target, Pawn constructor);

        /// <summary>
        /// Validates job implementation that calls the construction-specific method
        /// </summary>
        public override bool ValidateJob(Thing target, Pawn actor) 
            => ValidateConstructionJob(target, actor);

        /// <summary>
        /// Creates the job for the constructor to perform on the target
        /// </summary>
        public override Job CreateJob(Pawn actor, Thing target) 
            => CreateConstructionJob(actor, target);

        /// <summary>
        /// Construction-specific implementation of job creation
        /// </summary>
        protected abstract Job CreateConstructionJob(Pawn constructor, Thing target);

        /// <summary>
        /// Helper method to check if a blueprint/frame is worth working on
        /// </summary>
        protected bool IsConstructionWorthDoing(Thing constructible, Pawn constructor)
        {
            if (constructible == null || !constructible.Spawned || constructor?.Map == null) 
                return false;

            // Skip if construction is forbidden or not usable
            if (constructible.IsForbidden(constructor))
                return false;

            // Skip if construction is claimed by someone else
            if (!constructor.CanReserve(constructible))
                return false;

            // For frames, check if we have the resources to complete it
            if (constructible is Frame frame)
            {
                return GenConstruct.CanConstruct(frame, constructor, false);
            }

            // For blueprints, check if resources are available 
            if (constructible is Blueprint blueprint)
            {
                return GenConstruct.CanConstruct(blueprint, constructor, false);
            }

            return false;
        }

        /// <summary>
        /// Helper method to check if resources required for construction are available
        /// </summary>
        protected bool CheckResourceAvailability(Frame frame, Pawn constructor)
        {
            if (frame == null || constructor?.Map == null) return false;

            // Use the TotalMaterialCost method from Frame class
            List<ThingDefCountClass> materials = frame.TotalMaterialCost();
            if (materials.NullOrEmpty()) return true; // No materials needed

            foreach (ThingDefCountClass mat in materials)
            {
                // Use the ThingCountNeeded method from Frame class
                int needed = frame.ThingCountNeeded(mat.thingDef);
                if (needed > 0)
                {
                    // Not enough resources available
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Default cache update: collect every frame or blueprint that
        /// satisfies ShouldProcessBuildable. Uses progressive scanning for better performance.
        /// </summary>
        public override void UpdateCache(Map map, List<Thing> targetCache)
        {
            if (map == null) return;

            // Use progressive cache update with the appropriate filter
            UpdateCacheProgressively(
                map,
                targetCache,
                ref _lastUpdateTick,
                RelevantThingRequestGroups,
                constructible => ShouldProcessBuildable(constructible, map),
                null,
                CacheUpdateInterval
            );
        }

        // Track last update tick for progressive updates
        private static int _lastUpdateTick = -999;

        /// <summary>
        /// Reset any static data when language changes or game reloads
        /// </summary>
        public override void ResetStaticData()
        {
            _lastUpdateTick = -999;
        }
    }
}