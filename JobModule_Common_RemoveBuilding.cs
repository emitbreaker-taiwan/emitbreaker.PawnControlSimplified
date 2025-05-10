using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Common abstract base class for modules that remove buildings with designations.
    /// Can be used by both PlantCutting and Construction work types.
    /// </summary>
    public abstract class JobModule_Common_RemoveBuilding : JobModuleCore
    {
        // Cache for designated things to remove
        protected static readonly Dictionary<int, List<Thing>> _targetCache = new Dictionary<int, List<Thing>>();
        protected static readonly Dictionary<int, Dictionary<Thing, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Thing, bool>>();
        protected static int _lastCacheUpdateTick = -999;

        // Define constants for cache management
        public override int CacheUpdateInterval => 180; // 3 seconds
        protected const int MAX_CACHE_SIZE = 100;

        // Define distance thresholds for bucketing
        protected static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        // Configuration properties that can be overridden by derived classes or adapter implementations
        protected virtual DesignationDef Designation => DesignationDefOf.Deconstruct;
        protected virtual JobDef RemoveBuildingJob => JobDefOf.Deconstruct;
        protected virtual bool RequiresConstructionSkill => true;
        protected virtual bool RequiresPlantWorkSkill => false;
        protected virtual bool ValidatePlantTarget => false;

        /// <summary>
        /// Initialize or reset caches
        /// </summary>
        public override void ResetStaticData()
        {
            Utility_CacheManager.ResetJobGiverCache(_targetCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        /// <summary>
        /// Check if this thing has the proper designation for removal
        /// Implementation shared by both Construction and PlantCutting modules
        /// </summary>
        public bool ShouldProcessTarget(Thing thing, Map map)
        {
            if (thing == null || !thing.Spawned || map == null) return false;

            // Check if it has the appropriate designation
            if (thing.Map.designationManager.DesignationOn(thing, Designation) == null)
                return false;

            // If plant validation is required, check if it's a plant
            if (ValidatePlantTarget && !(thing is Plant))
                return false;

            return true;
        }

        /// <summary>
        /// Check if the worker can remove this designated thing
        /// Implementation shared by both Construction and PlantCutting modules
        /// </summary>
        public bool ValidateRemovalJob(Thing thing, Pawn worker)
        {
            if (thing == null || worker == null || !thing.Spawned || !worker.Spawned)
                return false;

            // Check faction interaction validity
            if (!Utility_JobGiverManager.IsValidFactionInteraction(thing, worker, requiresDesignator: true))
                return false;

            // Skip if no longer valid
            if (thing.Destroyed || !thing.Spawned)
                return false;

            // Skip if no longer designated
            if (thing.Map.designationManager.DesignationOn(thing, Designation) == null)
                return false;

            // Check for timed explosives - avoid removing things about to explode
            CompExplosive explosive = thing.TryGetComp<CompExplosive>();
            if (explosive != null && explosive.wickStarted)
                return false;

            // Skip if forbidden or unreachable
            if (thing.IsForbidden(worker) ||
                !worker.CanReserve(thing, 1, -1, null, false) ||
                !worker.CanReach(thing, PathEndMode.Touch, Danger.Some))
                return false;

            // Check for required skills
            if (RequiresConstructionSkill)
            {
                if (worker.skills?.GetSkill(SkillDefOf.Construction)?.Level < 1)
                    return false;
            }

            if (RequiresPlantWorkSkill)
            {
                // Check plant-specific requirements
                if (thing is Plant plant)
                {
                    if (plant.def.plant.sowMinSkill > 0)
                    {
                        int plantSkill = worker.skills?.GetSkill(SkillDefOf.Plants)?.Level ?? 0;
                        if (plantSkill < plant.def.plant.sowMinSkill)
                        {
                            JobFailReason.Is("UnderAllowedSkill".Translate(plant.def.plant.sowMinSkill));
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Create a job to remove the designated thing
        /// Implementation shared by both Construction and PlantCutting modules
        /// </summary>
        public Job CreateRemovalJob(Pawn worker, Thing thing)
        {
            if (thing == null || worker == null || !thing.Spawned || !worker.Spawned)
                return null;

            // Create job if target is valid
            Job job = JobMaker.MakeJob(RemoveBuildingJob, thing);
            Utility_DebugManager.LogNormal($"{worker.LabelShort} created job to {RemoveBuildingJob.defName} {thing.LabelCap}");
            return job;
        }

        /// <summary>
        /// Updates the cache of designated things that need to be removed
        /// </summary>
        public void UpdateRemovalTargetCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CacheUpdateInterval ||
                !_targetCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_targetCache.ContainsKey(mapId))
                    _targetCache[mapId].Clear();
                else
                    _targetCache[mapId] = new List<Thing>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Thing, bool>();

                // Find all designated things for removal
                var designations = map.designationManager.SpawnedDesignationsOfDef(Designation);
                foreach (Designation designation in designations)
                {
                    Thing thing = designation.target.Thing;
                    if (thing != null && thing.Spawned && ShouldProcessTarget(thing, map))
                    {
                        _targetCache[mapId].Add(thing);

                        // Limit cache size for performance
                        if (_targetCache[mapId].Count >= MAX_CACHE_SIZE)
                            break;
                    }
                }

                _lastCacheUpdateTick = currentTick;

                // Record whether we found any targets
                SetHasTargets(map, _targetCache[mapId].Count > 0);
            }
        }

        /// <summary>
        /// Gets the cached list of designated things from the given map
        /// </summary>
        public List<Thing> GetRemovalTargets(Map map)
        {
            if (map == null)
                return new List<Thing>();

            int mapId = map.uniqueID;

            if (_targetCache.TryGetValue(mapId, out var cachedTargets))
                return cachedTargets;

            return new List<Thing>();
        }

        /// <summary>
        /// Find a valid target for removal from the cache
        /// </summary>
        protected Thing FindValidRemovalTarget(Pawn worker, Map map)
        {
            if (worker?.Map == null) return null;

            // Update the cache first
            UpdateRemovalTargetCache(map);

            int mapId = map.uniqueID;
            if (!_targetCache.ContainsKey(mapId) || _targetCache[mapId].Count == 0)
                return null;

            // Use distance bucketing for efficient selection
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                worker,
                _targetCache[mapId],
                (thing) => (thing.Position - worker.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Find the best target to remove
            return Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                worker,
                (thing, pawn) => ValidateRemovalJob(thing, pawn),
                _reachabilityCache
            );
        }

        /// <summary>
        /// Create a job for the worker from cached targets
        /// </summary>
        public Job CreateJobFor(Pawn worker)
        {
            try
            {
                if (worker == null) return null;

                // Find a valid removal target
                Thing target = FindValidRemovalTarget(worker, worker.Map);
                if (target == null) return null;

                // Create the job
                Job job = CreateRemovalJob(worker, target);
                return job;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating removal job: {ex}");
                return null;
            }
        }
    }
}