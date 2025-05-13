using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Common base class for all doctor-related job givers.
    /// Handles faction validation and provides standard structure for medical tasks.
    /// </summary>
    public abstract class JobGiver_Doctor_PawnControl : JobGiver_Scan_PawnControl
    {
        #region Configuration

        /// <summary>
        /// All doctor job givers use this work tag
        /// </summary>
        public override string WorkTag => "Doctor";

        /// <summary>
        /// Human-readable name for debug logging
        /// </summary>
        protected override string DebugName => "Doctor";

        /// <summary>
        /// Standard distance thresholds for bucketing
        /// </summary>
        protected virtual float[] DistanceThresholds => new float[] { 100f, 400f, 900f }; // 10, 20, 30 tiles

        /// <summary>
        /// Whether this job giver requires a designator to operate
        /// </summary>
        protected override bool RequiresDesignator => false;

        /// <summary>
        /// Whether this job giver requires map zone or area
        /// </summary>
        protected override bool RequiresMapZoneorArea => false;

        /// <summary>
        /// Whether this job giver requires player faction specifically
        /// </summary>
        protected override bool RequiresPlayerFaction => false;

        /// <summary>
        /// Whether this doctor job requires specific tag for non-humanlike pawns
        /// </summary>
        public override PawnEnumTags RequiredTag => PawnEnumTags.AllowWork_Doctor;

        /// <summary>
        /// Update cache every 3 seconds for medical tasks (patients' conditions change rapidly)
        /// </summary>
        protected override int CacheUpdateInterval => 180;

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_Doctor_PawnControl() : base()
        {
            // Base constructor already initializes the cache system
        }

        #endregion

        #region Faction Validation

        /// <summary>
        /// Common implementation for ShouldSkip that enforces faction requirements
        /// </summary>
        public override bool ShouldSkip(Pawn pawn)
        {
            if (base.ShouldSkip(pawn))
                return true;

            // Use the standardized faction validation for medical jobs
            if (!IsValidFactionForMedical(pawn))
                return true;

            return false;
        }

        /// <summary>
        /// Determines if the pawn's faction is allowed to perform medical work
        /// Can be overridden by derived classes to customize faction rules
        /// </summary>
        protected virtual bool IsValidFactionForMedical(Pawn pawn)
        {
            // Check if player faction is specifically required
            if (RequiresPlayerFaction)
            {
                return pawn.Faction == Faction.OfPlayer ||
                       (pawn.IsSlave && pawn.HostFaction == Faction.OfPlayer);
            }

            // For general medical jobs, pawns can work for their own faction (or host faction if slave)
            return Utility_JobGiverManager.IsValidFactionInteraction(null, pawn, RequiresDesignator);
        }

        /// <summary>
        /// Determines if a patient is valid for a specific doctor pawn based on faction relationships
        /// </summary>
        protected virtual bool IsPatientValidForDoctor(Pawn patient, Pawn doctor)
        {
            // Player pawns can treat their own colonists, slaves, prisoners, and animals
            if (doctor.Faction == Faction.OfPlayer ||
               (doctor.IsSlave && doctor.HostFaction == Faction.OfPlayer))
            {
                // For player doctors, we check if the patient is:
                // 1. A colonist (same faction)
                // 2. A prisoner of the player
                // 3. An animal owned by the player
                // 4. A slave owned by the player
                return patient.Faction == Faction.OfPlayer ||
                       (patient.guest != null && patient.HostFaction == Faction.OfPlayer) ||
                       (patient.IsPrisoner && patient.HostFaction == Faction.OfPlayer) ||
                       (patient.RaceProps.Animal && patient.Faction == Faction.OfPlayer) ||
                       (patient.IsSlave && patient.HostFaction == Faction.OfPlayer);
            }

            // For non-player pawns, they can only treat patients of their own faction
            // This includes their own faction's animals
            if (doctor.Faction != null)
            {
                return patient.Faction == doctor.Faction ||
                       (patient.guest != null && patient.HostFaction == doctor.Faction) ||
                       (patient.RaceProps.Animal && patient.Faction == doctor.Faction);
            }

            // No valid relationship found
            return false;
        }

        #endregion

        #region Core Flow

        /// <summary>
        /// Standard implementation of TryGiveJob that ensures proper faction validation
        /// and checks map requirements before proceeding with job creation
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            // Skip if already filtered out by base class
            if (ShouldSkip(pawn))
                return null;

            // Skip if map requirements not met
            if (!AreMapRequirementsMet(pawn))
                return null;

            // Use the standardized job creation pattern with centralized cache
            return base.TryGiveJob(pawn);
        }

        /// <summary>
        /// Template method for creating a job that handles cache update logic
        /// </summary>
        protected override Job CreateJobFor(Pawn pawn, bool forced)
        {
            if (pawn?.Map == null)
                return null;

            int mapId = pawn.Map.uniqueID;

            if (!ShouldExecuteNow(mapId))
                return null;

            // Update cache if needed using the centralized cache system
            if (ShouldUpdateCache(mapId))
            {
                UpdateCache(mapId, pawn.Map);
            }

            // Get targets from the cache
            var targets = GetCachedTargets(mapId);

            // Skip if no targets and they're required
            if ((targets == null || targets.Count == 0) && RequiresPawnTargets())
                return null;

            // Call the specialized medical job creation method
            return CreateMedicalJob(pawn, forced);
        }

        /// <summary>
        /// Checks if the map meets requirements for this medical job
        /// </summary>
        protected virtual bool AreMapRequirementsMet(Pawn pawn)
        {
            // Default implementation - check for wounded pawns on the map
            return pawn?.Map != null &&
                   pawn.Map.mapPawns.SpawnedPawnsWithAnyHediff.Any(p =>
                       ShouldTreatPawn(p, pawn) && IsPatientValidForDoctor(p, pawn));
        }

        #endregion

        #region Target Selection

        /// <summary>
        /// Job-specific cache update method that implements specialized target collection logic
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            // Default implementation - derived classes should override this
            return GetTargets(map);
        }

        /// <summary>
        /// Gets all potential patients on the given map - derived classes must implement
        /// </summary>
        protected abstract override IEnumerable<Thing> GetTargets(Map map);

        /// <summary>
        /// Creates a job for the pawn using the cached targets
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // Skip if pawn invalid or doesn't meet requirements
            if (pawn == null || !IsValidFactionForMedical(pawn) || !HasRequiredCapabilities(pawn))
                return null;

            // Skip if no targets and they're required
            if ((targets == null || targets.Count == 0) && RequiresPawnTargets())
                return null;

            // Call the specialized medical job creation method
            return CreateMedicalJob(pawn, forced);
        }

        /// <summary>
        /// Whether this job giver requires Pawn targets or uses other targets
        /// </summary>
        protected virtual bool RequiresPawnTargets()
        {
            // Most medical job givers target pawns
            return true;
        }

        #endregion

        #region Job Creation

        /// <summary>
        /// Implement to create the specific medical job
        /// </summary>
        protected abstract Job CreateMedicalJob(Pawn pawn, bool forced);

        #endregion

        #region Patient Validation

        /// <summary>
        /// Determines if a patient needs treatment by this doctor
        /// Must be implemented by derived classes for specific medical tasks
        /// </summary>
        protected abstract bool ShouldTreatPawn(Pawn patient, Pawn doctor);

        /// <summary>
        /// Check if a given pawn should be considered a potential patient
        /// </summary>
        protected virtual bool IsValidPatient(Pawn patient, Pawn doctor, bool forced = false)
        {
            // Skip if patient is null
            if (patient == null || !patient.Spawned || patient.Map != doctor.Map)
                return false;

            // Skip if forbidden
            if (patient.IsForbidden(doctor))
                return false;

            // Skip if doctor can't reserve
            if (!doctor.CanReserve(patient, 1, -1, null, forced))
                return false;

            // Skip if doctor can't reach patient
            if (!doctor.CanReach(patient, PathEndMode.InteractionCell, Danger.Deadly))
                return false;

            // Skip if doctor can't medically manipulate
            if (!doctor.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                return false;

            // Check if valid faction relationship between doctor and patient
            if (!IsPatientValidForDoctor(patient, doctor))
                return false;

            // Skip if mutant without medical care rights
            if (patient.IsMutant && !patient.mutant.Def.entitledToMedicalCare)
                return false;

            // Skip if in mental state (unless affected by Scaria)
            if (patient.InAggroMentalState && !patient.health.hediffSet.HasHediff(HediffDefOf.Scaria))
                return false;

            return true;
        }

        /// <summary>
        /// Check if the patient has appropriate laying status for treatment
        /// </summary>
        protected virtual bool GoodLayingStatusForTreatment(Pawn patient, Pawn doctor)
        {
            // Self-treatment doesn't require lying down
            if (patient == doctor)
                return true;

            // Humanlike patients should be in bed
            if (patient.RaceProps.Humanlike)
                return patient.InBed();

            // Animals should not be standing
            return patient.GetPosture() != PawnPosture.Standing;
        }

        #endregion

        #region Medicine Helpers

        /// <summary>
        /// Find the best medicine for treating a patient
        /// </summary>
        protected Thing FindBestMedicine(Pawn doctor, Pawn patient)
        {
            return HealthAIUtility.FindBestMedicine(doctor, patient);
        }

        /// <summary>
        /// Find an appropriate bed for a patient
        /// </summary>
        protected Building_Bed FindBedFor(Pawn doctor, Pawn patient)
        {
            return RestUtility.FindBedFor(patient, doctor, checkSocialProperness: false,
                ignoreOtherReservations: false, patient.GuestStatus);
        }

        #endregion

        #region Utility

        /// <summary>
        /// Sanitizes a list by limiting its size to avoid performance issues
        /// </summary>
        protected List<T> LimitListSize<T>(List<T> list, int maxSize = 1000)
        {
            if (list == null || list.Count <= maxSize)
                return list;

            return list.Take(maxSize).ToList();
        }

        /// <summary>
        /// Check if patient is reserved by another doctor
        /// </summary>
        protected bool IsPatientReservedByAnother(Pawn doctor, Pawn patient, JobDef jobDef)
        {
            if (doctor?.Map?.mapPawns?.FreeColonistsSpawned == null || patient == null)
                return false;

            List<Pawn> pawns = doctor.Map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                if (pawns[i] != doctor && pawns[i].CurJobDef == jobDef)
                {
                    LocalTargetInfo target = pawns[i].CurJob?.GetTarget(TargetIndex.A) ?? default;
                    if (target.IsValid && target.Thing == patient)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public override string ToString()
        {
            return $"JobGiver_Common_Doctor_PawnControl({DebugName})";
        }

        #endregion

        #region Reset

        /// <summary>
        /// Reset the cache - implements IResettableCache
        /// </summary>
        public override void Reset()
        {
            // Use centralized cache reset
            base.Reset();
        }

        #endregion
    }
}