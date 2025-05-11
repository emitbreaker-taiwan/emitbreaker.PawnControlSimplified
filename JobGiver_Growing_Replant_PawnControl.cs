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
        public override float GetPriority(Pawn pawn)
        {
            // Growing is a lower priority than construction
            return 5.4f;
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
            int maxCacheSize = 200;
            if (plantBlueprints.Count > maxCacheSize)
            {
                plantBlueprints = plantBlueprints.Take(maxCacheSize).ToList();
            }

            return plantBlueprints;
        }

        /// <summary>
        /// Override TryGiveJob to use StandardTryGiveJob pattern
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            return CreateDeliveryJob<JobGiver_Growing_Replant_PawnControl>(pawn);
        }

        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // Since replanting doesn't use resource sharing logic, we can return null or implement a basic behavior.
            // For now, we'll return null to indicate no job is created from cached targets.
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

        #endregion

        #region Utility

        /// <summary>
        /// Reset static data including additional translation strings
        /// </summary>
        public static new void ResetStaticData()
        {
            // Call base to initialize common strings
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