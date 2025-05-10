using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for emptying egg boxes
    /// </summary>
    public class JobModule_Hauling_EmptyEggBox : JobModule_Hauling
    {
        public override string UniqueID => "EmptyEggBox";
        public override float Priority => 5.2f; // Same as original JobGiver
        public override string Category => "Logistics";
        public override int CacheUpdateInterval => 300; // Update every 5 seconds

        // Static caches for map-specific data persistence
        private static readonly Dictionary<int, List<Thing>> _eggBoxCache = new Dictionary<int, List<Thing>>();
        private static readonly Dictionary<int, Dictionary<Thing, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Thing, bool>>();
        private static int _lastCacheUpdateTick = -999;

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 2500f }; // 15, 25, 50 tiles

        // These ThingRequestGroups are what this module cares about
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups =>
            new HashSet<ThingRequestGroup> { ThingRequestGroup.BuildingArtificial };

        public override void UpdateCache(Map map, List<Thing> targetCache)
        {
            base.UpdateCache(map, targetCache);

            if (map == null) return;
            int mapId = map.uniqueID;
            bool hasTargets = false;

            int currentTick = Find.TickManager.TicksGame;

            // Initialize caches if needed
            if (!_eggBoxCache.ContainsKey(mapId))
                _eggBoxCache[mapId] = new List<Thing>();

            if (!_reachabilityCache.ContainsKey(mapId))
                _reachabilityCache[mapId] = new Dictionary<Thing, bool>();

            // Only do a full update if needed
            if (currentTick > _lastCacheUpdateTick + CacheUpdateInterval ||
                !_eggBoxCache.ContainsKey(mapId))
            {
                try
                {
                    // Clear existing caches for this map
                    _eggBoxCache[mapId].Clear();
                    _reachabilityCache[mapId].Clear();

                    // Find all egg boxes on the map
                    foreach (Thing thing in map.listerThings.ThingsOfDef(ThingDefOf.EggBox))
                    {
                        if (thing != null && thing.Spawned && !thing.Destroyed)
                        {
                            CompEggContainer comp = thing.TryGetComp<CompEggContainer>();
                            if (comp?.ContainedThing != null)
                            {
                                _eggBoxCache[mapId].Add(thing);

                                // Also add to the target cache provided by the job giver
                                targetCache.Add(thing);
                            }
                        }
                    }

                    if (_eggBoxCache[mapId].Count > 0)
                    {
                        Utility_DebugManager.LogNormal(
                            $"Found {_eggBoxCache[mapId].Count} egg boxes with contents on map {map.uniqueID}");
                    }

                    _lastCacheUpdateTick = currentTick;
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"Error updating egg box cache: {ex}");
                }
            }
            else
            {
                // Just add the cached targets to the target cache
                foreach (Thing thing in _eggBoxCache[mapId])
                {
                    // Skip things that are no longer valid
                    if (!thing.Spawned || thing.Destroyed)
                        continue;

                    CompEggContainer comp = thing.TryGetComp<CompEggContainer>();
                    if (comp?.ContainedThing == null)
                        continue;

                    targetCache.Add(thing);
                }
            }

            SetHasTargets(map, hasTargets || (targetCache != null && targetCache.Count > 0));
        }

        public override bool ShouldHaulItem(Thing thing, Map map)
        {
            try
            {
                if (thing == null || map == null || !thing.Spawned) return false;

                // Check if it's an egg box
                if (thing.def != ThingDefOf.EggBox)
                    return false;

                int mapId = map.uniqueID;

                // Check if it's in our cache first
                if (_eggBoxCache.ContainsKey(mapId) && _eggBoxCache[mapId].Contains(thing))
                    return true;

                // If not in cache, check if egg box has contents
                CompEggContainer comp = thing.TryGetComp<CompEggContainer>();
                return comp?.ContainedThing != null;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in ShouldHaulItem for egg box: {ex}");
                return false;
            }
        }

        public override bool ValidateHaulingJob(Thing thing, Pawn hauler)
        {
            try
            {
                if (thing == null || hauler == null || !thing.Spawned || !hauler.Spawned)
                    return false;

                // Check faction interaction
                if (!Utility_JobGiverManager.IsValidFactionInteraction(thing, hauler, requiresDesignator: false))
                    return false;

                // Skip if egg box doesn't exist or is forbidden
                if (thing.Destroyed || thing.IsForbidden(hauler))
                    return false;

                // Skip if we can't reserve the egg box
                if (!hauler.CanReserve(thing))
                    return false;

                // Get the egg container component
                CompEggContainer comp = thing.TryGetComp<CompEggContainer>();
                if (comp?.ContainedThing == null || (!comp.CanEmpty && !hauler.WorkTagIsDisabled(WorkTags.Violent)))
                    return false;

                // Find storage for the eggs
                IntVec3 foundCell;
                IHaulDestination haulDestination;
                if (!StoreUtility.TryFindBestBetterStorageFor(comp.ContainedThing, hauler, hauler.Map,
                    StoragePriority.Unstored, hauler.Faction, out foundCell, out haulDestination))
                    return false;

                return true;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error validating empty egg box job: {ex}");
                return false;
            }
        }

        protected override Job CreateHaulingJob(Pawn hauler, Thing thing)
        {
            try
            {
                if (hauler == null || thing == null)
                    return null;

                // Get the egg container component
                CompEggContainer comp = thing.TryGetComp<CompEggContainer>();
                if (comp?.ContainedThing == null)
                    return null;

                // Find storage for the eggs
                IntVec3 foundCell;
                IHaulDestination haulDestination;
                if (!StoreUtility.TryFindBestBetterStorageFor(comp.ContainedThing, hauler, hauler.Map,
                    StoragePriority.Unstored, hauler.Faction, out foundCell, out haulDestination))
                    return null;

                // Create the job
                Job job = JobMaker.MakeJob(JobDefOf.EmptyThingContainer, thing, comp.ContainedThing, foundCell);
                job.count = comp.ContainedThing.stackCount;

                Utility_DebugManager.LogNormal($"{hauler.LabelShort} created job to empty egg box containing {comp.ContainedThing.Label} ({comp.ContainedThing.stackCount})");
                return job;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating empty egg box job: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public override void ResetStaticData()
        {
            Utility_CacheManager.ResetJobGiverCache(_eggBoxCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }
    }
}