using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Base abstract class for all doctor job modules
    /// </summary>
    public abstract class JobModule_Doctor : JobModule<Pawn>
    {
        // Simple fixed priority (higher number = higher priority)
        public override float Priority => 8.5f;

        /// <summary>
        /// Gets the work type this module is associated with
        /// </summary>
        public override string WorkTypeName => "Doctor";

        /// <summary>
        /// Fast filter check for doctors
        /// </summary>
        public override bool QuickFilterCheck(Pawn pawn)
        {
            return !pawn.WorkTagIsDisabled(WorkTags.Caring);
        }

        public override bool WorkTypeApplies(Pawn pawn)
        {
            // Quick check based on work settings
            return pawn?.workSettings?.WorkIsActive(WorkTypeDefOf.Doctor) == true;
        }

        /// <summary>
        /// Default cache update interval - 3 seconds for medical jobs
        /// </summary>
        public override int CacheUpdateInterval => 180; // Update every 3 seconds

        /// <summary>
        /// Relevant ThingRequestGroups for doctor jobs
        /// </summary>
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups { get; } =
            new HashSet<ThingRequestGroup> { ThingRequestGroup.Pawn };

        /// <summary>
        /// Filter function to identify targets for this job (specifically named for doctor jobs)
        /// </summary>
        public abstract bool ShouldProcessPatient(Pawn patient, Map map);

        /// <summary>
        /// Filter function implementation that calls the doctor-specific method
        /// </summary>
        public override bool ShouldProcessTarget(Pawn target, Map map)
            => ShouldProcessPatient(target, map);

        /// <summary>
        /// Validates if the doctor can perform this job on the target
        /// </summary>
        public abstract bool ValidateMedicalJob(Pawn target, Pawn doctor);

        /// <summary>
        /// Validates job implementation that calls the doctor-specific method
        /// </summary>
        public override bool ValidateJob(Pawn target, Pawn actor)
            => ValidateMedicalJob(target, actor);

        /// <summary>
        /// Creates the job for the doctor to perform on the target
        /// </summary>
        public override Job CreateJob(Pawn actor, Pawn target)
            => CreateMedicalJob(actor, target);

        /// <summary>
        /// Doctor-specific implementation of job creation
        /// </summary>
        protected abstract Job CreateMedicalJob(Pawn doctor, Pawn patient);

        /// <summary>
        /// Helper method to check if a patient needs medical attention
        /// </summary>
        protected bool NeedsMedicalAttention(Pawn patient)
        {
            if (patient == null || !patient.Spawned) return false;

            // Check if patient has health issues requiring treatment
            if (patient.health?.HasHediffsNeedingTend() == true) return true;

            // Check for health conditions requiring treatment
            return patient.health?.hediffSet?.hediffs.Any(hediff =>
                hediff.Visible &&
                hediff.TendableNow() &&
                !hediff.IsPermanent() &&
                hediff.TryGetComp<HediffComp_TendDuration>() != null) == true;
        }

        /// <summary>
        /// Helper method to check if doctor can treat patient
        /// </summary>
        protected bool CanTreatPatient(Pawn patient, Pawn doctor)
        {
            if (patient == null || doctor == null || !patient.Spawned || !doctor.Spawned)
                return false;

            // Skip if patient is forbidden or not usable
            if (patient.IsForbidden(doctor))
                return false;

            // Skip if patient is claimed by someone else
            if (!doctor.CanReserve(patient))
                return false;

            // Can't treat self unless it's an emergency (high bleeding or pain)
            if (patient == doctor && !HasLifeThreateningCondition(patient))
                return false;

            // Check path to patient
            if (!doctor.CanReach(patient, PathEndMode.Touch, doctor.NormalMaxDanger()))
                return false;

            return true;
        }

        /// <summary>
        /// Helper method to determine if a pawn has a life-threatening condition
        /// that would warrant self-treatment
        /// </summary>
        private bool HasLifeThreateningCondition(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null) return false;

            // Check for significant bleeding
            if (pawn.health.hediffSet.BleedRateTotal > 0.3f) return true;

            // Check for extreme pain
            if (pawn.health.hediffSet.PainTotal > 0.8f) return true;

            // Check for vital part injuries
            return pawn.health.hediffSet.hediffs.Any(h =>
                h is Hediff_Injury injury &&
                h.Part != null &&
                (h.Part.def == pawn.health.hediffSet.GetBrain().def ||
                h.Part.def == BodyPartDefOf.Heart ||
                h.Part.def == Utility_Common.BodyPartDefNamed("Neck")) &&
                injury.Severity > 10f);
        }

        /// <summary>
        /// Helper method to check medicine availability for treatment
        /// </summary>
        protected bool CheckMedicineAvailability(Pawn patient, Pawn doctor, bool allowHerbal = true)
        {
            if (patient == null || doctor?.Map == null) return false;

            // Check if any medicine is available on the map
            bool medicineAvailable = doctor.Map.listerThings.ThingsInGroup(ThingRequestGroup.Medicine)
                .Any(m =>
                    !m.IsForbidden(doctor) &&
                    doctor.CanReserve(m) &&
                    (allowHerbal || m.def != ThingDefOf.MedicineHerbal));

            return medicineAvailable;
        }

        /// <summary>
        /// Default cache update: collect every pawn that
        /// satisfies ShouldProcessPatient. Uses progressive scanning for better performance.
        /// </summary>
        public override void UpdateCache(Map map, List<Pawn> targetCache)
        {
            if (map == null) return;

            // Use progressive cache update with the appropriate filter
            UpdateCacheProgressively(
                map,
                targetCache,
                ref _lastUpdateTick,
                RelevantThingRequestGroups,
                patient => patient.RaceProps.Humanlike && ShouldProcessPatient(patient, map),
                null,
                CacheUpdateInterval
            );
        }

        // Track last update tick for progressive updates
        private static int _lastUpdateTick = -999;

        /// <summary>
        /// Reset any static data when language changes or game reloads
        /// </summary>
        public override void ResetStaticData()
        {
            _lastUpdateTick = -999;
        }
    }
}