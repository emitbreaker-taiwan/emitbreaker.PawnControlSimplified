using RimWorld;
using System.Collections.Generic;
using System.Threading;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to strip downed pawns or corpses.
    /// Uses the Hauling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Strip_PawnControl : ThinkNode_JobGiver
    {
        // Cache for things that need to be stripped
        private static readonly Dictionary<int, List<Thing>> _strippableThingsCache = new Dictionary<int, List<Thing>>();
        private static readonly Dictionary<int, Dictionary<Thing, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Thing, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 90; // Update every 1.5 seconds

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 100f, 400f, 900f }; // 10, 20, 30 tiles

        public override float GetPriority(Pawn pawn)
        {
            // Stripping is moderately important
            return 5.7f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Quick early exit if there are no strip designations
            if (!pawn.Map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.Strip))
            {
                return null;
            }

            return Utility_JobGiverManager.StandardTryGiveJob<Plant>(
                pawn,
                "Hauling",
                (p, forced) => {
                    // Update plant cache
                    UpdateStrippableThingsCache(p.Map);

                    // Find and create a job for cutting plants with VALID DESIGNATORS ONLY
                    return TryCreateStripJob(p);
                },
                debugJobDesc: "strip target assignment");
        }

        /// <summary>
        /// Updates the cache of things designated for stripping
        /// </summary>
        private void UpdateStrippableThingsCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_strippableThingsCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_strippableThingsCache.ContainsKey(mapId))
                    _strippableThingsCache[mapId].Clear();
                else
                    _strippableThingsCache[mapId] = new List<Thing>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Thing, bool>();

                // Find all things designated for stripping
                foreach (Designation designation in map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Strip))
                {
                    if (designation.target.HasThing)
                    {
                        Thing thing = designation.target.Thing;
                        if (thing != null && StrippableUtility.CanBeStrippedByColony(thing))
                        {
                            _strippableThingsCache[mapId].Add(thing);
                        }
                    }
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Create a job for stripping a target using manager-driven bucket processing
        /// </summary>
        private Job TryCreateStripJob(Pawn pawn)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_strippableThingsCache.ContainsKey(mapId) || _strippableThingsCache[mapId].Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing and target selection
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                _strippableThingsCache[mapId],
                (thing) => (thing.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Find the best target to strip
            Thing targetThing = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (thing, p) => {
                    // IMPORTANT: Check faction interaction validity first
                    if (!Utility_JobGiverManager.IsValidFactionInteraction(thing, p, requiresDesignator: true))
                        return false;

                    // Skip if no longer valid
                    if (thing == null || thing.Destroyed || !thing.Spawned)
                        return false;

                    // Skip if no longer designated for stripping
                    if (thing.Map.designationManager.DesignationOn(thing, DesignationDefOf.Strip) == null)
                        return false;

                    // Skip if cannot be stripped by colony
                    if (!StrippableUtility.CanBeStrippedByColony(thing))
                        return false;

                    // Skip if in mental state (for pawns)
                    Pawn targetPawn = thing as Pawn;
                    if (targetPawn != null && targetPawn.InAggroMentalState)
                        return false;

                    // Skip if forbidden or unreachable
                    if (thing.IsForbidden(p) || 
                        !p.CanReserve(thing) || 
                        !p.CanReach(thing, PathEndMode.ClosestTouch, Danger.Deadly))
                        return false;
                    
                    return true;
                },
                _reachabilityCache
            );

            // Create job if target found
            if (targetThing != null)
            {
                Job job = JobMaker.MakeJob(JobDefOf.Strip, targetThing);
                
                if (Prefs.DevMode)
                {
                    string targetDesc = targetThing is Corpse corpse ? $"corpse of {corpse.InnerPawn.LabelCap}" : targetThing.LabelCap;
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to strip {targetDesc}");
                }
                
                return job;
            }

            return null;
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_strippableThingsCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_Strip_PawnControl";
        }
    }
}