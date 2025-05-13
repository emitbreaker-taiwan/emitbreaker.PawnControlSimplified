using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver for feeding humanlike prisoner patients.
    /// </summary>
    public class JobGiver_Doctor_FeedPrisonerPatient_PawnControl : JobGiver_Common_FeedPatient_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        protected override bool RequiresMapZoneorArea => false;
        protected override bool FeedHumanlikesOnly => true;
        protected override bool FeedAnimalsOnly => false;
        protected override bool FeedPrisonersOnly => true;
        public override string WorkTag => "Doctor";
        protected override float JobPriority => 8.0f;  // Slightly lower priority than feeding colonists
        protected override string JobDescription => "feed prisoner patient";

        // Only player faction can feed prisoners (not other factions)
        protected override bool RequiresPlayerFaction => false;

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_Doctor_FeedPrisonerPatient_PawnControl() : base()
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

        /// <summary>
        /// Override faction validation for prisoner feeding - only player pawns and slaves can feed prisoners
        /// </summary>
        protected override bool IsValidFactionForMedical(Pawn pawn)
        {
            // Only player pawns and their slaves can feed prisoners
            return pawn.Faction == Faction.OfPlayer ||
                   (pawn.IsSlave && pawn.HostFaction == Faction.OfPlayer);
        }

        #endregion

        #region Target Selection

        /// <summary>
        /// Get all potential hungry prisoner patients on the given map
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null) yield break;

            // Find all hungry prisoners that need feeding
            foreach (Pawn potentialPatient in map.mapPawns.AllPawnsSpawned)
            {
                // Skip pawns that don't match our criteria
                if (ShouldSkipPawn(potentialPatient))
                    continue;

                // Check if hungry and should be fed
                if (!FeedPatientUtility.IsHungry(potentialPatient) ||
                    !FeedPatientUtility.ShouldBeFed(potentialPatient))
                    continue;

                // Must be a prisoner
                if (!potentialPatient.IsPrisoner || potentialPatient.HostFaction != Faction.OfPlayer)
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
        /// Create a job for feeding a prisoner patient
        /// </summary>
        protected override Job CreateMedicalJob(Pawn pawn, bool forced)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;

            // Get targets from the cache
            List<Thing> cachedTargets = GetCachedTargets(mapId);
            if (cachedTargets == null || cachedTargets.Count == 0)
                return null;

            // Filter to valid prisoner patients for this pawn
            List<Pawn> validPatients = cachedTargets
                .OfType<Pawn>()
                .Where(patient =>
                    patient.RaceProps.Humanlike &&
                    patient.IsPrisoner &&
                    patient.HostFaction == Faction.OfPlayer &&
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

            // Find the best prisoner to feed using the centralized cache system
            Pawn targetPrisoner = (Pawn)Utility_JobGiverManager.FindFirstValidTargetInBuckets<Pawn>(
                buckets,
                pawn,
                (patient, feeder) => ValidatePatientForFeeding((Pawn)patient, feeder, forced),
                null // Let the JobGiverManager handle reachability caching internally
            );

            // Create job if target found
            if (targetPrisoner != null)
            {
                // Find the best food source
                Thing foodSource;
                ThingDef foodDef;
                bool starving = targetPrisoner.needs?.food?.CurCategory == HungerCategory.Starving;

                if (FoodUtility.TryFindBestFoodSourceFor(pawn, targetPrisoner, starving,
                    out foodSource, out foodDef, false, canUsePackAnimalInventory: true, allowVenerated: true))
                {
                    float nutrition = FoodUtility.GetNutrition(targetPrisoner, foodSource, foodDef);
                    Job job = JobMaker.MakeJob(JobDefOf.FeedPatient);
                    job.targetA = foodSource;
                    job.targetB = targetPrisoner;
                    job.count = FoodUtility.WillIngestStackCountOf(targetPrisoner, foodDef, nutrition);

                    if (Prefs.DevMode)
                    {
                        string foodInfo = "";
                        if (FoodUtility.MoodFromIngesting(targetPrisoner, foodSource, FoodUtility.GetFinalIngestibleDef(foodSource)) < 0)
                            foodInfo = " (disliked food)";

                        Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to feed prisoner {targetPrisoner.LabelShort}{foodInfo}");
                    }

                    return job;
                }
            }

            return null;
        }

        /// <summary>
        /// Processes cached targets for feeding prisoner patients
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
        /// Checks if a pawn is a valid patient type (prisoner-specific rules)
        /// </summary>
        protected override bool IsValidPatientType(Pawn pawn)
        {
            if (!base.IsValidPatientType(pawn))
                return false;

            // Must be a prisoner of the player faction
            return pawn.IsPrisoner && pawn.HostFaction == Faction.OfPlayer;
        }

        /// <summary>
        /// Determines if we should feed a prisoner patient
        /// </summary>
        protected override bool ShouldTreatPawn(Pawn patient, Pawn doctor)
        {
            // Must be a prisoner of the player faction
            if (!patient.IsPrisoner || patient.HostFaction != Faction.OfPlayer)
                return false;

            // Perform the base checks for feeding
            return base.ShouldTreatPawn(patient, doctor);
        }

        /// <summary>
        /// Enforces prisoner-specific validation rules
        /// </summary>
        protected override bool IsPatientValidForDoctor(Pawn patient, Pawn doctor)
        {
            // Validate with base rules first
            if (!base.IsPatientValidForDoctor(patient, doctor))
                return false;

            // Only player faction can feed prisoners
            if (doctor.Faction != Faction.OfPlayer &&
                !(doctor.IsSlave && doctor.HostFaction == Faction.OfPlayer))
                return false;

            // Must be a prisoner of the player
            return patient.IsPrisoner && patient.HostFaction == Faction.OfPlayer;
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
            return "JobGiver_Doctor_FeedPrisonerPatient_PawnControl";
        }

        #endregion
    }
}