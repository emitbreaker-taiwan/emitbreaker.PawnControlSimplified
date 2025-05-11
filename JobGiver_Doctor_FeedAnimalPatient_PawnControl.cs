using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver for feeding animal patients with the Doctor work tag.
    /// </summary>
    public class JobGiver_Doctor_FeedAnimalPatient_PawnControl : JobGiver_Common_FeedPatient_PawnControl
    {
        #region Override Configuration

        protected override bool FeedHumanlikesOnly => false;
        protected override bool FeedAnimalsOnly => true;
        protected override bool FeedPrisonersOnly => false;
        protected override string WorkTag => "Doctor";
        protected override float JobPriority => 7.8f;  // Lower priority than feeding humanlike patients
        protected override string JobDescription => "feed animal patient (doctor)";

        #endregion

        #region Overrides

        /// <summary>
        /// Override to use the generic CreateFeedJob helper with the correct type
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            return CreateFeedJob<JobGiver_Doctor_FeedAnimalPatient_PawnControl>(pawn);
        }

        /// <summary>
        /// Processes cached targets for feeding animal patients.
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            foreach (var target in targets)
            {
                if (target is Pawn animal && animal.RaceProps.Animal && !animal.Dead && pawn.CanReserveAndReach(animal, PathEndMode.Touch, Danger.Deadly))
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
            return "JobGiver_Doctor_FeedAnimalPatient_PawnControl";
        }

        #endregion
    }
}