using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Base class for warden job givers with specialized cache management
    /// </summary>
    public abstract class JobGiver_Warden_PawnControl : JobGiver_Scan_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        protected override bool RequiresMapZoneorArea => false;

        protected override string WorkTag => "Warden";
        protected virtual float[] DistanceThresholds => new float[] { 100f, 225f, 625f }; // 10, 15, 25 tiles
        #endregion

        #region Caching
        // Domain-specific caches
        protected static readonly Dictionary<int, List<Pawn>> _prisonerCache = new Dictionary<int, List<Pawn>>();
        protected static readonly Dictionary<int, Dictionary<Pawn, bool>> _prisonerReachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        protected static readonly Dictionary<int, int> _lastWardenCacheUpdate = new Dictionary<int, int>();
        #endregion

        #region Utility

        // Specialized prisoner handling methods
        protected virtual bool IsValidPrisonerTarget(Pawn prisoner, Pawn warden) { return true;/* Common warden logic */ }

        #endregion

        #region Helpers

        /// <summary>
        /// Updates a cache of prisoners matching specific criteria
        /// </summary>
        public static void UpdatePrisonerCache(
            Map map,
            ref int lastUpdateTick,
            int updateInterval,
            Dictionary<int, List<Pawn>> prisonerCache,
            Dictionary<int, Dictionary<Pawn, bool>> reachabilityCache,
            Func<Pawn, bool> prisonerFilter)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > lastUpdateTick + updateInterval ||
                !prisonerCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (prisonerCache.ContainsKey(mapId))
                    prisonerCache[mapId].Clear();
                else
                    prisonerCache[mapId] = new List<Pawn>();

                // Clear reachability cache too
                if (reachabilityCache.ContainsKey(mapId))
                    reachabilityCache[mapId].Clear();
                else
                    reachabilityCache[mapId] = new Dictionary<Pawn, bool>();

                // Find all matching prisoners using the provided filter
                foreach (Pawn prisoner in map.mapPawns.PrisonersOfColonySpawned)
                {
                    if (prisoner == null || prisoner.Destroyed || !prisoner.Spawned)
                        continue;

                    if (prisonerFilter(prisoner))
                    {
                        prisonerCache[mapId].Add(prisoner);
                    }
                }

                lastUpdateTick = currentTick;
            }
        }

        #endregion

        #region Cache Management

        // Reset cache helpers
        public static void ResetWardenCache()
        {
            _prisonerCache.Clear();
            _prisonerReachabilityCache.Clear();
            _lastWardenCacheUpdate.Clear();
        }

        #endregion
    }
}