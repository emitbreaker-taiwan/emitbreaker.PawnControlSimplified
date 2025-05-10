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
    /// Module for replanting trees (moving minified trees to new locations)
    /// </summary>
    public class JobModule_Growing_Replant : JobModule_Growing
    {
        // We need a field rather than a property to use with ref parameters
        private static int _lastLocalUpdateTick = -999;

        // Cache for replant targets
        private static readonly Dictionary<int, List<Blueprint_Install>> _replantTargetCache =
            new Dictionary<int, List<Blueprint_Install>>();
        private static readonly Dictionary<int, Dictionary<Blueprint_Install, bool>> _reachabilityCache =
            new Dictionary<int, Dictionary<Blueprint_Install, bool>>();

        // Reference to the common implementation for resource delivery
        private readonly JobModule_Common_DeliverResources_Adapter _commonImpl;

        // Translation strings
        private static string ForbiddenLowerTranslated;
        private static string NoPathTranslated;
        private static string BlockedByRoofTranslated;
        private static string BeingCarriedByTranslated;
        private static string ReservedByTranslated;

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 2500f }; // 15, 25, 50 tiles

        // Module metadata
        public override string UniqueID => "Growing_Replant";
        public override float Priority => 5.4f; // Lower priority than harvesting
        public override string Category => "Growing";

        /// <summary>
        /// Relevant ThingRequestGroups for replant jobs - override Plant focus with Blueprint focus
        /// </summary>
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups =>
            new HashSet<ThingRequestGroup> { ThingRequestGroup.Blueprint };

        // Constructor initializes common implementation
        public JobModule_Growing_Replant()
        {
            _commonImpl = new JobModule_Common_DeliverResources_Adapter(this);
        }

        /// <summary>
        /// Reset any static data when language changes or game reloads
        /// </summary>
        public override void ResetStaticData()
        {
            base.ResetStaticData();
            _lastLocalUpdateTick = -999;
            _replantTargetCache.Clear();
            _reachabilityCache.Clear();

            // Initialize translation strings
            ForbiddenLowerTranslated = "ForbiddenLower".Translate();
            NoPathTranslated = "NoPath".Translate();
            BlockedByRoofTranslated = "BlockedByRoof".Translate();
            BeingCarriedByTranslated = "BeingCarriedBy".Translate();
            ReservedByTranslated = "ReservedBy".Translate();

            // Reset common impl
            _commonImpl.ResetStaticData();
        }

        /// <summary>
        /// Plants are not actually processed in this module - we use Blueprint_Install instead,
        /// but we need to implement this method as part of JobModule_Growing
        /// </summary>
        public override bool ShouldProcessGrowingTarget(Plant plant, Map map)
        {
            return false; // We don't process Plants directly
        }

        /// <summary>
        /// Validate if pawn can replant - this method won't be called directly
        /// since we override the job creation flow
        /// </summary>
        public override bool ValidateGrowingJob(Plant plant, Pawn grower)
        {
            return false; // We don't process Plants directly
        }

        /// <summary>
        /// Create a growing job - not used directly
        /// </summary>
        protected override Job CreateGrowingJob(Pawn grower, Plant plant)
        {
            return null; // We don't process Plants directly
        }

        /// <summary>
        /// Update cache for replant targets - this overrides the Plant-focused cache update
        /// </summary>
        public override void UpdateCache(Map map, List<Plant> targetCache)
        {
            if (map == null) return;

            // We don't use the Plant targetCache since we're working with blueprints
            targetCache?.Clear();

            // Update our blueprint cache instead
            UpdateReplantCache(map);
        }

        /// <summary>
        /// Updates the cache of plant blueprints that need replanting
        /// </summary>
        private void UpdateReplantCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastLocalUpdateTick + CacheUpdateInterval ||
                !_replantTargetCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_replantTargetCache.ContainsKey(mapId))
                    _replantTargetCache[mapId].Clear();
                else
                    _replantTargetCache[mapId] = new List<Blueprint_Install>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Blueprint_Install, bool>();

                // Find all plant blueprints
                foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint))
                {
                    // Only look for Blueprint_Install that are plants
                    Blueprint_Install blueprint = thing as Blueprint_Install;
                    if (blueprint == null || !(blueprint.def.entityDefToBuild is ThingDef entityDef) || entityDef.plant == null)
                        continue;

                    // Add to cache
                    _replantTargetCache[mapId].Add(blueprint);
                }

                // Limit cache size for performance
                int maxCacheSize = 200;
                if (_replantTargetCache[mapId].Count > maxCacheSize)
                {
                    _replantTargetCache[mapId] = _replantTargetCache[mapId].Take(maxCacheSize).ToList();
                }

                _lastLocalUpdateTick = currentTick;

                // Record whether we have targets
                SetHasTargets(map, _replantTargetCache[mapId].Count > 0);
            }
        }

        /// <summary>
        /// Find a valid blueprint to replant a tree
        /// </summary>
        public Blueprint_Install FindValidReplantTarget(Pawn pawn)
        {
            if (pawn?.Map == null) return null;

            // Update the cache first
            UpdateReplantCache(pawn.Map);

            int mapId = pawn.Map.uniqueID;
            if (!_replantTargetCache.ContainsKey(mapId) || _replantTargetCache[mapId].Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing 
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                _replantTargetCache[mapId],
                (blueprint) => (blueprint.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Process each bucket to first check for blocking jobs
            for (int i = 0; i < buckets.Length; i++)
            {
                foreach (Blueprint_Install blueprint in buckets[i])
                {
                    // Filter out invalid blueprints immediately
                    if (blueprint.Faction != pawn.Faction || blueprint == null || blueprint.Destroyed || !blueprint.Spawned)
                        continue;

                    // Get the tree to replant
                    Thing minifiedTree = blueprint.MiniToInstallOrBuildingToReinstall;
                    if (minifiedTree == null)
                        continue;

                    // Check for blocking things first
                    Thing blocker = GenConstruct.FirstBlockingThing(blueprint, pawn);
                    if (blocker != null)
                    {
                        Job blockingJob = GenConstruct.HandleBlockingThingJob(blueprint, pawn, false);
                        if (blockingJob != null)
                        {
                            // This blueprint has a blocking job, but it's handled elsewhere
                            // so we skip it for now
                            continue;
                        }
                    }
                }
            }

            // With no blocking jobs found, proceed with normal target selection
            return Utility_JobGiverManager.FindFirstValidTargetInBuckets<Blueprint_Install>(
                buckets,
                pawn,
                ValidateReplantTarget,
                _reachabilityCache
            );
        }

        /// <summary>
        /// Validate if pawn can replant at this blueprint location
        /// </summary>
        private bool ValidateReplantTarget(Blueprint_Install blueprint, Pawn pawn)
        {
            // IMPORTANT: Check faction interaction validity first
            if (!Utility_JobGiverManager.IsValidFactionInteraction(blueprint, pawn, requiresDesignator: false))
                return false;

            // Skip blueprints from different factions
            if (blueprint.Faction != pawn.Faction)
                return false;

            // Skip if blueprint is missing or destroyed
            if (blueprint == null || blueprint.Destroyed || !blueprint.Spawned)
                return false;

            // Get the tree to replant
            Thing minifiedTree = blueprint.MiniToInstallOrBuildingToReinstall;
            if (minifiedTree == null)
                return false;

            // Check for blocking things
            Thing blocker = GenConstruct.FirstBlockingThing(blueprint, pawn);
            if (blocker != null)
                return false;

            // Check if the plant can be planted here
            ThingDef plantDef = blueprint.def.entityDefToBuild as ThingDef;
            if (plantDef == null)
                return false;

            // Check if the plant can ever be planted at this location
            AcceptanceReport report = plantDef.CanEverPlantAt(blueprint.Position, pawn.Map, out Thing _, true);
            if (!report.Accepted)
            {
                JobFailReason.Is(report.Reason);
                return false;
            }

            // Check if the plant can be planted right now
            if (!plantDef.CanNowPlantAt(blueprint.Position, pawn.Map, true))
                return false;

            // Check for roof interference
            if (plantDef.plant.interferesWithRoof && blueprint.Position.Roofed(pawn.Map))
            {
                JobFailReason.Is(BlockedByRoofTranslated);
                return false;
            }

            // Check if the tree is being carried
            IThingHolder parentHolder = minifiedTree.ParentHolder;
            if (parentHolder is Pawn_CarryTracker carryTracker)
            {
                JobFailReason.Is(BeingCarriedByTranslated, carryTracker.pawn.LabelShort);
                return false;
            }

            // Check if the tree is forbidden
            if (minifiedTree.IsForbidden(pawn))
                return false;

            // Check if pawn can reach the tree
            if (!pawn.CanReach(minifiedTree, PathEndMode.ClosestTouch, pawn.NormalMaxDanger()))
                return false;

            // Check if pawn can reserve the tree
            if (!pawn.CanReserve(minifiedTree))
            {
                Pawn reserver = pawn.Map.reservationManager.FirstRespectedReserver(minifiedTree, pawn);
                if (reserver != null)
                    JobFailReason.Is(ReservedByTranslated);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Create a job to replant a tree for a specific pawn
        /// </summary>
        public Job CreateReplantJob(Pawn pawn)
        {
            if (pawn == null || pawn.Faction != Faction.OfPlayer)
                return null;

            // Find a valid blueprint target
            Blueprint_Install bestBlueprint = FindValidReplantTarget(pawn);
            if (bestBlueprint == null)
                return null;

            // Get required info for job creation
            Thing minifiedTree = bestBlueprint.MiniToInstallOrBuildingToReinstall;
            ThingDef plantDef = bestBlueprint.def.entityDefToBuild as ThingDef;
            if (minifiedTree == null || plantDef == null)
                return null;

            // Create the replant job
            Job job = JobMaker.MakeJob(JobDefOf.Replant);
            job.targetA = minifiedTree;
            job.targetB = bestBlueprint;
            job.plantDefToSow = plantDef;
            job.count = 1;
            job.haulMode = HaulMode.ToContainer;

            Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to replant tree at {bestBlueprint.Position}");
            return job;
        }

        /// <summary>
        /// Adapter class for JobModule_Common_DeliverResources
        /// </summary>
        private class JobModule_Common_DeliverResources_Adapter : JobModule_Common_DeliverResources
        {
            private readonly JobModule_Growing_Replant _outer;

            public JobModule_Common_DeliverResources_Adapter(JobModule_Growing_Replant outer)
            {
                _outer = outer;
            }

            // Required implementations from JobModuleCore
            public override string UniqueID => _outer.UniqueID + "_Common";
            public override float Priority => _outer.Priority;
            public override string WorkTypeName => _outer.WorkTypeName;
            public override string Category => _outer.Category;

            // Configuration properties - specific for replanting (growing job)
            protected override bool RequiresConstructionSkill => false;
            protected override bool AllowHaulingWorkType => false;
            protected override bool OnlyFrames => false;
        }
    }
}