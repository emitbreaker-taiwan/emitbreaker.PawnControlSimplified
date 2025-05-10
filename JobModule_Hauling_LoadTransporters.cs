using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for loading items into transporters like shuttles
    /// </summary>
    public class JobModule_Hauling_LoadTransporters : JobModule_Hauling
    {
        public override string UniqueID => "LoadTransporters";
        public override float Priority => 5.9f; // Same as original JobGiver
        public override string Category => "Logistics"; // Added category for consistency
        public override int CacheUpdateInterval => 180; // Update every 3 seconds

        // Cache for transporters found during this update - converted to static for persistence
        private static readonly Dictionary<int, List<Thing>> _transporterCache = new Dictionary<int, List<Thing>>();
        private static readonly Dictionary<int, Dictionary<Thing, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Thing, bool>>();
        private static int _lastCacheUpdateTick = -999;

        // Mapping to quickly look up CompTransporter components by their parent Thing
        private static readonly Dictionary<int, Dictionary<Thing, CompTransporter>> _thingToTransporterMap = new Dictionary<int, Dictionary<Thing, CompTransporter>>();

        // These ThingRequestGroups are what this module cares about
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups =>
            new HashSet<ThingRequestGroup> { ThingRequestGroup.Transporter };

        public override void UpdateCache(Map map, List<Thing> targetCache)
        {
            base.UpdateCache(map, targetCache);

            if (map == null) return;
            int mapId = map.uniqueID;
            bool hasTargets = false;

            // Initialize the transporter map for this map if needed
            if (!_thingToTransporterMap.ContainsKey(mapId))
                _thingToTransporterMap[mapId] = new Dictionary<Thing, CompTransporter>();

            // Use the base class's progressive cache update
            UpdateCacheProgressively(
                map,
                targetCache,
                ref _lastCacheUpdateTick,
                RelevantThingRequestGroups,
                thing => {
                    CompTransporter transporter = thing.TryGetComp<CompTransporter>();
                    if (transporter != null && transporter.parent.Faction == Faction.OfPlayer)
                    {
                        // Keep our lookup map updated
                        _thingToTransporterMap[mapId][transporter.parent] = transporter;
                        return true;
                    }
                    return false;
                },
                _transporterCache,
                CacheUpdateInterval
            );

            SetHasTargets(map, hasTargets || (targetCache != null && targetCache.Count > 0));
        }

        public override bool ShouldHaulItem(Thing item, Map map)
        {
            if (map == null || item == null || !item.Spawned) return false;

            int mapId = map.uniqueID;

            // Check from our cached map first for better performance
            if (_thingToTransporterMap.ContainsKey(mapId) && _thingToTransporterMap[mapId].ContainsKey(item))
                return true;

            // If not in cache, check directly
            CompTransporter transporter = item.TryGetComp<CompTransporter>();
            return transporter != null && transporter.parent.Faction == Faction.OfPlayer;
        }

        public override bool ValidateHaulingJob(Thing thing, Pawn hauler)
        {
            if (thing == null || hauler == null || !thing.Spawned || !hauler.Spawned)
                return false;

            int mapId = hauler.Map.uniqueID;

            // Get the transporter component
            CompTransporter transporter = null;

            // Try from cache first
            if (_thingToTransporterMap.ContainsKey(mapId) && _thingToTransporterMap[mapId].TryGetValue(thing, out transporter))
            {
                // Use cached mapping
            }
            else
            {
                // Try to get directly
                transporter = thing.TryGetComp<CompTransporter>();
            }

            if (transporter == null)
                return false;

            // Check faction interaction
            if (!Utility_JobGiverManager.IsValidFactionInteraction(thing, hauler, requiresDesignator: false))
                return false;

            // Check if the hauler can do work on this transporter
            if (thing.IsForbidden(hauler) ||
                !hauler.CanReserve(thing, 1, -1) ||
                !hauler.CanReach(thing, PathEndMode.Touch, hauler.NormalMaxDanger()))
                return false;

            // Use the utility method if available to check if there's a valid job
            return IsValidTransporterJob(hauler, transporter);
        }

        protected override Job CreateHaulingJob(Pawn hauler, Thing thing)
        {
            try
            {
                if (thing == null || hauler == null) return null;

                int mapId = hauler.Map.uniqueID;

                // Get the transporter component
                CompTransporter transporter = null;

                // Try from cache first
                if (_thingToTransporterMap.ContainsKey(mapId) && _thingToTransporterMap[mapId].TryGetValue(thing, out transporter))
                {
                    // Use cached mapping
                }
                else
                {
                    // Try to get directly
                    transporter = thing.TryGetComp<CompTransporter>();
                }

                if (transporter == null)
                    return null;

                // Create the job using utility method if available
                Job job = CreateLoadTransporterJob(hauler, transporter);

                if (job != null)
                {
                    Utility_DebugManager.LogNormal($"{hauler.LabelShort} created job to load transporter {transporter.parent.LabelCap}");
                }

                return job;
            }
            catch (System.Exception ex)
            {
                Utility_DebugManager.LogError($"Error creating transporter loading job: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Check if there's a valid job for this transporter using the utility class
        /// </summary>
        private bool IsValidTransporterJob(Pawn hauler, CompTransporter transporter)
        {
            try
            {
                // Try using the utility class directly
                if (LoadTransportersJobUtility.HasJobOnTransporter(hauler, transporter))
                {
                    return true;
                }
            }
            catch (System.Exception ex)
            {
                // If the utility method fails, use a fallback approach
                Utility_DebugManager.LogWarning($"Error checking transporter job validity: {ex.Message}");
            }

            // Fallback implementation - check if this transporter is accepting goods
            try
            {
                // Basic check - can the pawn reach the transporter?
                if (!hauler.CanReserveAndReach(transporter.parent, PathEndMode.Touch, hauler.NormalMaxDanger()))
                    return false;

                // If there's an active ship or there are items to load, it's valid
                return transporter.AnyInGroupHasAnythingLeftToLoad;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Create a job to load a transporter using the utility class
        /// </summary>
        private Job CreateLoadTransporterJob(Pawn hauler, CompTransporter transporter)
        {
            try
            {
                // Try using the utility class directly
                return LoadTransportersJobUtility.JobOnTransporter(hauler, transporter);
            }
            catch (System.Exception ex)
            {
                // If the utility method fails, log the error
                Utility_DebugManager.LogWarning($"Error creating transporter job: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public override void ResetStaticData()
        {
            Utility_CacheManager.ResetJobGiverCache(_transporterCache, _reachabilityCache);

            // Also clear the transporter mapping
            foreach (var mapDict in _thingToTransporterMap.Values)
            {
                mapDict.Clear();
            }
            _thingToTransporterMap.Clear();

            _lastCacheUpdateTick = -999;
        }
    }
}