using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    public class JobGiver_PlantCutting_ExtractTree_PawnControl : JobGiver_Common_RemoveBuilding_PawnControl
    {
        #region Overrides

        /// <summary>
        /// Whether this construction job requires specific tag for non-humanlike pawns
        /// </summary>
        protected override PawnEnumTags RequiredTag => PawnEnumTags.AllowWork_PlantCutting;

        protected override DesignationDef TargetDesignation => DesignationDefOf.ExtractTree;

        protected override JobDef WorkJobDef => JobDefOf.ExtractTree;

        protected override string WorkTag => "PlantCutting";

        protected override string DebugName => "ExtractTree";

        protected override float GetBasePriority(string workTag)
        {
            return 5.8f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            return CreateRemovalJob<JobGiver_PlantCutting_ExtractTree_PawnControl>(pawn);
        }

        protected override bool ValidateTarget(Thing thing, Pawn pawn)
        {
            if (!base.ValidateTarget(thing, pawn))
                return false;

            // Must be a plant
            if (!(thing is Plant plant))
                return false;

            // Check plant skill requirements
            if (plant.def.plant.sowMinSkill > 0)
            {
                int plantSkill = pawn.skills?.GetSkill(SkillDefOf.Plants)?.Level ?? 0;
                if (plantSkill < plant.def.plant.sowMinSkill)
                {
                    JobFailReason.Is("UnderAllowedSkill".Translate(plant.def.plant.sowMinSkill));
                    return false;
                }
            }

            return true;
        }

        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            if (pawn == null || targets == null || targets.Count == 0)
                return null;

            // Extra faction validation to ensure only allowed pawns can perform this job
            if (!IsPawnValidFaction(pawn))
                return null;

            // Use the parent class's ExecuteJobGiverInternal method for consistent behavior
            return ExecuteJobGiverInternal(pawn, LimitListSize(targets));
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return "JobGiver_PlantCutting_ExtractTree_PawnControl";
        }

        #endregion
    }
}