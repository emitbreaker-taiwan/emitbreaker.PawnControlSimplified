using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    public class JobGiver_PlantCutting_ExtractTree_PawnControl : JobGiver_Common_RemoveBuilding_PawnControl
    {
        #region Overrides

        protected override DesignationDef Designation => DesignationDefOf.ExtractTree;

        protected override JobDef RemoveBuildingJob => JobDefOf.ExtractTree;

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

            return thing is Plant;
        }

        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // Process the cached targets to create a job for the pawn
            foreach (var target in targets)
            {
                if (ValidateTarget(target, pawn))
                {
                    return JobMaker.MakeJob(RemoveBuildingJob, target);
                }
            }
            return null;
        }

        #endregion

        #region Plant-specific helpers

        private Job ExecuteJobGiverWithPlantValidation(Pawn pawn, List<Thing> targets)
        {
            Job baseJob = ExecuteJobGiverInternal(pawn, targets);

            if (baseJob != null && baseJob.targetA.Thing is Plant tree)
            {
                if (tree.def.plant.sowMinSkill > 0)
                {
                    int plantSkill = pawn.skills?.GetSkill(SkillDefOf.Plants)?.Level ?? 0;
                    if (plantSkill < tree.def.plant.sowMinSkill)
                    {
                        JobFailReason.Is("UnderAllowedSkill".Translate(tree.def.plant.sowMinSkill));
                        return null;
                    }
                }

                return baseJob;
            }

            return null;
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