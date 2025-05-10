using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for construction tasks specifically for uninstalling buildings
    /// </summary>
    public class JobModule_Construction_Uninstall : JobModule_Construction
    {
        // Reference to the common implementation
        private readonly JobModule_Common_RemoveBuilding _commonImpl;

        // We need a field rather than a property to use with ref parameters
        //private static int _lastLocalUpdateTick = -999;

        // Module metadata
        public override string UniqueID => "Uninstall";
        public override float Priority => 5.8f; // Slightly lower priority than deconstruct, matching original JobGiver
        public override string Category => "Construction";

        // These ThingRequestGroups are what this module cares about
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups =>
            new HashSet<ThingRequestGroup> { ThingRequestGroup.BuildingArtificial };

        // Constructor initializes the common implementation
        public JobModule_Construction_Uninstall()
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
        /// Check if this building should be uninstalled
        /// </summary>
        public override bool ShouldProcessBuildable(Thing thing, Map map)
        {
            return _commonImpl.ShouldProcessTarget(thing, map);
        }

        /// <summary>
        /// Check if the constructor can uninstall this building
        /// </summary>
        public override bool ValidateConstructionJob(Thing thing, Pawn constructor)
        {
            try
            {
                // First check common validation through the adapter
                if (!_commonImpl.ValidateRemovalJob(thing, constructor))
                    return false;

                // UNINSTALL-SPECIFIC CHECKS
                // Check ownership - if claimable, must be owned by pawn's faction
                if (thing.def.Claimable)
                {
                    if (thing.Faction != constructor.Faction)
                        return false;
                }
                // If not claimable, pawn must belong to player faction
                else if (constructor.Faction != Faction.OfPlayer)
                    return false;

                return true;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error validating uninstall job: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Create a job to uninstall this building
        /// </summary>
        protected override Job CreateConstructionJob(Pawn constructor, Thing thing)
        {
            return _commonImpl.CreateRemovalJob(constructor, thing);
        }

        /// <summary>
        /// Update the cache of buildings to uninstall
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

            // Log if we found targets
            if (removalTargets.Count > 0)
            {
                Utility_DebugManager.LogNormal(
                    $"Found {removalTargets.Count} buildings designated for uninstall on map {map.uniqueID}");
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
            private readonly JobModule_Construction_Uninstall _outer;

            public JobModule_Common_RemoveBuilding_Adapter(JobModule_Construction_Uninstall outer)
            {
                _outer = outer;
            }

            // Required implementations from JobModuleCore
            public override string UniqueID => _outer.UniqueID + "_Common";
            public override float Priority => _outer.Priority;
            public override string WorkTypeName => _outer.WorkTypeName;
            public override string Category => _outer.Category;

            // Configuration properties
            protected override DesignationDef Designation => DesignationDefOf.Uninstall;
            protected override JobDef RemoveBuildingJob => JobDefOf.Uninstall;
            protected override bool RequiresConstructionSkill => true;
            protected override bool RequiresPlantWorkSkill => false;
            protected override bool ValidatePlantTarget => false;
        }
    }
}