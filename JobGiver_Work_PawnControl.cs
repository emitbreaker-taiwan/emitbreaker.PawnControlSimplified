// File: JobGiver_Work_PawnControl.cs
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    public class JobGiver_Work_PawnControl : ThinkNode
    {
        public bool emergency;

        public override ThinkNode DeepCopy(bool resolve = true)
        {
            JobGiver_Work_PawnControl obj = (JobGiver_Work_PawnControl)base.DeepCopy(resolve);
            obj.emergency = emergency;
            return obj;
        }

        public override float GetPriority(Pawn pawn)
        {
            return 5.5f;
        }

        public override ThinkResult TryIssueJobPackage(Pawn pawn, JobIssueParams jobParams)
        {
            if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                Utility_DebugManager.LogNormal($"JobGiver_Work_PawnControl.TryIssueJobPackage started for {pawn.LabelShort}, emergency={emergency}");

            if (!Utility_Common.PawnChecker(pawn))
            {
                if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                    Utility_DebugManager.LogNormal($"PawnChecker failed for {pawn.LabelShort}, returning NoJob");
                return ThinkResult.NoJob;
            }

            if (pawn.Faction == null)
            {
                if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} has no faction, returning NoJob");
                return ThinkResult.NoJob;
            }

            if (Utility_CacheManager.GetModExtension(pawn.def) == null)
            {
                if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} has no mod extension");
                return ThinkResult.NoJob;
            }

            if (!Utility_TagManager.HasTagSet(pawn.def))
            {
                if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} has no tag set");
                return ThinkResult.NoJob;
            }

            if (pawn.RaceProps.Humanlike && pawn.health.hediffSet.InLabor())
            {
                if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} is in labor, returning NoJob");
                return ThinkResult.NoJob;
            }

            if (emergency && pawn.mindState.priorityWork.IsPrioritized)
            {
                if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                    Utility_DebugManager.LogNormal($"Processing emergency prioritized work for {pawn.LabelShort}");
                List<WorkGiverDef> workGiversByPriority = pawn.mindState.priorityWork.WorkGiver.workType.workGiversByPriority;
                for (int i = 0; i < workGiversByPriority.Count; i++)
                {
                    WorkGiver worker = workGiversByPriority[i].Worker;
                    if (!WorkGiversRelated(pawn.mindState.priorityWork.WorkGiver, worker.def))
                    {
                        continue;
                    }

                    if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                        Utility_DebugManager.LogNormal($"Trying emergency prioritized job from {worker.def.defName}");
                    Job job = GiverTryGiveJobPrioritized(pawn, worker, pawn.mindState.priorityWork.Cell);
                    if (job != null)
                    {
                        if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                            Utility_DebugManager.LogNormal($"Found emergency job: {job.def.defName} from {worker.def.defName}");
                        job.playerForced = true;
                        if (pawn.jobs.debugLog)
                        {
                            pawn.jobs.DebugLogEvent($"JobGiver_Work produced emergency Job {job.ToStringSafe()} from {worker}");
                        }

                        return new ThinkResult(job, this, workGiversByPriority[i].tagToGive);
                    }
                }

                if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                    Utility_DebugManager.LogNormal($"No emergency job found, clearing priority work for {pawn.LabelShort}");
                pawn.mindState.priorityWork.Clear();
            }

            List<WorkGiver> list = ((!emergency) ? pawn.workSettings.WorkGiversInOrderNormal : pawn.workSettings.WorkGiversInOrderEmergency);
            if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                Utility_DebugManager.LogNormal($"Processing {list.Count} work givers in {(emergency ? "emergency" : "normal")} mode for {pawn.LabelShort}");

            int num = -999;
            TargetInfo bestTargetOfLastPriority = TargetInfo.Invalid;
            WorkGiver_Scanner scannerWhoProvidedTarget = null;
            for (int j = 0; j < list.Count; j++)
            {
                WorkGiver workGiver = list[j];
                if (workGiver.def.priorityInType != num && bestTargetOfLastPriority.IsValid)
                {
                    if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                        Utility_DebugManager.LogNormal($"Breaking out of loop: priority changed and we already have a valid target");
                    break;
                }

                if (!PawnCanUseWorkGiver(pawn, workGiver))
                {
                    if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                        Utility_DebugManager.LogNormal($"{pawn.LabelShort} cannot use WorkGiver {workGiver.def.defName}, skipping");
                    continue;
                }

                try
                {
                    if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                        Utility_DebugManager.LogNormal($"Trying non-scan job from {workGiver.def.defName}");
                    Job job2 = workGiver.NonScanJob(pawn);
                    if (job2 != null)
                    {
                        if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                            Utility_DebugManager.LogNormal($"Found non-scan job: {job2.def.defName} from {workGiver.def.defName}");
                        if (pawn.jobs.debugLog)
                        {
                            pawn.jobs.DebugLogEvent($"JobGiver_Work produced non-scan Job {job2.ToStringSafe()} from {workGiver}");
                        }

                        return new ThinkResult(job2, this, list[j].def.tagToGive);
                    }

                    WorkGiver_Scanner scanner;
                    IntVec3 pawnPosition;
                    float closestDistSquared;
                    float bestPriority;
                    bool prioritized;
                    bool allowUnreachable;
                    Danger maxPathDanger;
                    if ((scanner = workGiver as WorkGiver_Scanner) != null)
                    {
                        if (scanner.def.scanThings)
                        {
                            if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                                Utility_DebugManager.LogNormal($"Processing scanThings for {scanner.def.defName}");
                            IEnumerable<Thing> enumerable = scanner.PotentialWorkThingsGlobal(pawn);
                            bool flag = pawn.carryTracker?.CarriedThing != null && scanner.PotentialWorkThingRequest.Accepts(pawn.carryTracker.CarriedThing) && Validator(pawn.carryTracker.CarriedThing);
                            Thing thing;
                            if (scanner.Prioritized)
                            {
                                if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                                    Utility_DebugManager.LogNormal($"Scanner is prioritized");
                                IEnumerable<Thing> searchSet = enumerable ?? pawn.Map.listerThings.ThingsMatching(scanner.PotentialWorkThingRequest);
                                if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                                    Utility_DebugManager.LogNormal($"Looking for closest thing with reachable={!scanner.AllowUnreachable}");
                                thing = ((!scanner.AllowUnreachable) ? GenClosest.ClosestThing_Global_Reachable(pawn.Position, pawn.Map, searchSet, scanner.PathEndMode, TraverseParms.For(pawn, scanner.MaxPathDanger(pawn)), 9999f, Validator, (Thing x) => scanner.GetPriority(pawn, x)) : GenClosest.ClosestThing_Global(pawn.Position, searchSet, 99999f, Validator, (Thing x) => scanner.GetPriority(pawn, x)));
                                if (flag)
                                {
                                    if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                                        Utility_DebugManager.LogNormal("Evaluating carried thing as potential work thing");
                                    if (thing != null)
                                    {
                                        float num2 = scanner.GetPriority(pawn, pawn.carryTracker.CarriedThing);
                                        float num3 = scanner.GetPriority(pawn, thing);
                                        if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                                            Utility_DebugManager.LogNormal($"Comparing priorities: carried={num2}, found={num3}");
                                        if (num2 >= num3)
                                        {
                                            thing = pawn.carryTracker.CarriedThing;
                                            if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                                                Utility_DebugManager.LogNormal("Using carried thing as it has higher priority");
                                        }
                                    }
                                    else
                                    {
                                        thing = pawn.carryTracker.CarriedThing;
                                        if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                                            Utility_DebugManager.LogNormal("Using carried thing as no other thing found");
                                    }
                                }
                            }
                            else if (flag)
                            {
                                thing = pawn.carryTracker.CarriedThing;
                                if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                                    Utility_DebugManager.LogNormal("Using carried thing (non-prioritized scanner)");
                            }
                            else if (scanner.AllowUnreachable)
                            {
                                if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                                    Utility_DebugManager.LogNormal("Scanner allows unreachable, using global search");
                                IEnumerable<Thing> searchSet2 = enumerable ?? pawn.Map.listerThings.ThingsMatching(scanner.PotentialWorkThingRequest);
                                thing = GenClosest.ClosestThing_Global(pawn.Position, searchSet2, 99999f, Validator);
                            }
                            else
                            {
                                if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                                    Utility_DebugManager.LogNormal("Using normal reachable search");
                                thing = GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, scanner.PotentialWorkThingRequest, scanner.PathEndMode, TraverseParms.For(pawn, scanner.MaxPathDanger(pawn)), 9999f, Validator, enumerable, 0, scanner.MaxRegionsToScanBeforeGlobalSearch, enumerable != null);
                            }

                            if (thing != null)
                            {
                                if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                                    Utility_DebugManager.LogNormal($"Found valid work thing: {thing}");
                                bestTargetOfLastPriority = thing;
                                scannerWhoProvidedTarget = scanner;
                            }
                            else
                            {
                                if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                                    Utility_DebugManager.LogNormal("No valid work thing found");
                            }
                        }

                        if (scanner.def.scanCells)
                        {
                            if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                                Utility_DebugManager.LogNormal($"Processing scanCells for {scanner.def.defName}");
                            pawnPosition = pawn.Position;
                            closestDistSquared = 99999f;
                            bestPriority = float.MinValue;
                            prioritized = scanner.Prioritized;
                            allowUnreachable = scanner.AllowUnreachable;
                            maxPathDanger = scanner.MaxPathDanger(pawn);
                            IEnumerable<IntVec3> enumerable2 = scanner.PotentialWorkCellsGlobal(pawn);
                            if (enumerable2 is IList<IntVec3> list2)
                            {
                                if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                                    Utility_DebugManager.LogNormal($"Processing {list2.Count} cells from IList");
                                for (int k = 0; k < list2.Count; k++)
                                {
                                    ProcessCell(list2[k]);
                                }
                            }
                            else
                            {
                                if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                                    Utility_DebugManager.LogNormal("Processing cells from general IEnumerable");
                                foreach (IntVec3 item in enumerable2)
                                {
                                    ProcessCell(item);
                                }
                            }
                        }
                    }

                    void ProcessCell(IntVec3 c)
                    {
                        bool flag2 = false;
                        float num4 = (c - pawnPosition).LengthHorizontalSquared;
                        float num5 = 0f;
                        if (prioritized)
                        {
                            if (!c.IsForbidden(pawn) && scanner.HasJobOnCell(pawn, c))
                            {
                                if (!allowUnreachable && !pawn.CanReach(c, scanner.PathEndMode, maxPathDanger))
                                {
                                    if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                                        Utility_DebugManager.LogNormal($"Cell {c} is unreachable, skipping");
                                    return;
                                }

                                num5 = scanner.GetPriority(pawn, c);
                                if (num5 > bestPriority || (num5 == bestPriority && num4 < closestDistSquared))
                                {
                                    flag2 = true;
                                    if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                                        Utility_DebugManager.LogNormal($"Cell {c} is valid priority target (prio: {num5}, dist: {num4})");
                                }
                            }
                        }
                        else if (num4 < closestDistSquared && !c.IsForbidden(pawn) && scanner.HasJobOnCell(pawn, c))
                        {
                            if (!allowUnreachable && !pawn.CanReach(c, scanner.PathEndMode, maxPathDanger))
                            {
                                if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                                    Utility_DebugManager.LogNormal($"Cell {c} is unreachable, skipping");
                                return;
                            }

                            flag2 = true;
                            if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                                Utility_DebugManager.LogNormal($"Cell {c} is valid distance-based target (dist: {num4})");
                        }

                        if (flag2)
                        {
                            bestTargetOfLastPriority = new TargetInfo(c, pawn.Map);
                            scannerWhoProvidedTarget = scanner;
                            closestDistSquared = num4;
                            bestPriority = num5;
                        }
                    }

                    bool Validator(Thing t)
                    {
                        bool result = !t.IsForbidden(pawn) && scanner.HasJobOnThing(pawn, t);
                        if (Utility_DebugManager.ShouldLogDetailed())
                        {
                            Utility_DebugManager.LogNormal($"Validating {t}: forbidden={t.IsForbidden(pawn)}, hasJob={scanner.HasJobOnThing(pawn, t)}, result={result}");
                        }
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"{pawn} threw exception in WorkGiver {workGiver.def.defName}: {ex}");
                    Log.Error(string.Concat(pawn, " threw exception in WorkGiver ", workGiver.def.defName, ": ", ex.ToString()));
                }
                finally
                {
                }

                if (bestTargetOfLastPriority.IsValid)
                {
                    if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                        Utility_DebugManager.LogNormal($"Found valid target, attempting to get job");
                    Job job3 = ((!bestTargetOfLastPriority.HasThing) ? scannerWhoProvidedTarget.JobOnCell(pawn, bestTargetOfLastPriority.Cell) : scannerWhoProvidedTarget.JobOnThing(pawn, bestTargetOfLastPriority.Thing));
                    if (job3 != null)
                    {
                        if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                            Utility_DebugManager.LogNormal($"Successfully created job {job3.def.defName} from scanner {scannerWhoProvidedTarget.def.defName}");
                        job3.workGiverDef = scannerWhoProvidedTarget.def;
                        if (pawn.jobs.debugLog)
                        {
                            pawn.jobs.DebugLogEvent($"JobGiver_Work produced scan Job {job3.ToStringSafe()} from {scannerWhoProvidedTarget}");
                        }

                        return new ThinkResult(job3, this, list[j].def.tagToGive);
                    }

                    Log.ErrorOnce(string.Concat(scannerWhoProvidedTarget, " provided target ", bestTargetOfLastPriority, " but yielded no actual job for pawn ", pawn, ". The CanGiveJob and JobOnX methods may not be synchronized."), 6112651);
                }

                num = workGiver.def.priorityInType;
            }

            if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                Utility_DebugManager.LogNormal($"No job found for {pawn.LabelShort}, returning NoJob");
            return ThinkResult.NoJob;
        }

        private bool PawnCanUseWorkGiver(Pawn pawn, WorkGiver giver)
        {
            if (!Utility_Common.PawnCompatibilityChecker(pawn))
            {
                if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} failed PawnCompatibilityChecker");
                return false;
            }

            if (!Utility_TagManager.IsWorkEnabled(pawn, giver.def.workType))
            {
                if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                    Utility_DebugManager.LogNormal($"Work type {giver.def.workType?.defName ?? "null"} is not enabled for {pawn.LabelShort}");
                return false;
            }

            if (giver.def.workType != null && Utility_TagManager.IsWorkDisabled(pawn, giver.def.workType))
            {
                if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                    Utility_DebugManager.LogNormal($"Work type {giver.def.workType.defName} is explicitly disabled for {pawn.LabelShort}");
                return false;
            }

            if (giver.ShouldSkip(pawn))
            {
                if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                    Utility_DebugManager.LogNormal($"WorkGiver {giver.def.defName} reports it should be skipped for {pawn.LabelShort}");
                return false;
            }

            if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} can use WorkGiver {giver.def.defName}");
            return true;
        }

        private bool WorkGiversRelated(WorkGiverDef current, WorkGiverDef next)
        {
            bool result;
            if (next != WorkGiverDefOf.Repair || current == WorkGiverDefOf.Repair)
            {
                result = next.doesSmoothing == current.doesSmoothing;
            }
            else
            {
                result = false;
            }

            if (Utility_DebugManager.ShouldLogDetailed())
                Utility_DebugManager.LogNormal($"WorkGiversRelated: {current.defName} -> {next.defName} = {result}");
            return result;
        }

        private Job GiverTryGiveJobPrioritized(Pawn pawn, WorkGiver giver, IntVec3 cell)
        {
            if (!PawnCanUseWorkGiver(pawn, giver))
            {
                if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                    Utility_DebugManager.LogNormal($"GiverTryGiveJobPrioritized: {pawn.LabelShort} cannot use {giver.def.defName}");
                return null;
            }

            try
            {
                if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                    Utility_DebugManager.LogNormal($"GiverTryGiveJobPrioritized: Trying non-scan job for {giver.def.defName}");
                Job job = giver.NonScanJob(pawn);
                if (job != null)
                {
                    if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                        Utility_DebugManager.LogNormal($"GiverTryGiveJobPrioritized: Found non-scan job {job.def.defName}");
                    return job;
                }

                WorkGiver_Scanner scanner = giver as WorkGiver_Scanner;
                if (scanner != null)
                {
                    if (giver.def.scanThings)
                    {
                        if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                            Utility_DebugManager.LogNormal($"GiverTryGiveJobPrioritized: Scanning things at cell {cell}");
                        Predicate<Thing> predicate = (Thing t) => !t.IsForbidden(pawn) && scanner.HasJobOnThing(pawn, t);
                        List<Thing> thingList = cell.GetThingList(pawn.Map);
                        if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                            Utility_DebugManager.LogNormal($"Found {thingList.Count} things at cell");

                        for (int i = 0; i < thingList.Count; i++)
                        {
                            Thing thing = thingList[i];
                            if (scanner.PotentialWorkThingRequest.Accepts(thing) && predicate(thing))
                            {
                                if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                                    Utility_DebugManager.LogNormal($"Found acceptable thing: {thing}");
                                Job job2 = scanner.JobOnThing(pawn, thing);
                                if (job2 != null)
                                {
                                    if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                                        Utility_DebugManager.LogNormal($"Created job {job2.def.defName} for thing {thing}");
                                    job2.workGiverDef = giver.def;
                                    return job2;
                                }
                                if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                                    Utility_DebugManager.LogWarning($"Thing {thing} passed validation but JobOnThing returned null");
                            }
                        }
                    }

                    if (giver.def.scanCells && !cell.IsForbidden(pawn) && scanner.HasJobOnCell(pawn, cell))
                    {
                        if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                            Utility_DebugManager.LogNormal($"GiverTryGiveJobPrioritized: Checking for job on cell {cell}");
                        Job job3 = scanner.JobOnCell(pawn, cell);
                        if (job3 != null)
                        {
                            if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                                Utility_DebugManager.LogNormal($"Created cell-based job {job3.def.defName}");
                            job3.workGiverDef = giver.def;
                            return job3;
                        }
                        if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLog())
                            Utility_DebugManager.LogWarning($"Cell {cell} passed HasJobOnCell but JobOnCell returned null");
                    }
                }
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"{pawn} threw exception in GiverTryGiveJobPrioritized for WorkGiver {giver.def.defName}: {ex}");
                Log.Error(string.Concat(pawn, " threw exception in GiverTryGiveJobTargeted on WorkGiver ", giver.def.defName, ": ", ex.ToString()));
            }

            if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                Utility_DebugManager.LogNormal($"GiverTryGiveJobPrioritized: No job found for {pawn.LabelShort} with {giver.def.defName}");
            return null;
        }
    }
}