using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Base class for job givers that don't scan the environment,
    /// but find jobs through other mechanisms like lords or special utilities.
    /// </summary>
    public abstract class JobGiver_Noscan_PawnControl : JobGiver_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Default to Hauling for non-scan job givers as most are hauling related
        /// </summary>
        protected override string WorkTag => "Hauling";

        /// <summary>
        /// Define whether colonists should be considered as carriers
        /// </summary>
        protected virtual bool AllowColonistsAsCarriers => false;

        #endregion

        #region Caching

        /// <summary>
        /// Last update tick for caches
        /// </summary>
        protected static readonly Dictionary<int, int> _lastCacheUpdateTick = new Dictionary<int, int>();

        #endregion

        #region Core flow

        /// <summary>
        /// Creates a job without scanning for targets, using specialized non-scan methods
        /// </summary>
        protected override Job CreateJobFor(Pawn pawn, bool forced)
        {
            if (pawn?.Map == null)
                return null;

            int mapId = pawn.Map.uniqueID;
            int now = Find.TickManager.TicksGame;

            if (!ShouldExecuteNow(mapId))
                return null;

            // Update specialized caches if needed
            if (!_lastCacheUpdateTick.TryGetValue(mapId, out int last)
                || now - last >= CacheUpdateInterval)
            {
                _lastCacheUpdateTick[mapId] = now;
                UpdateSpecializedCache(pawn.Map, now);
            }

            // Delegate to derived class to create the job from specialized cache
            return CreateJobFromSpecializedCache(pawn, forced);
        }

        #endregion

        #region Hooks for derived classes

        /// <summary>
        /// Updates any specialized caches that the non-scan job giver uses
        /// </summary>
        protected abstract void UpdateSpecializedCache(Map map, int currentTick);

        /// <summary>
        /// Creates a job using specialized caches or non-scan methods
        /// </summary>
        protected abstract Job CreateJobFromSpecializedCache(Pawn pawn, bool forced);

        #endregion

        #region Cache management

        /// <summary>
        /// Reset all caches for this class
        /// </summary>
        public static void ResetCache()
        {
            _lastCacheUpdateTick.Clear();
        }

        #endregion
    }
}