using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for fixing broken down buildings
    /// </summary>
    public class JobModule_Construction_FixBrokenDown : JobModule_Construction
    {
        public override string UniqueID => "FixBrokenDown";
        public override float Priority => 6.5f; // Same as original JobGiver priority
        public override string Category => "Construction";
        public override int CacheUpdateInterval => 300; // Update every 5 seconds (broken buildings don't change often)

        // Cache for buildings that need repairing
        private static readonly Dictionary<int, List<Building>> _brokenBuildingsCache = new Dictionary<int, List<Building>>();
        private static readonly Dictionary<Building, Thing> _componentCache = new Dictionary<Building, Thing>();
        private static readonly Dictionary<int, Dictionary<Building, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Building, bool>>();
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 100f, 400f, 900f }; // 10, 20, 30 tiles

        // Static translation strings
        private static string NotInHomeAreaTrans;
        private static string NoComponentsToRepairTrans;

        // This module deals with buildings that are broken down
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups =>
            new HashSet<ThingRequestGroup> { ThingRequestGroup.BuildingArtificial };

        public override void ResetStaticData()
        {
            Utility_CacheManager.ResetJobGiverCache<Building>(_brokenBuildingsCache, _reachabilityCache);
            _componentCache.Clear(); // Clear component cache too

            NotInHomeAreaTrans = "NotInHomeArea".Translate();
            NoComponentsToRepairTrans = "NoComponentsToRepair".Translate();
        }

        public override void UpdateCache(Map map, List<Thing> targetCache)
        {
            base.UpdateCache(map, targetCache);

            if (map == null) return;
            int mapId = map.uniqueID;
            bool hasTargets = false;

            // Initialize caches if needed
            if (!_brokenBuildingsCache.ContainsKey(mapId))
                _brokenBuildingsCache[mapId] = new List<Building>();

            if (!_reachabilityCache.ContainsKey(mapId))
                _reachabilityCache[mapId] = new Dictionary<Building, bool>();

            // Clear existing caches for this map
            _brokenBuildingsCache[mapId].Clear();

            // Use the BreakdownManager to get all broken buildings
            var brokenDownThings = map.GetComponent<BreakdownManager>()?.brokenDownThings;
            if (brokenDownThings != null)
            {
                foreach (Building building in brokenDownThings)
                {
                    if (building != null && building.Spawned && building.def.building.repairable)
                    {
                        _brokenBuildingsCache[mapId].Add(building);

                        // Also add to the target cache provided by the job giver
                        targetCache.Add(building);
                    }
                }
            }

            if (_brokenBuildingsCache[mapId].Count > 0)
            {
                Utility_DebugManager.LogNormal(
                    $"Found {_brokenBuildingsCache[mapId].Count} broken buildings needing repair on map {map.uniqueID}");
            }

            SetHasTargets(map, hasTargets || (targetCache != null && targetCache.Count > 0));
        }

        public override bool ShouldProcessBuildable(Thing thing, Map map)
        {
            try
            {
                if (thing == null || map == null || !thing.Spawned) return false;

                // Must be a building
                Building building = thing as Building;
                if (building == null) return false;

                int mapId = map.uniqueID;

                // Check if it's in our cache first
                if (_brokenBuildingsCache.ContainsKey(mapId) && _brokenBuildingsCache[mapId].Contains(building))
                    return true;

                // Check if it's broken down
                return building.IsBrokenDown() && building.def.building.repairable;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in ShouldProcessBuildable for broken building: {ex}");
                return false;
            }
        }

        public override bool ValidateConstructionJob(Thing thing, Pawn constructionWorker)
        {
            try
            {
                if (thing == null || constructionWorker == null || !thing.Spawned || !constructionWorker.Spawned)
                    return false;

                // Must be a building
                Building building = thing as Building;
                if (building == null) return false;

                // Building must be broken down
                if (!building.IsBrokenDown())
                    return false;

                // Check building faction - must be same as pawn's
                if (building.Faction != constructionWorker.Faction)
                    return false;

                // Check if in home area (for player faction)
                if (constructionWorker.Faction == Faction.OfPlayer &&
                    !constructionWorker.Map.areaManager.Home[building.Position])
                {
                    JobFailReason.Is(NotInHomeAreaTrans);
                    return false;
                }

                // Check existing designations
                if (constructionWorker.Map.designationManager.DesignationOn(building, DesignationDefOf.Deconstruct) != null)
                    return false;

                // Check if building is on fire
                if (building.IsBurning())
                    return false;

                // Check if pawn can reserve the building
                if (!constructionWorker.CanReserve(building, 1, -1, null, false))
                    return false;

                // Check if reachable
                if (!constructionWorker.CanReach(building, PathEndMode.Touch, Danger.Deadly))
                    return false;

                // Check for components to repair with
                Thing component = FindClosestComponent(constructionWorker);
                if (component == null)
                {
                    JobFailReason.Is(NoComponentsToRepairTrans);
                    return false;
                }

                // Store the actual component for later use
                _componentCache[building] = component;

                return true;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error validating broken building repair job: {ex}");
                return false;
            }
        }

        protected override Job CreateConstructionJob(Pawn constructionWorker, Thing thing)
        {
            try
            {
                if (constructionWorker == null || thing == null)
                    return null;

                Building building = thing as Building;
                if (building == null) return null;

                // Use the cached component or find one
                Thing component = null;
                if (_componentCache.TryGetValue(building, out var cachedComponent))
                {
                    component = cachedComponent;
                }
                else
                {
                    component = FindClosestComponent(constructionWorker);
                }

                if (component != null)
                {
                    Job job = JobMaker.MakeJob(JobDefOf.FixBrokenDownBuilding, building, component);
                    job.count = 1;
                    Utility_DebugManager.LogNormal($"{constructionWorker.LabelShort} created job to repair broken {building.LabelCap}");
                    return job;
                }

                return null;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating broken building repair job: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Find the closest component available for repairs
        /// </summary>
        private Thing FindClosestComponent(Pawn pawn)
        {
            return GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForDef(ThingDefOf.ComponentIndustrial),
                PathEndMode.InteractionCell,
                TraverseParms.For(pawn, pawn.NormalMaxDanger()),
                validator: x => !x.IsForbidden(pawn) && pawn.CanReserve(x)
            );
        }
    }
}