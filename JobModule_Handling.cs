using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Base abstract class for all animal handling job modules
    /// </summary>
    public abstract class JobModule_Handling : JobModule<Pawn>
    {
        // Simple fixed priority (higher number = higher priority)
        public override float Priority => 7.0f;

        /// <summary>
        /// Gets the work type this module is associated with
        /// </summary>
        public override string WorkTypeName => "Handling";

        /// <summary>
        /// Fast filter check for animal handlers
        /// </summary>
        public override bool QuickFilterCheck(Pawn pawn)
        {
            return !pawn.WorkTagIsDisabled(WorkTags.Animals);
        }

        public override bool WorkTypeApplies(Pawn pawn)
        {
            // Quick check based on work settings
            return pawn?.workSettings?.WorkIsActive(WorkTypeDefOf.Handling) == true;
        }

        /// <summary>
        /// Default cache update interval - 5 seconds for handling jobs
        /// </summary>
        public override int CacheUpdateInterval => 300; // Update every 5 seconds

        /// <summary>
        /// Relevant ThingRequestGroups for handling jobs
        /// </summary>
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups { get; } =
            new HashSet<ThingRequestGroup> { ThingRequestGroup.HaulableEver };

        /// <summary>
        /// Filter function to identify targets for this job (specially named for handling jobs)
        /// </summary>
        public abstract bool ShouldProcessAnimal(Pawn animal, Map map);

        /// <summary>
        /// Filter function implementation that calls the handling-specific method
        /// </summary>
        public override bool ShouldProcessTarget(Pawn target, Map map)
            => ShouldProcessAnimal(target, map);

        /// <summary>
        /// Validates if the handler can perform this job on the target animal
        /// </summary>
        public abstract bool ValidateHandlingJob(Pawn animal, Pawn handler);

        /// <summary>
        /// Validates job implementation that calls the handling-specific method
        /// </summary>
        public override bool ValidateJob(Pawn target, Pawn actor)
            => ValidateHandlingJob(target, actor);

        /// <summary>
        /// Creates the job for the handler to perform on the target animal
        /// </summary>
        public override Job CreateJob(Pawn actor, Pawn target)
            => CreateHandlingJob(actor, target);

        /// <summary>
        /// Handling-specific implementation of job creation
        /// </summary>
        protected abstract Job CreateHandlingJob(Pawn handler, Pawn animal);

        /// <summary>
        /// Helper method to check if an animal is valid for handling
        /// </summary>
        protected bool IsValidHandlingTarget(Pawn animal, Pawn handler)
        {
            if (animal == null || handler == null || !animal.Spawned || !handler.Spawned)
                return false;
                
            // Must be an animal
            if (!animal.RaceProps.Animal)
                return false;
                
            // Skip forbidden animals
            if (animal.IsForbidden(handler))
                return false;
                
            // Skip animals that can't be reserved
            if (!handler.CanReserveAndReach(animal, PathEndMode.Touch, handler.NormalMaxDanger()))
                return false;
                
            return true;
        }

        /// <summary>
        /// Helper method to check if a handler has enough skill for the animal
        /// </summary>
        protected bool HasEnoughHandlingSkill(Pawn animal, Pawn handler, int requiredSkill = 0)
        {
            if (animal == null || handler == null) return false;
            
            // Get handler's animals skill
            int handlerSkill = handler.skills?.GetSkill(SkillDefOf.Animals)?.Level ?? 0;
            
            // Check if animal has minimum handling skill requirement
            if (requiredSkill > 0 && handlerSkill < requiredSkill)
                return false;
                
            // Check wildness vs skill requirement
            if (animal.RaceProps.wildness > 0)
            {
                float wildnessSkillFactor = animal.RaceProps.wildness * 10f;
                if (handlerSkill < wildnessSkillFactor)
                    return false;
            }
            
            return true;
        }

        /// <summary>
        /// Default cache update: collect every animal that satisfies ShouldProcessAnimal.
        /// Uses progressive scanning for better performance.
        /// </summary>
        public override void UpdateCache(Map map, List<Pawn> targetCache)
        {
            if (map == null) return;

            // Use progressive cache update with the appropriate filter
            UpdateCacheProgressively(
                map,
                targetCache,
                ref _lastUpdateTick,
                RelevantThingRequestGroups,
                pawn => {
                    // Only process animals
                    if (pawn == null || !pawn.Spawned || pawn.Dead || !pawn.RaceProps.Animal)
                        return false;
                        
                    // Use the module's specific logic to determine if this animal should be processed
                    return ShouldProcessAnimal(pawn, map);
                },
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