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

        // Additional translation strings specific to replanting
        private static string BlockedByRoofTranslated;
        private static string BeingCarriedByTranslated;
        private static string ReservedByTranslated;

        #endregion

        #region Overrides

        /// <summary>
        /// Set work tag to Growing
        /// </summary>
        protected override string WorkTag => "Growing";

        /// <summary>
        /// Unique name for debug messages
        /// </summary>
        protected override string JobDescription => "tree replanting assignment";

        /// <summary>
        /// Lower priority than construction
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            return 5.4f;
        }

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        protected override bool RequiresMapZoneorArea => true;

        /// <summary>
        /// Replanting is strictly a player faction activity
        /// </summary>
        protected override bool RequiresPlayerFaction => true;

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

        /// <summary>
        /// Override map requirements to verify player faction is active
        /// </summary>
        protected override bool AreMapRequirementsMet(Pawn pawn)
        {
            // For replanting, ensure the map exists and the pawn is player faction
            return pawn?.Map != null && pawn.Faction == Faction.OfPlayer;
        }

        #endregion

        #region Utility

        /// <summary>
        /// Reset static data including additional translation strings
        /// </summary>
        public static new void ResetStaticData()
        {
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