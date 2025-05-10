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

        // Performance metrics storage
        private static readonly Dictionary<Type, JobGiverMetrics> _jobGiverMetrics = new Dictionary<Type, JobGiverMetrics>();
        private static readonly Dictionary<int, Dictionary<Type, PawnSpecificMetrics>> _pawnMetrics = 
            new Dictionary<int, Dictionary<Type, PawnSpecificMetrics>>();
        
        // Metrics for debugging and visualization
        private static readonly Dictionary<Type, List<float>> _recentExecutionTimes = new Dictionary<Type, List<float>>();
        private static readonly Dictionary<Type, List<bool>> _recentSuccessRates = new Dictionary<Type, List<bool>>();
        
        // Dynamic interval adjustments
        private static readonly Dictionary<Type, int> _dynamicIntervals = new Dictionary<Type, int>();
        private static readonly Dictionary<Type, float> _performanceScores = new Dictionary<Type, float>();
        
        // Profiling state
        private static bool _profilingEnabled = false;
        private static readonly Stopwatch _profiler = new Stopwatch();
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
        /// Metrics for a specific JobGiver type
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
        /// Metrics for a specific pawn using a specific JobGiver
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
        /// Enables profiling of JobGivers
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
        /// Registers a JobGiver type for automatic optimization
        /// </summary>
        public static void RegisterForAutoOptimization(Type jobGiverType)
        {
            if (!typeof(ThinkNode_JobGiver).IsAssignableFrom(jobGiverType))
                return;
            
            _jobGiversToOptimize.Add(jobGiverType);
            
            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Registered {jobGiverType.Name} for auto-optimization");
            }
        }

        /// <summary>
        /// Starts profiling a JobGiver execution
        /// </summary>
        public static void BeginProfiling()
        {
            if (!_profilingEnabled)
                return;
                
            _profiler.Restart();
        }

        /// <summary>
        /// Ends profiling for a JobGiver execution and records results
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
            
            // Record for this JobGiver type
            RecordJobGiverMetrics(jobGiverType, executionTimeMs, wasSuccessful, currentTick);
            
            // Record for this specific pawn
            if (pawn != null)
            {
                RecordPawnMetrics(jobGiverType, pawn, executionTimeMs, wasSuccessful, currentTick, result);
            }
            
            // Check if this JobGiver should have its update interval adjusted
            if (_jobGiversToOptimize.Contains(jobGiverType))
            {
                UpdatePerformanceScore(jobGiverType);
            }
        }
        
        /// <summary>
        /// Records metrics for a JobGiver type
        /// </summary>
        private static void RecordJobGiverMetrics(Type jobGiverType, float executionTimeMs, 
            bool wasSuccessful, int currentTick)
        {
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
        /// Records metrics for a specific pawn using a JobGiver
        /// </summary>
        private static void RecordPawnMetrics(Type jobGiverType, Pawn pawn, float executionTimeMs, 
            bool wasSuccessful, int currentTick, Job job)
        {
            if (pawn == null)
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
        
        #endregion

        #region Adaptive Optimization
        
        /// <summary>
        /// Calculates and updates the performance score for a JobGiver
        /// </summary>
        private static void UpdatePerformanceScore(Type jobGiverType)
        {
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
        /// Adjusts the update interval based on performance score
        /// </summary>
        private static void AdjustIntervalBasedOnPerformance(Type jobGiverType, float performanceScore)
        {
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
        /// Gets the current dynamic interval for a JobGiver
        /// </summary>
        public static int GetDynamicInterval(Type jobGiverType)
        {
            if (_dynamicIntervals.TryGetValue(jobGiverType, out int interval))
                return interval;
                
            return Utility_JobGiverTickManager.GetBaseInterval(jobGiverType);
        }
        
        /// <summary>
        /// Gets the current performance score for a JobGiver (0-1, lower is better)
        /// </summary>
        public static float GetPerformanceScore(Type jobGiverType)
        {
            if (_performanceScores.TryGetValue(jobGiverType, out float score))
                return score;
                
            return 0.5f; // Default neutral score
        }
        
        #endregion

        #region Reporting and Visualization
        
        /// <summary>
        /// Gets metrics for a specific JobGiver type
        /// </summary>
        public static JobGiverMetricsReport GetJobGiverMetrics(Type jobGiverType)
        {
            if (_jobGiverMetrics.TryGetValue(jobGiverType, out var metrics))
            {
                return new JobGiverMetricsReport
                {
                    JobGiverType = jobGiverType,
                    TotalExecutions = metrics.TotalExecutions,
                    SuccessfulExecutions = metrics.SuccessfulExecutions,
                    SuccessRate = metrics.SuccessRate,
                    AverageExecutionTime = metrics.AverageExecutionTime,
                    MaxExecutionTime = metrics.MaxExecutionTime,
                    LastExecutionTime = metrics.LastExecutionTime,
                    IsExpensive = metrics.IsExpensive,
                    IsVeryExpensive = metrics.IsVeryExpensive,
                    CurrentInterval = GetDynamicInterval(jobGiverType),
                    BaseInterval = Utility_JobGiverTickManager.GetBaseInterval(jobGiverType),
                    PerformanceScore = GetPerformanceScore(jobGiverType)
                };
            }
            
            return new JobGiverMetricsReport { JobGiverType = jobGiverType };
        }
        
        /// <summary>
        /// Gets metrics for all tracked JobGiver types
        /// </summary>
        public static List<JobGiverMetricsReport> GetAllMetrics()
        {
            var result = new List<JobGiverMetricsReport>();
            
            foreach (var jobGiverType in _jobGiverMetrics.Keys)
            {
                result.Add(GetJobGiverMetrics(jobGiverType));
            }
            
            // Sort by average execution time (most expensive first)
            result.Sort((a, b) => b.AverageExecutionTime.CompareTo(a.AverageExecutionTime));
            
            return result;
        }
        
        /// <summary>
        /// Gets the most expensive JobGivers
        /// </summary>
        public static List<JobGiverMetricsReport> GetMostExpensiveJobGivers(int count = 5)
        {
            return GetAllMetrics().Take(count).ToList();
        }
        
        /// <summary>
        /// Gets metrics for a specific pawn
        /// </summary>
        public static PawnMetricsReport GetPawnMetrics(Pawn pawn)
        {
            if (pawn == null)
                return null;
                
            int pawnId = pawn.thingIDNumber;
            var report = new PawnMetricsReport
            {
                Pawn = pawn,
                JobGiverMetrics = new List<PawnJobGiverMetrics>()
            };
            
            if (_pawnMetrics.TryGetValue(pawnId, out var pawnJobGivers))
            {
                foreach (var entry in pawnJobGivers)
                {
                    var jobGiverMetrics = new PawnJobGiverMetrics
                    {
                        JobGiverType = entry.Key,
                        TotalExecutions = entry.Value.TotalExecutions,
                        SuccessfulExecutions = entry.Value.SuccessfulExecutions,
                        SuccessRate = entry.Value.SuccessRate,
                        AverageExecutionTime = entry.Value.AverageExecutionTime
                    };
                    
                    // Add job type distribution
                    foreach (var jobType in entry.Value.JobTypeSuccesses)
                    {
                        jobGiverMetrics.JobTypeDistribution.Add(jobType.Key, jobType.Value);
                    }
                    
                    report.JobGiverMetrics.Add(jobGiverMetrics);
                }
                
                // Sort by execution count (most used first)
                report.JobGiverMetrics.Sort((a, b) => b.TotalExecutions.CompareTo(a.TotalExecutions));
            }
            
            return report;
        }
        
        /// <summary>
        /// Gets recent execution time history for a JobGiver
        /// </summary>
        public static List<float> GetRecentExecutionTimes(Type jobGiverType)
        {
            if (_recentExecutionTimes.TryGetValue(jobGiverType, out var times))
                return new List<float>(times);
                
            return new List<float>();
        }
        
        /// <summary>
        /// Gets visualization data for a JobGiver's performance over time
        /// </summary>
        public static JobGiverPerformanceData GetPerformanceData(Type jobGiverType)
        {
            var data = new JobGiverPerformanceData
            {
                JobGiverType = jobGiverType,
                ExecutionTimes = new List<float>(),
                Successes = new List<bool>(),
                IntervalAdjustments = new List<int>()
            };
            
            if (_recentExecutionTimes.TryGetValue(jobGiverType, out var times))
                data.ExecutionTimes.AddRange(times);
                
            if (_recentSuccessRates.TryGetValue(jobGiverType, out var successes))
                data.Successes.AddRange(successes);
                
            // Generate synthetic interval adjustment history
            // This is a placeholder - in a real implementation you'd track actual adjustments
            data.IntervalAdjustments.Add(Utility_JobGiverTickManager.GetBaseInterval(jobGiverType));
            
            return data;
        }
        
        #endregion
        
        #region Report Classes
        
        /// <summary>
        /// Report data for a JobGiver's metrics
        /// </summary>
        public class JobGiverMetricsReport
        {
            public Type JobGiverType { get; set; }
            public string Name => JobGiverType?.Name ?? "Unknown";
            public int TotalExecutions { get; set; }
            public int SuccessfulExecutions { get; set; }
            public float SuccessRate { get; set; }
            public float AverageExecutionTime { get; set; }
            public float MaxExecutionTime { get; set; }
            public float LastExecutionTime { get; set; }
            public bool IsExpensive { get; set; }
            public bool IsVeryExpensive { get; set; }
            public int CurrentInterval { get; set; }
            public int BaseInterval { get; set; }
            public float PerformanceScore { get; set; }
            
            public override string ToString()
            {
                return $"{Name}: {AverageExecutionTime:F3}ms avg, {SuccessRate*100:F1}% success rate, " +
                       $"interval {BaseInterval}->{CurrentInterval} (score: {PerformanceScore:F2})";
            }
        }
        
        /// <summary>
        /// Report data for a pawn's JobGiver usage
        /// </summary>
        public class PawnMetricsReport
        {
            public Pawn Pawn { get; set; }
            public string Name => Pawn?.LabelShort ?? "Unknown";
            public List<PawnJobGiverMetrics> JobGiverMetrics { get; set; }
            
            public override string ToString()
            {
                return $"{Name} ({Pawn?.ThingID ?? "??"}): {JobGiverMetrics?.Count ?? 0} JobGivers used";
            }
        }
        
        /// <summary>
        /// Metrics for a specific JobGiver used by a specific pawn
        /// </summary>
        public class PawnJobGiverMetrics
        {
            public Type JobGiverType { get; set; }
            public string Name => JobGiverType?.Name ?? "Unknown";
            public int TotalExecutions { get; set; }
            public int SuccessfulExecutions { get; set; }
            public float SuccessRate { get; set; }
            public float AverageExecutionTime { get; set; }
            public Dictionary<string, int> JobTypeDistribution { get; set; } = new Dictionary<string, int>();
            
            public override string ToString()
            {
                return $"{Name}: {TotalExecutions} executions, {SuccessRate*100:F1}% success rate";
            }
        }
        
        /// <summary>
        /// Performance data for visualization
        /// </summary>
        public class JobGiverPerformanceData
        {
            public Type JobGiverType { get; set; }
            public string Name => JobGiverType?.Name ?? "Unknown";
            public List<float> ExecutionTimes { get; set; }
            public List<bool> Successes { get; set; }
            public List<int> IntervalAdjustments { get; set; }
        }
        
        #endregion
        
        #region Debug Visualization
        
        /// <summary>
        /// Creates a performance visualization window for a JobGiver
        /// </summary>
        public static void OpenVisualizationWindow(Type jobGiverType)
        {
            if (!_profilingEnabled || jobGiverType == null)
                return;
                
            var window = new Dialog_JobGiverPerformance(jobGiverType);
            Find.WindowStack.Add(window);
        }
        
        /// <summary>
        /// Debug dialog for visualizing JobGiver performance
        /// </summary>
        public class Dialog_JobGiverPerformance : Window
        {
            private Type _jobGiverType;
            private JobGiverMetricsReport _metrics;
            private JobGiverPerformanceData _performanceData;
            private Vector2 _scrollPosition = Vector2.zero;
            private bool _showDetailedView = false;
            private bool _autoRefresh = true;
            private int _lastRefreshTick = 0;
            
            public override Vector2 InitialSize => new Vector2(700f, 600f);
            
            public Dialog_JobGiverPerformance(Type jobGiverType)
            {
                _jobGiverType = jobGiverType;
                _metrics = GetJobGiverMetrics(jobGiverType);
                _performanceData = GetPerformanceData(jobGiverType);
                doCloseX = true;
                absorbInputAroundWindow = true;
                closeOnClickedOutside = true;
                _lastRefreshTick = Find.TickManager.TicksGame;
            }
            
            public override void DoWindowContents(Rect inRect)
            {
                // Auto-refresh data if enabled
                if (_autoRefresh && Find.TickManager.TicksGame - _lastRefreshTick > 60)
                {
                    _metrics = GetJobGiverMetrics(_jobGiverType);
                    _performanceData = GetPerformanceData(_jobGiverType);
                    _lastRefreshTick = Find.TickManager.TicksGame;
                }
                
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(0, 0, inRect.width, 40f), $"JobGiver Performance: {_metrics.Name}");
                Text.Font = GameFont.Small;
                
                Rect topRect = new Rect(0, 40f, inRect.width, 100f);
                DrawMetricsSummary(topRect);
                
                Rect graphRect = new Rect(0, 150f, inRect.width, 250f);
                DrawPerformanceGraph(graphRect);
                
                Rect optionsRect = new Rect(0, 410f, inRect.width, 30f);
                DrawOptions(optionsRect);
                
                Rect detailsRect = new Rect(0, 440f, inRect.width, inRect.height - 440f);
                
                if (_showDetailedView)
                    DrawDetailedView(detailsRect);
                else
                    DrawBasicSummary(detailsRect);
            }
            
            private void DrawMetricsSummary(Rect rect)
            {
                Rect labelRect = rect;
                labelRect.height = 25f;
                
                GUI.color = _metrics.IsVeryExpensive ? Color.red : (_metrics.IsExpensive ? Color.yellow : Color.white);
                Widgets.Label(labelRect, $"Average execution time: {_metrics.AverageExecutionTime:F3}ms (Max: {_metrics.MaxExecutionTime:F3}ms)");
                GUI.color = Color.white;
                
                labelRect.y += 25f;
                Widgets.Label(labelRect, $"Success rate: {_metrics.SuccessRate*100:F1}% ({_metrics.SuccessfulExecutions}/{_metrics.TotalExecutions})");
                
                labelRect.y += 25f;
                GUI.color = _metrics.PerformanceScore > SLOWING_THRESHOLD_SCORE ? Color.yellow : 
                    (_metrics.PerformanceScore < SPEEDING_THRESHOLD_SCORE ? Color.green : Color.white);
                Widgets.Label(labelRect, $"Performance score: {_metrics.PerformanceScore:F2} (lower is better)");
                GUI.color = Color.white;
                
                labelRect.y += 25f;
                string intervalText = _metrics.CurrentInterval != _metrics.BaseInterval ? 
                    $"Update interval: {_metrics.CurrentInterval} ticks (adjusted from {_metrics.BaseInterval})" :
                    $"Update interval: {_metrics.BaseInterval} ticks (not adjusted)";
                Widgets.Label(labelRect, intervalText);
            }
            
            private void DrawPerformanceGraph(Rect rect)
            {
                Widgets.DrawBox(rect);
                var innerRect = rect.ContractedBy(2f);
                
                // No data to display
                if (_performanceData.ExecutionTimes.Count == 0)
                {
                    Widgets.Label(innerRect, "No performance data available");
                    return;
                }
                
                // Draw graph axes
                Widgets.DrawLine(
                    new Vector2(innerRect.x, innerRect.yMax), 
                    new Vector2(innerRect.xMax, innerRect.yMax), 
                    Color.gray, 1f);
                    
                Widgets.DrawLine(
                    new Vector2(innerRect.x, innerRect.y), 
                    new Vector2(innerRect.x, innerRect.yMax), 
                    Color.gray, 1f);
                    
                // Draw time threshold lines
                float expensiveY = innerRect.yMax - (EXPENSIVE_THRESHOLD_MS / VERY_EXPENSIVE_THRESHOLD_MS * innerRect.height);
                Widgets.DrawLine(
                    new Vector2(innerRect.x, expensiveY), 
                    new Vector2(innerRect.xMax, expensiveY), 
                    new Color(1f, 0.7f, 0f, 0.5f), 1f);
                    
                float veryExpensiveY = innerRect.yMax - innerRect.height;
                Widgets.DrawLine(
                    new Vector2(innerRect.x, veryExpensiveY), 
                    new Vector2(innerRect.xMax, veryExpensiveY), 
                    new Color(1f, 0f, 0f, 0.5f), 1f);
                
                // Calculate data points
                float xStep = innerRect.width / (_performanceData.ExecutionTimes.Count - 1);
                float maxTime = Math.Max(VERY_EXPENSIVE_THRESHOLD_MS, _performanceData.ExecutionTimes.Max() * 1.1f);
                
                // Draw execution time line
                for (int i = 0; i < _performanceData.ExecutionTimes.Count - 1; i++)
                {
                    float x1 = innerRect.x + i * xStep;
                    float y1 = innerRect.yMax - (_performanceData.ExecutionTimes[i] / maxTime * innerRect.height);
                    y1 = Mathf.Clamp(y1, innerRect.y, innerRect.yMax);
                    
                    float x2 = innerRect.x + (i + 1) * xStep;
                    float y2 = innerRect.yMax - (_performanceData.ExecutionTimes[i+1] / maxTime * innerRect.height);
                    y2 = Mathf.Clamp(y2, innerRect.y, innerRect.yMax);
                    
                    // Color based on success
                    Color lineColor = _performanceData.Successes[i] ? Color.green : Color.red;
                    Widgets.DrawLine(new Vector2(x1, y1), new Vector2(x2, y2), lineColor, 1f);
                }
                
                // Draw legends
                Widgets.Label(new Rect(innerRect.x + 5f, innerRect.y + 5f, 200f, 20f), 
                    $"Max time: {maxTime:F1}ms");
                    
                Widgets.Label(new Rect(innerRect.x + 5f, expensiveY - 20f, 200f, 20f), 
                    $"Expensive: {EXPENSIVE_THRESHOLD_MS}ms");
                    
                Widgets.Label(new Rect(innerRect.x + 5f, veryExpensiveY - 20f, 200f, 20f), 
                    $"Very expensive: {VERY_EXPENSIVE_THRESHOLD_MS}ms");
            }
            
            private void DrawOptions(Rect rect)
            {
                Rect autoRefreshRect = new Rect(rect.x, rect.y, 200f, rect.height);
                bool newAutoRefresh = Widgets.RadioButtonLabeled(autoRefreshRect, "Auto-refresh", _autoRefresh);
                if (newAutoRefresh != _autoRefresh)
                {
                    _autoRefresh = newAutoRefresh;
                }
                
                Rect refreshBtnRect = new Rect(rect.x + 210f, rect.y, 100f, rect.height);
                if (Widgets.ButtonText(refreshBtnRect, "Refresh"))
                {
                    _metrics = GetJobGiverMetrics(_jobGiverType);
                    _performanceData = GetPerformanceData(_jobGiverType);
                    _lastRefreshTick = Find.TickManager.TicksGame;
                }
                
                Rect detailViewRect = new Rect(rect.x + 320f, rect.y, 200f, rect.height);
                bool newDetailView = Widgets.RadioButtonLabeled(detailViewRect, "Show detailed data", _showDetailedView);
                if (newDetailView != _showDetailedView)
                {
                    _showDetailedView = newDetailView;
                }
            }
            
            private void DrawBasicSummary(Rect rect)
            {
                Widgets.Label(rect, "Performance Assessment:");
                
                string assessment;
                Color assessmentColor;
                
                if (_metrics.IsVeryExpensive)
                {
                    assessment = "This JobGiver is VERY EXPENSIVE and significantly impacts performance. " +
                        "Consider optimizing it or reducing its update frequency.";
                    assessmentColor = Color.red;
                }
                else if (_metrics.IsExpensive)
                {
                    assessment = "This JobGiver is somewhat expensive. " +
                        "You may want to optimize it or adjust its update frequency if used frequently.";
                    assessmentColor = Color.yellow;
                }
                else if (_metrics.TotalExecutions < 20)
                {
                    assessment = "Not enough data yet to make a proper assessment.";
                    assessmentColor = Color.gray;
                }
                else
                {
                    assessment = "This JobGiver performs well and doesn't appear to have performance issues.";
                    assessmentColor = Color.green;
                }
                
                Rect assessRect = new Rect(rect.x, rect.y + 25f, rect.width, rect.height - 25f);
                GUI.color = assessmentColor;
                Widgets.Label(assessRect, assessment);
                GUI.color = Color.white;
            }
            
            private void DrawDetailedView(Rect rect)
            {
                // Show raw performance data in a scrollable list
                Widgets.BeginScrollView(rect, ref _scrollPosition, 
                    new Rect(0, 0, rect.width - 16f, _performanceData.ExecutionTimes.Count * 25f + 25f));
                
                Widgets.Label(new Rect(0, 0, 300, 25), "Execution Time (ms)");
                Widgets.Label(new Rect(300, 0, 100, 25), "Success");
                
                for (int i = 0; i < _performanceData.ExecutionTimes.Count; i++)
                {
                    float y = 25f + (i * 25f);
                    
                    GUI.color = _performanceData.ExecutionTimes[i] > VERY_EXPENSIVE_THRESHOLD_MS ? Color.red :
                        (_performanceData.ExecutionTimes[i] > EXPENSIVE_THRESHOLD_MS ? Color.yellow : Color.white);
                        
                    Widgets.Label(new Rect(0, y, 300, 25), 
                        $"{_performanceData.ExecutionTimes[i]:F3}ms");
                    GUI.color = Color.white;
                    
                    GUI.color = _performanceData.Successes[i] ? Color.green : Color.red;
                    Widgets.Label(new Rect(300, y, 100, 25), 
                        _performanceData.Successes[i] ? "Yes" : "No");
                    GUI.color = Color.white;
                }
                
                Widgets.EndScrollView();
            }
        }

        #endregion

        /// <summary>
        /// Records execution metrics for a JobGiver type
        /// </summary>
        /// <param name="jobGiverType">The type of JobGiver being executed</param>
        /// <param name="executionTimeMs">Execution time in milliseconds</param>
        /// <param name="successful">Whether the job creation was successful</param>
        public static void RecordJobGiverExecution(Type jobGiverType, float executionTimeMs, bool successful)
        {
            if (!_profilingEnabled || jobGiverType == null)
                return;

            int currentTick = Find.TickManager.TicksGame;

            // Record for this JobGiver type
            if (!_jobGiverMetrics.TryGetValue(jobGiverType, out var metrics))
            {
                metrics = new JobGiverMetrics();
                _jobGiverMetrics[jobGiverType] = metrics;
            }

            metrics.RecordExecution(executionTimeMs, successful, currentTick);

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

            successes.Add(successful);

            // Check if this JobGiver should have its update interval adjusted
            if (_jobGiversToOptimize.Contains(jobGiverType))
            {
                UpdatePerformanceScore(jobGiverType);
            }
        }
    }
}