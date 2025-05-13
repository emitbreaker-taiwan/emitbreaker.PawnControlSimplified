using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver for feeding animal patients with the Handling work tag.
    /// </summary>
    public class JobGiver_Handling_FeedAnimalPatient_PawnControl : JobGiver_Common_FeedPatient_PawnControl
    {
        #region Configuration

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
        public override string WorkTag => "Handling";
        protected override float JobPriority => 7.5f;  // Lowest priority among feeding jobs
        protected override string JobDescription => "feed animal patient (handling)";

        /// <summary>
        /// Whether this construction job requires specific tag for non-humanlike pawns
        /// </summary>
        public override PawnEnumTags RequiredTag => PawnEnumTags.AllowWork_Handling;

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_Handling_FeedAnimalPatient_PawnControl() : base()
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
            // Use the standard job pipeline from parent class
            return base.TryGiveJob(pawn);
        }

        #endregion

        #region Target Selection

        /// <summary>
        /// Get all potential hungry animal patients on the given map for handlers
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null) yield break;

            // Find all hungry animals that need feeding and aren't downed
            foreach (Pawn potentialPatient in map.mapPawns.AllPawnsSpawned)
            {
                // Skip pawns that don't match our criteria
                if (ShouldSkipPawn(potentialPatient))
                    continue;

                // Check if hungry and should be fed
                if (!FeedPatientUtility.IsHungry(potentialPatient) ||
                    !FeedPatientUtility.ShouldBeFed(potentialPatient))
                    continue;

                // Must be an animal
                if (!potentialPatient.RaceProps.Animal)
                    continue;

                // Handlers only feed non-downed animals
                if (potentialPatient.Downed)
                    continue;

                // Check if this pawn is a valid patient
                if (!IsValidPatientType(potentialPatient))
                    continue;

                yield return potentialPatient;
            }
        }

        #endregion

        #region Job Creation

        /// <summary>
        /// Create a job for feeding an animal patient (handler version)
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
                    !animal.Downed && // Handlers only feed non-downed animals
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
                        Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to feed animal {targetAnimal.LabelShort} (handling)");
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
        /// Additional validation for animal patients - handlers only feed non-downed animals
        /// </summary>
        protected override bool IsValidPatientType(Pawn pawn)
        {
            if (!base.IsValidPatientType(pawn))
                return false;

            // Must be an animal and not downed
            return pawn.RaceProps.Animal && !pawn.Downed;
        }

        /// <summary>
        /// Determines if we should feed an animal patient as a handler
        /// </summary>
        protected override bool ShouldTreatPawn(Pawn patient, Pawn doctor)
        {
            // Must be an animal and not downed
            if (!patient.RaceProps.Animal || patient.Downed)
                return false;

            // Perform the base checks for feeding
            return base.ShouldTreatPawn(patient, doctor);
        }

        /// <summary>
        /// Special handling for animal patients - handlers only feed non-downed animals
        /// </summary>
        protected override bool IsPatientValidForDoctor(Pawn patient, Pawn doctor)
        {
            if (!base.IsPatientValidForDoctor(patient, doctor))
                return false;

            // Handlers only feed non-downed animals
            return !patient.Downed;
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
            return "JobGiver_Handling_FeedAnimalPatient_PawnControl";
        }

        #endregion
    }
}