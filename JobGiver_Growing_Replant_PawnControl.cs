using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that allows pawns to replant trees (move minified trees to new locations).
    /// Uses the Growing work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Growing_Replant_PawnControl : JobGiver_Common_ConstructDeliverResources_PawnControl<Blueprint_Install>
    {
        #region Configuration

        /// <summary>
        /// Set work tag to Growing
        /// </summary>
        public override string WorkTag => "Growing";

        /// <summary>
        /// Unique name for debug messages
        /// </summary>
        protected override string JobDescription => "tree replanting assignment";

        /// <summary>
        /// Human-readable name for debug logging
        /// </summary>
        protected override string DebugName => JobDescription;

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// </summary>
        protected override bool RequiresMapZoneorArea => true;

        /// <summary>
        /// Replanting is strictly a player faction activity
        /// </summary>
        protected override bool RequiresPlayerFaction => true;

        /// <summary>
        /// Cache update interval - update slightly less often for replanting
        /// </summary>
        protected override int CacheUpdateInterval => 250; // ~4.2 seconds

        // Additional translation strings specific to replanting
        private static string BlockedByRoofTranslated;
        private static string BeingCarriedByTranslated;
        private static string ReservedByTranslated;

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_Growing_Replant_PawnControl() : base()
        {
            // Base constructor already initializes the cache system
        }

        #endregion

        #region Core Flow

        /// <summary>
        /// Lower priority than construction
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            return 5.4f;
        }

        /// <summary>
        /// Override map requirements to verify player faction is active
        /// </summary>
        protected override bool AreMapRequirementsMet(Pawn pawn)
        {
            // For replanting, ensure the map exists and the pawn is player faction
            if (pawn?.Map == null || pawn.Faction != Faction.OfPlayer)
                return false;

            // Check if there are any plant blueprints
            return pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint)
                .OfType<Blueprint_Install>()
                .Any(bp => bp.def.entityDefToBuild is ThingDef entityDef && entityDef.plant != null);
        }

        #endregion

        #region Faction Validation

        /// <summary>
        /// Replanting is strictly for player faction pawns only
        /// </summary>
        protected override bool IsValidFactionForConstruction(Pawn pawn)
        {
            // For replanting, only player faction pawns are allowed (not even slaves)
            // This is because replanting is a player-designated activity
            return pawn?.Faction == Faction.OfPlayer;
        }

        /// <summary>
        /// Double-check faction validation for replant targets
        /// </summary>
        protected override bool IsValidTargetFaction(Blueprint_Install target, Pawn pawn)
        {
            // For replanting, the target blueprint must be from player faction
            return target != null && target.Faction == Faction.OfPlayer;
        }

        #endregion

        #region Target Selection

        /// <summary>
        /// Job-specific cache update method for plant blueprint targets
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            // Get all plant blueprints
            return GetConstructionTargets(map).Cast<Thing>();
        }

        /// <summary>
        /// Get plant blueprint targets that need replanting
        /// </summary>
        protected override List<Blueprint_Install> GetConstructionTargets(Map map)
        {
            if (map == null) return new List<Blueprint_Install>();

            List<Blueprint_Install> plantBlueprints = new List<Blueprint_Install>();

            // Find all plant blueprints
            foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint))
            {
                // Only look for Blueprint_Install that are plants
                Blueprint_Install blueprint = thing as Blueprint_Install;
                if (blueprint == null || !(blueprint.def.entityDefToBuild is ThingDef entityDef) || entityDef.plant == null)
                    continue;

                // Add to result
                plantBlueprints.Add(blueprint);
            }

            // Limit size for performance
            return LimitListSize(plantBlueprints, 200);
        }

        #endregion

        #region Job Creation

        /// <summary>
        /// Override ProcessCachedTargets for replant-specific logic
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            if (pawn == null || targets == null || targets.Count == 0)
                return null;

            // First check faction validation - strict player faction check
            if (pawn.Faction != Faction.OfPlayer)
                return null;

            // Convert generic Things to Blueprint_Install for plant blueprints
            List<Blueprint_Install> plantBlueprints = targets
                .OfType<Blueprint_Install>()
                .Where(bp => bp.Spawned &&
                           !bp.IsForbidden(pawn) &&
                           bp.Faction == Faction.OfPlayer &&
                           bp.def.entityDefToBuild is ThingDef entityDef &&
                           entityDef.plant != null)
                .ToList();

            if (plantBlueprints.Count == 0)
                return null;

            // Try to find a valid replanting job
            foreach (Blueprint_Install blueprint in plantBlueprints)
            {
                // Check if we can work with this blueprint
                if (!GenConstruct.CanConstruct(blueprint, pawn, false))
                    continue;

                // Try to create a job for this target
                Job job = ResourceDeliverJobFor(pawn, blueprint);
                if (job != null)
                    return job;
            }

            return null;
        }

        /// <summary>
        /// Creates a construction job for delivering plants to replant sites
        /// </summary>
        protected override Job CreateConstructionJob(Pawn pawn, bool forced)
        {
            if (pawn?.Map == null || pawn.Faction != Faction.OfPlayer)
                return null;

            int mapId = pawn.Map.uniqueID;

            // Get targets from the cache
            List<Thing> cachedTargets = GetCachedTargets(mapId);
            List<Blueprint_Install> plantBlueprints;

            // If cache is empty or not yet populated
            if (cachedTargets == null || cachedTargets.Count == 0)
            {
                // Try to update cache if needed
                if (ShouldUpdateCache(mapId))
                {
                    UpdateCache(mapId, pawn.Map);
                    cachedTargets = GetCachedTargets(mapId);
                }

                // If still empty, get targets directly
                if (cachedTargets == null || cachedTargets.Count == 0)
                {
                    plantBlueprints = GetConstructionTargets(pawn.Map);
                }
                else
                {
                    // Convert cached targets to proper type
                    plantBlueprints = cachedTargets.OfType<Blueprint_Install>().ToList();
                }
            }
            else
            {
                // Convert cached targets to proper type
                plantBlueprints = cachedTargets.OfType<Blueprint_Install>().ToList();
            }

            if (plantBlueprints.Count == 0)
                return null;

            // Filter for valid replanting targets
            plantBlueprints = plantBlueprints
                .Where(bp => bp.Spawned &&
                           !bp.IsForbidden(pawn) &&
                           bp.Faction == Faction.OfPlayer &&
                           bp.def.entityDefToBuild is ThingDef entityDef &&
                           entityDef.plant != null)
                .ToList();

            // Try to find a valid replanting job
            foreach (Blueprint_Install blueprint in plantBlueprints)
            {
                // Check if we can work with this blueprint
                if (!GenConstruct.CanConstruct(blueprint, pawn, false))
                    continue;

                // Try to create a job for this target
                Job job = ResourceDeliverJobFor(pawn, blueprint);
                if (job != null)
                    return job;
            }

            return null;
        }

        #endregion

        #region Resource Delivery Helpers

        /// <summary>
        /// Find nearby construction sites that need the same resources
        /// Not used for replanting since each tree is unique
        /// </summary>
        protected override HashSet<Thing> FindNearbyNeeders(
            Pawn pawn,
            ThingDef stuff,
            Blueprint_Install originalTarget,
            int resNeeded,
            int resTotalAvailable,
            out int neededTotal,
            out Job jobToMakeNeederAvailable)
        {
            // Replanting doesn't use resource sharing logic
            neededTotal = resNeeded;
            jobToMakeNeederAvailable = null;
            return new HashSet<Thing>();
        }

        /// <summary>
        /// Not used for replanting since each tree is unique
        /// </summary>
        protected override bool IsNewValidNearbyNeeder(
            Thing t,
            HashSet<Thing> nearbyNeeders,
            Blueprint_Install originalTarget,
            Pawn pawn)
        {
            // Not applicable for replanting
            return false;
        }

        #endregion

        #region Thing-Based Helpers

        /// <summary>
        /// Basic validation for blueprint targets
        /// </summary>
        protected override bool ValidateConstructionTarget(Thing thing, Pawn pawn, bool forced = false)
        {
            // First perform base validation
            if (!base.ValidateConstructionTarget(thing, pawn, forced))
                return false;

            // Add plant-specific validation
            Blueprint_Install blueprint = thing as Blueprint_Install;
            if (blueprint == null)
                return false;

            // Check if this is a plant blueprint
            if (!(blueprint.def.entityDefToBuild is ThingDef entityDef) || entityDef.plant == null)
                return false;

            // Check if the blueprint is from the player faction
            if (blueprint.Faction != Faction.OfPlayer)
                return false;

            return true;
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

        /// <summary>
        /// Reset static data including additional translation strings
        /// </summary>
        public static new void ResetStaticData()
        {
            // Call parent method for base translations
            JobGiver_Common_ConstructDeliverResources_PawnControl<Blueprint_Install>.ResetStaticData();

            // Initialize replant-specific strings
            BlockedByRoofTranslated = "BlockedByRoof".Translate();
            BeingCarriedByTranslated = "BeingCarriedBy".Translate();
            ReservedByTranslated = "ReservedBy".Translate();
        }

        public override string ToString()
        {
            return "JobGiver_Replant_PawnControl";
        }

        #endregion
    }
}