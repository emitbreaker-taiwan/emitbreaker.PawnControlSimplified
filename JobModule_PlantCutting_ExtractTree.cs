using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for plant cutting tasks specifically for extracting trees
    /// </summary>
    public class JobModule_PlantCutting_ExtractTree : JobModule_PlantCutting
    {
        // Reference to the common implementation
        private readonly JobModule_Common_RemoveBuilding _commonImpl;

        // We need a field rather than a property to use with ref parameters
        //private static int _lastLocalUpdateTick = -999;

        // Module metadata
        public override string UniqueID => "PlantCutting_ExtractTree";
        public override float Priority => 5.8f; // Slightly lower priority than construction
        public override string Category => "PlantCutting";

        // These ThingRequestGroups are what this module cares about
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups =>
            new HashSet<ThingRequestGroup> { ThingRequestGroup.Plant };

        // Constructor initializes the common implementation
        public JobModule_PlantCutting_ExtractTree()
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
        /// Check if this plant should be extracted
        /// </summary>
        public override bool ShouldProcessPlant(Plant plant, Map map)
        {
            return _commonImpl.ShouldProcessTarget(plant, map);
        }

        /// <summary>
        /// Check if the grower can extract this tree
        /// </summary>
        protected override bool ValidatePlantCuttingJob(Plant plant, Pawn grower)
        {
            return _commonImpl.ValidateRemovalJob(plant, grower);
        }

        /// <summary>
        /// Create a job to extract this tree
        /// </summary>
        protected override Job CreatePlantCuttingJob(Pawn grower, Plant plant)
        {
            return _commonImpl.CreateRemovalJob(grower, plant);
        }

        /// <summary>
        /// Update the cache of trees to extract
        /// </summary>
        public override void UpdateCache(Map map, List<Plant> targetCache)
        {
            if (map == null) return;

            // Update common implementation's cache
            _commonImpl.UpdateRemovalTargetCache(map);

            // Update our local cache with the same targets
            List<Thing> removalTargets = _commonImpl.GetRemovalTargets(map);
            targetCache.Clear();

            // We need to cast the Things to Plants since our targetCache is for Plants
            foreach (Thing thing in removalTargets)
            {
                if (thing is Plant plant)
                {
                    targetCache.Add(plant);
                }
            }

            // Determine if we have targets based on the targetCache contents
            bool hasTargets = targetCache.Count > 0;
            SetHasTargets(map, hasTargets);
        }

        /// <summary>
        /// Adapter class for JobModule_Common_RemoveBuilding
        /// </summary>
        private class JobModule_Common_RemoveBuilding_Adapter : JobModule_Common_RemoveBuilding
        {
            private readonly JobModule_PlantCutting_ExtractTree _outer;

            public JobModule_Common_RemoveBuilding_Adapter(JobModule_PlantCutting_ExtractTree outer)
            {
                _outer = outer;
            }

            // Required implementations from JobModuleCore
            public override string UniqueID => _outer.UniqueID + "_Common";
            public override float Priority => _outer.Priority;
            public override string WorkTypeName => _outer.WorkTypeName;
            public override string Category => _outer.Category;

            // Configuration properties
            protected override DesignationDef Designation => DesignationDefOf.ExtractTree;
            protected override JobDef RemoveBuildingJob => JobDefOf.ExtractTree;
            protected override bool RequiresConstructionSkill => false;
            protected override bool RequiresPlantWorkSkill => true;
            protected override bool ValidatePlantTarget => true;
        }
    }
}