using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that allows non-humanlike pawns to extract trees with the ExtractTree designation.
    /// </summary>
    public class JobGiver_PlantCutting_ExtractTree_PawnControl : JobGiver_Common_RemoveBuilding_PawnControl
    {
        protected override DesignationDef Designation => DesignationDefOf.ExtractTree;

        protected override JobDef RemoveBuildingJob => JobDefOf.ExtractTree;

        public override float GetPriority(Pawn pawn)
        {
            // Plant cutting is a medium priority task, slightly lower than construction
            return 5.8f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Standardized approach to job giving using your utility class
            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_PlantCutting_ExtractTree_PawnControl>(
                pawn,
                "PlantCutting", // This work type is PlantCutting, not Construction
                (p, forced) => {
                    // Let the base class handle the cache update and job creation
                    Job baseJob = base.TryGiveJob(p);

                    // If we got a job from base class, let's do some extra validation specific for trees
                    if (baseJob != null)
                    {
                        Thing target = baseJob.targetA.Thing;
                        if (target is Plant tree)
                        {
                            // Check if pawn has required plant skill (if applicable)
                            if (tree.def.plant.sowMinSkill > 0)
                            {
                                int plantSkill = p.skills?.GetSkill(SkillDefOf.Plants)?.Level ?? 0;
                                if (plantSkill < tree.def.plant.sowMinSkill)
                                {
                                    JobFailReason.Is("UnderAllowedSkill".Translate(tree.def.plant.sowMinSkill));
                                    return null;
                                }
                            }

                            // Additional tree-specific logic could go here

                            return baseJob;
                        }
                        else
                        {
                            // Not a plant, so we can't extract it
                            return null;
                        }
                    }

                    return null;
                },
                debugJobDesc: "tree extraction assignment",
                skipEmergencyCheck: true);
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static new void ResetCache()
        {
            JobGiver_Common_RemoveBuilding_PawnControl.ResetCache();
        }

        public override string ToString()
        {
            return "JobGiver_ExtractTree_PawnControl";
        }
    }
}