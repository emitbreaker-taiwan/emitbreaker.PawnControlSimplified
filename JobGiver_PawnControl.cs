using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Provides a common base structure for all PawnControl JobGivers.
    /// This abstract class defines the shared interface and functionality
    /// while allowing derived classes to implement their own caching systems.
    /// </summary>
    public abstract class JobGiver_PawnControl : ThinkNode_JobGiver
    {
        #region Configuration

        /// <summary>
        /// Tag used for eligibility checks in the wrapper
        /// </summary>
        protected abstract string WorkTag { get; }

        /// <summary>
        /// Human-readable name for debug logging
        /// </summary>
        protected virtual string DebugName => GetType().Name;

        /// <summary>
        /// How many ticks between cache rebuilds
        /// </summary>
        protected virtual int CacheUpdateInterval => 120;

        #endregion

        #region Core flow

        /// <summary>
        /// Standard implementation of TryGiveJob that delegates to the derived class's
        /// job creation logic.
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (ShouldSkip(pawn))
            {
                return null;
            }

            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) => CreateJobFor(p, forced),
                debugJobDesc: DebugName,
                skipEmergencyCheck: false,
                jobGiverType: GetType()
            );
        }

        /// <summary>
        /// Creates a job for the given pawn. This is where derived classes should implement
        /// their specific job creation logic.
        /// </summary>
        /// <param name="pawn">The pawn to create a job for</param>
        /// <param name="forced">Whether the job was forced</param>
        /// <returns>A job if one could be created, null otherwise</returns>
        protected abstract Job CreateJobFor(Pawn pawn, bool forced);

        /// <summary>
        /// Determines if the job giver should execute on this tick.
        /// By default, executes every 5 ticks.
        /// </summary>
        protected virtual bool ShouldExecuteNow(int mapId)
        {
            return Find.TickManager.TicksGame % 5 == 0;
        }

        #endregion

        protected virtual bool ShouldSkip(Pawn pawn)
        {
            // Skip if there are no pawns with UnloadEverything flag on the map (quick optimization)
            if (pawn?.Map == null)
                return true;
            return false;
        }

        #region Priority

        /// <summary>
        /// Unified priority lookup based on WorkTag
        /// </summary>
        public override float GetPriority(Pawn pawn)
        {
            return GetBasePriority(WorkTag);
        }

        /// <summary>
        /// Standard priority lookup table based on work type
        /// </summary>
        protected static float GetBasePriority(string workTag)
        {
            switch (workTag)
            {
                // Emergency/Critical
                case "Firefighter": return 9.0f;
                case "Patient": return 8.8f;
                case "Doctor": return 8.5f;

                // High Priority
                case "PatientBedRest": return 8.0f;
                case "BasicWorker": return 7.8f;
                case "Childcare": return 7.5f;
                case "Warden": return 7.2f;
                case "Handling": return 7.0f;
                case "Cooking": return 6.8f;

                // Medium-High Priority
                case "Hunting": return 6.5f;
                case "Construction": return 6.2f;
                case "Growing": return 5.8f;
                case "Mining": return 5.5f;

                // Medium Priority
                case "PlantCutting": return 5.2f;
                case "Smithing": return 4.9f;
                case "Tailoring": return 4.7f;
                case "Art": return 4.5f;
                case "Crafting": return 4.3f;

                // Low Priority
                case "Hauling": return 3.9f;
                case "Cleaning": return 3.5f;
                case "Research": return 3.2f;
                case "DarkStudy": return 3.0f;

                default: return 5.0f;
            }
        }

        #endregion

        #region Debug support

        public override string ToString()
        {
            return DebugName;
        }

        #endregion
    }
}