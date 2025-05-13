using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Common base class for all construction-related job givers.
    /// Handles faction validation and provides standard structure for construction tasks.
    /// </summary>
    public abstract class JobGiver_Construction_PawnControl : JobGiver_Scan_PawnControl
    {
        #region Configuration

        /// <summary>
        /// All construction job givers use this work tag
        /// </summary>
        public override string WorkTag => "Construction";

        /// <summary>
        /// Human-readable name for debug logging
        /// </summary>
        protected override string DebugName => "Construction";

        /// <summary>
        /// Standard distance thresholds for bucketing
        /// </summary>
        protected virtual float[] DistanceThresholds => new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// </summary>
        protected override bool RequiresDesignator => true;

        /// <summary>
        /// Whether this job giver requires map zone or area
        /// </summary>
        protected override bool RequiresMapZoneorArea => false;

        /// <summary>
        /// Whether this job giver requires player faction specifically (for jobs like deconstruct)
        /// </summary>
        protected override bool RequiresPlayerFaction => false;

        /// <summary>
        /// Whether this construction job requires specific tag for non-humanlike pawns
        /// </summary>
        public override PawnEnumTags RequiredTag => PawnEnumTags.AllowWork_Construction;

        /// <summary>
        /// Update cache every 4 seconds for construction tasks
        /// </summary>
        protected override int CacheUpdateInterval => 240;

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_Construction_PawnControl() : base()
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

            // Use the standardized faction validation for construction jobs
            if (!IsValidFactionForConstruction(pawn))
                return true;

            return false;
        }

        /// <summary>
        /// Determines if the pawn's faction is allowed to perform construction work
        /// Can be overridden by derived classes to customize faction rules
        /// </summary>
        protected virtual bool IsValidFactionForConstruction(Pawn pawn)
        {
            // Check if player faction is specifically required
            if (RequiresPlayerFaction)
            {
                return pawn.Faction == Faction.OfPlayer ||
                       (pawn.IsSlave && pawn.HostFaction == Faction.OfPlayer);
            }

            // For general construction jobs, pawns can work for their own faction (or host faction if slave)
            return Utility_JobGiverManager.IsValidFactionInteraction(null, pawn, RequiresDesignator);
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

            // Use the standardized job creation pattern with centralized cache
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

            // Update cache if needed using the centralized cache system
            if (ShouldUpdateCache(mapId))
            {
                UpdateCache(mapId, pawn.Map);
            }

            // Get targets from the cache
            var targets = GetCachedTargets(mapId);

            // Skip if no targets and they're required
            if ((targets == null || targets.Count == 0) && RequiresThingTargets())
                return null;

            // Call the specialized construction job creation method
            return CreateConstructionJob(pawn, forced);
        }

        /// <summary>
        /// Checks if the map meets requirements for this construction job
        /// </summary>
        protected virtual bool AreMapRequirementsMet(Pawn pawn)
        {
            // Default implementation - derived classes should override this
            return pawn?.Map != null;
        }

        #endregion

        #region Target Selection

        /// <summary>
        /// Job-specific cache update method that implements specialized target collection logic
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            // Default implementation - derived classes should override this
            return GetTargets(map);
        }

        /// <summary>
        /// Gets all potential targets on the given map - derived classes must implement
        /// </summary>
        protected abstract override IEnumerable<Thing> GetTargets(Map map);

        /// <summary>
        /// Creates a job for the pawn using the cached targets
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // Skip if pawn invalid or doesn't meet requirements
            if (pawn == null || !IsValidFactionForConstruction(pawn) || !HasRequiredCapabilities(pawn))
                return null;

            // Skip if no targets and they're required
            if ((targets == null || targets.Count == 0) && RequiresThingTargets())
                return null;

            // Call the specialized job creation method
            return CreateConstructionJob(pawn, forced);
        }

        /// <summary>
        /// Whether this job giver requires Thing targets or uses cell-based targets
        /// </summary>
        protected virtual bool RequiresThingTargets()
        {
            // Most cell-based construction job givers should override this to return false
            return true;
        }

        #endregion

        #region Job Creation

        /// <summary>
        /// Implement to create the specific construction job
        /// </summary>
        protected abstract Job CreateConstructionJob(Pawn pawn, bool forced);

        #endregion

        #region Cell-Based Helpers

        /// <summary>
        /// Creates distance buckets for IntVec3 cells using the centralized bucket system
        /// </summary>
        protected List<IntVec3>[] CreateDistanceBuckets(Pawn pawn, IEnumerable<IntVec3> cells)
        {
            if (pawn == null || cells == null)
                return null;

            List<IntVec3>[] buckets = new List<IntVec3>[DistanceThresholds.Length + 1];
            for (int i = 0; i < buckets.Length; i++)
                buckets[i] = new List<IntVec3>();

            foreach (IntVec3 cell in cells)
            {
                float distanceSq = (cell - pawn.Position).LengthHorizontalSquared;

                int bucketIndex = buckets.Length - 1;
                for (int i = 0; i < DistanceThresholds.Length; i++)
                {
                    if (distanceSq <= DistanceThresholds[i])
                    {
                        bucketIndex = i;
                        break;
                    }
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

        /// <summary>
        /// Basic cell validation for construction cells
        /// </summary>
        protected virtual bool ValidateConstructionCell(IntVec3 cell, Pawn pawn, bool forced = false)
        {
            // Skip if not valid
            if (!IsValidCell(cell, pawn.Map))
                return false;

            // Skip if forbidden or unreachable
            if (cell.IsForbidden(pawn) ||
                !pawn.CanReserve(cell) ||
                !pawn.CanReach(cell, PathEndMode.Touch, pawn.NormalMaxDanger()))
                return false;

            return true;
        }

        #endregion

        #region Thing-Based Helpers

        /// <summary>
        /// Basic validation for construction target things
        /// </summary>
        protected virtual bool ValidateConstructionTarget(Thing thing, Pawn pawn, bool forced = false)
        {
            // Skip if no longer valid
            if (thing == null || thing.Destroyed || !thing.Spawned)
                return false;

            // Skip if forbidden or unreachable
            if (thing.IsForbidden(pawn) ||
                !pawn.CanReserve(thing, 1, -1) ||
                !pawn.CanReach(thing, PathEndMode.Touch, Danger.Some))
                return false;

            return true;
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

        /// <summary>
        /// Check if thing is reserved by another pawn
        /// </summary>
        protected bool IsThingReservedByAnother(Pawn pawn, Thing thing, JobDef jobDef)
        {
            if (pawn?.Map?.mapPawns?.FreeColonistsSpawned == null || thing == null)
                return false;

            List<Pawn> pawns = pawn.Map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                if (pawns[i] != pawn && pawns[i].CurJobDef == jobDef)
                {
                    LocalTargetInfo target = pawns[i].CurJob?.GetTarget(TargetIndex.A) ?? default;
                    if (target.IsValid && target.Thing == thing)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public override string ToString()
        {
            return $"JobGiver_Common_Construction_PawnControl({DebugName})";
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
    }
}