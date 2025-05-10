using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    public static class Utility_JobManager
    {
        public static Job TryGetJobFromWorkGiver(Pawn pawn, WorkGiver workGiver)
        {
            if (workGiver is WorkGiver_Scanner scanner)
            {
                return TryGetJobFromScanner(pawn, scanner);
            }

            return null;
        }

        private static Job TryGetJobFromScanner(Pawn pawn, WorkGiver_Scanner scanner)
        {
            if (scanner.def.scanThings)
            {
                var things = scanner.PotentialWorkThingsGlobal(pawn);
                if (things != null)
                {
                    // Use foreach instead of LINQ operations
                    foreach (var thing in things)
                    {
                        if (!thing.IsForbidden(pawn) && scanner.HasJobOnThing(pawn, thing))
                        {
                            return scanner.JobOnThing(pawn, thing);
                        }
                    }
                }
            }

            if (scanner.def.scanCells)
            {
                var cells = scanner.PotentialWorkCellsGlobal(pawn);
                if (cells != null)
                {
                    // Use foreach instead of LINQ operations
                    foreach (var cell in cells)
                    {
                        if (!cell.IsForbidden(pawn) && scanner.HasJobOnCell(pawn, cell))
                        {
                            return scanner.JobOnCell(pawn, cell);
                        }
                    }
                }
            }

            return null;
        }

        public static bool PawnCanUseWorkGiver(Pawn pawn, WorkGiver giver)
        {
            if (!Utility_ThinkTreeManager.HasAllowOrBlockWorkTag(pawn.def) && !giver.def.nonColonistsCanDo && !pawn.IsColonist && !pawn.IsColonyMech && !pawn.IsMutant)
            {
                return false;
            }

            if (Utility_TagManager.WorkDisabled(pawn.def, giver.def.workType.workTags.ToString()))
            {
                return false;
            }

            if (giver.def.workType != null && !Utility_TagManager.WorkEnabled(pawn.def, giver.def.workType.workTags.ToString()))
            {
                return false;
            }

            if (giver.ShouldSkip(pawn))
            {
                return false;
            }

            if (Utility_CacheManager.GetModExtension(pawn.def) == null && giver.MissingRequiredCapacity(pawn) != null)
            {
                return false;
            }

            if (Utility_CacheManager.GetModExtension(pawn.def) == null && pawn.RaceProps.IsMechanoid && !giver.def.canBeDoneByMechs)
            {
                return false;
            }

            if (Utility_CacheManager.GetModExtension(pawn.def) == null && pawn.IsMutant && !giver.def.canBeDoneByMutants)
            {
                return false;
            }

            return true;
        }

        public static Job TraverseThinkTreeAndFindFirstValidJob(Pawn pawn, ThinkNode node)
        {
            if (node == null)
                return null;

            try
            {
                // Attempt to issue a job from the current node
                ThinkResult result = node.TryIssueJobPackage(pawn, default(JobIssueParams));
                if (result.IsValid && result.Job != null)
                {
                    return result.Job;
                }
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Exception while traversing {node.GetType().Name}: {ex}");
            }

            // Recursively traverse subnodes
            if (node.subNodes != null)
            {
                foreach (var subNode in node.subNodes)
                {
                    Job job = TraverseThinkTreeAndFindFirstValidJob(pawn, subNode);
                    if (job != null)
                        return job;
                }
            }

            return null;
        }

        public static Toil GeneratePatchedToil(Toil carryToContainerToil, TargetIndex primaryTargetInd)
        {
            Toil toil = new Toil();

            toil.initAction = () =>
            {
                Pawn actor = toil.actor;
                Job curJob = actor.jobs?.curJob;
                if (actor.carryTracker?.CarriedThing == null || curJob?.targetQueueB == null || curJob.targetQueueB.Count == 0)
                    return;

                if (!actor.Spawned || actor.Map == null)
                    return;

                Thing carried = actor.carryTracker.CarriedThing;
                Thing primaryTarget = curJob.GetTarget(primaryTargetInd).Thing;

                int needed = 0;
                if (primaryTarget is IConstructible constructible)
                {
                    needed = constructible.ThingCountNeeded(carried.def);
                }
                bool hasSpareItems = carried.stackCount > needed;

                Predicate<Thing> validator = th =>
                {
                    if (th == null || th.Destroyed)
                        return false;

                    if (th == primaryTarget)
                        return true;

                    if (th is IConstructible c)
                    {
                        var costList = c.TotalMaterialCost();

                        // Hand-rolled replacement for costList.Any(m => m.thingDef == carried.def)
                        bool foundMatch = false;
                        if (costList != null)
                        {
                            for (int i = 0; i < costList.Count; i++)
                            {
                                if (costList[i].thingDef == carried.def)
                                {
                                    foundMatch = true;
                                    break;
                                }
                            }
                        }

                        if (foundMatch)
                        {
                            return actor.CanReserveAndReach(th, PathEndMode.Touch, Danger.Some);
                        }
                    }

                    return false;
                };

                // The GenClosest method still uses LINQ internally but we can't change that
                List<LocalTargetInfo> targetQueue = new List<LocalTargetInfo>(curJob.targetQueueB);
                List<Thing> thingTargets = new List<Thing>(targetQueue.Count);

                // Convert LocalTargetInfo to Things manually instead of using Select
                for (int i = 0; i < targetQueue.Count; i++)
                {
                    if (targetQueue[i].Thing != null)
                    {
                        thingTargets.Add(targetQueue[i].Thing);
                    }
                }

                Thing nextTarget = GenClosest.ClosestThing_Global_Reachable(
                    actor.Position,
                    actor.Map,
                    thingTargets,
                    PathEndMode.Touch,
                    TraverseParms.For(actor),
                    99999f,
                    validator);

                if (nextTarget != null)
                {
                    // Replace RemoveAll with a manual loop for better performance
                    for (int i = curJob.targetQueueB.Count - 1; i >= 0; i--)
                    {
                        if (curJob.targetQueueB[i].Thing == nextTarget)
                        {
                            curJob.targetQueueB.RemoveAt(i);
                        }
                    }

                    curJob.targetB = nextTarget;
                    actor.jobs.curDriver.JumpToToil(carryToContainerToil);
                }
            };

            return toil;
        }
    }
}