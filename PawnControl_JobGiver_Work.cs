using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Copy of vanilla JobGiver_Work with minimal adjustments for PawnControl special worker pawns.
    /// </summary>
    public class PawnControl_JobGiver_Work : ThinkNode
    {
        public bool emergency;

        public override ThinkNode DeepCopy(bool resolve = true)
        {
            PawnControl_JobGiver_Work obj = (PawnControl_JobGiver_Work)base.DeepCopy(resolve);
            obj.emergency = emergency;
            return obj;
        }

        public override float GetPriority(Pawn pawn)
        {
            if (pawn.workSettings == null || !pawn.workSettings.EverWork)
            {
                return 0f;
            }

            return 9f;

            //TimeAssignmentDef timeAssignmentDef = ((pawn.timetable == null) ? TimeAssignmentDefOf.Anything : pawn.timetable.CurrentAssignment);
            //if (timeAssignmentDef == TimeAssignmentDefOf.Anything)
            //{
            //    return 5.5f;
            //}
            //if (timeAssignmentDef == TimeAssignmentDefOf.Work)
            //{
            //    return 9f;
            //}
            //if (timeAssignmentDef == TimeAssignmentDefOf.Sleep)
            //{
            //    return 3f;
            //}
            //if (timeAssignmentDef == TimeAssignmentDefOf.Joy)
            //{
            //    return 2f;
            //}
            //if (timeAssignmentDef == TimeAssignmentDefOf.Meditate)
            //{
            //    return 2f;
            //}
            //throw new NotImplementedException();
        }

        public override ThinkResult TryIssueJobPackage(Pawn pawn, JobIssueParams jobParams)
        {
            if (Prefs.DevMode)
            {
                if (pawn.def.defName.Contains("Snotling"))
                {
                    List<WorkGiver> normalList = pawn.workSettings?.WorkGiversInOrderNormal;
                    List<WorkGiver> emergencyList = pawn.workSettings?.WorkGiversInOrderEmergency;
                    
                    Log.Warning($"[PawnControl DEBUG] Snotling work diagnostics for {pawn.LabelCap}:");
                    Log.Warning($"- workSettings: {(pawn.workSettings == null ? "NULL" : "exists")}");
                    Log.Warning($"- EverWork: {pawn.workSettings?.EverWork}");
                    Log.Warning($"- WorkGiversInOrderNormal: {(normalList == null ? "NULL" : normalList.Count.ToString() + " items")}");
                    Log.Warning($"- WorkGiversInOrderEmergency: {(emergencyList == null ? "NULL" : emergencyList.Count.ToString() + " items")}");
                    Log.Warning($"- hasPawnControlTags: {Utility_ThinkTreeManager.HasAllowOrBlockWorkTag(pawn.def)}");
                    
                    if (normalList != null && normalList.Count > 0)
                    {
                        Log.Warning($"- First WorkGiver: {normalList[0].def.defName}");
                    }
                }
            }

            if (pawn == null || pawn.def == null || pawn.workSettings == null || pawn.workSettings.EverWork == false)
            {
                if (Prefs.DevMode)
                {
                    Log.Warning($"[PawnControl] {pawn?.LabelCap ?? "null pawn"} skipped in TryIssueJobPackage due to invalid work settings.");
                }
                return ThinkResult.NoJob;
            }

            if (pawn.RaceProps.Humanlike && pawn.health.hediffSet.InLabor())
            {
                return ThinkResult.NoJob;
            }

            // Replace Humanlike check with HasAllowOrBlockWorkTag check
            bool hasPawnControlTags = Utility_ThinkTreeManager.HasAllowOrBlockWorkTag(pawn.def);

            // Only perform labor check for non-tagged pawns that are humanlike
            if (!hasPawnControlTags)
            {
                return ThinkResult.NoJob;
            }

            // Handle priority work (keep from vanilla implementation)
            if (emergency && pawn.mindState != null && pawn.mindState.priorityWork != null && pawn.mindState.priorityWork.IsPrioritized)
            {
                List<WorkGiverDef> workGiversByPriority = pawn.mindState.priorityWork.WorkGiver.workType.workGiversByPriority;
                for (int i = 0; i < workGiversByPriority.Count; i++)
                {
                    WorkGiver worker = workGiversByPriority[i].Worker;
                    if (!WorkGiversRelated(pawn.mindState.priorityWork.WorkGiver, worker.def))
                    {
                        continue;
                    }

                    Job job = GiverTryGiveJobPrioritized(pawn, worker, pawn.mindState.priorityWork.Cell);
                    if (job != null)
                    {
                        job.playerForced = true;
                        if (pawn.jobs.debugLog)
                        {
                            pawn.jobs.DebugLogEvent($"JobGiver_Work produced emergency Job {job.ToStringSafe()} from {worker}");
                        }

                        return new ThinkResult(job, this, workGiversByPriority[i].tagToGive);
                    }
                }

                pawn.mindState.priorityWork.Clear();
            }

            // Additional diagnostic code for emergency list
            if (emergency && Prefs.DevMode && pawn.def.defName.Contains("Snotling"))
            {
                Log.Warning($"[PawnControl DEBUG] Emergency WorkGivers check for {pawn.LabelCap}:");
                if (pawn.mindState?.priorityWork != null)
                {
                    Log.Warning($"- Priority work: {pawn.mindState.priorityWork.WorkGiver?.defName ?? "null"}");
                    Log.Warning($"- Priority work is prioritized: {pawn.mindState.priorityWork.IsPrioritized}");
                    Log.Warning($"- Priority work workGivers count: {(pawn.mindState.priorityWork.WorkGiver?.workType?.workGiversByPriority?.Count ?? 0)}");
                }
                else
                {
                    Log.Warning("- No priority work found");
                }
            }

            List<WorkGiver> list;

            // Improved emergency list handling with fallback
            if (emergency)
            {
                var emergencyList = pawn.workSettings.WorkGiversInOrderEmergency;
                
                // If emergency list is empty or null, try to rebuild both lists
                if (emergencyList == null || emergencyList.Count == 0)
                {
                    if (Prefs.DevMode)
                    {
                        Log.Warning($"[PawnControl] Empty emergency WorkGiver list for {pawn.LabelCap}. Attempting to rebuild...");
                    }
                    
                    Utility_WorkSettingsManager.EnsureWorkGiversPopulated(pawn);
                    emergencyList = pawn.workSettings.WorkGiversInOrderEmergency;
                    
                    // If still empty after rebuild, fall back to normal list
                    if (emergencyList == null || emergencyList.Count == 0)
                    {
                        if (Prefs.DevMode)
                        {
                            Log.Warning($"[PawnControl] Falling back to normal WorkGiver list for {pawn.LabelCap} in emergency mode");
                        }
                        list = pawn.workSettings.WorkGiversInOrderNormal;
                    }
                    else
                    {
                        list = emergencyList;
                    }
                }
                else
                {
                    list = emergencyList;
                }
            }
            else
            {
                list = pawn.workSettings.WorkGiversInOrderNormal;
            }

            if (list == null || list.Count == 0)
            {
                if (Prefs.DevMode)
                {
                    Log.Warning($"[PawnControl] Empty WorkGiver list for {pawn.LabelCap}. Attempting to rebuild...");
                }
                Utility_WorkSettingsManager.EnsureWorkGiversPopulated(pawn);
                
                // Get the lists again
                list = ((!emergency) ? pawn.workSettings.WorkGiversInOrderNormal : pawn.workSettings.WorkGiversInOrderEmergency);
                
                if (list == null || list.Count == 0)
                {
                    if (Prefs.DevMode)
                    {
                        Log.Error($"[PawnControl] Failed to rebuild WorkGiver lists for {pawn.LabelCap}. Cannot issue jobs.");
                    }
                    return ThinkResult.NoJob;
                }
            }

            int num = -999;
            TargetInfo bestTargetOfLastPriority = TargetInfo.Invalid;
            WorkGiver_Scanner scannerWhoProvidedTarget = null;
            for (int j = 0; j < list.Count; j++)
            {
                WorkGiver workGiver = list[j];
                if (workGiver.def.priorityInType != num && bestTargetOfLastPriority.IsValid)
                {
                    break;
                }          

                if (!Utility_JobManager.PawnCanUseWorkGiver(pawn, workGiver))
                {
                    continue;
                }

                try
                {
                    Job job2 = workGiver.NonScanJob(pawn);
                    if (job2 != null)
                    {
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
                            IEnumerable<Thing> enumerable = scanner.PotentialWorkThingsGlobal(pawn);
                            bool flag = pawn.carryTracker?.CarriedThing != null && scanner.PotentialWorkThingRequest.Accepts(pawn.carryTracker.CarriedThing) && Validator(pawn.carryTracker.CarriedThing);
                            Thing thing;
                            if (scanner.Prioritized)
                            {
                                IEnumerable<Thing> searchSet = enumerable ?? pawn.Map.listerThings.ThingsMatching(scanner.PotentialWorkThingRequest);
                                thing = ((!scanner.AllowUnreachable) ? GenClosest.ClosestThing_Global_Reachable(pawn.Position, pawn.Map, searchSet, scanner.PathEndMode, TraverseParms.For(pawn, scanner.MaxPathDanger(pawn)), 9999f, Validator, (Thing x) => scanner.GetPriority(pawn, x)) : GenClosest.ClosestThing_Global(pawn.Position, searchSet, 99999f, Validator, (Thing x) => scanner.GetPriority(pawn, x)));
                                if (flag)
                                {
                                    if (thing != null)
                                    {
                                        float num2 = scanner.GetPriority(pawn, pawn.carryTracker.CarriedThing);
                                        float num3 = scanner.GetPriority(pawn, thing);
                                        if (num2 >= num3)
                                        {
                                            thing = pawn.carryTracker.CarriedThing;
                                        }
                                    }
                                    else
                                    {
                                        thing = pawn.carryTracker.CarriedThing;
                                    }
                                }
                            }
                            else if (flag)
                            {
                                thing = pawn.carryTracker.CarriedThing;
                            }
                            else if (scanner.AllowUnreachable)
                            {
                                IEnumerable<Thing> searchSet2 = enumerable ?? pawn.Map.listerThings.ThingsMatching(scanner.PotentialWorkThingRequest);
                                thing = GenClosest.ClosestThing_Global(pawn.Position, searchSet2, 99999f, Validator);
                            }
                            else
                            {
                                thing = GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, scanner.PotentialWorkThingRequest, scanner.PathEndMode, TraverseParms.For(pawn, scanner.MaxPathDanger(pawn)), 9999f, Validator, enumerable, 0, scanner.MaxRegionsToScanBeforeGlobalSearch, enumerable != null);
                            }

                            if (thing != null)
                            {
                                bestTargetOfLastPriority = thing;
                                scannerWhoProvidedTarget = scanner;
                            }
                        }

                        if (scanner.def.scanCells)
                        {
                            pawnPosition = pawn.Position;
                            closestDistSquared = 99999f;
                            bestPriority = float.MinValue;
                            prioritized = scanner.Prioritized;
                            allowUnreachable = scanner.AllowUnreachable;
                            maxPathDanger = scanner.MaxPathDanger(pawn);
                            IEnumerable<IntVec3> enumerable2 = scanner.PotentialWorkCellsGlobal(pawn);
                            if (enumerable2 is IList<IntVec3> list2)
                            {
                                for (int k = 0; k < list2.Count; k++)
                                {
                                    ProcessCell(list2[k]);
                                }
                            }
                            else
                            {
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
                                    return;
                                }

                                num5 = scanner.GetPriority(pawn, c);
                                if (num5 > bestPriority || (num5 == bestPriority && num4 < closestDistSquared))
                                {
                                    flag2 = true;
                                }
                            }
                        }
                        else if (num4 < closestDistSquared && !c.IsForbidden(pawn) && scanner.HasJobOnCell(pawn, c))
                        {
                            if (!allowUnreachable && !pawn.CanReach(c, scanner.PathEndMode, maxPathDanger))
                            {
                                return;
                            }

                            flag2 = true;
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
                        if (!t.IsForbidden(pawn))
                        {
                            return scanner.HasJobOnThing(pawn, t);
                        }

                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(string.Concat(pawn, " threw exception in WorkGiver ", workGiver.def.defName, ": ", ex.ToString()));
                }
                finally
                {
                }

                if (bestTargetOfLastPriority.IsValid)
                {
                    Job job3 = ((!bestTargetOfLastPriority.HasThing) ? scannerWhoProvidedTarget.JobOnCell(pawn, bestTargetOfLastPriority.Cell) : scannerWhoProvidedTarget.JobOnThing(pawn, bestTargetOfLastPriority.Thing));
                    if (job3 != null)
                    {
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

            return ThinkResult.NoJob;
        }

        // Required helper methods from original implementation
        private bool WorkGiversRelated(WorkGiverDef current, WorkGiverDef next)
        {
            if (current == null || next == null)
            {
                return false;
            }

            if (next != WorkGiverDefOf.Repair || current == WorkGiverDefOf.Repair)
            {
                return next.doesSmoothing == current.doesSmoothing;
            }

            return false;
        }

        private Job GiverTryGiveJobPrioritized(Pawn pawn, WorkGiver giver, IntVec3 cell)
        {
            // Use our utility method for consistency
            if (!Utility_JobManager.PawnCanUseWorkGiver(pawn, giver))
            {
                return null;
            }

            try
            {
                Job job = giver.NonScanJob(pawn);
                if (job != null)
                {
                    return job;
                }

                WorkGiver_Scanner scanner = giver as WorkGiver_Scanner;
                if (scanner != null)
                {
                    if (giver.def.scanThings)
                    {
                        Predicate<Thing> predicate = (Thing t) => !t.IsForbidden(pawn) && scanner.HasJobOnThing(pawn, t);
                        List<Thing> thingList = cell.GetThingList(pawn.Map);
                        for (int i = 0; i < thingList.Count; i++)
                        {
                            Thing thing = thingList[i];
                            if (scanner.PotentialWorkThingRequest.Accepts(thing) && predicate(thing))
                            {
                                Job job2 = scanner.JobOnThing(pawn, thing);
                                if (job2 != null)
                                {
                                    job2.workGiverDef = giver.def;
                                }

                                return job2;
                            }
                        }
                    }

                    if (giver.def.scanCells && !cell.IsForbidden(pawn) && scanner.HasJobOnCell(pawn, cell))
                    {
                        Job job3 = scanner.JobOnCell(pawn, cell);
                        if (job3 != null)
                        {
                            job3.workGiverDef = giver.def;
                        }

                        return job3;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"{pawn} threw exception in GiverTryGiveJobPrioritized on WorkGiver {giver.def.defName}: {ex}");
            }

            return null;
        }
    }
}