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
    public class JobGiver_WorkNonHumanlike : ThinkNode
    {


        public bool emergency;

        public override ThinkNode DeepCopy(bool resolve = true)
        {
            JobGiver_WorkNonHumanlike obj = (JobGiver_WorkNonHumanlike)base.DeepCopy(resolve);
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
        }

        public override ThinkResult TryIssueJobPackage(Pawn pawn, JobIssueParams jobParams)
        {
            try
            {
                if (pawn == null || pawn.def == null)
                {
                    return ThinkResult.NoJob;
                }

                var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                if (modExtension == null)
                {
                    return ThinkResult.NoJob;
                }

                // CRITICAL FIX: Set mind state to active, which is required for work AI to function
                if (pawn.mindState != null)
                {
                    pawn.mindState.Active = true;

                    if (Prefs.DevMode && modExtension.debugMode)
                    {
                        Log.Message($"[PawnControl] Setting {pawn.LabelShort}'s mindState.Active = true");
                    }
                }

                // Work settings check
                if (pawn.workSettings == null || !pawn.workSettings.EverWork)
                {
                    if (Prefs.DevMode && modExtension.debugMode)
                    {
                        Log.Warning($"[PawnControl] {pawn.LabelShort} has no work settings or !EverWork");
                    }
                    return ThinkResult.NoJob;
                }

                // Labor check only applies to humanlike pawns
                if (pawn.RaceProps.Humanlike && pawn.health.hediffSet.InLabor())
                {
                    return ThinkResult.NoJob;
                }

                // FIXED: Check if this pawn should be handled by PawnControl
                bool hasPawnControlTags = Utility_ThinkTreeManager.HasAllowOrBlockWorkTag(pawn.def);

                // Only continue if pawn has tags OR is an animal
                if (!hasPawnControlTags)
                {
                    if (Prefs.DevMode && modExtension.debugMode)
                    {
                        Log.Warning($"[PawnControl] {pawn.LabelShort} has no work tags.");
                    }
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
                if (emergency && Prefs.DevMode && modExtension.debugMode)
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
                        if (Prefs.DevMode && modExtension.debugMode)
                        {
                            Log.Warning($"[PawnControl] Empty emergency WorkGiver list for {pawn.LabelCap}. Attempting to rebuild...");
                        }

                        Utility_WorkSettingsManager.EnsureWorkGiversPopulated(pawn);
                        emergencyList = pawn.workSettings.WorkGiversInOrderEmergency;

                        // If still empty after rebuild, fall back to normal list
                        if (emergencyList == null || emergencyList.Count == 0)
                        {
                            if (Prefs.DevMode && modExtension.debugMode)
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
                    if (Prefs.DevMode && modExtension.debugMode)
                    {
                        Log.Warning($"[PawnControl] Empty WorkGiver list for {pawn.LabelCap}. Attempting to rebuild...");
                    }
                    Utility_WorkSettingsManager.EnsureWorkGiversPopulated(pawn);

                    // Get the lists again
                    list = ((!emergency) ? pawn.workSettings.WorkGiversInOrderNormal : pawn.workSettings.WorkGiversInOrderEmergency);

                    if (list == null || list.Count == 0)
                    {
                        if (Prefs.DevMode && modExtension.debugMode)
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
                        Job job3 = null;
                        try
                        {
                            job3 = bestTargetOfLastPriority.HasThing
                                ? scannerWhoProvidedTarget.JobOnThing(pawn, bestTargetOfLastPriority.Thing)
                                : scannerWhoProvidedTarget.JobOnCell(pawn, bestTargetOfLastPriority.Cell);
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"[PawnControl] Exception at priority boundary from {scannerWhoProvidedTarget?.def?.defName}: {ex}");
                        }

                        if (job3 != null)
                        {
                            job3.workGiverDef = scannerWhoProvidedTarget.def;
                            return new ThinkResult(job3, this, list[j - 1].def.tagToGive);
                        }

                        bestTargetOfLastPriority = TargetInfo.Invalid;
                        scannerWhoProvidedTarget = null;
                        continue;
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

                            if (Prefs.DevMode && modExtension.debugMode)
                            {
                                Log.Message($"[PawnControl] Found NonScanJob {job2.def.defName} for {pawn.LabelShort} from {workGiver.def.defName}");
                            }

                            return new ThinkResult(job2, this, list[j].def.tagToGive);
                        }

                        WorkGiver_Scanner scanner = workGiver as WorkGiver_Scanner;
                        if (scanner != null)
                        {
                            // Handle scanThings part
                            if (scanner.def.scanThings)
                            {
                                IEnumerable<Thing> searchSet = scanner.PotentialWorkThingsGlobal(pawn) ?? pawn.Map.listerThings.ThingsMatching(scanner.PotentialWorkThingRequest);
                                bool allowUnreachable = scanner.AllowUnreachable;
                                Danger maxDanger = scanner.MaxPathDanger(pawn);

                                Predicate<Thing> validator = (Thing t) => !t.IsForbidden(pawn) && scanner.HasJobOnThing(pawn, t);
                                Func<Thing, float> priorityGetter = (Thing t) => scanner.GetPriority(pawn, t);

                                Thing thing;

                                if (scanner.Prioritized)
                                {
                                    thing = allowUnreachable
                                        ? GenClosest.ClosestThing_Global(pawn.Position, searchSet, 9999f, validator, priorityGetter)
                                        : GenClosest.ClosestThing_Global_Reachable(pawn.Position, pawn.Map, searchSet, scanner.PathEndMode,
                                            TraverseParms.For(pawn, maxDanger), 9999f, validator, priorityGetter);
                                }
                                else
                                {
                                    thing = allowUnreachable
                                        ? GenClosest.ClosestThing_Global(pawn.Position, searchSet, 9999f, validator)
                                        : GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, scanner.PotentialWorkThingRequest,
                                            scanner.PathEndMode, TraverseParms.For(pawn, maxDanger), 9999f, validator, searchSet,
                                            0, scanner.MaxRegionsToScanBeforeGlobalSearch, true);
                                }

                                if (thing != null)
                                {
                                    bestTargetOfLastPriority = thing;
                                    scannerWhoProvidedTarget = scanner;

                                    // Add job caching here - create and cache the job immediately
                                    try
                                    {
                                        Job cachedJob = scanner.JobOnThing(pawn, thing);
                                        if (cachedJob != null)
                                        {
                                            Utility_CacheManager._jobCache[thing] = cachedJob;
                                            if (Prefs.DevMode && modExtension.debugMode)
                                            {
                                                Log.Message($"[PawnControl] Cached job {cachedJob.def.defName} for {thing.Label}");
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Error($"[PawnControl] Error while caching job: {ex}");
                                    }

                                    if (Prefs.DevMode && modExtension.debugMode)
                                    {
                                        Log.Message($"[PawnControl] Found potential work thing for {pawn.LabelShort}: {thing.Label}");
                                    }
                                }
                                else if (Prefs.DevMode && modExtension.debugMode)
                                {
                                    Log.Warning($"[PawnControl DEBUG] No valid thing target found for {scanner.def.defName} on {pawn.LabelCap}");
                                }
                            }

                            // FIXED: Handle scanCells part
                            if (scanner.def.scanCells)
                            {
                                IntVec3 pawnPosition = pawn.Position;
                                float closestDistSquared = 99999f;
                                float bestPriority = float.MinValue;
                                bool prioritized = scanner.Prioritized;
                                bool allowUnreachable = scanner.AllowUnreachable;
                                Danger maxPathDanger = scanner.MaxPathDanger(pawn);

                                foreach (IntVec3 cell in scanner.PotentialWorkCellsGlobal(pawn))
                                {
                                    bool isBetter = false;
                                    float distSq = (cell - pawnPosition).LengthHorizontalSquared;

                                    if (prioritized)
                                    {
                                        if (!cell.IsForbidden(pawn) && scanner.HasJobOnCell(pawn, cell))
                                        {
                                            if (!allowUnreachable && !pawn.CanReach(cell, scanner.PathEndMode, maxPathDanger))
                                                continue;

                                            float priority = scanner.GetPriority(pawn, cell);
                                            if (priority > bestPriority || (priority == bestPriority && distSq < closestDistSquared))
                                            {
                                                isBetter = true;
                                                bestPriority = priority;
                                            }
                                        }
                                    }
                                    else if (distSq < closestDistSquared && !cell.IsForbidden(pawn) && scanner.HasJobOnCell(pawn, cell))
                                    {
                                        if (!allowUnreachable && !pawn.CanReach(cell, scanner.PathEndMode, maxPathDanger))
                                            continue;

                                        isBetter = true;
                                    }

                                    if (isBetter)
                                    {
                                        bestTargetOfLastPriority = new TargetInfo(cell, pawn.Map);
                                        scannerWhoProvidedTarget = scanner;
                                        closestDistSquared = distSq;

                                        if (Prefs.DevMode && modExtension.debugMode)
                                        {
                                            Log.Message($"[PawnControl] Found potential work cell for {pawn.LabelShort} at {cell}");
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(string.Concat(pawn, " threw exception in WorkGiver ", workGiver.def.defName, ": ", ex.ToString()));
                    }

                    if (bestTargetOfLastPriority.IsValid)
                    {
                        Job job3 = null;
                        try
                        {
                            job3 = (!bestTargetOfLastPriority.HasThing)
                                ? scannerWhoProvidedTarget.JobOnCell(pawn, bestTargetOfLastPriority.Cell)
                                : scannerWhoProvidedTarget.JobOnThing(pawn, bestTargetOfLastPriority.Thing);
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"[PawnControl] Exception getting job from scanner {scannerWhoProvidedTarget?.def?.defName}: {ex}");
                        }

                        if (job3 != null)
                        {
                            job3.workGiverDef = scannerWhoProvidedTarget.def;

                            // Log successful job creation
                            if (Prefs.DevMode && modExtension.debugMode)
                            {
                                Log.Message($"[PawnControl] Created job {job3.def.defName} for {pawn.LabelShort} with workGiver {scannerWhoProvidedTarget.def.defName}");
                            }

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

                // ✅ Must add after the WorkGiver loop to return the job if found
                if (bestTargetOfLastPriority.IsValid && scannerWhoProvidedTarget != null)
                {
                    Job job3 = null;
                    try
                    {
                        if (Prefs.DevMode && modExtension.debugMode)
                        {
                            Log.Message($"[PawnControl] Attempting final job creation for {pawn.LabelShort} with {(bestTargetOfLastPriority.HasThing ? bestTargetOfLastPriority.Thing.Label : "cell")}");
                        }

                        // Check for cached job first before calling JobOnThing again
                        if (bestTargetOfLastPriority.HasThing && Utility_CacheManager._jobCache.TryGetValue(bestTargetOfLastPriority.Thing, out Job cachedJob))
                        {
                            job3 = cachedJob;
                            Utility_CacheManager._jobCache.Remove(bestTargetOfLastPriority.Thing); // Use it once then remove
                            
                            if (Prefs.DevMode && modExtension.debugMode)
                            {
                                Log.Message($"[PawnControl] Using cached job {job3.def.defName} for {bestTargetOfLastPriority.Thing.Label}");
                            }
                        }
                        else
                        {
                            // Fall back to original job creation if no cached job exists
                            job3 = (!bestTargetOfLastPriority.HasThing)
                                ? scannerWhoProvidedTarget.JobOnCell(pawn, bestTargetOfLastPriority.Cell)
                                : scannerWhoProvidedTarget.JobOnThing(pawn, bestTargetOfLastPriority.Thing);
                        }

                        if (job3 == null && Prefs.DevMode && modExtension.debugMode)
                        {
                            Log.Warning($"[PawnControl] Second JobOnThing call returned null despite initial success");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[PawnControl] Exception getting job from scanner {scannerWhoProvidedTarget?.def?.defName}: {ex}");
                    }

                    // The rest of the code remains the same
                    if (job3 != null)
                    {
                        if (Prefs.DevMode && modExtension.debugMode)
                        {
                            Log.Message($"[PawnControl] Final job created successfully: {job3.def.defName}");
                        }
                        job3.workGiverDef = scannerWhoProvidedTarget.def;
                        // Verify the job is valid
                        if (Prefs.DevMode && modExtension.debugMode)
                        {
                            if (!pawn.CanReserveAndReach(job3.targetA.Thing, job3.targetA.Thing == null ? PathEndMode.OnCell : PathEndMode.Touch, Danger.Some))
                            {
                                Log.Warning($"[PawnControl] Job target is unreachable: {(job3.targetA.Thing?.LabelCap ?? "null")}");
                            }

                            if (job3.targetA.Thing != null && job3.targetA.Thing.IsForbidden(pawn))
                            {
                                Log.Warning($"[PawnControl] Job target is forbidden: {job3.targetA.Thing.LabelCap}");
                            }
                        }

                        if (pawn.jobs.debugLog)
                        {
                            pawn.jobs.DebugLogEvent($"JobGiver_Work produced post-loop scan Job {job3.ToStringSafe()} from {scannerWhoProvidedTarget}");
                        }

                        return new ThinkResult(job3, this, scannerWhoProvidedTarget.def.tagToGive);
                    }
                    else if (Prefs.DevMode && modExtension.debugMode)
                    {
                        Log.Warning($"[PawnControl] Final job creation returned null");
                    }

                    Log.ErrorOnce(string.Concat(scannerWhoProvidedTarget, " provided target ", bestTargetOfLastPriority, " but yielded no actual job for pawn ", pawn, ". The CanGiveJob and JobOnX methods may not be synchronized."), 6112651);
                }

            }
            catch (Exception ex)
            {
                Log.Error($"[PawnControl] Critical error in JobGiver_WorkNonHumanlike for {pawn?.LabelShort ?? "unknown"}: {ex}");
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

        /// <summary>
        /// Clears the job cache. Call this when saving/loading or when jobs might become invalid.
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager._jobCache.Clear();
        }
    }
}