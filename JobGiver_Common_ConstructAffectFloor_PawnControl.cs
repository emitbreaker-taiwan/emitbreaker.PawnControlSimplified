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

        // Distance thresholds for bucketing
        protected static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        /// <summary>
        /// Set work tag to Construction for eligibility checks
        /// </summary>
        protected override string WorkTag => "Construction";

        /// <summary>
        /// Override debug name for better logging
        /// </summary>
        protected override string DebugName => $"{TargetDesignation.defName} assignment";

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
        protected override int CacheUpdateInterval => CACHE_UPDATE_INTERVAL;

        // Must be implemented by subclasses to specify which designation to target
        protected override DesignationDef TargetDesignation { get; }

        // Must be implemented by subclasses to specify which job to use
        protected override JobDef WorkJobDef { get; }

        /// <summary>
        /// Cache update interval in ticks
        /// </summary>
        protected const int CACHE_UPDATE_INTERVAL = 120; // Update every 2 seconds

        #endregion

        #region Caching

        // Cache for cells with designations
        protected static readonly Dictionary<int, Dictionary<DesignationDef, List<IntVec3>>> _designatedCellsCache = new Dictionary<int, Dictionary<DesignationDef, List<IntVec3>>>();
        protected static readonly Dictionary<int, Dictionary<IntVec3, bool>> _reachabilityCache = new Dictionary<int, Dictionary<IntVec3, bool>>();
        protected static int _lastCacheUpdateTick = -999;

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

        #region Target Selection

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
        /// Standard implementation of TryGiveJob that ensures proper faction validation
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            // Use the standardized job creation pattern
            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Common_ConstructAffectFloor_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) => {
                    // Extra faction validation in case this is called directly
                    if (!IsValidFactionForConstruction(p))
                        return null;

                    // Check if map requirements are met
                    if (!AreMapRequirementsMet(p))
                        return null;

                    // Call the specialized job creation method
                    return CreateFloorConstructionJob(p, forced);
                },
                debugJobDesc: DebugName);
        }

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
        /// Checks if the map meets requirements for this floor job
        /// </summary>
        protected override bool AreMapRequirementsMet(Pawn pawn)
        {
            // Check if map has the required designations
            return pawn?.Map != null &&
                   pawn.Map.designationManager.AnySpawnedDesignationOfDef(TargetDesignation);
        }

        /// <summary>
        /// Creates a job for the specified floor construction task
        /// </summary>
        protected virtual Job CreateFloorConstructionJob(Pawn pawn, bool forced)
        {
            if (pawn?.Map == null)
                return null;

            // Update cache
            UpdateDesignatedCellsCache(pawn.Map);

            // Get cells from cache
            int mapId = pawn.Map.uniqueID;
            if (!_designatedCellsCache.ContainsKey(mapId) ||
                !_designatedCellsCache[mapId].ContainsKey(TargetDesignation) ||
                _designatedCellsCache[mapId][TargetDesignation].Count == 0)
                return null;

            // Get the list of designated cells
            List<IntVec3> cells = _designatedCellsCache[mapId][TargetDesignation];

            // Create buckets and process cells
            List<IntVec3>[] buckets = CreateDistanceBuckets(pawn, cells);

            // Find the best cell using the cell validator
            IntVec3 targetCell = FindBestCell(buckets, pawn, (cell, p) => ValidateFloorCell(cell, p, forced));

            if (!targetCell.IsValid)
                return null;

            // Create the job
            Job job = JobMaker.MakeJob(WorkJobDef, targetCell);

            Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to affect floor at {targetCell} using {TargetDesignation.defName}");
            return job;
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

            return true;
        }

        #endregion

        #region Cache Management

        /// <summary>
        /// Updates the cache of cells with floor designations
        /// </summary>
        protected void UpdateDesignatedCellsCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_designatedCellsCache.ContainsKey(mapId) ||
                !_designatedCellsCache[mapId].ContainsKey(TargetDesignation))
            {
                // Initialize cache dictionaries if needed
                if (!_designatedCellsCache.ContainsKey(mapId))
                    _designatedCellsCache[mapId] = new Dictionary<DesignationDef, List<IntVec3>>();

                if (!_designatedCellsCache[mapId].ContainsKey(TargetDesignation))
                    _designatedCellsCache[mapId][TargetDesignation] = new List<IntVec3>();
                else
                    _designatedCellsCache[mapId][TargetDesignation].Clear();

                // Clear reachability cache too
                if (!_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId] = new Dictionary<IntVec3, bool>();
                else
                    _reachabilityCache[mapId].Clear();

                // Find all cells with designations
                foreach (Designation designation in map.designationManager.SpawnedDesignationsOfDef(TargetDesignation))
                {
                    _designatedCellsCache[mapId][TargetDesignation].Add(designation.target.Cell);
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetConstructAffectFloorCache()
        {
            _designatedCellsCache.Clear();
            _reachabilityCache.Clear();
            _lastCacheUpdateTick = -999;
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