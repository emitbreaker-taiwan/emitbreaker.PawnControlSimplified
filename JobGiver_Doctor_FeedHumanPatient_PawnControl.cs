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

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        protected override bool RequiresMapZoneorArea => false;
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
        /// Processes cached targets for feeding humanlike patients.
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // Verify pawn is allowed to feed patients based on faction
            if (!IsValidFactionForFeedingPatients(pawn))
                return null;

            // Filter targets based on faction relationship
            foreach (var target in targets)
            {
                if (target is Pawn patient &&
                    patient.RaceProps.Humanlike &&
                    !patient.IsPrisoner &&
                    !ShouldSkipPawn(patient) &&
                    IsPatientValidForPawn(patient, pawn))
                {
                    // Create feed job for the humanlike patient
                    return CreateFeedJob<JobGiver_Doctor_FeedHumanPatient_PawnControl>(pawn);
                }
            }

            return null;
        }

        /// <summary>
        /// Enforces additional validation specific to humanlike patients
        /// </summary>
        protected override bool IsPatientValidForPawn(Pawn patient, Pawn feeder)
        {
            if (!base.IsPatientValidForPawn(patient, feeder))
                return false;

            // Additional check to ensure we're not handling prisoners
            return !patient.IsPrisoner;
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