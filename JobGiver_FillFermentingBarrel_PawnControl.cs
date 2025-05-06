using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to fill fermenting barrels with wort.
    /// Uses the Hauling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_FillFermentingBarrel_PawnControl : ThinkNode_JobGiver
    {
        // Cache for fermenting barrels that need filling
        private static readonly Dictionary<int, List<Building_FermentingBarrel>> _barrelCache = new Dictionary<int, List<Building_FermentingBarrel>>();
        private static readonly Dictionary<int, Dictionary<Building_FermentingBarrel, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Building_FermentingBarrel, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 300; // Update every 5 seconds

        // Cache for translation strings
        private static string TemperatureTrans;
        private static string NoWortTrans;

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

        public static void ResetStaticData()
        {
            TemperatureTrans = "BadTemperature".Translate();
            NoWortTrans = "NoWort".Translate();
        }

        public override float GetPriority(Pawn pawn)
        {
            // Filling barrels is moderately important
            return 5.3f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // IMPORTANT: Only player pawns and slaves owned by player should fill fermenting barrels
            if (pawn.Faction != Faction.OfPlayer &&
                !(pawn.IsSlave && pawn.HostFaction == Faction.OfPlayer))
                return null;

            return Utility_JobGiverManager.StandardTryGiveJob<Plant>(
                pawn,
                "Hauling",
                (p, forced) => {
                    // Update plant cache
                    UpdateBarrelCacheSafely(p.Map);

                    // Find and create a job for cutting plants with VALID DESIGNATORS ONLY
                    return TryCreateFillBarrelJob(p);
                },
                debugJobDesc: "filling a fermenting barrel assignment");
        }

        /// <summary>
        /// Updates the cache of fermenting barrels that need filling
        /// </summary>
        private void UpdateBarrelCacheSafely(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_barrelCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_barrelCache.ContainsKey(mapId))
                    _barrelCache[mapId].Clear();
                else
                    _barrelCache[mapId] = new List<Building_FermentingBarrel>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Building_FermentingBarrel, bool>();

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
                        }
                    }
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Creates a job for filling a fermenting barrel
        /// </summary>
        private Job TryCreateFillBarrelJob(Pawn pawn)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_barrelCache.ContainsKey(mapId) || _barrelCache[mapId].Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                _barrelCache[mapId],
                (barrel) => (barrel.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Find the best barrel to fill
            Building_FermentingBarrel targetBarrel = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (barrel, p) => {
                    // IMPORTANT: Check faction interaction validity first
                    if (!Utility_JobGiverManager.IsValidFactionInteraction(barrel, p, requiresDesignator: true))
                        return false;

                    // Skip if no longer valid
                    if (barrel == null || barrel.Destroyed || !barrel.Spawned)
                        return false;
                    
                    // Skip if fermented, full, burning, or forbidden
                    if (barrel.Fermented || barrel.SpaceLeftForWort <= 0 ||
                        barrel.IsBurning() || barrel.IsForbidden(p))
                        return false;
                    
                    // Skip if being deconstructed
                    if (p.Map.designationManager.DesignationOn(barrel, DesignationDefOf.Deconstruct) != null)
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
                    if (!p.CanReserve(barrel))
                        return false;
                    
                    // Skip if no wort is available
                    if (FindWort(p, barrel) == null)
                        return false;
                    
                    return true;
                },
                _reachabilityCache
            );

            // Create job if target found
            if (targetBarrel != null)
            {
                // Find wort to fill the barrel with
                Thing wort = FindWort(pawn, targetBarrel);
                
                if (wort != null)
                {
                    Job job = JobMaker.MakeJob(JobDefOf.FillFermentingBarrel, targetBarrel, wort);
                    
                    if (Prefs.DevMode)
                    {
                        Log.Message($"[PawnControl] {pawn.LabelShort} created job to fill fermenting barrel with wort");
                    }
                    
                    return job;
                }
            }

            return null;
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

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_barrelCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_FillFermentingBarrel_PawnControl";
        }
    }
}