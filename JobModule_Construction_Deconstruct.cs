using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for construction tasks specifically for deconstructing buildings
    /// </summary>
    public class JobModule_Construction_Deconstruct : JobModule_Construction
    {
        // Reference to the common implementation
        private readonly JobModule_Common_RemoveBuilding _commonImpl;

        // We need a field rather than a property to use with ref parameters
        //private static int _lastLocalUpdateTick = -999;

        // Module metadata
        public override string UniqueID => "Construction_Deconstruct";
        public override float Priority => 5.9f; // Standard priority for construction
        public override string Category => "Construction";

        // These ThingRequestGroups are what this module cares about
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups =>
            new HashSet<ThingRequestGroup> { ThingRequestGroup.BuildingArtificial };

        // Constructor initializes the common implementation
        public JobModule_Construction_Deconstruct()
        {
            _commonImpl = new JobModule_Common_RemoveBuilding_Adapter(this);
        }

        /// <summary>
        /// Reset any static data when language changes or game reloads
        /// </summary>
        public override void ResetStaticData()
        {
            base.ResetStaticData();
            _commonImpl.ResetStaticData();
            //_lastLocalUpdateTick = -999;
        }

        /// <summary>
        /// Check if this thing should be deconstructed
        /// </summary>
        public override bool ShouldProcessBuildable(Thing thing, Map map)
        {
            return _commonImpl.ShouldProcessTarget(thing, map);
        }

        /// <summary>
        /// Check if the constructor can deconstruct this thing
        /// </summary>
        public override bool ValidateConstructionJob(Thing thing, Pawn constructor)
        {
            return _commonImpl.ValidateRemovalJob(thing, constructor);
        }

        /// <summary>
        /// Create a job to deconstruct this thing
        /// </summary>
        protected override Job CreateConstructionJob(Pawn constructor, Thing thing)
        {
            return _commonImpl.CreateRemovalJob(constructor, thing);
        }

        /// <summary>
        /// Update the cache of things to deconstruct
        /// </summary>
        public override void UpdateCache(Map map, List<Thing> targetCache)
        {
            if (map == null) return;

            // Update common implementation's cache
            _commonImpl.UpdateRemovalTargetCache(map);

            // Update our local cache with the same targets
            List<Thing> removalTargets = _commonImpl.GetRemovalTargets(map);
            targetCache.Clear();
            targetCache.AddRange(removalTargets);

            // Determine if we have targets based on the targetCache contents
            bool hasTargets = targetCache.Count > 0;
            SetHasTargets(map, hasTargets);
        }

        /// <summary>
        /// Adapter class for JobModule_Common_RemoveBuilding
        /// </summary>
        private class JobModule_Common_RemoveBuilding_Adapter : JobModule_Common_RemoveBuilding
        {
            private readonly JobModule_Construction_Deconstruct _outer;

            public JobModule_Common_RemoveBuilding_Adapter(JobModule_Construction_Deconstruct outer)
            {
                _outer = outer;
            }

            // Required implementations from JobModuleCore
            public override string UniqueID => _outer.UniqueID + "_Common";
            public override float Priority => _outer.Priority;
            public override string WorkTypeName => _outer.WorkTypeName;
            public override string Category => _outer.Category;

            // Configuration properties
            protected override DesignationDef Designation => DesignationDefOf.Deconstruct;
            protected override JobDef RemoveBuildingJob => JobDefOf.Deconstruct;
            protected override bool RequiresConstructionSkill => true;
            protected override bool RequiresPlantWorkSkill => false;
            protected override bool ValidatePlantTarget => false;
        }
    }
}