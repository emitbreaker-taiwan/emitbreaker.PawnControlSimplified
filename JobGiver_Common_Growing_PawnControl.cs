using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Abstract base class for JobGivers that handle growing activities.
    /// </summary>
    public abstract class JobGiver_Common_Growing_PawnControl : JobGiver_Scan_PawnControl
    {
        #region Configuration

        // Cache for plant growing targets
        protected static readonly List<IntVec3> _targetCells = new List<IntVec3>();
        protected static readonly List<IPlantToGrowSettable> _targetZonesAndGrowers = new List<IPlantToGrowSettable>();
        protected static ThingDef _wantedPlantDef;

        // Distance thresholds for bucketing
        protected static readonly float[] DISTANCE_THRESHOLDS = new float[] { 100f, 400f, 1600f }; // 10, 20, 40 tiles

        #endregion

        #region Overrides

        /// <summary>
        /// Whether to use Growing or PlantCutting work tag
        /// </summary>
        protected override string WorkTag => "Growing";

        /// <summary>
        /// Unique name for debug messages
        /// </summary>
        protected abstract string JobDescription { get; }

        /// <summary>
        /// Override debug name to use JobDescription
        /// </summary>
        protected override string DebugName => JobDescription;

        /// <summary>
        /// Override cache interval - growing targets don't change as often
        /// </summary>
        protected override int CacheUpdateInterval => 300; // Update every 5 seconds

        /// <summary>
        /// Get cells that need growing work
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // Growing JobGivers work with cells, not things
            // We need to override this with an empty implementation
            return Enumerable.Empty<Thing>();
        }

        /// <summary>
        /// Standard TryGiveJob pattern using the JobGiverManager
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            // Default implementation calls CreateGrowingJob with the base class type
            return CreateGrowingJob<JobGiver_Common_Growing_PawnControl>(pawn);
        }

        #endregion

        #region Growing-specific helpers

        /// <summary>
        /// Generic helper method to create a growing job that can be used by all subclasses
        /// </summary>
        /// <typeparam name="T">The specific JobGiver subclass type</typeparam>
        /// <param name="pawn">The pawn that will perform the growing job</param>
        /// <param name="jobProcessor">Optional custom function to process cells and create specific job types</param>
        /// <returns>A job related to growing, or null if no valid job could be created</returns>
        protected Job CreateGrowingJob<T>(Pawn pawn, Func<Pawn, List<IntVec3>, Job> jobProcessor = null) where T : JobGiver_Common_Growing_PawnControl
        {
            // Use the StandardTryGiveJob pattern with the generic type
            return Utility_JobGiverManager.StandardTryGiveJob<T>(
                pawn,
                WorkTag,
                (p, forced) => {
                    if (p?.Map == null)
                        return null;

                    // Get growing cells using the common method
                    List<IntVec3> cells = GetGrowingWorkCells(p);
                    if (cells == null || cells.Count == 0)
                        return null;

                    // If a custom processor was provided, use it
                    if (jobProcessor != null)
                    {
                        return jobProcessor(p, cells);
                    }

                    // Default implementation (subclasses should override this or provide a processor)
                    return null;
                },
                debugJobDesc: JobDescription);
        }

        /// <summary>
        /// Gets cells where growing work (sowing, harvesting, etc.) is needed
        /// </summary>
        protected List<IntVec3> GetGrowingWorkCells(Pawn pawn)
        {
            var result = new List<IntVec3>();
            _targetZonesAndGrowers.Clear();
            _wantedPlantDef = null;

            if (pawn?.Map == null)
                return result;

            Danger maxDanger = pawn.NormalMaxDanger();

            // Check growing buildings (hydroponics, etc.)
            foreach (Building building in pawn.Map.listerBuildings.allBuildingsColonist)
            {
                if (!(building is Building_PlantGrower grower))
                    continue;

                if (!ExtraRequirements(grower, pawn) ||
                    grower.IsForbidden(pawn) ||
                    !pawn.CanReach(grower, PathEndMode.OnCell, maxDanger) ||
                    grower.IsBurning())
                    continue;

                _targetZonesAndGrowers.Add(grower);
                foreach (IntVec3 cell in grower.OccupiedRect())
                {
                    result.Add(cell);
                }

                _wantedPlantDef = null;
            }

            // Check growing zones
            foreach (Zone zone in pawn.Map.zoneManager.AllZones)
            {
                if (!(zone is Zone_Growing growZone))
                    continue;

                if (growZone.cells.Count == 0)
                {
                    Log.ErrorOnce($"Grow zone has 0 cells: {growZone}", -563487);
                    continue;
                }

                if (!ExtraRequirements(growZone, pawn) ||
                    growZone.ContainsStaticFire ||
                    !pawn.CanReach(growZone.Cells[0], PathEndMode.OnCell, maxDanger))
                    continue;

                _targetZonesAndGrowers.Add(growZone);
                result.AddRange(growZone.cells);
                _wantedPlantDef = null;
            }

            // Limit collection size for performance
            int maxCells = 1000;
            if (result.Count > maxCells)
            {
                result = result.Take(maxCells).ToList();
            }

            return result;
        }

        /// <summary>
        /// Checks if a grow zone or grower meets any additional requirements
        /// </summary>
        protected virtual bool ExtraRequirements(IPlantToGrowSettable settable, Pawn pawn)
        {
            return true;
        }

        /// <summary>
        /// Gets the plant def that should be grown in a particular cell
        /// </summary>
        protected ThingDef CalculateWantedPlantDef(IntVec3 c, Map map)
        {
            return c.GetPlantToGrowSettable(map)?.GetPlantDefToGrow();
        }

        /// <summary>
        /// Find cells suitable for specific growing actions
        /// </summary>
        protected List<IntVec3> FindTargetCells(Pawn pawn, List<IntVec3> allCells, Func<IntVec3, Pawn, Map, bool> validator)
        {
            if (pawn?.Map == null || allCells == null || allCells.Count == 0)
                return new List<IntVec3>();

            List<IntVec3> validCells = new List<IntVec3>();
            Map map = pawn.Map;

            foreach (IntVec3 cell in allCells)
            {
                if (validator(cell, pawn, map))
                {
                    validCells.Add(cell);

                    // Cap to prevent performance issues
                    if (validCells.Count >= 200)
                        break;
                }
            }

            return validCells;
        }

        /// <summary>
        /// Find the best cell to work on using distance bucketing
        /// </summary>
        protected IntVec3 FindBestCell(Pawn pawn, List<IntVec3> cells)
        {
            if (pawn?.Map == null || cells == null || cells.Count == 0)
                return IntVec3.Invalid;

            // Create our own distance buckets for IntVec3 types
            List<IntVec3>[] buckets = new List<IntVec3>[DISTANCE_THRESHOLDS.Length + 1];
            for (int i = 0; i < buckets.Length; i++)
            {
                buckets[i] = new List<IntVec3>();
            }

            // Sort cells into buckets by distance
            foreach (IntVec3 cell in cells)
            {
                float distanceSq = (cell - pawn.Position).LengthHorizontalSquared;

                int bucketIndex = 0;
                while (bucketIndex < DISTANCE_THRESHOLDS.Length && distanceSq > DISTANCE_THRESHOLDS[bucketIndex])
                {
                    bucketIndex++;
                }

                buckets[bucketIndex].Add(cell);
            }

            // Find the best cell by distance
            for (int b = 0; b < buckets.Length; b++)
            {
                if (buckets[b].Count == 0)
                    continue;

                // Randomize within each bucket for even distribution
                buckets[b].Shuffle();
                return buckets[b][0];
            }

            return IntVec3.Invalid;
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return $"JobGiver_Growing_PawnControl_{JobDescription}";
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetGrowingCache()
        {
            _targetCells.Clear();
            _targetZonesAndGrowers.Clear();
            _wantedPlantDef = null;
        }

        #endregion
    }
}