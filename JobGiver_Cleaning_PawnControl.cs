using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Common base class for all cleaning-related job givers.
    /// Handles faction validation and provides standard structure for cleaning tasks.
    /// </summary>
    public abstract class JobGiver_Cleaning_PawnControl : JobGiver_Scan_PawnControl
    {
        #region Configuration

        /// <summary>
        /// All cleaning job givers use this work tag
        /// </summary>
        public override string WorkTag => "Cleaning";

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "Cleaning";

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        protected override bool RequiresMapZoneorArea => false;

        /// <summary>
        /// Whether this job giver requires player faction specifically (for jobs like deconstruct)
        /// </summary>
        protected override bool RequiresPlayerFaction => true;

        /// <summary>
        /// Whether this construction job requires specific tag for non-humanlike pawns
        /// </summary>
        public override PawnEnumTags RequiredTag => PawnEnumTags.AllowWork_Cleaning;

        /// <summary>
        /// Standard distance thresholds for bucketing
        /// </summary>
        protected virtual float[] DistanceThresholds => new float[] { 100f, 400f, 1600f }; // 10, 20, 40 tiles

        /// <summary>
        /// Maximum number of items to process per job
        /// </summary>
        protected virtual int MaxItemsPerJob => 15;

        /// <summary>
        /// Cache update interval - update cleaning targets frequently
        /// </summary>
        protected override int CacheUpdateInterval => 180; // Every 3 seconds

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_Cleaning_PawnControl() : base()
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

            // Use the standardized faction validation - cleaning requires player/slave pawns
            if (!IsValidFactionForCleaning(pawn))
                return true;

            return false;
        }

        /// <summary>
        /// Determines if the pawn's faction is allowed to perform cleaning work
        /// Can be overridden by derived classes to customize faction rules
        /// </summary>
        protected virtual bool IsValidFactionForCleaning(Pawn pawn)
        {
            // For cleaning jobs, only player pawns or player's slaves should perform them
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
            // Skip if already filtered out by parent class
            if (ShouldSkip(pawn))
                return null;

            // Skip if map requirements not met
            if (!AreMapRequirementsMet(pawn))
                return null;

            // Proceed with normal job creation
            return base.TryGiveJob(pawn);
        }

        /// <summary>
        /// Creates a job for the pawn using the cached targets
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // Skip if pawn invalid or faction not valid
            if (pawn == null || !IsValidFactionForCleaning(pawn))
                return null;

            // Skip if no targets and they're required
            if ((targets == null || targets.Count == 0) && RequiresThingTargets())
                return null;

            // Create job using the specialized cleaning job creation method
            return CreateCleaningJob(pawn, forced);
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

            // Update cache if needed using the centralized cache system
            if (ShouldUpdateCache(mapId))
            {
                UpdateCache(mapId, pawn.Map);
            }

            // Get targets from the cache
            var targets = GetCachedTargets(mapId);

            // Call the specialized cleaning job creation method
            return CreateCleaningJob(pawn, forced);
        }

        /// <summary>
        /// Checks if the map meets requirements for this job giver
        /// </summary>
        protected virtual bool AreMapRequirementsMet(Pawn pawn)
        {
            // Default implementation - derived classes should override this
            return pawn?.Map != null;
        }

        #endregion

        #region Target selection

        /// <summary>
        /// Job-specific cache update method that derived classes should implement
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            // Use the base implementation by default
            return GetTargets(map);
        }

        /// <summary>
        /// Gets all potential targets on the given map - derived classes must implement
        /// </summary>
        protected abstract override IEnumerable<Thing> GetTargets(Map map);

        /// <summary>
        /// Whether this job giver requires Thing targets or uses cell-based targets
        /// </summary>
        protected virtual bool RequiresThingTargets()
        {
            // Most cell-based cleaning job givers should override this to return false
            return true;
        }

        #endregion

        #region Job creation

        /// <summary>
        /// Implement to create the specific cleaning job
        /// </summary>
        protected abstract Job CreateCleaningJob(Pawn pawn, bool forced);

        #endregion

        #region Bucket Helpers

        /// <summary>
        /// Creates distance buckets for IntVec3 cells using the centralized bucket system
        /// </summary>
        protected List<IntVec3>[] CreateDistanceBuckets(Pawn pawn, IEnumerable<IntVec3> cells)
        {
            if (pawn == null || cells == null)
                return null;

            // Initialize buckets based on distance thresholds
            List<IntVec3>[] buckets = new List<IntVec3>[DistanceThresholds.Length + 1];
            for (int i = 0; i < buckets.Length; i++)
                buckets[i] = new List<IntVec3>();

            // Distribute cells into buckets based on distance
            foreach (IntVec3 cell in cells)
            {
                float distanceSq = (cell - pawn.Position).LengthHorizontalSquared;

                int bucketIndex = 0;
                while (bucketIndex < DistanceThresholds.Length && distanceSq > DistanceThresholds[bucketIndex])
                {
                    bucketIndex++;
                }

                buckets[bucketIndex].Add(cell);
            }

            return buckets;
        }

        /// <summary>
        /// Finds the closest valid cell from bucketed cells
        /// </summary>
        protected IntVec3 FindBestCell(List<IntVec3>[] buckets, Pawn pawn, Func<IntVec3, Pawn, bool> validator)
        {
            if (buckets == null || pawn == null || validator == null)
                return IntVec3.Invalid;

            for (int b = 0; b < buckets.Length; b++)
            {
                if (buckets[b].Count == 0)
                    continue;

                // Randomize within each bucket for even distribution
                buckets[b].Shuffle();

                foreach (IntVec3 cell in buckets[b])
                {
                    if (validator(cell, pawn))
                    {
                        return cell;
                    }
                }
            }

            return IntVec3.Invalid;
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

        /// <summary>
        /// Consistency check on cell validity
        /// </summary>
        protected bool IsValidCell(IntVec3 cell, Map map)
        {
            return cell.IsValid && map != null;
        }

        /// <summary>
        /// Check if cell is reserved by another pawn
        /// </summary>
        protected bool IsCellReservedByAnother(Pawn pawn, IntVec3 cell, JobDef jobDef)
        {
            if (pawn?.Map?.mapPawns?.FreeColonistsSpawned == null)
                return false;

            List<Pawn> pawns = pawn.Map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                if (pawns[i] != pawn && pawns[i].CurJobDef == jobDef)
                {
                    LocalTargetInfo target = pawns[i].CurJob?.GetTarget(TargetIndex.A) ?? default;
                    if (target.IsValid && target.Cell == cell)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        #endregion

        #region Reset

        /// <summary>
        /// Reset the cache - implements IResettableCache
        /// </summary>
        public override void Reset()
        {
            // Use the centralized cache reset
            base.Reset();
        }

        #endregion
    }
}