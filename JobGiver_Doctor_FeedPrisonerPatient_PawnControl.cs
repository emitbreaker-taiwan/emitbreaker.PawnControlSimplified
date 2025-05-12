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

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        protected override bool RequiresMapZoneorArea => false;
        protected override bool FeedHumanlikesOnly => true;
        protected override bool FeedAnimalsOnly => false;
        protected override bool FeedPrisonersOnly => true;
        protected override string WorkTag => "Doctor";
        protected override float JobPriority => 8.0f;  // Slightly lower priority than feeding colonists
        protected override string JobDescription => "feed prisoner patient";

        // Only player faction can feed prisoners (not other factions)
        protected override bool RequiresPlayerFaction => false;

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
            // Verify pawn is allowed to feed patients based on faction (only player pawns)
            if (!IsValidFactionForFeedingPatients(pawn))
                return null;

            // Filter targets to find valid prisoner patients
            foreach (var target in targets)
            {
                if (target is Pawn patient &&
                    patient.RaceProps.Humanlike &&
                    patient.IsPrisoner &&
                    !ShouldSkipPawn(patient) &&
                    IsPatientValidForPawn(patient, pawn))
                {
                    // Create feed job for the prisoner patient
                    return CreateFeedJob<JobGiver_Doctor_FeedPrisonerPatient_PawnControl>(pawn);
                }
            }

            return null;
        }

        /// <summary>
        /// Override faction validation - only player pawns and slaves can feed prisoners
        /// </summary>
        protected override bool IsValidFactionForFeedingPatients(Pawn pawn)
        {
            // Only player pawns and their slaves can feed prisoners
            return pawn.Faction == Faction.OfPlayer ||
                   (pawn.IsSlave && pawn.HostFaction == Faction.OfPlayer);
        }

        /// <summary>
        /// Enforces prisoner-specific validation rules
        /// </summary>
        protected override bool IsPatientValidForPawn(Pawn patient, Pawn feeder)
        {
            // Only player faction can feed prisoners
            if (feeder.Faction != Faction.OfPlayer &&
                !(feeder.IsSlave && feeder.HostFaction == Faction.OfPlayer))
                return false;

            // Must be a prisoner of the player
            return patient.IsPrisoner && patient.HostFaction == Faction.OfPlayer;
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