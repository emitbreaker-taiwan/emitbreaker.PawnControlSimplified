using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Base abstract class for all basic worker job modules
    /// These are typically simple designation-based tasks like flicking, opening containers, extracting skulls, etc.
    /// </summary>
    public abstract class JobModule_BasicWorker : JobModule<Thing>
    {
        // Simple fixed priority (higher number = higher priority)
        public override float Priority => 7.8f; // higher priority

        /// <summary>
        /// Gets the work type this module is associated with
        /// </summary>
        public override string WorkTypeName => "BasicWorker";

        /// <summary>
        /// Fast filter check for basic workers
        /// </summary>
        public override bool QuickFilterCheck(Pawn pawn)
        {
            return !pawn.WorkTagIsDisabled(WorkTags.ManualSkilled);
        }

        public override bool WorkTypeApplies(Pawn pawn)
        {
            // Most basic worker tasks don't have a dedicated work type
            // They use the "miscellaneous" work tab that's available to all pawns
            return true;
        }

        /// <summary>
        /// Default cache update interval - 5 seconds for basic worker jobs
        /// </summary>
        public override int CacheUpdateInterval => 300; // Update every 5 seconds

        /// <summary>
        /// The designation def this job module handles, if applicable
        /// </summary>
        protected virtual DesignationDef TargetDesignation => null;

        /// <summary>
        /// Filter function to identify targets for this job
        /// </summary>
        public abstract bool ShouldProcessBasicTarget(Thing target, Map map);

        /// <summary>
        /// Filter function implementation that calls the basic-worker-specific method
        /// </summary>
        public override bool ShouldProcessTarget(Thing target, Map map)
            => ShouldProcessBasicTarget(target, map);

        /// <summary>
        /// Validates if the pawn can perform this job on the target
        /// </summary>
        public abstract bool ValidateBasicWorkerJob(Thing target, Pawn worker);

        /// <summary>
        /// Validates job implementation that calls the basic-worker-specific method
        /// </summary>
        public override bool ValidateJob(Thing target, Pawn actor)
            => ValidateBasicWorkerJob(target, actor);

        /// <summary>
        /// Creates the job for the worker to perform on the target
        /// </summary>
        public override Job CreateJob(Pawn actor, Thing target)
            => CreateBasicWorkerJob(actor, target);

        /// <summary>
        /// Basic-worker-specific implementation of job creation
        /// </summary>
        protected abstract Job CreateBasicWorkerJob(Pawn worker, Thing target);

        /// <summary>
        /// Helper method to check if a target has the appropriate designation
        /// </summary>
        protected bool HasTargetDesignation(Thing thing, Map map)
        {
            if (thing == null || !thing.Spawned || map == null || TargetDesignation == null)
                return false;

            return map.designationManager.DesignationOn(thing, TargetDesignation) != null;
        }

        /// <summary>
        /// Helper method to check if pawn can do work on the target
        /// </summary>
        protected bool CanWorkOn(Thing target, Pawn worker, PathEndMode pathMode = PathEndMode.Touch, Danger maxDanger = Danger.Deadly)
        {
            if (worker == null || target == null || !target.Spawned || worker.Map != target.Map)
                return false;

            // Skip if target is forbidden
            if (target.IsForbidden(worker))
                return false;

            // Skip if target is claimed by someone else
            if (!worker.CanReserve(target))
                return false;

            // Check if pawn can reach the target
            return worker.CanReach(target, pathMode, maxDanger);
        }

        /// <summary>
        /// Default implementation for designation-based job modules
        /// </summary>
        public override void UpdateCache(Map map, List<Thing> targetCache)
        {
            if (map == null || TargetDesignation == null) return;

            int currentTick = Find.TickManager.TicksGame;
            if (currentTick <= _lastUpdateTick + CacheUpdateInterval && targetCache.Count > 0)
                return;

            targetCache.Clear();

            // Get all designated targets
            foreach (Designation des in map.designationManager.SpawnedDesignationsOfDef(TargetDesignation))
            {
                Thing thing = des.target.Thing;
                if (thing != null && !thing.Destroyed && ShouldProcessBasicTarget(thing, map))
                {
                    targetCache.Add(thing);
                }
            }

            // Sort by importance if applicable
            if (targetCache.Count > 0 && CanPrioritizeTargets())
            {
                PrioritizeTargets(targetCache);
            }

            // Cap cache size for performance
            if (targetCache.Count > 200)
            {
                targetCache.RemoveRange(200, targetCache.Count - 200);
            }

            _lastUpdateTick = currentTick;
        }

        /// <summary>
        /// Override to implement custom target prioritization
        /// </summary>
        protected virtual bool CanPrioritizeTargets() => false;

        /// <summary>
        /// Override to implement custom target prioritization
        /// </summary>
        protected virtual void PrioritizeTargets(List<Thing> targets) { }

        /// <summary>
        /// ThingRequestGroups to scan for targets
        /// Default implementation returns null since most basic worker jobs use designations
        /// </summary>
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups => null;

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