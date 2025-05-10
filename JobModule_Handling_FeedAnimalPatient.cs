using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Handling-specific implementation for feeding animal patients
    /// </summary>
    public class JobModule_Handling_FeedAnimalPatient : JobModule_Handling
    {
        // Reference to the common implementation
        private readonly JobModule_Common_FeedPatients _commonImpl;

        // We need a field rather than a property to use with ref parameters
        private static int _lastLocalUpdateTick = -999;

        // Module metadata
        public override string UniqueID => "FeedAnimalPatient";
        public override float Priority => 7.8f; // Lower priority than feeding humans
        public override string Category => "AnimalCare";

        // Override update interval to match common implementation
        public override int CacheUpdateInterval => 180; // Update every 3 seconds

        // Constructor initializes the common implementation with animal-specific settings
        public JobModule_Handling_FeedAnimalPatient()
        {
            _commonImpl = new JobModule_Common_FeedAnimalPatientsImpl(this);
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
        /// Check if this animal needs feeding
        /// </summary>
        public override bool ShouldProcessAnimal(Pawn animal, Map map)
        {
            return _commonImpl.ShouldProcessTarget(animal, map);
        }

        /// <summary>
        /// Validate if the handler can feed this animal
        /// </summary>
        public override bool ValidateHandlingJob(Pawn animal, Pawn handler)
        {
            return _commonImpl.ValidateFeedingJob(animal, handler);
        }

        /// <summary>
        /// Create a job for the handler to feed this animal
        /// </summary>
        protected override Job CreateHandlingJob(Pawn handler, Pawn animal)
        {
            return _commonImpl.CreateFeedingJob(handler, animal);
        }

        /// <summary>
        /// Update the cache of animals that need feeding
        /// </summary>
        public override void UpdateCache(Map map, List<Pawn> targetCache)
        {
            if (map == null) return;

            // Use progressive scanning for better performance
            UpdateCacheProgressively(
                map,
                targetCache,
                ref _lastLocalUpdateTick,  // Use the local field instead of property
                RelevantThingRequestGroups,
                animal => ShouldProcessAnimal(animal, map),
                null,  // Changed from lambda to null to match signature
                CacheUpdateInterval
            );

            // Determine if we have targets based on the targetCache contents
            bool hasTargets = targetCache.Count > 0;
            SetHasTargets(map, hasTargets);
        }

        /// <summary>
        /// Animal-specific implementation of the common feed patients logic
        /// </summary>
        private class JobModule_Common_FeedAnimalPatientsImpl : JobModule_Common_FeedPatients
        {
            // Reference to the outer class
            private readonly JobModule_Handling_FeedAnimalPatient _outer;

            // Constructor with reference to the outer class
            public JobModule_Common_FeedAnimalPatientsImpl(JobModule_Handling_FeedAnimalPatient outer)
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