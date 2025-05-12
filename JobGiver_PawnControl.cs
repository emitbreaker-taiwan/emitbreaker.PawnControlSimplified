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

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        protected abstract bool RequiresDesignator { get; }

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        protected abstract bool RequiresMapZoneorArea { get; }

        /// <summary>
        /// Whether this job giver requires player faction specifically (for jobs like deconstruct)
        /// </summary>
        protected abstract bool RequiresPlayerFaction { get; }

        /// <summary>
        /// Whether this construction job requires specific tag for non-humanlike pawns
        /// </summary>
        protected abstract PawnEnumTags RequiredTag { get; }

        /// <summary>
        /// Checks if a non-humanlike pawn has the required capabilities for this job giver
        /// </summary>
        protected abstract bool HasRequiredCapabilities(Pawn pawn);

        /// <summary>
        /// The designation type this job giver handles
        /// </summary>
        protected abstract DesignationDef TargetDesignation { get; }

        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected abstract JobDef WorkJobDef { get; }

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
            // Derive how often to run from the static base priority:
            float pri = GetBasePriority(WorkTag);

            // Avoid div-zero or sub-1 intervals:
            int interval = System.Math.Max(1, (int)System.Math.Round(60f / pri));
            return Find.TickManager.TicksGame % interval == 0;
        }

        #endregion

        #region Priority

        /// <summary>
        /// Presort custom job givers beforce ThinkTree_PrioritySorter sorts them.
        /// </summary>
        public virtual bool ShouldSkip(Pawn pawn)
        {
            // 1) Skip if no map/pawn
            if (pawn?.Map == null) return true;

            // 2) Skip if the Work-tab toggle is off
            if (!Utility_TagManager.WorkTypeSettingEnabled(pawn, WorkTag))
                return true;

            // 3) Skip if this jobgiver shouldn't run on this tick
            if (!ShouldExecuteNow(pawn.Map.uniqueID))
                return true;

            // 4) Skip if mod-extension or global-state rules block it
            if (!Utility_JobGiverManager.IsEligibleForSpecializedJobGiver(pawn, WorkTag))
                return true;

            // 5) Skip if pawn has no reuired capabilities
            if (!HasRequiredCapabilities(pawn))
                return true;

            return false;
        }

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
        protected virtual float GetBasePriority(string workTag)
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