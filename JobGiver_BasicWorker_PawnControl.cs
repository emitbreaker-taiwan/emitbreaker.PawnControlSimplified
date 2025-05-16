using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Base class for basic worker job givers with specialized cache management.
    /// Handles common designation-based target finding and caching.
    /// </summary>
    public abstract class JobGiver_BasicWorker_PawnControl : JobGiver_Scan_PawnControl
    {
        #region Configuration

        /// <summary>
        /// All BasicWorker job givers share this work tag
        /// </summary>
        public override string WorkTag => "BasicWorker";

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        public override bool RequiresMapZoneorArea => false;

        /// <summary>
        /// Whether this job giver requires player faction (default true for designation-based jobs)
        /// </summary>
        public override bool RequiresPlayerFaction => true;

        /// <summary>
        /// Whether this construction job requires specific tag for non-humanlike pawns
        /// </summary>
        public override PawnEnumTags RequiredTag => PawnEnumTags.AllowWork_BasicWorker;

        /// <summary>
        /// Update cache every 5 seconds by default
        /// </summary>
        protected override int CacheUpdateInterval => base.CacheUpdateInterval;

        /// <summary>
        /// Default distance thresholds for bucketing (20, 40, 50 tiles)
        /// </summary>
        protected virtual float[] DistanceThresholds => new float[] { 400f, 1600f, 2500f };

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_BasicWorker_PawnControl() : base()
        {
            // Base constructor already initializes the cache system
        }

        #endregion

        #region Faction Validation

        /// <summary>
        /// Common implementation for ShouldSkip that enforces faction requirements
        /// </summary>
        public override bool ShouldSkip(Pawn pawn)
        {
            if (base.ShouldSkip(pawn))
                return true;

            // Check faction validation
            if (!IsValidFactionForDesignationWork(pawn))
                return true;

            return false;
        }

        /// <summary>
        /// Determines if the pawn's faction is allowed to perform designation-based work
        /// Can be overridden by derived classes to customize faction rules
        /// </summary>
        protected virtual bool IsValidFactionForDesignationWork(Pawn pawn)
        {
            // For designation-based jobs, only player pawns or player's slaves should perform them
            return Utility_JobGiverManager.IsValidFactionInteraction(null, pawn, RequiresDesignator);
        }

        #endregion

        #region Core flow

        /// <summary>
        /// Standard implementation of TryGiveJob that ensures proper faction validation
        /// and checks map requirements before proceeding with job creation
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            // Skip if map doesn't meet the requirements for this job type
            if (pawn?.Map == null || !AreMapRequirementsMet(pawn))
                return null;

            if (ShouldSkip(pawn))
            {
                return null;
            }

            // Proceed with standard job creation flow from parent
            return base.TryGiveJob(pawn);
        }

        /// <summary>
        /// Creates a job for the pawn using the cached targets
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // Skip if pawn invalid or no targets
            if (pawn == null || !IsValidFactionForDesignationWork(pawn) || targets == null || targets.Count == 0)
                return null;

            // Create job for the best target
            return CreateBasicWorkerJob(pawn, targets, forced);
        }

        /// <summary>
        /// Checks if the map meets requirements for this job giver
        /// </summary>
        protected virtual bool AreMapRequirementsMet(Pawn pawn)
        {
            // Check if map has the required designations
            return pawn?.Map != null &&
                   pawn.Map.designationManager.SpawnedDesignationsOfDef(TargetDesignation).Any();
        }

        /// <summary>
        /// Creates a job for the basic worker task based on targets
        /// </summary>
        protected virtual Job CreateBasicWorkerJob(Pawn pawn, List<Thing> targets, bool forced)
        {
            if (pawn == null || targets == null || targets.Count == 0)
                return null;

            // Use bucketing system to find closest valid target
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                targets,
                thing => (thing.Position - pawn.Position).LengthHorizontalSquared,
                DistanceThresholds);

            // Find the first valid target using the centralized cache system
            Thing bestTarget = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets, pawn, IsValidTarget, null); // Use parent's reachability caching

            // Create a job for the target if found
            if (bestTarget != null)
            {
                return CreateJobForTarget(bestTarget);
            }

            return null;
        }

        #endregion

        #region Target selection

        /// <summary>
        /// Gets all targets with the specified designation
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // Return all things with the specified designation
            if (map?.designationManager != null)
            {
                foreach (Designation designation in map.designationManager.SpawnedDesignationsOfDef(TargetDesignation))
                {
                    if (designation?.target.Thing != null && designation.target.Thing.Spawned)
                    {
                        yield return designation.target.Thing;
                    }
                }
            }
        }

        /// <summary>
        /// Determines if a target is valid for the job
        /// </summary>
        protected virtual bool IsValidTarget(Thing thing, Pawn worker)
        {
            return !thing.IsForbidden(worker) &&
                   worker.CanReserveAndReach(thing, PathEndMode.ClosestTouch, Danger.Deadly);
        }

        /// <summary>
        /// Creates a job for the specified target
        /// </summary>
        protected virtual Job CreateJobForTarget(Thing target)
        {
            return JobMaker.MakeJob(WorkJobDef, target);
        }

        #endregion

        #region Reset

        /// <summary>
        /// Reset the cache - implements IResettableCache
        /// </summary>
        public override void Reset()
        {
            base.Reset();
        }

        #endregion

        #region Utility

        /// <summary>
        /// Sanitizes a list by limiting its size to avoid performance issues
        /// </summary>
        protected List<T> LimitListSize<T>(List<T> list, int maxSize = 1000)
        {
            if (list == null || list.Count <= maxSize)
                return list;

            return list.Take(maxSize).ToList();
        }

        #endregion
    }
}