using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Doctor-specific implementation for feeding animal patients
    /// </summary>
    public class JobModule_Doctor_FeedAnimalPatient : JobModule_Doctor
    {
        // Reference to the common implementation
        private readonly JobModule_Common_FeedPatients _commonImpl;

        // We need a field rather than a property to use with ref parameters
        private static int _lastLocalUpdateTick = -999;

        // Module metadata
        public override string UniqueID => "DoctorFeedAnimalPatient";
        public override float Priority => 7.8f; // Lower priority than feeding humanlike patients
        public override string Category => "Medical";

        // Override update interval to match common implementation
        public override int CacheUpdateInterval => 180; // Update every 3 seconds

        // Constructor initializes the common implementation with animal-specific settings
        public JobModule_Doctor_FeedAnimalPatient()
        {
            _commonImpl = new JobModule_Common_FeedDoctorAnimalPatientsImpl(this);
        }

        /// <summary>
        /// Reset any static data when language changes or game reloads
        /// </summary>
        public override void ResetStaticData()
        {
            base.ResetStaticData();
            _commonImpl.ResetStaticData();
            _lastLocalUpdateTick = -999;
        }

        /// <summary>
        /// Check if this animal needs feeding (doctor perspective)
        /// </summary>
        public override bool ShouldProcessPatient(Pawn patient, Map map)
        {
            return _commonImpl.ShouldProcessTarget(patient, map);
        }

        /// <summary>
        /// Validate if the doctor can feed this animal patient
        /// </summary>
        public override bool ValidateMedicalJob(Pawn patient, Pawn doctor)
        {
            return _commonImpl.ValidateFeedingJob(patient, doctor);
        }

        /// <summary>
        /// Create a job for the doctor to feed this animal patient
        /// </summary>
        protected override Job CreateMedicalJob(Pawn doctor, Pawn patient)
        {
            return _commonImpl.CreateFeedingJob(doctor, patient);
        }

        /// <summary>
        /// Update the cache of animal patients that need feeding by doctors
        /// </summary>
        public override void UpdateCache(Map map, List<Pawn> targetCache)
        {
            if (map == null) return;

            // Use progressive scanning for better performance
            UpdateCacheProgressively(
                map,
                targetCache,
                ref _lastLocalUpdateTick,
                RelevantThingRequestGroups,
                patient => ShouldProcessPatient(patient, map),
                null,
                CacheUpdateInterval
            );

            // Determine if we have targets based on the targetCache contents
            bool hasTargets = targetCache.Count > 0;
            SetHasTargets(map, hasTargets);
        }

        /// <summary>
        /// Doctor-specific implementation for feeding animal patients 
        /// </summary>
        private class JobModule_Common_FeedDoctorAnimalPatientsImpl : JobModule_Common_FeedPatients
        {
            // Reference to the outer class
            private readonly JobModule_Doctor_FeedAnimalPatient _outer;

            // Constructor with reference to the outer class
            public JobModule_Common_FeedDoctorAnimalPatientsImpl(JobModule_Doctor_FeedAnimalPatient outer)
            {
                _outer = outer;
            }

            // Required implementations from JobModuleCore
            public override string UniqueID => _outer.UniqueID + "_Impl";
            public override float Priority => _outer.Priority;
            public override string WorkTypeName => _outer.WorkTypeName;

            // Override to feed only animals
            protected override bool FeedHumanlikesOnly => false;
            protected override bool FeedAnimalsOnly => true;
            protected override bool FeedPrisonersOnly => false;
        }
    }
}