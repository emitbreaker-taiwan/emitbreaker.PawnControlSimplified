using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for stripping downed pawns or corpses
    /// </summary>
    public class JobModule_Hauling_Strip : JobModule_Hauling
    {
        public override string UniqueID => "StripTargets";
        public override float Priority => 5.7f; // Same as original JobGiver
        public override string Category => "Logistics";
        public override int CacheUpdateInterval => 90; // Update every 1.5 seconds

        // Static caches for map-specific data persistence
        private static readonly Dictionary<int, List<Thing>> _strippableThingsCache = new Dictionary<int, List<Thing>>();
        private static readonly Dictionary<int, Dictionary<Thing, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Thing, bool>>();
        private static readonly Dictionary<int, Dictionary<Thing, bool>> _designationCache = new Dictionary<int, Dictionary<Thing, bool>>();
        private static int _lastUpdateCacheTick = -999;

        // These ThingRequestGroups are what this module cares about
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups =>
            new HashSet<ThingRequestGroup> { ThingRequestGroup.Pawn, ThingRequestGroup.Corpse };

        public override void UpdateCache(Map map, List<Thing> targetCache)
        {
            base.UpdateCache(map, targetCache);

            if (map == null) return;
            int mapId = map.uniqueID;
            bool hasTargets = false;

            // Quick early exit if there are no strip designations
            if (!map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.Strip))
                return;

            int currentTick = Find.TickManager.TicksGame;

            // Initialize caches if needed
            if (!_strippableThingsCache.ContainsKey(mapId))
                _strippableThingsCache[mapId] = new List<Thing>();

            if (!_reachabilityCache.ContainsKey(mapId))
                _reachabilityCache[mapId] = new Dictionary<Thing, bool>();

            if (!_designationCache.ContainsKey(mapId))
                _designationCache[mapId] = new Dictionary<Thing, bool>();

            // Only do a full update if needed
            if (currentTick > _lastUpdateCacheTick + CacheUpdateInterval ||
                !_strippableThingsCache.ContainsKey(mapId))
            {
                try
                {
                    // Clear existing caches for this map
                    _strippableThingsCache[mapId].Clear();
                    _reachabilityCache[mapId].Clear();
                    _designationCache[mapId].Clear();

                    // Find all things designated for stripping
                    foreach (Designation designation in map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Strip))
                    {
                        if (designation.target.HasThing)
                        {
                            Thing thing = designation.target.Thing;
                            if (thing != null && StrippableUtility.CanBeStrippedByColony(thing))
                            {
                                _strippableThingsCache[mapId].Add(thing);
                                targetCache.Add(thing);

                                // Cache designation status
                                _designationCache[mapId][thing] = true;
                            }
                        }
                    }

                    if (_strippableThingsCache[mapId].Count > 0)
                    {
                        Utility_DebugManager.LogNormal(
                            $"Found {_strippableThingsCache[mapId].Count} targets designated for stripping on map {map.uniqueID}");
                    }

                    _lastUpdateCacheTick = currentTick;
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"Error updating strippable things cache: {ex}");
                }
            }
            else
            {
                // Just add the cached targets to the target cache
                foreach (Thing thing in _strippableThingsCache[mapId])
                {
                    // Skip things that are no longer valid for stripping
                    if (!thing.Spawned ||
                        thing.Destroyed ||
                        thing.Map.designationManager.DesignationOn(thing, DesignationDefOf.Strip) == null ||
                        !StrippableUtility.CanBeStrippedByColony(thing))
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
                if (_strippableThingsCache.ContainsKey(mapId) && _strippableThingsCache[mapId].Contains(thing))
                    return true;

                // Check designation cache
                if (_designationCache.ContainsKey(mapId) &&
                    _designationCache[mapId].TryGetValue(thing, out bool isDesignated))
                {
                    return isDesignated;
                }

                // If not in cache, check if thing is designated for stripping
                bool shouldStrip = thing.Map.designationManager.DesignationOn(thing, DesignationDefOf.Strip) != null &&
                                  StrippableUtility.CanBeStrippedByColony(thing);

                // Cache the result
                if (_designationCache.ContainsKey(mapId))
                {
                    _designationCache[mapId][thing] = shouldStrip;
                }

                return shouldStrip;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in ShouldHaulItem for stripping: {ex}");
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
                if (!Utility_JobGiverManager.IsValidFactionInteraction(thing, hauler, requiresDesignator: true))
                    return false;

                // Skip if no longer designated for stripping
                if (thing.Map.designationManager.DesignationOn(thing, DesignationDefOf.Strip) == null)
                    return false;

                // Skip if cannot be stripped by colony
                if (!StrippableUtility.CanBeStrippedByColony(thing))
                    return false;

                // Skip if pawn is in mental state
                Pawn targetPawn = thing as Pawn;
                if (targetPawn != null && targetPawn.InAggroMentalState)
                    return false;

                // Skip if forbidden or unreachable
                if (thing.IsForbidden(hauler) ||
                    !hauler.CanReserve(thing) ||
                    !hauler.CanReach(thing, PathEndMode.ClosestTouch, Danger.Deadly))
                    return false;

                return true;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error validating strip job: {ex}");
                return false;
            }
        }

        protected override Job CreateHaulingJob(Pawn hauler, Thing thing)
        {
            try
            {
                // Create the strip job
                Job job = JobMaker.MakeJob(JobDefOf.Strip, thing);

                string targetDesc = thing is Corpse corpse ?
                    $"corpse of {corpse.InnerPawn.LabelCap}" :
                    thing.LabelCap;

                Utility_DebugManager.LogNormal($"{hauler.LabelShort} created job to strip {targetDesc}");
                return job;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating strip job: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public override void ResetStaticData()
        {
            Utility_CacheManager.ResetJobGiverCache(_strippableThingsCache, _reachabilityCache);

            foreach (var designationMap in _designationCache.Values)
            {
                designationMap.Clear();
            }
            _designationCache.Clear();

            _lastUpdateCacheTick = -999;
        }
    }
}