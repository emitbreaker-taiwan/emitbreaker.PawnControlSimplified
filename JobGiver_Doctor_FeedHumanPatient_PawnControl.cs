using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver for feeding humanlike non-prisoner patients. Highest priority among feeding jobs.
    /// </summary>
    public class JobGiver_Doctor_FeedHumanPatient_PawnControl : JobGiver_Common_FeedPatient_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        public override bool RequiresMapZoneorArea => false;
        protected override bool FeedHumanlikesOnly => true;
        protected override bool FeedAnimalsOnly => false;
        protected override bool FeedPrisonersOnly => false;
        public override string WorkTag => "Doctor";
        protected override float JobPriority => 8.5f;  // Highest priority
        protected override string JobDescription => "feed humanlike patient";

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_Doctor_FeedHumanPatient_PawnControl() : base()
        {
            // Base constructor already initializes the cache system
        }

        #endregion

        #region Core Flow

        /// <summary>
        /// Standard implementation of TryGiveJob that ensures proper faction validation
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (ShouldSkip(pawn))
            {
                return null;
            }

            // Use the standard job pipeline from parent class
            return base.TryGiveJob(pawn);
        }

        #endregion

        #region Target Selection

        /// <summary>
        /// Get all potential hungry humanlike patients on the given map
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // Filter targets from the parent implementation to only include humanlike non-prisoners
            foreach (Thing target in base.GetTargets(map))
            {
                if (target is Pawn patient &&
                    patient.RaceProps.Humanlike &&
                    !patient.IsPrisoner)
                {
                    yield return patient;
                }
            }
        }

        #endregion

        #region Job Creation

        /// <summary>
        /// Create a job for feeding a humanlike patient
        /// </summary>
        protected override Job CreateMedicalJob(Pawn pawn, bool forced)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;

            // Get targets from the cache
            List<Thing> cachedTargets = GetCachedTargets(mapId);
            if (cachedTargets == null || cachedTargets.Count == 0)
                return null;

            // Filter to valid humanlike patients for this pawn
            List<Pawn> validPatients = cachedTargets
                .OfType<Pawn>()
                .Where(patient =>
                    patient.RaceProps.Humanlike &&
                    !patient.IsPrisoner &&
                    !ShouldSkipPawn(patient) &&
                    IsPatientValidForDoctor(patient, pawn))
                .ToList();

            if (validPatients.Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                validPatients,
                (patient) => (patient.Position - pawn.Position).LengthHorizontalSquared,
                DistanceThresholds
            );

            // Find the best patient to feed using the centralized cache system
            Pawn targetPatient = (Pawn)Utility_JobGiverManager.FindFirstValidTargetInBuckets<Pawn>(
                buckets,
                pawn,
                (patient, feeder) => ValidatePatientForFeeding((Pawn)patient, feeder, forced),
                null // Let the JobGiverManager handle reachability caching internally
            );

            // Create job if target found
            if (targetPatient != null)
            {
                // Find the best food source
                Thing foodSource;
                ThingDef foodDef;
                bool starving = targetPatient.needs?.food?.CurCategory == HungerCategory.Starving;

                if (FoodUtility.TryFindBestFoodSourceFor(pawn, targetPatient, starving,
                    out foodSource, out foodDef, false, canUsePackAnimalInventory: true, allowVenerated: true))
                {
                    float nutrition = FoodUtility.GetNutrition(targetPatient, foodSource, foodDef);
                    Job job = JobMaker.MakeJob(JobDefOf.FeedPatient);
                    job.targetA = foodSource;
                    job.targetB = targetPatient;
                    job.count = FoodUtility.WillIngestStackCountOf(targetPatient, foodDef, nutrition);

                    if (Prefs.DevMode)
                    {
                        string foodInfo = "";
                        if (FoodUtility.MoodFromIngesting(targetPatient, foodSource, FoodUtility.GetFinalIngestibleDef(foodSource)) < 0)
                            foodInfo = " (disliked food)";

                        Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to feed humanlike {targetPatient.LabelShort}{foodInfo}");
                    }

                    return job;
                }
            }

            return null;
        }

        /// <summary>
        /// Processes cached targets for feeding humanlike patients
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // Verify pawn is allowed to feed patients based on faction
            if (!IsValidFactionForMedical(pawn))
                return null;

            // Use the CreateMedicalJob method to create the job
            return CreateMedicalJob(pawn, forced);
        }

        #endregion

        #region Patient Validation

        /// <summary>
        /// Enforces additional validation specific to humanlike patients
        /// </summary>
        protected override bool IsValidPatientType(Pawn pawn)
        {
            if (!base.IsValidPatientType(pawn))
                return false;

            // Must be humanlike and not a prisoner
            return pawn.RaceProps.Humanlike && !pawn.IsPrisoner;
        }

        /// <summary>
        /// Determines if we should feed a humanlike patient
        /// </summary>
        protected override bool ShouldTreatPawn(Pawn patient, Pawn doctor)
        {
            // Must be humanlike and not a prisoner
            if (!patient.RaceProps.Humanlike || patient.IsPrisoner)
                return false;

            // Perform the base checks for feeding
            return base.ShouldTreatPawn(patient, doctor);
        }

        /// <summary>
        /// Enforces additional validation specific to humanlike patients
        /// </summary>
        protected override bool IsPatientValidForDoctor(Pawn patient, Pawn doctor)
        {
            if (!base.IsPatientValidForDoctor(patient, doctor))
                return false;

            // Additional check to ensure we're not handling prisoners
            return !patient.IsPrisoner;
        }

        #endregion

        #region Reset

        /// <summary>
        /// Reset the cache - implements IResettableCache
        /// </summary>
        public override void Reset()
        {
            // Use centralized cache reset from parent
            base.Reset();
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