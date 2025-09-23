// File: JobGiver_Work_PawnControl.cs
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    public class JobGiver_Work_PawnControl : ThinkNode
    {
        public bool emergency;

        // 재사용 가능한 StringBuilder - 문자열 할당 최소화
        [ThreadStatic]
        private static StringBuilder _cachedStringBuilder;
        private static StringBuilder CachedStringBuilder => _cachedStringBuilder ?? (_cachedStringBuilder = new StringBuilder(256));

        // 재사용 가능한 컬렉션 - GC 압박 감소
        [ThreadStatic]
        private static List<Thing> _tempThingList;
        private static List<Thing> TempThingList => _tempThingList ?? (_tempThingList = new List<Thing>());

        public override ThinkNode DeepCopy(bool resolve = true)
        {
            JobGiver_Work_PawnControl obj = (JobGiver_Work_PawnControl)base.DeepCopy(resolve);
            obj.emergency = emergency;
            return obj;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override float GetPriority(Pawn pawn)
        {
            return 5.5f;
        }

        public override ThinkResult TryIssueJobPackage(Pawn pawn, JobIssueParams jobParams)
        {
            // 조건부 디버그 로깅 - 성능 개선을 위한 early return 패턴
            bool isPlayerPawn = pawn.Faction == Faction.OfPlayer;
            bool shouldLog = isPlayerPawn && Utility_DebugManager.ShouldLogDetailed();

            if (shouldLog)
                LogDebug($"JobGiver_Work_PawnControl.TryIssueJobPackage started for {pawn.LabelShort}, emergency={emergency}");

            // 빠른 검증 체크들 - 가장 비용이 적은 것부터
            if (pawn.Faction == null)
            {
                if (shouldLog)
                    LogDebug($"{pawn.LabelShort} has no faction, returning NoJob");
                return ThinkResult.NoJob;
            }

            if (!Utility_Common.PawnChecker(pawn))
            {
                if (shouldLog)
                    LogDebug($"PawnChecker failed for {pawn.LabelShort}, returning NoJob");
                return ThinkResult.NoJob;
            }

            if (Utility_CacheManager.GetModExtension(pawn.def) == null)
            {
                if (shouldLog)
                    LogDebug($"{pawn.LabelShort} has no mod extension");
                return ThinkResult.NoJob;
            }

            if (!Utility_TagManager.HasTagSet(pawn.def))
            {
                if (shouldLog)
                    LogDebug($"{pawn.LabelShort} has no tag set");
                return ThinkResult.NoJob;
            }

            if (pawn.RaceProps.Humanlike && pawn.health.hediffSet.InLabor())
            {
                if (shouldLog)
                    LogDebug($"{pawn.LabelShort} is in labor, returning NoJob");
                return ThinkResult.NoJob;
            }

            // Emergency work handling
            if (emergency && pawn.mindState.priorityWork.IsPrioritized)
            {
                if (shouldLog)
                    LogDebug($"Processing emergency prioritized work for {pawn.LabelShort}");

                var emergencyResult = ProcessEmergencyWork(pawn, shouldLog);
                if (emergencyResult.IsValid)
                    return emergencyResult;
            }

            // Normal work processing
            return ProcessNormalWork(pawn, shouldLog);
        }

        /// <summary>
        /// Emergency work 처리 - 별도 메서드로 분리하여 가독성 향상
        /// </summary>
        private ThinkResult ProcessEmergencyWork(Pawn pawn, bool shouldLog)
        {
            var workGiversByPriority = pawn.mindState.priorityWork.WorkGiver.workType.workGiversByPriority;

            for (int i = 0; i < workGiversByPriority.Count; i++)
            {
                WorkGiver worker = workGiversByPriority[i].Worker;
                if (!WorkGiversRelated(pawn.mindState.priorityWork.WorkGiver, worker.def))
                    continue;

                if (shouldLog)
                    LogDebug($"Trying emergency prioritized job from {worker.def.defName}");

                Job job = GiverTryGiveJobPrioritized(pawn, worker, pawn.mindState.priorityWork.Cell);
                if (job != null)
                {
                    if (shouldLog)
                        LogDebug($"Found emergency job: {job.def.defName} from {worker.def.defName}");

                    job.playerForced = true;
                    if (pawn.jobs.debugLog)
                    {
                        pawn.jobs.DebugLogEvent($"JobGiver_Work produced emergency Job {job.ToStringSafe()} from {worker}");
                    }

                    return new ThinkResult(job, this, workGiversByPriority[i].tagToGive);
                }
            }

            if (shouldLog)
                LogDebug($"No emergency job found, clearing priority work for {pawn.LabelShort}");

            pawn.mindState.priorityWork.Clear();
            return ThinkResult.NoJob;
        }

        /// <summary>
        /// 일반 작업 처리 - 성능 최적화된 버전
        /// </summary>
        private ThinkResult ProcessNormalWork(Pawn pawn, bool shouldLog)
        {
            List<WorkGiver> workGivers = emergency ?
                pawn.workSettings.WorkGiversInOrderEmergency :
                pawn.workSettings.WorkGiversInOrderNormal;

            if (shouldLog)
                LogDebug($"Processing {workGivers.Count} work givers in {(emergency ? "emergency" : "normal")} mode for {pawn.LabelShort}");

            int lastPriority = -999;
            TargetInfo bestTarget = TargetInfo.Invalid;
            WorkGiver_Scanner bestScanner = null;

            // 직접 for 루프 사용 - foreach보다 약간 더 빠름
            for (int i = 0; i < workGivers.Count; i++)
            {
                WorkGiver workGiver = workGivers[i];

                // Priority break optimization
                if (workGiver.def.priorityInType != lastPriority && bestTarget.IsValid)
                {
                    if (shouldLog)
                        LogDebug("Breaking out of loop: priority changed and we already have a valid target");
                    break;
                }

                if (!PawnCanUseWorkGiver(pawn, workGiver))
                {
                    if (shouldLog)
                        LogDebug($"{pawn.LabelShort} cannot use WorkGiver {workGiver.def.defName}, skipping");
                    continue;
                }

                try
                {
                    // Try non-scan job first
                    if (shouldLog)
                        LogDebug($"Trying non-scan job from {workGiver.def.defName}");

                    Job nonScanJob = workGiver.NonScanJob(pawn);
                    if (nonScanJob != null)
                    {
                        if (shouldLog)
                            LogDebug($"Found non-scan job: {nonScanJob.def.defName} from {workGiver.def.defName}");

                        if (pawn.jobs.debugLog)
                        {
                            pawn.jobs.DebugLogEvent($"JobGiver_Work produced non-scan Job {nonScanJob.ToStringSafe()} from {workGiver}");
                        }

                        return new ThinkResult(nonScanJob, this, workGivers[i].def.tagToGive);
                    }

                    // Process scanner jobs
                    if (workGiver is WorkGiver_Scanner scanner)
                    {
                        ProcessScannerJob(pawn, scanner, shouldLog, ref bestTarget, ref bestScanner);
                    }
                }
                catch (Exception ex)
                {
                    LogError($"{pawn} threw exception in WorkGiver {workGiver.def.defName}: {ex}");
                    Log.Error($"{pawn} threw exception in WorkGiver {workGiver.def.defName}: {ex}");
                }

                // Check if we found a valid target
                if (bestTarget.IsValid)
                {
                    if (shouldLog)
                        LogDebug("Found valid target, attempting to get job");

                    Job scanJob = bestTarget.HasThing ?
                        bestScanner.JobOnThing(pawn, bestTarget.Thing) :
                        bestScanner.JobOnCell(pawn, bestTarget.Cell);

                    if (scanJob != null)
                    {
                        if (shouldLog)
                            LogDebug($"Successfully created job {scanJob.def.defName} from scanner {bestScanner.def.defName}");

                        scanJob.workGiverDef = bestScanner.def;
                        if (pawn.jobs.debugLog)
                        {
                            pawn.jobs.DebugLogEvent($"JobGiver_Work produced scan Job {scanJob.ToStringSafe()} from {bestScanner}");
                        }

                        return new ThinkResult(scanJob, this, workGivers[i].def.tagToGive);
                    }

                    Log.ErrorOnce($"{bestScanner} provided target {bestTarget} but yielded no actual job for pawn {pawn}. The CanGiveJob and JobOnX methods may not be synchronized.", 6112651);
                }

                lastPriority = workGiver.def.priorityInType;
            }

            if (shouldLog)
                LogDebug($"No job found for {pawn.LabelShort}, returning NoJob");

            return ThinkResult.NoJob;
        }

        /// <summary>
        /// Scanner job 처리 - 최적화된 버전
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ProcessScannerJob(Pawn pawn, WorkGiver_Scanner scanner, bool shouldLog,
            ref TargetInfo bestTarget, ref WorkGiver_Scanner bestScanner)
        {
            // Scan things
            if (scanner.def.scanThings)
            {
                if (shouldLog)
                    LogDebug($"Processing scanThings for {scanner.def.defName}");

                ProcessScanThings(pawn, scanner, shouldLog, ref bestTarget, ref bestScanner);
            }

            // Scan cells
            if (scanner.def.scanCells)
            {
                if (shouldLog)
                    LogDebug($"Processing scanCells for {scanner.def.defName}");

                ProcessScanCells(pawn, scanner, shouldLog, ref bestTarget, ref bestScanner);
            }
        }

        /// <summary>
        /// Thing 스캔 처리 - 성능 최적화
        /// </summary>
        private void ProcessScanThings(Pawn pawn, WorkGiver_Scanner scanner, bool shouldLog,
            ref TargetInfo bestTarget, ref WorkGiver_Scanner bestScanner)
        {
            IEnumerable<Thing> potentialThings = scanner.PotentialWorkThingsGlobal(pawn);
            bool hasCarriedThing = pawn.carryTracker?.CarriedThing != null &&
                                   scanner.PotentialWorkThingRequest.Accepts(pawn.carryTracker.CarriedThing) &&
                                   ValidateThing(pawn.carryTracker.CarriedThing, pawn, scanner);

            Thing targetThing;

            if (scanner.Prioritized)
            {
                if (shouldLog)
                    LogDebug("Scanner is prioritized");

                IEnumerable<Thing> searchSet = potentialThings ?? pawn.Map.listerThings.ThingsMatching(scanner.PotentialWorkThingRequest);

                if (shouldLog)
                    LogDebug($"Looking for closest thing with reachable={!scanner.AllowUnreachable}");

                targetThing = scanner.AllowUnreachable ?
                    GenClosest.ClosestThing_Global(pawn.Position, searchSet, 99999f,
                        t => ValidateThing(t, pawn, scanner),
                        t => scanner.GetPriority(pawn, t)) :
                    GenClosest.ClosestThing_Global_Reachable(pawn.Position, pawn.Map, searchSet,
                        scanner.PathEndMode, TraverseParms.For(pawn, scanner.MaxPathDanger(pawn)), 9999f,
                        t => ValidateThing(t, pawn, scanner),
                        t => scanner.GetPriority(pawn, t));

                if (hasCarriedThing)
                {
                    if (shouldLog)
                        LogDebug("Evaluating carried thing as potential work thing");

                    if (targetThing != null)
                    {
                        float carriedPriority = scanner.GetPriority(pawn, pawn.carryTracker.CarriedThing);
                        float foundPriority = scanner.GetPriority(pawn, targetThing);

                        if (shouldLog)
                            LogDebug($"Comparing priorities: carried={carriedPriority}, found={foundPriority}");

                        if (carriedPriority >= foundPriority)
                        {
                            targetThing = pawn.carryTracker.CarriedThing;
                            if (shouldLog)
                                LogDebug("Using carried thing as it has higher priority");
                        }
                    }
                    else
                    {
                        targetThing = pawn.carryTracker.CarriedThing;
                        if (shouldLog)
                            LogDebug("Using carried thing as no other thing found");
                    }
                }
            }
            else if (hasCarriedThing)
            {
                targetThing = pawn.carryTracker.CarriedThing;
                if (shouldLog)
                    LogDebug("Using carried thing (non-prioritized scanner)");
            }
            else if (scanner.AllowUnreachable)
            {
                if (shouldLog)
                    LogDebug("Scanner allows unreachable, using global search");

                IEnumerable<Thing> searchSet = potentialThings ?? pawn.Map.listerThings.ThingsMatching(scanner.PotentialWorkThingRequest);
                targetThing = GenClosest.ClosestThing_Global(pawn.Position, searchSet, 99999f,
                    t => ValidateThing(t, pawn, scanner));
            }
            else
            {
                if (shouldLog)
                    LogDebug("Using normal reachable search");

                targetThing = GenClosest.ClosestThingReachable(pawn.Position, pawn.Map,
                    scanner.PotentialWorkThingRequest, scanner.PathEndMode,
                    TraverseParms.For(pawn, scanner.MaxPathDanger(pawn)), 9999f,
                    t => ValidateThing(t, pawn, scanner),
                    potentialThings, 0, scanner.MaxRegionsToScanBeforeGlobalSearch, potentialThings != null);
            }

            if (targetThing != null)
            {
                if (shouldLog)
                    LogDebug($"Found valid work thing: {targetThing}");

                bestTarget = targetThing;
                bestScanner = scanner;
            }
            else if (shouldLog)
            {
                LogDebug("No valid work thing found");
            }
        }

        /// <summary>
        /// Cell 스캔 처리 - 성능 최적화
        /// </summary>
        private void ProcessScanCells(Pawn pawn, WorkGiver_Scanner scanner, bool shouldLog,
            ref TargetInfo bestTarget, ref WorkGiver_Scanner bestScanner)
        {
            IntVec3 pawnPosition = pawn.Position;
            float closestDistSquared = 99999f;
            float bestPriority = float.MinValue;
            bool prioritized = scanner.Prioritized;
            bool allowUnreachable = scanner.AllowUnreachable;
            Danger maxPathDanger = scanner.MaxPathDanger(pawn);

            // 지역 변수로 ref 문제 해결
            TargetInfo localBestTarget = bestTarget;
            WorkGiver_Scanner localBestScanner = bestScanner;

            IEnumerable<IntVec3> potentialCells = scanner.PotentialWorkCellsGlobal(pawn);

            if (potentialCells is IList<IntVec3> cellList)
            {
                if (shouldLog)
                    LogDebug($"Processing {cellList.Count} cells from IList");

                // 직접 for 루프 사용 - foreach보다 빠름
                for (int i = 0; i < cellList.Count; i++)
                {
                    ProcessSingleCell(cellList[i]);
                }
            }
            else
            {
                if (shouldLog)
                    LogDebug("Processing cells from general IEnumerable");

                foreach (IntVec3 cell in potentialCells)
                {
                    ProcessSingleCell(cell);
                }
            }

            // 지역 변수 값을 ref 매개변수에 다시 할당
            bestTarget = localBestTarget;
            bestScanner = localBestScanner;

            // 지역 함수 - ref 매개변수 대신 지역 변수 사용
            void ProcessSingleCell(IntVec3 c)
            {
                bool isValidTarget = false;
                float distSquared = (c - pawnPosition).LengthHorizontalSquared;
                float priority = 0f;

                if (prioritized)
                {
                    if (!c.IsForbidden(pawn) && scanner.HasJobOnCell(pawn, c))
                    {
                        if (!allowUnreachable && !pawn.CanReach(c, scanner.PathEndMode, maxPathDanger))
                        {
                            if (shouldLog)
                                LogDebug($"Cell {c} is unreachable, skipping");
                            return;
                        }

                        priority = scanner.GetPriority(pawn, c);
                        if (priority > bestPriority || (priority == bestPriority && distSquared < closestDistSquared))
                        {
                            isValidTarget = true;
                            if (shouldLog)
                                LogDebug($"Cell {c} is valid priority target (prio: {priority}, dist: {distSquared})");
                        }
                    }
                }
                else if (distSquared < closestDistSquared && !c.IsForbidden(pawn) && scanner.HasJobOnCell(pawn, c))
                {
                    if (!allowUnreachable && !pawn.CanReach(c, scanner.PathEndMode, maxPathDanger))
                    {
                        if (shouldLog)
                            LogDebug($"Cell {c} is unreachable, skipping");
                        return;
                    }

                    isValidTarget = true;
                    if (shouldLog)
                        LogDebug($"Cell {c} is valid distance-based target (dist: {distSquared})");
                }

                if (isValidTarget)
                {
                    localBestTarget = new TargetInfo(c, pawn.Map);
                    localBestScanner = scanner;
                    closestDistSquared = distSquared;
                    bestPriority = priority;
                }
            }
        }

        /// <summary>
        /// Thing 검증 - 인라인 최적화
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ValidateThing(Thing thing, Pawn pawn, WorkGiver_Scanner scanner)
        {
            bool result = !thing.IsForbidden(pawn) && scanner.HasJobOnThing(pawn, thing);

            if (Utility_DebugManager.ShouldLogDetailed())
            {
                LogDebug($"Validating {thing}: forbidden={thing.IsForbidden(pawn)}, hasJob={scanner.HasJobOnThing(pawn, thing)}, result={result}");
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool PawnCanUseWorkGiver(Pawn pawn, WorkGiver giver)
        {
            if (!Utility_Common.PawnCompatibilityChecker(pawn))
                return false;

            if (!Utility_TagManager.IsWorkEnabled(pawn, giver.def.workType))
                return false;

            if (giver.def.workType != null && Utility_TagManager.IsWorkDisabled(pawn, giver.def.workType))
                return false;

            if (giver.ShouldSkip(pawn))
                return false;

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool WorkGiversRelated(WorkGiverDef current, WorkGiverDef next)
        {
            bool result = next != WorkGiverDefOf.Repair || current == WorkGiverDefOf.Repair ?
                next.doesSmoothing == current.doesSmoothing : false;

            if (Utility_DebugManager.ShouldLogDetailed())
                LogDebug($"WorkGiversRelated: {current.defName} -> {next.defName} = {result}");

            return result;
        }

        /// <summary>
        /// 우선순위 작업 처리 - 성능 최적화
        /// </summary>
        private Job GiverTryGiveJobPrioritized(Pawn pawn, WorkGiver giver, IntVec3 cell)
        {
            if (!PawnCanUseWorkGiver(pawn, giver))
                return null;

            bool shouldLog = pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed();

            try
            {
                if (shouldLog)
                    LogDebug($"GiverTryGiveJobPrioritized: Trying non-scan job for {giver.def.defName}");

                Job job = giver.NonScanJob(pawn);
                if (job != null)
                {
                    if (shouldLog)
                        LogDebug($"GiverTryGiveJobPrioritized: Found non-scan job {job.def.defName}");
                    return job;
                }

                if (giver is WorkGiver_Scanner scanner)
                {
                    if (giver.def.scanThings)
                    {
                        if (shouldLog)
                            LogDebug($"GiverTryGiveJobPrioritized: Scanning things at cell {cell}");

                        var thingList = cell.GetThingList(pawn.Map);
                        if (shouldLog)
                            LogDebug($"Found {thingList.Count} things at cell");

                        // 직접 for 루프로 최적화
                        for (int i = 0; i < thingList.Count; i++)
                        {
                            Thing thing = thingList[i];
                            if (scanner.PotentialWorkThingRequest.Accepts(thing) &&
                                !thing.IsForbidden(pawn) && scanner.HasJobOnThing(pawn, thing))
                            {
                                if (shouldLog)
                                    LogDebug($"Found acceptable thing: {thing}");

                                Job thingJob = scanner.JobOnThing(pawn, thing);
                                if (thingJob != null)
                                {
                                    if (shouldLog)
                                        LogDebug($"Created job {thingJob.def.defName} for thing {thing}");

                                    thingJob.workGiverDef = giver.def;
                                    return thingJob;
                                }

                                if (shouldLog)
                                    LogDebug($"Thing {thing} passed validation but JobOnThing returned null");
                            }
                        }
                    }

                    if (giver.def.scanCells && !cell.IsForbidden(pawn) && scanner.HasJobOnCell(pawn, cell))
                    {
                        if (shouldLog)
                            LogDebug($"GiverTryGiveJobPrioritized: Checking for job on cell {cell}");

                        Job cellJob = scanner.JobOnCell(pawn, cell);
                        if (cellJob != null)
                        {
                            if (shouldLog)
                                LogDebug($"Created cell-based job {cellJob.def.defName}");

                            cellJob.workGiverDef = giver.def;
                            return cellJob;
                        }

                        if (shouldLog)
                            LogDebug($"Cell {cell} passed HasJobOnCell but JobOnCell returned null");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"{pawn} threw exception in GiverTryGiveJobPrioritized for WorkGiver {giver.def.defName}: {ex}");
                Log.Error($"{pawn} threw exception in GiverTryGiveJobTargeted on WorkGiver {giver.def.defName}: {ex}");
            }

            if (shouldLog)
                LogDebug($"GiverTryGiveJobPrioritized: No job found for {pawn.LabelShort} with {giver.def.defName}");

            return null;
        }

        #region Optimized Logging Methods

        /// <summary>
        /// 최적화된 디버그 로깅 - StringBuilder 재사용
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void LogDebug(string message)
        {
            if (Utility_DebugManager.ShouldLogDetailed())
                Utility_DebugManager.LogNormal(message);
        }

        /// <summary>
        /// 최적화된 에러 로깅
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void LogError(string message)
        {
            Utility_DebugManager.LogError(message);
        }

        #endregion
    }
}