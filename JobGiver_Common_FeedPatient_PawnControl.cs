using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Base JobGiver for feeding patients. Handles common logic for all patient feeding jobs.
    /// Can be specialized through inheritance to target specific patient types.
    /// </summary>
    public abstract class JobGiver_Common_FeedPatient_PawnControl : JobGiver_Doctor_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging
        /// </summary>
        protected override string DebugName => "feed patient";

        /// <summary>
        /// Whether this job giver requires a designator to operate
        /// </summary>
        protected override bool RequiresDesignator => false;

        /// <summary>
        /// Whether this job giver requires map zone or area
        /// </summary>
        protected override bool RequiresMapZoneorArea => false;

        /// <summary>
        /// Whether non-player pawns can feed patients (always within their own faction)
        /// </summary>
        protected override bool RequiresPlayerFaction => true;

        /// <summary>
        /// Whether this construction job requires specific tag for non-humanlike pawns
        /// </summary>
        public override PawnEnumTags RequiredTag => PawnEnumTags.AllowWork_Doctor;

        /// <summary>
        /// Update cache every 3 seconds for feeding jobs
        /// </summary>
        protected override int CacheUpdateInterval => 180; // 3 seconds

        /// <summary>
        /// Distance thresholds for bucketing
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 100f, 400f, 900f }; // 10, 20, 30 tiles

        // Configuration to be overridden by subclasses
        protected virtual bool FeedHumanlikesOnly => true;
        protected virtual bool FeedAnimalsOnly => false;
        protected virtual bool FeedPrisonersOnly => false;
        public override string WorkTag => "Doctor";
        protected virtual float JobPriority => 8.0f; // High priority - people shouldn't starve
        protected virtual string JobDescription => "feed patient";

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_Common_FeedPatient_PawnControl() : base()
        {
            // Base constructor already initializes the cache system
        }

        #endregion

        #region Core Flow

        /// <summary>
        /// Override to set the appropriate priority for feeding jobs
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            return JobPriority;
        }

        /// <summary>
        /// Checks if the map meets requirements for this medical job
        /// </summary>
        protected override bool AreMapRequirementsMet(Pawn pawn)
        {
            // Check for hungry pawns on the map that might need feeding
            return pawn?.Map != null && pawn.Map.mapPawns.AllPawnsSpawned.Any(p =>
                !ShouldSkipPawn(p) &&
                FeedPatientUtility.IsHungry(p) &&
                FeedPatientUtility.ShouldBeFed(p) &&
                IsPatientValidForDoctor(p, pawn));
        }

        #endregion

        #region Target Selection

        /// <summary>
        /// Get all potential hungry patients on the given map
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null) yield break;

            // Find all hungry pawns that need feeding
            foreach (Pawn potentialPatient in map.mapPawns.AllPawnsSpawned)
            {
                // Skip pawns that don't match our criteria
                if (ShouldSkipPawn(potentialPatient))
                    continue;

                // Check if hungry and should be fed
                if (!FeedPatientUtility.IsHungry(potentialPatient) ||
                    !FeedPatientUtility.ShouldBeFed(potentialPatient))
                    continue;

                // Check prisoner status if needed
                if (FeedPrisonersOnly && !potentialPatient.IsPrisoner)
                    continue;

                if (!FeedPrisonersOnly && potentialPatient.IsPrisoner)
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
        /// Implement to create the specific medical job for feeding
        /// </summary>
        protected override Job CreateMedicalJob(Pawn pawn, bool forced)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;

            // Get targets from the cache
            List<Thing> cachedTargets = GetCachedTargets(mapId);
            if (cachedTargets == null || cachedTargets.Count == 0)
                return null;

            // Filter to valid patients for this pawn
            List<Pawn> validPatients = cachedTargets
                .OfType<Pawn>()
                .Where(patient => IsPatientValidForDoctor(patient, pawn))
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

            // Find the best patient to feed - using the centralized reachability cache system
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

                        Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to feed {targetPatient.LabelShort}{foodInfo}");
                    }

                    return job;
                }
            }

            return null;
        }

        #endregion

        #region Patient Validation

        /// <summary>
        /// Determines if we should treat a pawn (i.e., feed them)
        /// </summary>
        protected override bool ShouldTreatPawn(Pawn patient, Pawn doctor)
        {
            // Skip pawns that don't match our criteria
            if (ShouldSkipPawn(patient))
                return false;

            // Check if hungry and should be fed
            if (!FeedPatientUtility.IsHungry(patient) || !FeedPatientUtility.ShouldBeFed(patient))
                return false;

            // Check prisoner status if needed
            if (FeedPrisonersOnly && !patient.IsPrisoner)
                return false;

            if (!FeedPrisonersOnly && patient.IsPrisoner)
                return false;

            // Check if this pawn is a valid patient
            return IsValidPatientType(patient);
        }

        /// <summary>
        /// Additional validation for a patient during job creation
        /// </summary>
        protected bool ValidatePatientForFeeding(Pawn patient, Pawn feeder, bool forced)
        {
            // Skip if no longer valid
            if (!IsValidPatient(patient, feeder, forced))
                return false;

            // Skip if no longer a valid hungry patient
            if (!FeedPatientUtility.IsHungry(patient) || !FeedPatientUtility.ShouldBeFed(patient))
                return false;

            // Check food restriction policy
            if (patient.foodRestriction != null)
            {
                FoodPolicy respectedRestriction = patient.foodRestriction.GetCurrentRespectedRestriction(feeder);
                if (respectedRestriction != null && respectedRestriction.filter.AllowedDefCount == 0)
                    return false;
            }

            // Check if there's food available for this patient
            Thing foodSource;
            ThingDef foodDef;
            bool starving = patient.needs?.food?.CurCategory == HungerCategory.Starving;
            return FoodUtility.TryFindBestFoodSourceFor(feeder, patient, starving,
                out foodSource, out foodDef, false, canUsePackAnimalInventory: true, allowVenerated: true);
        }

        /// <summary>
        /// Checks if a pawn is a valid patient type (humanlike/animal/prisoner)
        /// </summary>
        protected virtual bool IsValidPatientType(Pawn pawn)
        {
            // Must have a faction or be a prisoner
            if (pawn.Faction == null && !pawn.IsPrisoner)
                return false;

            // Check species requirements
            if (FeedHumanlikesOnly && !pawn.RaceProps.Humanlike)
                return false;

            if (FeedAnimalsOnly && !pawn.RaceProps.Animal)
                return false;

            return true;
        }

        /// <summary>
        /// Determines if a pawn should be skipped based on race and other criteria
        /// </summary>
        protected virtual bool ShouldSkipPawn(Pawn pawn)
        {
            // Check for null pawn
            if (pawn == null) return true;

            // Skip babies - they're handled differently
            if (pawn.DevelopmentalStage.Baby()) return true;

            // Apply species filters
            if (FeedHumanlikesOnly && !pawn.RaceProps.Humanlike) return true;
            if (FeedAnimalsOnly && !pawn.RaceProps.Animal) return true;

            // Skip ourselves - can't feed yourself
            if (pawn == Current.Game.CurrentMap?.mapPawns?.FreeColonistsSpawned.FirstOrDefault(p => p.CurJob?.def.defName == "FeedPatient"))
                return true;

            return false;
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
            return $"JobGiver_Common_FeedPatient_PawnControl({DebugName})";
        }

        #endregion
    }
}