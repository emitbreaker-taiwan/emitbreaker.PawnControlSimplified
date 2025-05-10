using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Base abstract class for all warden job modules
    /// </summary>
    public abstract class JobModule_Warden : JobModule<Pawn>
    {
        // Simple fixed priority (higher number = higher priority)
        public override float Priority => 7.2f;

        /// <summary>
        /// Gets the work type this module is associated with
        /// </summary>
        public override string WorkTypeName => "Warden";

        /// <summary>
        /// Fast skip for pawns who have Warden work disabled
        /// </summary>
        public override bool QuickFilterCheck(Pawn pawn)
            => !pawn.WorkTagIsDisabled(WorkTags.Social);

        public override bool WorkTypeApplies(Pawn pawn)
        {
            // Quick check based on work settings
            return pawn?.workSettings?.WorkIsActive(WorkTypeDefOf.Warden) == true;
        }

        /// <summary>
        /// Default cache update interval - 3 seconds for warden jobs
        /// </summary>
        public override int CacheUpdateInterval => 180; // Update every 3 seconds

        /// <summary>
        /// Relevant ThingRequestGroups for warden jobs
        /// </summary>
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups { get; } =
            new HashSet<ThingRequestGroup> { ThingRequestGroup.Pawn };

        /// <summary>
        /// Filter function to identify targets for this job (specially named for warden jobs)
        /// </summary>
        public abstract bool ShouldProcessPrisoner(Pawn prisoner);

        /// <summary>
        /// Filter function implementation that calls the warden-specific method
        /// </summary>
        public override bool ShouldProcessTarget(Pawn target, Map map)
            => ShouldProcessPrisoner(target);

        /// <summary>
        /// Validates if the warden can perform this job on the target pawn
        /// </summary>
        public abstract bool ValidateTarget(Pawn target, Pawn warden);

        /// <summary>
        /// Validates job implementation that calls the warden-specific method
        /// </summary>
        public override bool ValidateJob(Pawn target, Pawn actor)
            => ValidateTarget(target, actor);

        /// <summary>
        /// Creates the job for the warden to perform on the target pawn
        /// </summary>
        public override Job CreateJob(Pawn actor, Pawn target)
            => CreateWardenJob(actor, target);

        /// <summary>
        /// Warden-specific implementation of job creation
        /// </summary>
        protected abstract Job CreateWardenJob(Pawn warden, Pawn target);

        /// <summary>
        /// Default cache update: collect every prisoner or colonist that
        /// satisfies ShouldProcessPrisoner. Uses progressive scanning for better performance.
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
                    // Check if this is the right type of pawn for this module
                    if (HandlesColonists)
                    {
                        // For modules that work on colonists
                        if (!pawn.IsColonist || pawn.Dead || pawn.Downed)
                            return false;
                    }
                    else
                    {
                        // For modules that work on prisoners
                        if (!pawn.IsPrisoner || pawn.Dead || pawn.Downed)
                            return false;
                    }

                    // Use the module's specific logic to determine if this pawn should be processed
                    return ShouldProcessPrisoner(pawn);
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