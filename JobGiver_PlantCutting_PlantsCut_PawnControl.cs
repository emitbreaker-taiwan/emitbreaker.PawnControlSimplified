using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Plant‐cutting JobGiver with minimal overrides:
    /// uses the standard TryGiveJob wrapper, a cache, and nearest‐plant selection.
    /// </summary>
    public class JobGiver_PlantCutting_PlantsCut_PawnControl : JobGiver_PlantCutting_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging
        /// </summary>
        protected override string DebugName => "PlantsCut";

        #endregion

        #region Core flow

        /// <summary>
        /// Override TryGiveJob to implement the standard pattern
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            return Utility_JobGiverManager.StandardTryGiveJob<Plant>(
                pawn,
                WorkTag,
                (p, forced) => {
                    // Use the centralized cache system from the parent class
                    List<Thing> targets = GetTargets(p.Map).ToList();
                    if (targets.Count == 0) return null;

                    // Use the parent's efficient target processing
                    return ProcessCachedTargets(p, targets, forced);
                },
                debugJobDesc: "plant cutting assignment");
        }

        #endregion

        #region Plant‐selection helpers

        /// <summary>
        /// Implementation uses the base class's GetPlantsNeedingCutting method
        /// which already has the correct logic for finding plants to cut
        /// </summary>
        protected override IEnumerable<Plant> GetPlantsNeedingCutting(Map map)
        {
            // Use the parent class implementation which has the same logic
            return base.GetPlantsNeedingCutting(map);
        }

        /// <summary>
        /// Implement the validation logic specifically for cutting plants
        /// </summary>
        protected override bool ValidatePlantTarget(Plant plant, Pawn pawn)
        {
            if (plant == null || plant.Destroyed || !plant.Spawned)
                return false;

            bool isDesignated =
                pawn.Map.designationManager.DesignationOn(plant, DesignationDefOf.CutPlant) != null
                || pawn.Map.designationManager.DesignationOn(plant, DesignationDefOf.HarvestPlant) != null;

            if (isDesignated)
            {
                if (RequiresPlayerFaction && pawn.Faction != Faction.OfPlayer)
                    return false;
            }
            else
            {
                var zone = pawn.Map.zoneManager.ZoneAt(plant.Position) as Zone_Growing;
                if (zone == null || !zone.allowCut || (RequiresPlayerFaction && pawn.Faction != Faction.OfPlayer))
                    return false;
            }

            if (plant.IsForbidden(pawn) || !PlantUtility.PawnWillingToCutPlant_Job(plant, pawn))
                return false;

            return pawn.CanReserve((LocalTargetInfo)plant, 1, -1);
        }

        #endregion

        #region Debug

        public override string ToString()
        {
            return "JobGiver_PlantCutting_PlantsCut_PawnControl";
        }

        #endregion
    }
}