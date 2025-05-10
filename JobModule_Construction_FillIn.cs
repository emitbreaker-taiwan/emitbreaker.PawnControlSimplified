using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Job Module for filling in pit burrows or other designated areas
    /// Uses the common building removal framework adapted for fill-in operations
    /// </summary>
    public class JobModule_Construction_FillIn : JobModule_Construction
    {
        /// <summary>
        /// Gets a unique identifier for this module
        /// </summary>
        public override string UniqueID => "Construction_FillIn";

        /// <summary>
        /// Gets the priority of this module (higher priority modules run first)
        /// </summary>
        public override float Priority => 100f;

        // Define constants specific to fill-in operations
        protected virtual DesignationDef Designation => DesignationDefOf.FillIn;
        protected virtual JobDef RemoveBuildingJob => JobDefOf.FillIn;

        // Cache for job creation
        private static readonly Dictionary<int, List<Thing>> _fillInTargetCache = new Dictionary<int, List<Thing>>();
        private static readonly Dictionary<int, Dictionary<Thing, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Thing, bool>>();
        private static int _lastCacheUpdateTick = -999;

        // Constants for cache management and distance bucketing
        private const int MAX_CACHE_SIZE = 100;
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        /// <summary>
        /// Update interval for cache refreshing - 3 seconds
        /// </summary>
        public override int CacheUpdateInterval => 180;

        /// <summary>
        /// Reset cache when game is loaded or restarted
        /// </summary>
        public override void ResetStaticData()
        {
            Utility_CacheManager.ResetJobGiverCache(_fillInTargetCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        /// <summary>
        /// Check if the thing should be processed for fill-in operations
        /// </summary>
        public override bool ShouldProcessBuildable(Thing constructible, Map map)
        {
            if (constructible == null || !constructible.Spawned || map == null) return false;

            // Check if it has the appropriate fill-in designation
            if (constructible.Map.designationManager.DesignationOn(constructible, Designation) == null)
                return false;

            // Specifically handles PitBurrow objects or anything with a fill-in designation
            return true;
        }

        /// <summary>
        /// Validates if a pawn can perform the fill-in job on the target
        /// </summary>
        public override bool ValidateConstructionJob(Thing target, Pawn constructor)
        {
            if (target == null || constructor == null || !target.Spawned || !constructor.Spawned)
                return false;

            // Check basic requirements
            if (constructor.WorkTagIsDisabled(WorkTags.Constructing))
                return false;

            // Check faction interaction validity
            if (!Utility_JobGiverManager.IsValidFactionInteraction(target, constructor, requiresDesignator: true))
                return false;

            // Skip if no longer valid
            if (target.Destroyed || !target.Spawned)
                return false;

            // Skip if no longer designated
            if (target.Map.designationManager.DesignationOn(target, Designation) == null)
                return false;

            // Skip if forbidden or unreachable
            if (target.IsForbidden(constructor) ||
                !constructor.CanReserve(target, 1, -1, null, false) ||
                !constructor.CanReach(target, PathEndMode.Touch, Danger.Some))
                return false;

            // Check construction skill requirement
            if (constructor.skills?.GetSkill(SkillDefOf.Construction)?.Level < 1)
                return false;

            // All checks passed
            return true;
        }

        /// <summary>
        /// Creates the specific fill-in job for the constructor
        /// </summary>
        protected override Job CreateConstructionJob(Pawn constructor, Thing target)
        {
            if (target == null || constructor == null || !target.Spawned || !constructor.Spawned)
                return null;

            Job job = JobMaker.MakeJob(RemoveBuildingJob, target);
            Utility_DebugManager.LogNormal($"{constructor.LabelShort} created job to {RemoveBuildingJob.defName} {target.LabelCap}");
            return job;
        }

        /// <summary>
        /// Cache updating override specifically for fill-in targets
        /// </summary>
        public override void UpdateCache(Map map, List<Thing> targetCache)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CacheUpdateInterval ||
                !_fillInTargetCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_fillInTargetCache.ContainsKey(mapId))
                    _fillInTargetCache[mapId].Clear();
                else
                    _fillInTargetCache[mapId] = new List<Thing>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Thing, bool>();

                // Find all things designated for filling in
                var designations = map.designationManager.SpawnedDesignationsOfDef(Designation);
                foreach (Designation designation in designations)
                {
                    Thing thing = designation.target.Thing;
                    if (thing != null && thing.Spawned && ShouldProcessBuildable(thing, map))
                    {
                        _fillInTargetCache[mapId].Add(thing);

                        // Limit cache size for performance
                        if (_fillInTargetCache[mapId].Count >= MAX_CACHE_SIZE)
                            break;
                    }
                }

                _lastCacheUpdateTick = currentTick;

                // Update target cache for job creation
                if (targetCache != null)
                {
                    targetCache.Clear();
                    if (_fillInTargetCache.ContainsKey(mapId))
                        targetCache.AddRange(_fillInTargetCache[mapId]);
                }

                // Record whether we found any targets
                SetHasTargets(map, _fillInTargetCache[mapId].Count > 0);
            }
        }

        /// <summary>
        /// Find the best fill-in target for a given pawn
        /// </summary>
        public Thing FindBestTarget(Pawn worker, Map map)
        {
            if (worker?.Map == null) return null;

            // Update the cache first
            int mapId = map.uniqueID;
            UpdateCache(map, null);

            if (!_fillInTargetCache.ContainsKey(mapId) || _fillInTargetCache[mapId].Count == 0)
                return null;

            // Use distance bucketing for efficient selection
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                worker,
                _fillInTargetCache[mapId],
                (thing) => (thing.Position - worker.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Find the best target to fill in
            return Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                worker,
                (thing, pawn) => ValidateConstructionJob(thing, pawn),
                _reachabilityCache
            );
        }

        /// <summary>
        /// Entry point for job creation - attempts to create a job for the worker
        /// </summary>
        public Job CreateJobFor(Pawn worker)
        {
            try
            {
                if (worker == null) return null;

                // Find a valid fill-in target
                Thing target = FindBestTarget(worker, worker.Map);
                if (target == null) return null;

                // Create and return the job
                return CreateConstructionJob(worker, target);
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating fill-in job: {ex}");
                return null;
            }
        }
    }
}