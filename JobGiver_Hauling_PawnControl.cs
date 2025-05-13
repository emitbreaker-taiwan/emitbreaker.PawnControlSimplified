using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Base class for hauling job givers with specialized cache management
    /// </summary>
    public abstract class JobGiver_Hauling_PawnControl : JobGiver_Scan_PawnControl
    {
        #region Configuration

        public override string WorkTag => "Hauling";

        protected virtual float[] DistanceThresholds => new float[] { 225f, 625f, 2500f }; // 15, 25, 50 tiles

        public override PawnEnumTags RequiredTag => PawnEnumTags.AllowWork_Hauling;

        #endregion

        #region Cache System

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_Hauling_PawnControl() : base()
        {
            // Base constructor already initializes the cache system with this job giver's type
        }

        /// <summary>
        /// Reset the cache - implements IResettableCache
        /// </summary>
        public override void Reset()
        {
            base.Reset();
        }

        #endregion

        #region Core flow

        /// <summary>
        /// Job-specific cache update method that derived classes should override to implement
        /// specialized target collection logic.
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            // By default, use the standard GetTargets method
            // Derived classes can override to provide specialized caching
            return GetTargets(map);
        }

        #endregion

        #region Hooks for derived classes

        // Required abstract method implementation from JobGiver_Scan_PawnControl
        protected abstract override IEnumerable<Thing> GetTargets(Map map);

        // Required abstract method implementation from JobGiver_Scan_PawnControl
        protected abstract override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced);

        #endregion

        #region Utility

        // Specialized hauling methods
        protected virtual bool CanHaulThing(Thing t, Pawn p) { return true;/* Common hauling logic */ }

        #endregion
    }
}