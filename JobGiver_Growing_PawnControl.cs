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
    public abstract class JobGiver_Growing_PawnControl : JobGiver_Scan_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        protected override bool RequiresMapZoneorArea => true;

        /// <summary>
        /// Whether to use Growing or PlantCutting work tag
        /// </summary>
        public override string WorkTag => "Growing";

        public override PawnEnumTags RequiredTag => PawnEnumTags.AllowWork_Growing;

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
        /// Distance thresholds for bucketing - adjust as needed for growing jobs
        /// </summary>
        protected virtual float[] DistanceThresholds => new float[] { 100f, 400f, 1600f }; // 10, 20, 40 tiles

        /// <summary>
        /// Maximum number of cells to process for performance
        /// </summary>
        private const int MAX_CELL_CACHE = 1000;

        /// <summary>
        /// Maximum number of valid cells to return for specific operations
        /// </summary>
        private const int MAX_VALID_CELLS = 200;

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_Growing_PawnControl() : base()
        {
            // Base constructor already initializes the cache system for Things
            // We'll implement our own cell caching in the derived methods
        }

        #endregion

        #region Core Flow

        /// <summary>
        /// Standard TryGiveJob pattern using the JobGiverManager
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            // Default implementation calls CreateGrowingJob with the base class type
            return CreateGrowingJob<JobGiver_Growing_PawnControl>(pawn);
        }

        #endregion

        #region Target Selection

        /// <summary>
        /// Get cells that need growing work
        /// Growing JobGivers work with cells, not things - we use this for compatibility
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // Growing JobGivers work with cells, not things
            // We need to override this with an empty implementation
            return Enumerable.Empty<Thing>();
        }

        /// <summary>
        /// Job-specific cache update method - override to work with cells instead of Things
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            // The growing system uses cells instead of Things, so this is a placeholder
            // Real caching happens in GetGrowingWorkCells
            return Enumerable.Empty<Thing>();
        }

        #endregion

        #region Job Creation

        /// <summary>
        /// Generic helper method to create a growing job that can be used by all subclasses
        /// </summary>
        /// <typeparam name="T">The specific JobGiver subclass type</typeparam>
        /// <param name="pawn">The pawn that will perform the growing job</param>
        /// <param name="jobProcessor">Optional custom function to process cells and create specific job types</param>
        /// <returns>A job related to growing, or null if no valid job could be created</returns>
        protected Job CreateGrowingJob<T>(Pawn pawn, Func<Pawn, List<IntVec3>, Job> jobProcessor = null)
            where T : JobGiver_Growing_PawnControl
        {
            return Utility_JobGiverManager.StandardTryGiveJob<T>(
                pawn,
                WorkTag,
                (p, forced) =>
                {
                    if (p?.Map == null)
                        return null;

                    // 1) Gather all potential growing cells
                    List<IntVec3> cells = GetGrowingWorkCells(p);
                    if (cells == null || cells.Count == 0)
                        return null;

                    // 2) Filter out any cells whose plant isn't valid for this pawn/faction
                    var validCells = new List<IntVec3>();
                    Map map = p.Map;

                    foreach (var cell in cells)
                    {
                        // get the plant (if any) at this location
                        var plant = cell.GetPlant(map);
                        if (plant == null)
                            continue;

                        // skip if pawn isn't allowed to interact (no designator needed here)
                        if (!Utility_JobGiverManager.IsValidFactionInteraction(plant, p, requiresDesignator: false))
                            continue;

                        validCells.Add(cell);
                    }

                    if (validCells.Count == 0)
                        return null;

                    // 3) If a custom processor was provided, use it with the filtered list
                    if (jobProcessor != null)
                        return jobProcessor(p, validCells);

                    // 4) Otherwise no default behavior
                    return null;
                },
                debugJobDesc: JobDescription);
        }

        /// <summary>
        /// Processes cached targets to find a valid job
        /// This is an abstract requirement from JobGiver_Scan_PawnControl
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // This class uses its own cell-based system instead of the target system
            // This method is required by the parent class but not used in our implementation
            return null;
        }

        #endregion

        #region Growing-specific helpers

        /// <summary>
        /// Gets cells where growing work (sowing, harvesting, etc.) is needed
        /// This is a map-specific cache implementation
        /// </summary>
        protected List<IntVec3> GetGrowingWorkCells(Pawn pawn)
        {
            if (pawn?.Map == null)
                return new List<IntVec3>();

            int mapId = pawn.Map.uniqueID;
            Type jobGiverType = this.GetType();

            // Use a type-keyed dictionary cache to store cell lists
            // We'll use the specific type of this job giver as the cache key
            string cacheKey = jobGiverType.FullName + "_GrowCells_" + typeof(IntVec3).FullName;

            // Try to get cached cells from the central cache manager
            var cellCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<IntVec3>>(mapId);

            // Check if we need to update the cache
            int currentTick = Find.TickManager.TicksGame;
            int lastUpdateTick = Utility_MapCacheManager.GetLastCacheUpdateTick(mapId, cacheKey);

            if (currentTick - lastUpdateTick > CacheUpdateInterval ||
                !cellCache.TryGetValue(cacheKey, out List<IntVec3> result) ||
                result == null)
            {
                // Initialize 'result' to avoid unassigned variable error
                result = new List<IntVec3>();

                // Rebuild the cache
                var zonesAndGrowers = new List<IPlantToGrowSettable>();
                Map map = pawn.Map;
                Danger maxDanger = pawn.NormalMaxDanger();

                // Check growing buildings (hydroponics, etc.)
                foreach (Building building in map.listerBuildings.allBuildingsColonist)
                {
                    if (!(building is Building_PlantGrower grower))
                        continue;

                    if (!ExtraRequirements(grower, pawn) ||
                        grower.IsForbidden(pawn) ||
                        !pawn.CanReach(grower, PathEndMode.OnCell, maxDanger) ||
                        grower.IsBurning())
                        continue;

                    zonesAndGrowers.Add(grower);
                    foreach (IntVec3 cell in grower.OccupiedRect())
                    {
                        result.Add(cell);
                    }
                }

                // Check growing zones
                foreach (Zone zone in map.zoneManager.AllZones)
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

                    zonesAndGrowers.Add(growZone);
                    result.AddRange(growZone.cells);
                }

                // Limit collection size for performance
                if (result.Count > MAX_CELL_CACHE)
                {
                    result = result.Take(MAX_CELL_CACHE).ToList();
                }

                // Store in the central cache
                cellCache[cacheKey] = result;
                Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, currentTick);

                // Also store the zones and growers in a separate cache for future reference
                var zoneCacheKey = cacheKey + "_Zones";
                var zoneCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<IPlantToGrowSettable>>(mapId);
                zoneCache[zoneCacheKey] = zonesAndGrowers;

                if (Prefs.DevMode)
                {
                    Utility_DebugManager.LogNormal($"Updated growing cache for {jobGiverType.Name}, found {result.Count} cells");
                }
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
                    if (validCells.Count >= MAX_VALID_CELLS)
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
            List<IntVec3>[] buckets = new List<IntVec3>[DistanceThresholds.Length + 1];
            for (int i = 0; i < buckets.Length; i++)
            {
                buckets[i] = new List<IntVec3>();
            }

            // Sort cells into buckets by distance
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

        #region Reset

        /// <summary>
        /// Reset the cache - implements IResettableCache
        /// </summary>
        public override void Reset()
        {
            // Call base reset first
            base.Reset();

            // Now clear any map-specific caches for growing cells
            foreach (int mapId in Find.Maps.Select(map => map.uniqueID))
            {
                string cacheKey = this.GetType().Name + "_GrowCells";
                var cellCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<IntVec3>>(mapId);

                if (cellCache.ContainsKey(cacheKey))
                {
                    cellCache.Remove(cacheKey);
                }

                string zoneCacheKey = cacheKey + "_Zones";
                var zoneCache = Utility_MapCacheManager.GetOrCreateMapCache<string, List<IPlantToGrowSettable>>(mapId);

                if (zoneCache.ContainsKey(zoneCacheKey))
                {
                    zoneCache.Remove(zoneCacheKey);
                }

                // Clear the update tick record too
                Utility_MapCacheManager.SetLastCacheUpdateTick(mapId, cacheKey, -1);
            }

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Reset growing caches for {this.GetType().Name}");
            }
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return $"JobGiver_Growing_PawnControl_{JobDescription}";
        }

        #endregion
    }
}