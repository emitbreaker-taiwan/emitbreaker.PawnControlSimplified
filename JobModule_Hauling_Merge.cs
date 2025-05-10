using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for merging partial stacks of the same item
    /// </summary>
    public class JobModule_Hauling_Merge : JobModule_Hauling
    {
        public override string UniqueID => "MergeStacks";
        public override float Priority => 5.0f; // Same as original JobGiver
        public override string Category => "Logistics";
        public override int CacheUpdateInterval => 400; // Update every ~6.6 seconds

        // Static caches for map-specific data persistence
        private static readonly Dictionary<int, List<Thing>> _mergeableCache = new Dictionary<int, List<Thing>>();
        private static readonly Dictionary<int, Dictionary<Thing, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Thing, bool>>();
        private static int _lastCacheUpdateTick = -999;

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 2500f }; // 15, 25, 50 tiles

        // These ThingRequestGroups are what this module cares about
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups =>
            new HashSet<ThingRequestGroup> { ThingRequestGroup.HaulableEver };

        public override void UpdateCache(Map map, List<Thing> targetCache)
        {
            base.UpdateCache(map, targetCache);

            if (map == null) return;
            int mapId = map.uniqueID;
            bool hasTargets = false;

            int currentTick = Find.TickManager.TicksGame;

            // Initialize caches if needed
            if (!_mergeableCache.ContainsKey(mapId))
                _mergeableCache[mapId] = new List<Thing>();

            if (!_reachabilityCache.ContainsKey(mapId))
                _reachabilityCache[mapId] = new Dictionary<Thing, bool>();

            // Only do a full update if needed
            if (currentTick > _lastCacheUpdateTick + CacheUpdateInterval ||
                !_mergeableCache.ContainsKey(mapId))
            {
                try
                {
                    // Clear existing caches for this map
                    _mergeableCache[mapId].Clear();
                    _reachabilityCache[mapId].Clear();

                    // Find all things potentially needing merging
                    List<Thing> mergeables = map.listerMergeables.ThingsPotentiallyNeedingMerging();
                    if (mergeables != null && mergeables.Count > 0)
                    {
                        _mergeableCache[mapId].AddRange(mergeables);
                        
                        // Also add them to the target cache provided by the job giver
                        foreach (Thing thing in mergeables)
                        {
                            targetCache.Add(thing);
                        }
                        
                        Utility_DebugManager.LogNormal(
                            $"Found {mergeables.Count} targets for potential merging on map {map.uniqueID}");
                    }

                    _lastCacheUpdateTick = currentTick;
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"Error updating mergeable things cache: {ex}");
                }
            }
            else
            {
                // Just add the cached targets to the target cache
                foreach (Thing thing in _mergeableCache[mapId])
                {
                    // Skip if no longer valid
                    if (!thing.Spawned || thing.Destroyed)
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

                int mapId = map.uniqueID;

                // Check if it's in our cache first
                if (_mergeableCache.ContainsKey(mapId) && _mergeableCache[mapId].Contains(thing))
                    return true;

                // Simple check if it's potentially mergeable
                if (thing.def.stackLimit <= 1 || thing.stackCount >= thing.def.stackLimit)
                    return false;

                // If not in cache but might be mergeable, do a deep check
                ISlotGroup slotGroup = thing.GetSlotGroup();
                if (slotGroup == null)
                    return false;

                // Get the storage group
                ISlotGroup storageGroup = slotGroup.StorageGroup ?? slotGroup;

                // Check if there's another stack to merge with
                foreach (Thing otherThing in storageGroup.HeldThings)
                {
                    if (otherThing != thing && otherThing.CanStackWith(thing) && 
                        otherThing.stackCount < otherThing.def.stackLimit)
                        return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in ShouldHaulItem for merging: {ex}");
                return false;
            }
        }

        public override bool ValidateHaulingJob(Thing thing, Pawn hauler)
        {
            try
            {
                if (thing == null || hauler == null || !thing.Spawned || !hauler.Spawned)
                    return false;

                // Skip if at stack limit
                if (thing.stackCount >= thing.def.stackLimit)
                    return false;

                // Skip if we can't haul this automatically
                if (!HaulAIUtility.PawnCanAutomaticallyHaul(hauler, thing, false))
                    return false;

                // Skip if we can't reserve the position
                if (!hauler.CanReserve(thing.Position))
                    return false;

                // Get the slot group (storage)
                ISlotGroup slotGroup1 = thing.GetSlotGroup();
                if (slotGroup1 == null)
                    return false;

                // Get the overall storage group if available
                ISlotGroup slotGroup2 = slotGroup1.StorageGroup ?? slotGroup1;

                // Check if there's a valid target to merge with
                foreach (Thing heldThing in slotGroup2.HeldThings)
                {
                    // Skip if this is the same thing or can't stack with our item
                    if (heldThing == thing || !heldThing.CanStackWith(thing))
                        continue;

                    // Prefer to merge smaller stacks into larger ones
                    if (heldThing.stackCount < thing.stackCount)
                        continue;

                    // Skip if target stack is already full
                    if (heldThing.stackCount >= heldThing.def.stackLimit)
                        continue;

                    // Skip if can't reserve both position and item
                    if (!hauler.CanReserve(heldThing.Position) || !hauler.CanReserve(heldThing))
                        continue;

                    // Skip if target cell isn't valid storage for the item
                    if (!heldThing.Position.IsValidStorageFor(heldThing.Map, thing))
                        continue;

                    // Skip if target cell has fire
                    if (heldThing.Position.ContainsStaticFire(heldThing.Map))
                        continue;

                    // Found a valid target
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error validating merge job: {ex}");
                return false;
            }
        }

        protected override Job CreateHaulingJob(Pawn hauler, Thing thing)
        {
            try
            {
                if (thing == null || hauler == null)
                    return null;

                // Get the slot group (storage)
                ISlotGroup slotGroup1 = thing.GetSlotGroup();
                if (slotGroup1 == null)
                    return null;

                // Get the overall storage group if available
                ISlotGroup slotGroup2 = slotGroup1.StorageGroup ?? slotGroup1;

                // Find a valid target to merge with
                foreach (Thing heldThing in slotGroup2.HeldThings)
                {
                    // Skip if this is the same thing or can't stack with our item
                    if (heldThing == thing || !heldThing.CanStackWith(thing))
                        continue;

                    // Prefer to merge smaller stacks into larger ones
                    if (heldThing.stackCount < thing.stackCount)
                        continue;

                    // Skip if target stack is already full
                    if (heldThing.stackCount >= heldThing.def.stackLimit)
                        continue;

                    // Skip if can't reserve both position and item
                    if (!hauler.CanReserve(heldThing.Position) || !hauler.CanReserve(heldThing))
                        continue;

                    // Skip if target cell isn't valid storage for the item
                    if (!heldThing.Position.IsValidStorageFor(heldThing.Map, thing))
                        continue;

                    // Skip if target cell has fire
                    if (heldThing.Position.ContainsStaticFire(heldThing.Map))
                        continue;

                    // Create the hauling job
                    Job job = JobMaker.MakeJob(JobDefOf.HaulToCell, thing, heldThing.Position);
                    job.count = Mathf.Min(heldThing.def.stackLimit - heldThing.stackCount, thing.stackCount);
                    job.haulMode = HaulMode.ToCellStorage;
                    Utility_DebugManager.LogNormal($"{hauler.LabelShort} created job to merge {thing.Label} ({thing.stackCount}) with {heldThing.Label} ({heldThing.stackCount})");
                    return job;
                }

                return null;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating merge job: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public override void ResetStaticData()
        {
            Utility_CacheManager.ResetJobGiverCache(_mergeableCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }
    }
}