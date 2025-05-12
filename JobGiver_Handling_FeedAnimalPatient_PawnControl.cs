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

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        protected override bool RequiresMapZoneorArea => false;

        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.FeedPatient;

        protected override bool FeedHumanlikesOnly => false;
        protected override bool FeedAnimalsOnly => true;
        protected override bool FeedPrisonersOnly => false;
        protected override string WorkTag => "Handling";
        protected override float JobPriority => 7.5f;  // Lowest priority among feeding jobs
        protected override string JobDescription => "feed animal patient (handling)";

        #endregion

        #region Overrides
        /// <summary>
        /// Whether this construction job requires specific tag for non-humanlike pawns
        /// </summary>
        protected override PawnEnumTags RequiredTag => PawnEnumTags.AllowWork_Handling;


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
            // Verify pawn is allowed to feed patients based on faction
            if (!IsValidFactionForFeedingPatients(pawn))
                return null;

            // Filter targets based on faction relationship
            List<Pawn> validPatients = new List<Pawn>();
            foreach (var target in targets)
            {
                if (target is Pawn animal &&
                    animal.RaceProps.Animal &&
                    !animal.Dead &&
                    !animal.Downed && // Only non-downed animals for handling
                    IsPatientValidForPawn(animal, pawn))
                {
                    validPatients.Add(animal);
                }
            }

            if (validPatients.Count > 0)
            {
                // Find closest valid animal patient
                Pawn closestPatient = null;
                float closestDistSq = float.MaxValue;

                foreach (Pawn patient in validPatients)
                {
                    float distSq = (patient.Position - pawn.Position).LengthHorizontalSquared;
                    if (distSq < closestDistSq)
                    {
                        closestDistSq = distSq;
                        closestPatient = patient;
                    }
                }

                if (closestPatient != null)
                {
                    // Create feed job for the closest animal
                    Thing foodSource;
                    ThingDef foodDef;
                    bool starving = closestPatient.needs?.food?.CurCategory == HungerCategory.Starving;

                    if (FoodUtility.TryFindBestFoodSourceFor(pawn, closestPatient, starving,
                        out foodSource, out foodDef, false, canUsePackAnimalInventory: true, allowVenerated: true))
                    {
                        float nutrition = FoodUtility.GetNutrition(closestPatient, foodSource, foodDef);
                        Job job = JobMaker.MakeJob(WorkJobDef);
                        job.targetA = foodSource;
                        job.targetB = closestPatient;
                        job.count = FoodUtility.WillIngestStackCountOf(closestPatient, foodDef, nutrition);
                        return job;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Special handling for animal patients - handlers only feed non-downed animals
        /// </summary>
        protected override bool IsPatientValidForPawn(Pawn patient, Pawn feeder)
        {
            if (!base.IsPatientValidForPawn(patient, feeder))
                return false;

            // Handlers only feed non-downed animals
            return !patient.Downed;
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