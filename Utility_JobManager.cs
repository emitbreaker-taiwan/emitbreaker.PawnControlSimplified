using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    public static class Utility_JobManager
    {
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
                        bool hasMatchingDef = false;
                        if (costList != null)
                        {
                            for (int ci = 0, cc = costList.Count; ci < cc; ci++)
                            {
                                if (costList[ci].thingDef == carried.def)
                                {
                                    hasMatchingDef = true;
                                    break;
                                }
                            }
                        }
                        if (hasMatchingDef)
                        {
                            return actor.CanReserveAndReach(th, PathEndMode.Touch, Danger.Some);
                        }
                    }

                    return false;
                };

                // Build a plain List<Thing> from the Job’s targetQueueB
                List<Thing> thingSearchSet = new List<Thing>(curJob.targetQueueB.Count);
                for (int qi = 0, qc = curJob.targetQueueB.Count; qi < qc; qi++)
                {
                    var q = curJob.targetQueueB[qi];
                    if (q.Thing != null)
                        thingSearchSet.Add(q.Thing);
                }

                Thing nextTarget = GenClosest.ClosestThing_Global_Reachable(
                    actor.Position,
                    actor.Map,
                    thingSearchSet,                // now a List<Thing>, no LINQ
                    PathEndMode.Touch,
                    TraverseParms.For(actor),
                    99999f,
                    validator);

                if (nextTarget != null)
                {
                    curJob.targetQueueB.RemoveAll(t => t.Thing == nextTarget);
                    curJob.targetB = nextTarget;
                    actor.jobs.curDriver.JumpToToil(carryToContainerToil);
                }
            };

            return toil;
        }
    }
}
