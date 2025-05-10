using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Base abstract class for all firefighting job modules
    /// </summary>
    public abstract class JobModule_Firefighter : JobModule<Fire>
    {
        // Simple fixed priority (higher number = higher priority)
        public override float Priority => 9.0f;

        /// <summary>
        /// Gets the work type this module is associated with
        /// </summary>
        public override string WorkTypeName => "Firefighter";

        /// <summary>
        /// Fast filter check for firefighters
        /// </summary>
        public override bool QuickFilterCheck(Pawn pawn)
        {
            return !pawn.WorkTagIsDisabled(WorkTags.Firefighting);
        }

        public override bool WorkTypeApplies(Pawn pawn)
        {
            // Quick check based on work settings
            return pawn?.workSettings?.WorkIsActive(WorkTypeDefOf.Firefighter) == true;
        }

        /// <summary>
        /// Default cache update interval - 1 second for firefighting jobs (more frequent than others due to urgency)
        /// </summary>
        public override int CacheUpdateInterval => 60; // Update every 1 second

        /// <summary>
        /// Relevant ThingRequestGroups for firefighting jobs
        /// </summary>
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups { get; } =
            new HashSet<ThingRequestGroup> { ThingRequestGroup.Fire };

        /// <summary>
        /// The distance threshold for considering fires being handled
        /// </summary>
        protected const float HANDLED_DISTANCE = 5f; // Same as original WorkGiver

        /// <summary>
        /// The radius to check for pawns on fire outside home area
        /// </summary>
        protected const int NEARBY_PAWN_RADIUS = 15; // Same as original WorkGiver

        /// <summary>
        /// Filter function to identify targets for this job (specifically named for firefighting jobs)
        /// </summary>
        public abstract bool ShouldProcessFire(Fire fire, Map map);

        /// <summary>
        /// Filter function implementation that calls the firefighting-specific method
        /// </summary>
        public override bool ShouldProcessTarget(Fire target, Map map)
            => ShouldProcessFire(target, map);

        /// <summary>
        /// Validates if the pawn can perform this job on the target
        /// </summary>
        public abstract bool ValidateFirefightingJob(Fire target, Pawn firefighter);

        /// <summary>
        /// Validates job implementation that calls the firefighting-specific method
        /// </summary>
        public override bool ValidateJob(Fire target, Pawn actor)
            => ValidateFirefightingJob(target, actor);

        /// <summary>
        /// Creates the job for the pawn to perform on the target fire
        /// </summary>
        public override Job CreateJob(Pawn actor, Fire target)
            => CreateFirefightingJob(actor, target);

        /// <summary>
        /// Firefighting-specific implementation of job creation
        /// </summary>
        protected abstract Job CreateFirefightingJob(Pawn firefighter, Fire fire);

        /// <summary>
        /// Helper method to check if a fire is being handled by another pawn
        /// </summary>
        protected bool FireIsBeingHandled(Fire fire, Pawn potentialHandler)
        {
            if (!fire.Spawned)
                return false;

            Pawn pawn = fire.Map.reservationManager.FirstRespectedReserver(fire, potentialHandler);
            return pawn != null && pawn.Position.InHorDistOf(fire.Position, HANDLED_DISTANCE);
        }

        /// <summary>
        /// Helper method to check if pawn can fight a fire
        /// </summary>
        protected bool CanFightFire(Fire fire, Pawn firefighter)
        {
            if (fire == null || firefighter == null || !fire.Spawned || !firefighter.Spawned || firefighter.Map != fire.Map)
                return false;

            // Skip if fire is forbidden
            if (fire.IsForbidden(firefighter))
                return false;

            // Skip if fire is claimed by someone else
            if (!firefighter.CanReserve(fire))
                return false;

            // Skip if already being handled
            if (FireIsBeingHandled(fire, firefighter))
                return false;

            // Skip fires outside home area unless on pawns
            if (fire.parent as Pawn != null && !firefighter.Map.areaManager.Home[fire.Position])
                return false;

            // Special handling for fires on pawns
            if (fire.parent is Pawn parentPawn)
            {
                // Skip if the burning pawn is the firefighter
                if (parentPawn == firefighter)
                    return false;

                // Skip if the burning pawn is an enemy
                if ((parentPawn.Faction == null || parentPawn.Faction != firefighter.Faction) &&
                    (parentPawn.HostFaction == null || (parentPawn.HostFaction != firefighter.Faction && parentPawn.HostFaction != firefighter.HostFaction)))
                    return false;

                // Skip distant burning pawns outside home area
                if (!firefighter.Map.areaManager.Home[fire.Position] &&
                    IntVec3Utility.ManhattanDistanceFlat(firefighter.Position, parentPawn.Position) > NEARBY_PAWN_RADIUS)
                    return false;

                // Skip unreachable pawn fires
                if (!firefighter.CanReach(parentPawn, PathEndMode.Touch, Danger.Deadly))
                    return false;
            }

            // Check if firefighter can reach the fire
            return firefighter.CanReach(fire, PathEndMode.Touch, firefighter.NormalMaxDanger());
        }

        /// <summary>
        /// Default cache update: collect every fire that
        /// satisfies ShouldProcessFire. Uses progressive scanning for better performance.
        /// </summary>
        public override void UpdateCache(Map map, List<Fire> targetCache)
        {
            if (map == null) return;

            // Use progressive cache update with the appropriate filter
            UpdateCacheProgressively(
                map,
                targetCache,
                ref _lastUpdateTick,
                RelevantThingRequestGroups,
                fire => ShouldProcessFire(fire, map),
                null,
                CacheUpdateInterval
            );

            // Cap cache size for performance
            if (targetCache.Count > 500)
            {
                targetCache.RemoveRange(500, targetCache.Count - 500);
            }
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