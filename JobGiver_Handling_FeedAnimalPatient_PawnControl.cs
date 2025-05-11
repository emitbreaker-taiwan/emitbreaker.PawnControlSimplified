using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver for feeding animal patients with the Handling work tag.
    /// </summary>
    public class JobGiver_Handling_FeedAnimalPatient_PawnControl : JobGiver_Common_FeedPatient_PawnControl
    {
        #region Override Configuration

        protected override bool FeedHumanlikesOnly => false;
        protected override bool FeedAnimalsOnly => true;
        protected override bool FeedPrisonersOnly => false;
        protected override string WorkTag => "Handling";
        protected override float JobPriority => 7.5f;  // Lowest priority among feeding jobs
        protected override string JobDescription => "feed animal patient (handling)";

        #endregion

        #region Overrides

        /// <summary>
        /// Override to use the generic CreateFeedJob helper with the correct type
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            return CreateFeedJob<JobGiver_Handling_FeedAnimalPatient_PawnControl>(pawn);
        }

        /// <summary>
        /// Processes cached targets for feeding animal patients.
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            foreach (var target in targets)
            {
                if (target is Pawn animal && animal.RaceProps.Animal && !animal.Dead && !animal.Downed)
                {
                    return JobMaker.MakeJob(JobDefOf.FeedPatient, animal);
                }
            }
            return null;
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return "JobGiver_Handling_FeedAnimalPatient_PawnControl";
        }

        #endregion
    }
}