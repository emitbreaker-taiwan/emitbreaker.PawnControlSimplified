using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver for feeding humanlike prisoner patients.
    /// </summary>
    public class JobGiver_Doctor_FeedPrisonerPatient_PawnControl : JobGiver_Common_FeedPatient_PawnControl
    {
        #region Override Configuration

        protected override bool FeedHumanlikesOnly => true;
        protected override bool FeedAnimalsOnly => false;
        protected override bool FeedPrisonersOnly => true;
        protected override string WorkTag => "Doctor";
        protected override float JobPriority => 8.0f;  // Slightly lower priority than feeding colonists
        protected override string JobDescription => "feed prisoner patient";

        #endregion

        #region Overrides

        /// <summary>
        /// Override to use the generic CreateFeedJob helper with the correct type
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            return CreateFeedJob<JobGiver_Doctor_FeedPrisonerPatient_PawnControl>(pawn);
        }

        /// <summary>
        /// Processes cached targets for feeding prisoner patients.
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            foreach (var target in targets)
            {
                if (target is Pawn patient && !ShouldSkipPawn(patient))
                {
                    return CreateFeedJob<JobGiver_Doctor_FeedPrisonerPatient_PawnControl>(pawn);
                }
            }
            return null;
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return "JobGiver_Doctor_FeedPrisonerPatient_PawnControl";
        }

        #endregion
    }
}