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
    /// JobGiver that allows pawns to replant trees (move minified trees to new locations).
    /// Uses the Growing work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Growing_Replant_PawnControl : ThinkNode_JobGiver
    {
        // Cache for plant blueprints
        private static readonly Dictionary<int, List<Blueprint_Install>> _plantBlueprintCache = new Dictionary<int, List<Blueprint_Install>>();
        private static readonly Dictionary<int, Dictionary<Blueprint_Install, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Blueprint_Install, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 300; // Update every 5 seconds

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 2500f }; // 15, 25, 50 tiles

        // Translation strings
        private static string ForbiddenLowerTranslated;
        private static string NoPathTranslated;
        private static string BlockedByRoofTranslated;
        private static string BeingCarriedByTranslated;
        private static string ReservedByTranslated;

        public static void ResetStaticData()
        {
            ForbiddenLowerTranslated = "ForbiddenLower".Translate();
            NoPathTranslated = "NoPath".Translate();
            BlockedByRoofTranslated = "BlockedByRoof".Translate();
            BeingCarriedByTranslated = "BeingCarriedBy".Translate();
            ReservedByTranslated = "ReservedBy".Translate();
        }

        public override float GetPriority(Pawn pawn)
        {
            // Growing is a lower priority than construction
            return 5.4f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Quick early exit if there are no valid blueprint_installs on the map
            if (pawn?.Map == null ||
                pawn.Faction != Faction.OfPlayer || // Only player faction pawns can handle replanting
                !pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).OfType<Blueprint_Install>().Any())
                return null;

            return Utility_JobGiverManagerOld.StandardTryGiveJob<Blueprint_Install>(
                pawn,
                "Growing",
                (p, forced) => {
                    // Update cache
                    UpdatePlantBlueprintCache(p.Map);

                    // Find and create a job for replanting trees
                    return TryCreateReplantJob(p, forced);
                },
                debugJobDesc: "tree replanting assignment");
        }

        /// <summary>
        /// Updates the cache of plant blueprints that need replanting
        /// </summary>
        private void UpdatePlantBlueprintCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_plantBlueprintCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_plantBlueprintCache.ContainsKey(mapId))
                    _plantBlueprintCache[mapId].Clear();
                else
                    _plantBlueprintCache[mapId] = new List<Blueprint_Install>();

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
                    _plantBlueprintCache[mapId].Add(blueprint);
                }

                // Limit cache size for performance
                int maxCacheSize = 200;
                if (_plantBlueprintCache[mapId].Count > maxCacheSize)
                {
                    _plantBlueprintCache[mapId] = _plantBlueprintCache[mapId].Take(maxCacheSize).ToList();
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Creates a job for replanting trees
        /// </summary>
        private Job TryCreateReplantJob(Pawn pawn, bool forced)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_plantBlueprintCache.ContainsKey(mapId) || _plantBlueprintCache[mapId].Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing 
            var buckets = Utility_JobGiverManagerOld.CreateDistanceBuckets(
                pawn,
                _plantBlueprintCache[mapId],
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
                        Job blockingJob = GenConstruct.HandleBlockingThingJob(blueprint, pawn, forced);
                        if (blockingJob != null)
                        {
                            Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to handle {blocker.LabelCap} blocking replanting");
                            return blockingJob;
                        }
                    }
                }
            }

            // With no blocking jobs found, proceed with normal target selection
            Blueprint_Install bestBlueprint = Utility_JobGiverManagerOld.FindFirstValidTargetInBuckets<Blueprint_Install>(
                buckets,
                pawn,
                (blueprint, p) => {
                    // IMPORTANT: Check faction interaction validity first
                    if (!Utility_JobGiverManagerOld.IsValidFactionInteraction(blueprint, p, requiresDesignator: false))
                        return false;

                    // Skip blueprints from different factions
                    if (blueprint.Faction != p.Faction)
                        return false;

                    // Skip if blueprint is missing or destroyed
                    if (blueprint == null || blueprint.Destroyed || !blueprint.Spawned)
                        return false;

                    // Get the tree to replant
                    Thing minifiedTree = blueprint.MiniToInstallOrBuildingToReinstall;
                    if (minifiedTree == null)
                        return false;

                    // Check for blocking things - we already handled these
                    Thing blocker = GenConstruct.FirstBlockingThing(blueprint, p);
                    if (blocker != null)
                        return false;

                    // Check if the plant can be planted here
                    ThingDef plantDef = blueprint.def.entityDefToBuild as ThingDef;
                    if (plantDef == null)
                        return false;

                    // Check if the plant can ever be planted at this location
                    AcceptanceReport report = plantDef.CanEverPlantAt(blueprint.Position, p.Map, out Thing _, true);
                    if (!report.Accepted)
                    {
                        JobFailReason.Is(report.Reason);
                        return false;
                    }

                    // Check if the plant can be planted right now
                    if (!plantDef.CanNowPlantAt(blueprint.Position, p.Map, true))
                        return false;

                    // Check for roof interference
                    if (plantDef.plant.interferesWithRoof && blueprint.Position.Roofed(p.Map))
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
                    if (minifiedTree.IsForbidden(p))
                    {
                        JobFailReason.Is(ForbiddenLowerTranslated);
                        return false;
                    }

                    // Check if pawn can reach the tree
                    if (!p.CanReach(minifiedTree, PathEndMode.ClosestTouch, p.NormalMaxDanger()))
                    {
                        JobFailReason.Is(NoPathTranslated);
                        return false;
                    }

                    // Check if pawn can reserve the tree
                    if (!p.CanReserve(minifiedTree))
                    {
                        Pawn reserver = p.Map.reservationManager.FirstRespectedReserver(minifiedTree, p);
                        if (reserver != null)
                            JobFailReason.Is(ReservedByTranslated);
                        return false;
                    }

                    return true;
                },
                _reachabilityCache
            );

            // Create job if valid blueprint found
            if (bestBlueprint != null)
            {
                // Get required info again for job creation
                Thing minifiedTree = bestBlueprint.MiniToInstallOrBuildingToReinstall;
                ThingDef plantDef = bestBlueprint.def.entityDefToBuild as ThingDef;

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

            return null;
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_plantBlueprintCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
            ResetStaticData();
        }

        public override string ToString()
        {
            return "JobGiver_Replant_PawnControl";
        }
    }
}