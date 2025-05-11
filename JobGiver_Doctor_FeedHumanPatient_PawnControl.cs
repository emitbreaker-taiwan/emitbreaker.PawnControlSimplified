using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver for feeding humanlike non-prisoner patients. Highest priority among feeding jobs.
    /// </summary>
    public class JobGiver_Doctor_FeedHumanPatient_PawnControl : JobGiver_Common_FeedPatient_PawnControl
    {
        #region Override Configuration

        protected override bool FeedHumanlikesOnly => true;
        protected override bool FeedAnimalsOnly => false;
        protected override bool FeedPrisonersOnly => false;
        protected override string WorkTag => "Doctor";
        protected override float JobPriority => 8.5f;  // Highest priority
        protected override string JobDescription => "feed humanlike patient";

        #endregion

        #region Overrides

        /// <summary>
        /// Override to use the generic CreateFeedJob helper with the correct type
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            return CreateFeedJob<JobGiver_Doctor_FeedHumanPatient_PawnControl>(pawn);
        }

        /// <summary>
        /// Processes cached targets for feeding jobs.
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            foreach (var target in targets)
            {
                if (target is Pawn patient && !ShouldSkipPawn(patient))
                {
                    return CreateFeedJob<JobGiver_Doctor_FeedHumanPatient_PawnControl>(patient);
                }
            }
            return null;
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return "JobGiver_Doctor_FeedHumanPatient_PawnControl";
        }

        #endregion
    }
}