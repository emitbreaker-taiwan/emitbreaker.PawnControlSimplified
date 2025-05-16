using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver for feeding animal patients with the Doctor work tag.
    /// </summary>
    public class JobGiver_Doctor_FeedAnimalPatient_PawnControl : JobGiver_Common_FeedPatient_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        public override bool RequiresMapZoneorArea => false;

        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.FeedPatient;

        protected override bool FeedHumanlikesOnly => false;
        protected override bool FeedAnimalsOnly => true;
        protected override bool FeedPrisonersOnly => false;
        public override string WorkTag => "Doctor";
        protected override float JobPriority => 7.8f;  // Lower priority than feeding humanlike patients
        protected override string JobDescription => "feed animal patient (doctor)";

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_Doctor_FeedAnimalPatient_PawnControl() : base()
        {
            // Base constructor already initializes the cache system
        }

        #endregion

        #region Core Flow

        /// <summary>
        /// Specialized TryGiveJob implementation for animal feeding
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
        /// Get all potential hungry animal patients on the given map
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // Filter targets from the parent implementation to only include animals
            foreach (Thing target in base.GetTargets(map))
            {
                if (target is Pawn animal && animal.RaceProps.Animal)
                {
                    yield return animal;
                }
            }
        }

        #endregion

        #region Job Creation

        /// <summary>
        /// Create a job for feeding an animal patient
        /// </summary>
        protected override Job CreateMedicalJob(Pawn pawn, bool forced)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;

            // Get targets from the cache
            List<Thing> cachedTargets = GetCachedTargets(mapId);
            if (cachedTargets == null || cachedTargets.Count == 0)
                return null;

            // Filter to valid animal patients for this pawn
            List<Pawn> validAnimals = cachedTargets
                .OfType<Pawn>()
                .Where(animal =>
                    animal.RaceProps.Animal &&
                    !animal.Dead &&
                    IsPatientValidForDoctor(animal, pawn))
                .ToList();

            if (validAnimals.Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                validAnimals,
                (animal) => (animal.Position - pawn.Position).LengthHorizontalSquared,
                DistanceThresholds
            );

            // Find the best animal to feed using the centralized cache system
            Pawn targetAnimal = (Pawn)Utility_JobGiverManager.FindFirstValidTargetInBuckets<Pawn>(
                buckets,
                pawn,
                (animal, feeder) => ValidatePatientForFeeding((Pawn)animal, feeder, forced),
                null // Let the JobGiverManager handle reachability caching internally
            );

            // Create job if target found
            if (targetAnimal != null)
            {
                // Find the best food source
                Thing foodSource;
                ThingDef foodDef;
                bool starving = targetAnimal.needs?.food?.CurCategory == HungerCategory.Starving;

                if (FoodUtility.TryFindBestFoodSourceFor(pawn, targetAnimal, starving,
                    out foodSource, out foodDef, false, canUsePackAnimalInventory: true, allowVenerated: true))
                {
                    float nutrition = FoodUtility.GetNutrition(targetAnimal, foodSource, foodDef);
                    Job job = JobMaker.MakeJob(JobDefOf.FeedPatient);
                    job.targetA = foodSource;
                    job.targetB = targetAnimal;
                    job.count = FoodUtility.WillIngestStackCountOf(targetAnimal, foodDef, nutrition);

                    if (Prefs.DevMode)
                    {
                        Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to feed animal {targetAnimal.LabelShort}");
                    }

                    return job;
                }
            }

            return null;
        }

        /// <summary>
        /// Processes cached targets for feeding animal patients
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
        /// Additional validation for animal patients
        /// </summary>
        protected override bool IsValidPatientType(Pawn pawn)
        {
            return pawn.RaceProps.Animal && base.IsValidPatientType(pawn);
        }

        /// <summary>
        /// Determines if we should feed an animal patient
        /// </summary>
        protected override bool ShouldTreatPawn(Pawn patient, Pawn doctor)
        {
            // Must be an animal
            if (!patient.RaceProps.Animal)
                return false;

            // Perform the base checks for feeding
            return base.ShouldTreatPawn(patient, doctor);
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
            return "JobGiver_Doctor_FeedAnimalPatient_PawnControl";
        }

        #endregion
    }
}