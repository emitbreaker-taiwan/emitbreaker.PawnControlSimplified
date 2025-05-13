using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Abstract base class for JobGivers that affect floors (smooth, remove, etc.)
    /// Uses the Construction work tag for eligibility checking.
    /// </summary>
    public abstract class JobGiver_Common_ConstructAffectFloor_PawnControl : JobGiver_Construction_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Set work tag to Construction for eligibility checks
        /// </summary>
        public override string WorkTag => "Construction";

        /// <summary>
        /// Override debug name for better logging
        /// </summary>
        protected override string DebugName => $"{TargetDesignation?.defName} assignment";

        /// <summary>
        /// Whether this job giver requires player faction (always true for designations)
        /// </summary>
        protected override bool RequiresPlayerFaction => true;

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        protected override bool RequiresMapZoneorArea => true;

        /// <summary>
        /// Override cache interval
        /// </summary>
        protected override int CacheUpdateInterval => 120; // Update every 2 seconds

        /// <summary>
        /// Standard distance thresholds for floor construction bucketing
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        // Must be implemented by subclasses to specify which designation to target
        protected abstract override DesignationDef TargetDesignation { get; }

        // Must be implemented by subclasses to specify which job to use
        protected abstract override JobDef WorkJobDef { get; }

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_Common_ConstructAffectFloor_PawnControl() : base()
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

            // Check faction validation - floor construction requires player/slave pawns
            if (!IsValidFactionForConstruction(pawn))
                return true;

            return false;
        }

        /// <summary>
        /// Determines if the pawn's faction is allowed to perform construction work
        /// Can be overridden by derived classes to customize faction rules
        /// </summary>
        protected override bool IsValidFactionForConstruction(Pawn pawn)
        {
            // For floor construction jobs, only player pawns or player's slaves should perform them
            return Utility_JobGiverManager.IsValidFactionInteraction(null, pawn, RequiresPlayerFaction);
        }

        #endregion

        #region Core Flow

        /// <summary>
        /// Standard implementation of TryGiveJob that ensures proper faction validation
        /// and checks map requirements before proceeding with job creation
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            // Skip if already filtered out by base class
            if (ShouldSkip(pawn))
                return null;

            // Skip if map requirements not met
            if (!AreMapRequirementsMet(pawn))
                return null;

            // Use the standard job creation flow from base
            return base.TryGiveJob(pawn);
        }

        /// <summary>
        /// Template method for creating a job that handles cache update logic
        /// </summary>
        protected override Job CreateJobFor(Pawn pawn, bool forced)
        {
            if (pawn?.Map == null)
                return null;

            int mapId = pawn.Map.uniqueID;

            if (!ShouldExecuteNow(mapId))
                return null;

            // Create the floor construction job directly
            return CreateFloorConstructionJob(pawn, forced);
        }

        /// <summary>
        /// Checks if the map meets requirements for this floor job
        /// </summary>
        protected override bool AreMapRequirementsMet(Pawn pawn)
        {
            // Check if map has the required designations
            return pawn?.Map != null &&
                   TargetDesignation != null &&
                   pawn.Map.designationManager.AnySpawnedDesignationOfDef(TargetDesignation);
        }

        #endregion

        #region Target Selection

        /// <summary>
        /// Job-specific cache update method for floor cells
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            // Floor JobGivers work with cells, not things
            return Enumerable.Empty<Thing>();
        }

        /// <summary>
        /// Get targets - in this case we return an empty collection since we work with cells
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // Floor JobGivers work with cells, not things
            return Enumerable.Empty<Thing>();
        }

        /// <summary>
        /// Whether this job giver requires Thing targets or uses cell-based targets
        /// </summary>
        protected override bool RequiresThingTargets()
        {
            // Floor construction jobs are cell-based, not thing-based
            return false;
        }

        #endregion

        #region Job Creation

        /// <summary>
        /// Creates a job for the pawn using the cached targets
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // Skip if pawn invalid
            if (pawn == null || !IsValidFactionForConstruction(pawn))
                return null;

            // Call the specialized job creation method - ignore thing targets since we work with cells
            return CreateFloorConstructionJob(pawn, forced);
        }

        /// <summary>
        /// Creates a job for the specified floor construction task
        /// </summary>
        protected virtual Job CreateFloorConstructionJob(Pawn pawn, bool forced)
        {
            if (pawn?.Map == null || TargetDesignation == null)
                return null;

            // Get all cells with the target designation
            List<IntVec3> cells = GetDesignatedCells(pawn.Map);
            if (cells.Count == 0)
                return null;

            // Create buckets and process cells
            List<IntVec3>[] buckets = CreateDistanceBuckets(pawn, cells);
            if (buckets == null)
                return null;

            // Find the best cell using the cell validator
            IntVec3 targetCell = FindBestCell(buckets, pawn, (cell, p) => ValidateFloorCell(cell, p, forced));

            if (!targetCell.IsValid)
                return null;

            // Create the job
            Job job = JobMaker.MakeJob(WorkJobDef, targetCell);

            Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to affect floor at {targetCell} using {TargetDesignation.defName}");
            return job;
        }

        /// <summary>
        /// Implement to create the specific construction job
        /// </summary>
        protected override Job CreateConstructionJob(Pawn pawn, bool forced)
        {
            // Just delegate to our specific implementation
            return CreateFloorConstructionJob(pawn, forced);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Gets all cells with the target designation
        /// </summary>
        protected List<IntVec3> GetDesignatedCells(Map map)
        {
            if (map == null || TargetDesignation == null)
                return new List<IntVec3>();

            List<IntVec3> cells = new List<IntVec3>();
            foreach (Designation designation in map.designationManager.SpawnedDesignationsOfDef(TargetDesignation))
            {
                cells.Add(designation.target.Cell);
            }

            return LimitListSize(cells);
        }

        #endregion

        #region Validation

        /// <summary>
        /// Validates if a floor cell can be affected by this job
        /// </summary>
        protected virtual bool ValidateFloorCell(IntVec3 cell, Pawn pawn, bool forced)
        {
            if (!IsValidCell(cell, pawn?.Map))
                return false;

            // Check if still designated
            if (pawn.Map.designationManager.DesignationAt(cell, TargetDesignation) == null)
                return false;

            // Check if accessible
            if (cell.IsForbidden(pawn) ||
                !pawn.CanReserve(cell, 1, -1, null, forced) ||
                !pawn.CanReach(cell, PathEndMode.Touch, Danger.Some))
                return false;

            // Check if cell is already reserved by another pawn
            if (IsCellReservedByAnother(pawn, cell, WorkJobDef))
                return false;

            return true;
        }

        #endregion

        #region Reset

        /// <summary>
        /// Reset the cache - implements IResettableCache
        /// </summary>
        public override void Reset()
        {
            // Use centralized cache reset
            base.Reset();
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return $"JobGiver_ConstructAffectFloor_PawnControl({TargetDesignation?.defName ?? "null"})";
        }

        #endregion
    }
}