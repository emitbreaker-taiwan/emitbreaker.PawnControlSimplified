using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Provides runtime profiling and adaptive optimization for JobGivers
    /// </summary>
    public static class Utility_AdaptiveProfilingManager
    {
        #region Profiling Data Structures

        // Performance metrics storage by work tag
        private static readonly Dictionary<string, WorkTagMetrics> _workTagMetrics = new Dictionary<string, WorkTagMetrics>();
        private static readonly Dictionary<int, Dictionary<string, PawnSpecificWorkTagMetrics>> _pawnWorkTagMetrics =
            new Dictionary<int, Dictionary<string, PawnSpecificWorkTagMetrics>>();

        // Legacy performance metrics storage by JobGiver type (for backward compatibility)
        private static readonly Dictionary<Type, JobGiverMetrics> _jobGiverMetrics = new Dictionary<Type, JobGiverMetrics>();
        private static readonly Dictionary<int, Dictionary<Type, PawnSpecificMetrics>> _pawnMetrics =
            new Dictionary<int, Dictionary<Type, PawnSpecificMetrics>>();

        // Metrics for debugging and visualization - work tag based
        private static readonly Dictionary<string, List<float>> _recentWorkTagExecutionTimes = new Dictionary<string, List<float>>();
        private static readonly Dictionary<string, List<bool>> _recentWorkTagSuccessRates = new Dictionary<string, List<bool>>();

        // Legacy metrics for debugging and visualization (for backward compatibility)
        private static readonly Dictionary<Type, List<float>> _recentExecutionTimes = new Dictionary<Type, List<float>>();
        private static readonly Dictionary<Type, List<bool>> _recentSuccessRates = new Dictionary<Type, List<bool>>();

        // Dynamic interval adjustments - work tag based
        private static readonly Dictionary<string, int> _dynamicWorkTagIntervals = new Dictionary<string, int>();
        private static readonly Dictionary<string, float> _workTagPerformanceScores = new Dictionary<string, float>();

        // Legacy dynamic interval adjustments (for backward compatibility)
        private static readonly Dictionary<Type, int> _dynamicIntervals = new Dictionary<Type, int>();
        private static readonly Dictionary<Type, float> _performanceScores = new Dictionary<Type, float>();

        // Profiling state
        private static bool _profilingEnabled = false;
        private static readonly Stopwatch _profiler = new Stopwatch();

        // Collections for auto-optimization
        private static readonly HashSet<string> _workTagsToOptimize = new HashSet<string>();
        private static readonly HashSet<Type> _jobGiversToOptimize = new HashSet<Type>();

        // Constants
        private const int METRICS_HISTORY_SIZE = 100;  // Keep this many recent samples
        private const int MIN_SAMPLES_FOR_ADJUSTMENT = 20;  // Need this many samples before adjusting
        private const float EXPENSIVE_THRESHOLD_MS = 5.0f;  // JobGivers taking more than 5ms are "expensive"
        private const float VERY_EXPENSIVE_THRESHOLD_MS = 20.0f;  // JobGivers taking more than 20ms are "very expensive"

        // Auto-adjustment constants
        private const float PERFORMANCE_WEIGHT = 0.7f;         // How much to weight execution time vs. success
        private const float SUCCESS_WEIGHT = 0.3f;             // How much to weight success vs. execution time
        private const float SLOWING_THRESHOLD_SCORE = 0.8f;    // Score above which we slow down updates
        private const float SPEEDING_THRESHOLD_SCORE = 0.3f;   // Score below which we speed up updates
        private const float MAX_INTERVAL_MULTIPLIER = 4.0f;    // Maximum slowdown factor 
        private const float MIN_INTERVAL_MULTIPLIER = 0.5f;    // Maximum speedup factor

        #endregion

        #region Metric Classes

        /// <summary>
        /// Metrics for a specific work tag
        /// </summary>
        private class WorkTagMetrics
        {
            public int TotalExecutions { get; private set; }
            public int SuccessfulExecutions { get; private set; }
            public float TotalExecutionTime { get; private set; }
            public float MaxExecutionTime { get; private set; }
            public float AverageExecutionTime => TotalExecutions > 0 ? TotalExecutionTime / TotalExecutions : 0f;
            public float SuccessRate => TotalExecutions > 0 ? (float)SuccessfulExecutions / TotalExecutions : 0f;
            public float LastExecutionTime { get; private set; }
            public int LastExecutionTick { get; private set; }
            public bool IsExpensive => AverageExecutionTime > EXPENSIVE_THRESHOLD_MS;
            public bool IsVeryExpensive => AverageExecutionTime > VERY_EXPENSIVE_THRESHOLD_MS;

            public WorkTagMetrics()
            {
                TotalExecutions = 0;
                SuccessfulExecutions = 0;
                TotalExecutionTime = 0f;
                MaxExecutionTime = 0f;
                LastExecutionTime = 0f;
                LastExecutionTick = 0;
            }

            public void RecordExecution(float executionTimeMs, bool successful, int tick)
            {
                TotalExecutions++;
                TotalExecutionTime += executionTimeMs;
                LastExecutionTime = executionTimeMs;
                LastExecutionTick = tick;

                if (successful)
                    SuccessfulExecutions++;

                if (executionTimeMs > MaxExecutionTime)
                    MaxExecutionTime = executionTimeMs;
            }

            public override string ToString()
            {
                return $"Executions: {TotalExecutions}, Success: {SuccessfulExecutions} ({SuccessRate * 100f:F1}%), " +
                       $"Avg: {AverageExecutionTime:F3}ms, Max: {MaxExecutionTime:F3}ms";
            }
        }

        /// <summary>
        /// Metrics for a specific pawn using a specific work tag
        /// </summary>
        private class PawnSpecificWorkTagMetrics
        {
            public int SuccessfulExecutions { get; private set; }
            public int TotalExecutions { get; private set; }
            public float TotalExecutionTime { get; private set; }
            public int LastSuccessTick { get; private set; }
            public Dictionary<string, int> JobTypeSuccesses { get; private set; }

            public float SuccessRate => TotalExecutions > 0 ? (float)SuccessfulExecutions / TotalExecutions : 0f;
            public float AverageExecutionTime => TotalExecutions > 0 ? TotalExecutionTime / TotalExecutions : 0f;

            public PawnSpecificWorkTagMetrics()
            {
                SuccessfulExecutions = 0;
                TotalExecutions = 0;
                TotalExecutionTime = 0f;
                LastSuccessTick = 0;
                JobTypeSuccesses = new Dictionary<string, int>();
            }

            public void RecordExecution(float executionTimeMs, bool successful, int tick, Job job = null)
            {
                TotalExecutions++;
                TotalExecutionTime += executionTimeMs;

                if (successful)
                {
                    SuccessfulExecutions++;
                    LastSuccessTick = tick;

                    // Track job type distribution
                    if (job != null)
                    {
                        string jobDefName = job.def?.defName ?? "Unknown";
                        if (!JobTypeSuccesses.TryGetValue(jobDefName, out int count))
                            count = 0;
                        JobTypeSuccesses[jobDefName] = count + 1;
                    }
                }
            }
        }

        /// <summary>
        /// Legacy metrics for backward compatibility - for a specific JobGiver type
        /// </summary>
        private class JobGiverMetrics
        {
            public int TotalExecutions { get; private set; }
            public int SuccessfulExecutions { get; private set; }
            public float TotalExecutionTime { get; private set; }
            public float MaxExecutionTime { get; private set; }
            public float AverageExecutionTime => TotalExecutions > 0 ? TotalExecutionTime / TotalExecutions : 0f;
            public float SuccessRate => TotalExecutions > 0 ? (float)SuccessfulExecutions / TotalExecutions : 0f;
            public float LastExecutionTime { get; private set; }
            public int LastExecutionTick { get; private set; }
            public bool IsExpensive => AverageExecutionTime > EXPENSIVE_THRESHOLD_MS;
            public bool IsVeryExpensive => AverageExecutionTime > VERY_EXPENSIVE_THRESHOLD_MS;

            public JobGiverMetrics()
            {
                TotalExecutions = 0;
                SuccessfulExecutions = 0;
                TotalExecutionTime = 0f;
                MaxExecutionTime = 0f;
                LastExecutionTime = 0f;
                LastExecutionTick = 0;
            }

            public void RecordExecution(float executionTimeMs, bool successful, int tick)
            {
                TotalExecutions++;
                TotalExecutionTime += executionTimeMs;
                LastExecutionTime = executionTimeMs;
                LastExecutionTick = tick;

                if (successful)
                    SuccessfulExecutions++;

                if (executionTimeMs > MaxExecutionTime)
                    MaxExecutionTime = executionTimeMs;
            }

            public override string ToString()
            {
                return $"Executions: {TotalExecutions}, Success: {SuccessfulExecutions} ({SuccessRate * 100f:F1}%), " +
                       $"Avg: {AverageExecutionTime:F3}ms, Max: {MaxExecutionTime:F3}ms";
            }
        }

        /// <summary>
        /// Legacy metrics for backward compatibility - for a specific pawn using a specific JobGiver
        /// </summary>
        private class PawnSpecificMetrics
        {
            public int SuccessfulExecutions { get; private set; }
            public int TotalExecutions { get; private set; }
            public float TotalExecutionTime { get; private set; }
            public int LastSuccessTick { get; private set; }
            public Dictionary<string, int> JobTypeSuccesses { get; private set; }

            public float SuccessRate => TotalExecutions > 0 ? (float)SuccessfulExecutions / TotalExecutions : 0f;
            public float AverageExecutionTime => TotalExecutions > 0 ? TotalExecutionTime / TotalExecutions : 0f;

            public PawnSpecificMetrics()
            {
                SuccessfulExecutions = 0;
                TotalExecutions = 0;
                TotalExecutionTime = 0f;
                LastSuccessTick = 0;
                JobTypeSuccesses = new Dictionary<string, int>();
            }

            public void RecordExecution(float executionTimeMs, bool successful, int tick, Job job = null)
            {
                TotalExecutions++;
                TotalExecutionTime += executionTimeMs;

                if (successful)
                {
                    SuccessfulExecutions++;
                    LastSuccessTick = tick;

                    // Track job type distribution
                    if (job != null)
                    {
                        string jobDefName = job.def?.defName ?? "Unknown";
                        if (!JobTypeSuccesses.TryGetValue(jobDefName, out int count))
                            count = 0;
                        JobTypeSuccesses[jobDefName] = count + 1;
                    }
                }
            }
        }

        #endregion

        #region Profiling Control Methods

        /// <summary>
        /// Enables profiling of JobGivers and work tags
        /// </summary>
        public static void EnableProfiling(bool enabled = true)
        {
            _profilingEnabled = enabled;

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"JobGiver profiling {(enabled ? "enabled" : "disabled")}");
            }
        }

        /// <summary>
        /// Returns whether profiling is currently enabled
        /// </summary>
        public static bool IsProfilingEnabled => _profilingEnabled;

        /// <summary>
        /// Registers a work tag for automatic optimization
        /// </summary>
        public static void RegisterWorkTagForAutoOptimization(string workTag)
        {
            if (string.IsNullOrEmpty(workTag))
                return;

            _workTagsToOptimize.Add(workTag);

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Registered work tag '{workTag}' for auto-optimization");
            }
        }

        /// <summary>
        /// Registers a JobGiver type for automatic optimization (legacy method)
        /// </summary>
        public static void RegisterForAutoOptimization(Type jobGiverType)
        {
            if (jobGiverType == null || !typeof(ThinkNode_JobGiver).IsAssignableFrom(jobGiverType))
                return;

            _jobGiversToOptimize.Add(jobGiverType);

            // Also register the corresponding work tag if available
            string workTag = Utility_JobGiverManager.GetWorkTagForJobGiverType(jobGiverType);
            if (!string.IsNullOrEmpty(workTag))
            {
                RegisterWorkTagForAutoOptimization(workTag);
            }

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Registered {jobGiverType.Name} for auto-optimization");
            }
        }

        /// <summary>
        /// Starts profiling a job execution
        /// </summary>
        public static void BeginProfiling()
        {
            if (!_profilingEnabled)
                return;

            _profiler.Restart();
        }

        /// <summary>
        /// Ends profiling for a work tag job execution and records results
        /// </summary>
        public static void EndProfiling(string workTag, Pawn pawn, Job result)
        {
            if (!_profilingEnabled || string.IsNullOrEmpty(workTag))
                return;

            _profiler.Stop();

            // Record execution time in milliseconds
            float executionTimeMs = (float)_profiler.ElapsedTicks / (float)TimeSpan.TicksPerMillisecond;
            bool wasSuccessful = result != null;
            int currentTick = Find.TickManager.TicksGame;

            // Record for this work tag
            RecordWorkTagMetrics(workTag, executionTimeMs, wasSuccessful, currentTick);

            // Record for this specific pawn
            if (pawn != null)
            {
                RecordPawnWorkTagMetrics(workTag, pawn, executionTimeMs, wasSuccessful, currentTick, result);
            }

            // Check if this work tag should have its update interval adjusted
            if (_workTagsToOptimize.Contains(workTag))
            {
                UpdateWorkTagPerformanceScore(workTag);
            }
        }

        /// <summary>
        /// Ends profiling for a JobGiver execution and records results (legacy method)
        /// </summary>
        public static void EndProfiling(Type jobGiverType, Pawn pawn, Job result)
        {
            if (!_profilingEnabled || jobGiverType == null)
                return;

            _profiler.Stop();

            // Record execution time in milliseconds
            float executionTimeMs = (float)_profiler.ElapsedTicks / (float)TimeSpan.TicksPerMillisecond;
            bool wasSuccessful = result != null;
            int currentTick = Find.TickManager.TicksGame;

            // Record for this JobGiver type (legacy)
            RecordJobGiverMetrics(jobGiverType, executionTimeMs, wasSuccessful, currentTick);

            // Also record by work tag if available
            string workTag = Utility_JobGiverManager.GetWorkTagForJobGiverType(jobGiverType);
            if (!string.IsNullOrEmpty(workTag))
            {
                RecordWorkTagMetrics(workTag, executionTimeMs, wasSuccessful, currentTick);
            }

            // Record for this specific pawn
            if (pawn != null)
            {
                RecordPawnMetrics(jobGiverType, pawn, executionTimeMs, wasSuccessful, currentTick, result);

                // Also record by work tag if available
                if (!string.IsNullOrEmpty(workTag))
                {
                    RecordPawnWorkTagMetrics(workTag, pawn, executionTimeMs, wasSuccessful, currentTick, result);
                }
            }

            // Check if this JobGiver should have its update interval adjusted
            if (_jobGiversToOptimize.Contains(jobGiverType))
            {
                UpdatePerformanceScore(jobGiverType);
            }

            // Also check work tag if available
            if (!string.IsNullOrEmpty(workTag) && _workTagsToOptimize.Contains(workTag))
            {
                UpdateWorkTagPerformanceScore(workTag);
            }
        }

        /// <summary>
        /// Records metrics for a work tag
        /// </summary>
        private static void RecordWorkTagMetrics(string workTag, float executionTimeMs,
            bool wasSuccessful, int currentTick)
        {
            if (string.IsNullOrEmpty(workTag))
                return;

            // Get or create metrics for this work tag
            if (!_workTagMetrics.TryGetValue(workTag, out var metrics))
            {
                metrics = new WorkTagMetrics();
                _workTagMetrics[workTag] = metrics;
            }

            // Record the execution
            metrics.RecordExecution(executionTimeMs, wasSuccessful, currentTick);

            // Store recent execution times for visualization
            if (!_recentWorkTagExecutionTimes.TryGetValue(workTag, out var times))
            {
                times = new List<float>(METRICS_HISTORY_SIZE);
                _recentWorkTagExecutionTimes[workTag] = times;
            }

            // Keep only a limited history
            if (times.Count >= METRICS_HISTORY_SIZE)
                times.RemoveAt(0);

            times.Add(executionTimeMs);

            // Store recent success rates for visualization
            if (!_recentWorkTagSuccessRates.TryGetValue(workTag, out var successes))
            {
                successes = new List<bool>(METRICS_HISTORY_SIZE);
                _recentWorkTagSuccessRates[workTag] = successes;
            }

            // Keep only a limited history
            if (successes.Count >= METRICS_HISTORY_SIZE)
                successes.RemoveAt(0);

            successes.Add(wasSuccessful);
        }

        /// <summary>
        /// Records metrics for a specific pawn using a work tag
        /// </summary>
        private static void RecordPawnWorkTagMetrics(string workTag, Pawn pawn, float executionTimeMs,
            bool wasSuccessful, int currentTick, Job job)
        {
            if (string.IsNullOrEmpty(workTag) || pawn == null)
                return;

            int pawnId = pawn.thingIDNumber;

            // Get or create dictionary for this pawn
            if (!_pawnWorkTagMetrics.TryGetValue(pawnId, out var pawnWorkTags))
            {
                pawnWorkTags = new Dictionary<string, PawnSpecificWorkTagMetrics>();
                _pawnWorkTagMetrics[pawnId] = pawnWorkTags;
            }

            // Get or create metrics for this work tag
            if (!pawnWorkTags.TryGetValue(workTag, out var metrics))
            {
                metrics = new PawnSpecificWorkTagMetrics();
                pawnWorkTags[workTag] = metrics;
            }

            // Record the execution
            metrics.RecordExecution(executionTimeMs, wasSuccessful, currentTick, job);
        }

        /// <summary>
        /// Records metrics for a JobGiver type (legacy method)
        /// </summary>
        private static void RecordJobGiverMetrics(Type jobGiverType, float executionTimeMs,
            bool wasSuccessful, int currentTick)
        {
            if (jobGiverType == null)
                return;

            // Get or create metrics for this JobGiver type
            if (!_jobGiverMetrics.TryGetValue(jobGiverType, out var metrics))
            {
                metrics = new JobGiverMetrics();
                _jobGiverMetrics[jobGiverType] = metrics;
            }

            // Record the execution
            metrics.RecordExecution(executionTimeMs, wasSuccessful, currentTick);

            // Store recent execution times for visualization
            if (!_recentExecutionTimes.TryGetValue(jobGiverType, out var times))
            {
                times = new List<float>(METRICS_HISTORY_SIZE);
                _recentExecutionTimes[jobGiverType] = times;
            }

            // Keep only a limited history
            if (times.Count >= METRICS_HISTORY_SIZE)
                times.RemoveAt(0);

            times.Add(executionTimeMs);

            // Store recent success rates for visualization
            if (!_recentSuccessRates.TryGetValue(jobGiverType, out var successes))
            {
                successes = new List<bool>(METRICS_HISTORY_SIZE);
                _recentSuccessRates[jobGiverType] = successes;
            }

            // Keep only a limited history
            if (successes.Count >= METRICS_HISTORY_SIZE)
                successes.RemoveAt(0);

            successes.Add(wasSuccessful);
        }

        /// <summary>
        /// Records metrics for a specific pawn using a JobGiver (legacy method)
        /// </summary>
        private static void RecordPawnMetrics(Type jobGiverType, Pawn pawn, float executionTimeMs,
            bool wasSuccessful, int currentTick, Job job)
        {
            if (jobGiverType == null || pawn == null)
                return;

            int pawnId = pawn.thingIDNumber;

            // Get or create dictionary for this pawn
            if (!_pawnMetrics.TryGetValue(pawnId, out var pawnJobGivers))
            {
                pawnJobGivers = new Dictionary<Type, PawnSpecificMetrics>();
                _pawnMetrics[pawnId] = pawnJobGivers;
            }

            // Get or create metrics for this JobGiver type
            if (!pawnJobGivers.TryGetValue(jobGiverType, out var metrics))
            {
                metrics = new PawnSpecificMetrics();
                pawnJobGivers[jobGiverType] = metrics;
            }

            // Record the execution
            metrics.RecordExecution(executionTimeMs, wasSuccessful, currentTick, job);
        }

        /// <summary>
        /// Records execution metrics for a work tag
        /// </summary>
        /// <param name="workTag">The work tag being executed</param>
        /// <param name="executionTimeMs">Execution time in milliseconds</param>
        /// <param name="successful">Whether the job creation was successful</param>
        public static void RecordWorkTagExecution(string workTag, float executionTimeMs, bool successful)
        {
            if (!_profilingEnabled || string.IsNullOrEmpty(workTag))
                return;

            int currentTick = Find.TickManager.TicksGame;

            // Get or create metrics for this work tag
            if (!_workTagMetrics.TryGetValue(workTag, out var metrics))
            {
                metrics = new WorkTagMetrics();
                _workTagMetrics[workTag] = metrics;
            }

            // Record the execution
            metrics.RecordExecution(executionTimeMs, successful, currentTick);

            // Store recent execution times for visualization
            if (!_recentWorkTagExecutionTimes.TryGetValue(workTag, out var times))
            {
                times = new List<float>(METRICS_HISTORY_SIZE);
                _recentWorkTagExecutionTimes[workTag] = times;
            }

            // Keep only a limited history
            if (times.Count >= METRICS_HISTORY_SIZE)
                times.RemoveAt(0);

            times.Add(executionTimeMs);

            // Store recent success rates for visualization
            if (!_recentWorkTagSuccessRates.TryGetValue(workTag, out var successes))
            {
                successes = new List<bool>(METRICS_HISTORY_SIZE);
                _recentWorkTagSuccessRates[workTag] = successes;
            }

            // Keep only a limited history
            if (successes.Count >= METRICS_HISTORY_SIZE)
                successes.RemoveAt(0);

            successes.Add(successful);

            // Check if this work tag should have its update interval adjusted
            if (_workTagsToOptimize.Contains(workTag))
            {
                UpdateWorkTagPerformanceScore(workTag);
            }
        }

        #endregion

        #region Adaptive Optimization

        /// <summary>
        /// Calculates and updates the performance score for a work tag
        /// </summary>
        private static void UpdateWorkTagPerformanceScore(string workTag)
        {
            if (string.IsNullOrEmpty(workTag))
                return;

            // Need enough samples before making adjustments
            if (!_recentWorkTagExecutionTimes.TryGetValue(workTag, out var times) ||
                !_recentWorkTagSuccessRates.TryGetValue(workTag, out var successes) ||
                times.Count < MIN_SAMPLES_FOR_ADJUSTMENT)
                return;

            // Calculate performance metrics
            float avgExecutionTime = times.Average();
            float successRate = successes.Count(s => s) / (float)successes.Count;

            // Calculate performance score (0-1 where lower is better)
            // - Low execution time + high success rate = good performance (low score)
            // - High execution time + low success rate = poor performance (high score)
            float normalizedTime = Math.Min(avgExecutionTime / VERY_EXPENSIVE_THRESHOLD_MS, 1f);
            float normalizedSuccess = 1f - successRate;

            // Weight time and success 
            float performanceScore = (normalizedTime * PERFORMANCE_WEIGHT) + (normalizedSuccess * SUCCESS_WEIGHT);

            // Update the score
            _workTagPerformanceScores[workTag] = performanceScore;

            // Adjust interval based on score
            AdjustWorkTagIntervalBasedOnPerformance(workTag, performanceScore);
        }

        /// <summary>
        /// Adjusts the update interval based on performance score for a work tag
        /// </summary>
        private static void AdjustWorkTagIntervalBasedOnPerformance(string workTag, float performanceScore)
        {
            if (string.IsNullOrEmpty(workTag))
                return;

            // Get the base interval from the work tag's settings
            int baseInterval = Utility_JobGiverTickManager.GetWorkTagBaseInterval(workTag);
            if (baseInterval <= 0)
                return;

            float multiplier = 1.0f;

            // Poor performance - slow down updates
            if (performanceScore > SLOWING_THRESHOLD_SCORE)
            {
                // Map 0.8-1.0 to 1.0-MAX_INTERVAL_MULTIPLIER
                float factor = (performanceScore - SLOWING_THRESHOLD_SCORE) / (1f - SLOWING_THRESHOLD_SCORE);
                multiplier = 1.0f + (factor * (MAX_INTERVAL_MULTIPLIER - 1.0f));
            }
            // Good performance - speed up updates
            else if (performanceScore < SPEEDING_THRESHOLD_SCORE)
            {
                // Map 0.0-0.3 to MIN_INTERVAL_MULTIPLIER-1.0
                float factor = (SPEEDING_THRESHOLD_SCORE - performanceScore) / SPEEDING_THRESHOLD_SCORE;
                multiplier = 1.0f - (factor * (1.0f - MIN_INTERVAL_MULTIPLIER));
            }

            // Apply the multiplier
            int adjustedInterval = Mathf.RoundToInt(baseInterval * multiplier);

            // Store and apply the adjusted interval
            _dynamicWorkTagIntervals[workTag] = adjustedInterval;
            Utility_JobGiverTickManager.SetWorkTagDynamicInterval(workTag, adjustedInterval);

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal(
                    $"Adjusted interval for work tag '{workTag}': {baseInterval} -> {adjustedInterval} " +
                    $"(score: {performanceScore:F2}, multiplier: {multiplier:F2})");
            }
        }

        /// <summary>
        /// Legacy method - Calculates and updates the performance score for a JobGiver
        /// </summary>
        private static void UpdatePerformanceScore(Type jobGiverType)
        {
            if (jobGiverType == null)
                return;

            // Need enough samples before making adjustments
            if (!_recentExecutionTimes.TryGetValue(jobGiverType, out var times) ||
                !_recentSuccessRates.TryGetValue(jobGiverType, out var successes) ||
                times.Count < MIN_SAMPLES_FOR_ADJUSTMENT)
                return;

            // Calculate performance metrics
            float avgExecutionTime = times.Average();
            float successRate = successes.Count(s => s) / (float)successes.Count;

            // Calculate performance score (0-1 where lower is better)
            // - Low execution time + high success rate = good performance (low score)
            // - High execution time + low success rate = poor performance (high score)
            float normalizedTime = Math.Min(avgExecutionTime / VERY_EXPENSIVE_THRESHOLD_MS, 1f);
            float normalizedSuccess = 1f - successRate;

            // Weight time and success 
            float performanceScore = (normalizedTime * PERFORMANCE_WEIGHT) + (normalizedSuccess * SUCCESS_WEIGHT);

            // Update the score
            _performanceScores[jobGiverType] = performanceScore;

            // Adjust interval based on score
            AdjustIntervalBasedOnPerformance(jobGiverType, performanceScore);
        }

        /// <summary>
        /// Legacy method - Adjusts the update interval based on performance score
        /// </summary>
        private static void AdjustIntervalBasedOnPerformance(Type jobGiverType, float performanceScore)
        {
            if (jobGiverType == null)
                return;

            // Get the base interval from the JobGiver's settings
            int baseInterval = Utility_JobGiverTickManager.GetBaseInterval(jobGiverType);
            if (baseInterval <= 0)
                return;

            float multiplier = 1.0f;

            // Poor performance - slow down updates
            if (performanceScore > SLOWING_THRESHOLD_SCORE)
            {
                // Map 0.8-1.0 to 1.0-MAX_INTERVAL_MULTIPLIER
                float factor = (performanceScore - SLOWING_THRESHOLD_SCORE) / (1f - SLOWING_THRESHOLD_SCORE);
                multiplier = 1.0f + (factor * (MAX_INTERVAL_MULTIPLIER - 1.0f));
            }
            // Good performance - speed up updates
            else if (performanceScore < SPEEDING_THRESHOLD_SCORE)
            {
                // Map 0.0-0.3 to MIN_INTERVAL_MULTIPLIER-1.0
                float factor = (SPEEDING_THRESHOLD_SCORE - performanceScore) / SPEEDING_THRESHOLD_SCORE;
                multiplier = 1.0f - (factor * (1.0f - MIN_INTERVAL_MULTIPLIER));
            }

            // Apply the multiplier
            int adjustedInterval = Mathf.RoundToInt(baseInterval * multiplier);

            // Store and apply the adjusted interval
            _dynamicIntervals[jobGiverType] = adjustedInterval;
            Utility_JobGiverTickManager.SetDynamicInterval(jobGiverType, adjustedInterval);

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal(
                    $"Adjusted interval for {jobGiverType.Name}: {baseInterval} -> {adjustedInterval} " +
                    $"(score: {performanceScore:F2}, multiplier: {multiplier:F2})");
            }
        }

        /// <summary>
        /// Gets the current dynamic interval for a work tag
        /// </summary>
        public static int GetWorkTagDynamicInterval(string workTag)
        {
            if (string.IsNullOrEmpty(workTag))
                return 0;

            if (_dynamicWorkTagIntervals.TryGetValue(workTag, out int interval))
                return interval;

            return Utility_JobGiverTickManager.GetWorkTagBaseInterval(workTag);
        }

        /// <summary>
        /// Gets the current performance score for a work tag (0-1, lower is better)
        /// </summary>
        public static float GetWorkTagPerformanceScore(string workTag)
        {
            if (string.IsNullOrEmpty(workTag))
                return 0.5f;

            if (_workTagPerformanceScores.TryGetValue(workTag, out float score))
                return score;

            return 0.5f; // Default neutral score
        }

        #endregion
    }
}