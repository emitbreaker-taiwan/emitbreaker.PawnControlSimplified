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
    /// JobGiver that assigns tasks to empty egg boxes.
    /// Uses the Hauling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_EmptyEggBox_PawnControl : ThinkNode_JobGiver
    {
        // Cache for egg boxes that need emptying
        private static readonly Dictionary<int, List<Thing>> _eggBoxCache = new Dictionary<int, List<Thing>>();
        private static readonly Dictionary<int, Dictionary<Thing, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Thing, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 300; // Update every 5 seconds

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 225f, 625f, 2500f }; // 15, 25, 50 tiles

        public override float GetPriority(Pawn pawn)
        {
            // Emptying egg boxes is moderately important
            return 5.2f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // IMPORTANT: Only player pawns and slaves owned by player should empty egg boxes
            if (pawn.Faction != Faction.OfPlayer &&
                !(pawn.IsSlave && pawn.HostFaction == Faction.OfPlayer))
                return null;

            return Utility_JobGiverManager.StandardTryGiveJob<Plant>(
                pawn,
                "Hauling",
                (p, forced) => {
                    // Update fire cache
                    UpdateEggBoxCacheSafely(p.Map);

                    // Find and create a job for cutting plants with VALID DESIGNATORS ONLY
                    return TryCreateEmptyEggBoxJob(p);
                },
                debugJobDesc: "emptying egg boxes assignment",
                skipEmergencyCheck: false);
        }

        /// <summary>
        /// Updates the cache of egg boxes that need emptying
        /// </summary>
        private void UpdateEggBoxCacheSafely(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
                !_eggBoxCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_eggBoxCache.ContainsKey(mapId))
                    _eggBoxCache[mapId].Clear();
                else
                    _eggBoxCache[mapId] = new List<Thing>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Thing, bool>();

                // Find all egg boxes on the map
                foreach (Thing thing in map.listerThings.ThingsOfDef(ThingDefOf.EggBox))
                {
                    if (thing != null && thing.Spawned && !thing.Destroyed)
                    {
                        CompEggContainer comp = thing.TryGetComp<CompEggContainer>();
                        if (comp?.ContainedThing != null)
                        {
                            _eggBoxCache[mapId].Add(thing);
                        }
                    }
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Creates a job for emptying an egg box
        /// </summary>
        private Job TryCreateEmptyEggBoxJob(Pawn pawn)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_eggBoxCache.ContainsKey(mapId) || _eggBoxCache[mapId].Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                _eggBoxCache[mapId],
                (eggBox) => (eggBox.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Find a valid egg box to empty
            for (int b = 0; b < buckets.Length; b++)
            {
                if (buckets[b].Count == 0)
                    continue;

                // Randomize within each bucket for even distribution
                buckets[b].Shuffle();

                foreach (Thing eggBox in buckets[b])
                {
                    // IMPORTANT: Check faction interaction validity
                    if (!Utility_JobGiverManager.IsValidFactionInteraction(eggBox, pawn, requiresDesignator: false))
                        continue;

                    // Skip if egg box doesn't exist or is forbidden
                    if (eggBox == null || eggBox.Destroyed || !eggBox.Spawned || eggBox.IsForbidden(pawn))
                        continue;

                    // Skip if we can't reserve the egg box
                    if (!pawn.CanReserve(eggBox))
                        continue;

                    // Get the egg container component
                    CompEggContainer comp = eggBox.TryGetComp<CompEggContainer>();
                    if (comp?.ContainedThing == null || (!comp.CanEmpty && !pawn.WorkTagIsDisabled(WorkTags.Violent)))
                        continue;

                    // Find storage for the eggs
                    IntVec3 foundCell;
                    IHaulDestination haulDestination;
                    if (!StoreUtility.TryFindBestBetterStorageFor(comp.ContainedThing, pawn, pawn.Map, StoragePriority.Unstored, pawn.Faction, out foundCell, out haulDestination))
                        continue;

                    // Create the job
                    Job job = JobMaker.MakeJob(JobDefOf.EmptyThingContainer, eggBox, comp.ContainedThing, foundCell);
                    job.count = comp.ContainedThing.stackCount;

                    // Debug message
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to empty egg box containing {comp.ContainedThing.Label} ({comp.ContainedThing.stackCount})");
                    return job;
                }
            }

            return null;
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_eggBoxCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_EmptyEggBox_PawnControl";
        }
    }
}