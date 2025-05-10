using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for filling fermenting barrels with wort
    /// </summary>
    public class JobModule_Hauling_FillFermentingBarrel : JobModule_Hauling
    {
        public override string UniqueID => "FillFermentingBarrel";
        public override float Priority => 5.3f; // Same as original JobGiver
        public override string Category => "Production";
        public override int CacheUpdateInterval => 300; // Update every 5 seconds

        // Static caches for map-specific data persistence
        private static readonly Dictionary<int, List<Building_FermentingBarrel>> _barrelCache = new Dictionary<int, List<Building_FermentingBarrel>>();
        private static readonly Dictionary<int, Dictionary<Building_FermentingBarrel, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Building_FermentingBarrel, bool>>();
        private static int _lastCacheUpdateTick = -999;

        // Cache for translation strings
        private static string TemperatureTrans;
        private static string NoWortTrans;

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        // These ThingRequestGroups are what this module cares about
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups =>
            new HashSet<ThingRequestGroup> { ThingRequestGroup.BuildingArtificial };

        public override void ResetStaticData()
        {
            TemperatureTrans = "BadTemperature".Translate();
            NoWortTrans = "NoWort".Translate();
            Utility_CacheManager.ResetJobGiverCache(_barrelCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override void UpdateCache(Map map, List<Thing> targetCache)
        {
            base.UpdateCache(map, targetCache);

            if (map == null) return;
            int mapId = map.uniqueID;
            bool hasTargets = false;

            int currentTick = Find.TickManager.TicksGame;

            // Initialize caches if needed
            if (!_barrelCache.ContainsKey(mapId))
                _barrelCache[mapId] = new List<Building_FermentingBarrel>();

            if (!_reachabilityCache.ContainsKey(mapId))
                _reachabilityCache[mapId] = new Dictionary<Building_FermentingBarrel, bool>();

            // Only do a full update if needed
            if (currentTick > _lastCacheUpdateTick + CacheUpdateInterval ||
                !_barrelCache.ContainsKey(mapId))
            {
                try
                {
                    // Clear existing caches for this map
                    _barrelCache[mapId].Clear();
                    _reachabilityCache[mapId].Clear();

                    // Find all fermenting barrels on the map that need filling
                    foreach (Thing thing in map.listerThings.ThingsOfDef(ThingDefOf.FermentingBarrel))
                    {
                        Building_FermentingBarrel barrel = thing as Building_FermentingBarrel;
                        if (barrel != null && barrel.Spawned && !barrel.Fermented && barrel.SpaceLeftForWort > 0)
                        {
                            // Check if temperature is suitable for fermentation
                            float ambientTemperature = barrel.AmbientTemperature;
                            CompProperties_TemperatureRuinable compProperties = barrel.def.GetCompProperties<CompProperties_TemperatureRuinable>();
                            if ((double)ambientTemperature >= (double)compProperties.minSafeTemperature + 2.0 && 
                                (double)ambientTemperature <= (double)compProperties.maxSafeTemperature - 2.0)
                            {
                                _barrelCache[mapId].Add(barrel);
                                
                                // Also add to the target cache provided by the job giver
                                targetCache.Add(barrel);
                            }
                        }
                    }

                    if (_barrelCache[mapId].Count > 0)
                    {
                        Utility_DebugManager.LogNormal(
                            $"Found {_barrelCache[mapId].Count} fermenting barrels that need filling on map {map.uniqueID}");
                    }

                    _lastCacheUpdateTick = currentTick;
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"Error updating fermenting barrel cache: {ex}");
                }
            }
            else
            {
                // Just add the cached targets to the target cache
                foreach (Building_FermentingBarrel barrel in _barrelCache[mapId])
                {
                    // Skip if no longer valid
                    if (!barrel.Spawned || barrel.Destroyed || barrel.Fermented || barrel.SpaceLeftForWort <= 0)
                        continue;
                        
                    targetCache.Add(barrel);
                }
            }

            SetHasTargets(map, hasTargets || (targetCache != null && targetCache.Count > 0));
        }

        public override bool ShouldHaulItem(Thing thing, Map map)
        {
            try
            {
                if (thing == null || map == null || !thing.Spawned) return false;

                // Check if it's a fermenting barrel
                Building_FermentingBarrel barrel = thing as Building_FermentingBarrel;
                if (barrel == null)
                    return false;

                int mapId = map.uniqueID;

                // Check if it's in our cache first
                if (_barrelCache.ContainsKey(mapId) && _barrelCache[mapId].Contains(barrel))
                    return true;

                // If not in cache but might be a valid barrel, do a deep check
                if (barrel.Fermented || barrel.SpaceLeftForWort <= 0)
                    return false;
                    
                // Check if temperature is suitable for fermentation
                float ambientTemperature = barrel.AmbientTemperature;
                CompProperties_TemperatureRuinable compProperties = barrel.def.GetCompProperties<CompProperties_TemperatureRuinable>();
                return (double)ambientTemperature >= (double)compProperties.minSafeTemperature + 2.0 && 
                       (double)ambientTemperature <= (double)compProperties.maxSafeTemperature - 2.0;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in ShouldHaulItem for fermenting barrel: {ex}");
                return false;
            }
        }

        public override bool ValidateHaulingJob(Thing thing, Pawn hauler)
        {
            try
            {
                if (thing == null || hauler == null || !thing.Spawned || !hauler.Spawned)
                    return false;
                    
                Building_FermentingBarrel barrel = thing as Building_FermentingBarrel;
                if (barrel == null)
                    return false;

                // IMPORTANT: Check faction interaction validity first
                if (!Utility_JobGiverManager.IsValidFactionInteraction(barrel, hauler, requiresDesignator: false))
                    return false;

                // Skip if fermented, full, burning, or forbidden
                if (barrel.Fermented || barrel.SpaceLeftForWort <= 0 ||
                    barrel.IsBurning() || barrel.IsForbidden(hauler))
                    return false;
                
                // Skip if being deconstructed
                if (hauler.Map.designationManager.DesignationOn(barrel, DesignationDefOf.Deconstruct) != null)
                    return false;
                
                // Skip if temperature is not suitable
                float ambientTemperature = barrel.AmbientTemperature;
                CompProperties_TemperatureRuinable compProperties = barrel.def.GetCompProperties<CompProperties_TemperatureRuinable>();
                if ((double)ambientTemperature < (double)compProperties.minSafeTemperature + 2.0 ||
                    (double)ambientTemperature > (double)compProperties.maxSafeTemperature - 2.0)
                {
                    return false;
                }
                
                // Skip if unreachable or can't be reserved
                if (!hauler.CanReserve(barrel))
                    return false;
                
                // Skip if no wort is available
                if (FindWort(hauler, barrel) == null)
                    return false;
                
                return true;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error validating fermenting barrel job: {ex}");
                return false;
            }
        }

        protected override Job CreateHaulingJob(Pawn hauler, Thing thing)
        {
            try
            {
                if (hauler == null || thing == null)
                    return null;
                    
                Building_FermentingBarrel barrel = thing as Building_FermentingBarrel;
                if (barrel == null)
                    return null;

                // Find wort to fill the barrel with
                Thing wort = FindWort(hauler, barrel);
                
                if (wort != null)
                {
                    Job job = JobMaker.MakeJob(JobDefOf.FillFermentingBarrel, barrel, wort);
                    Utility_DebugManager.LogNormal($"{hauler.LabelShort} created job to fill fermenting barrel with wort");
                    return job;
                }

                return null;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating fermenting barrel job: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Finds wort that can be used to fill the barrel
        /// </summary>
        private Thing FindWort(Pawn pawn, Building_FermentingBarrel barrel)
        {
            Predicate<Thing> validator = (x => !x.IsForbidden(pawn) && pawn.CanReserve(x));
            return GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForDef(ThingDefOf.Wort),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn),
                validator: validator
            );
        }
    }
}