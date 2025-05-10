using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using RimWorld;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Registry to track all job givers in the system
    /// </summary>
    public static class JobGiverRegistry
    {
        // Registry of all JobGivers
        private static readonly List<JobGiverEntry> _allJobGivers = new List<JobGiverEntry>();
        
        /// <summary>
        /// All registered JobGivers
        /// </summary>
        public static IReadOnlyList<JobGiverEntry> AllJobGivers => _allJobGivers;

        /// <summary>
        /// Register a JobGiver
        /// </summary>
        public static void Register(Type jobGiverType, float priority, IEnumerable<string> workTypes)
        {
            if (jobGiverType == null || workTypes == null)
                return;

            // Check if already registered
            var existingEntry = _allJobGivers.FirstOrDefault(e => e.JobGiverType == jobGiverType);
            if (existingEntry != null)
            {
                // Update existing entry
                existingEntry.Priority = priority;
                existingEntry.WorkTypes.Clear();
                foreach (string workType in workTypes)
                {
                    existingEntry.WorkTypes.Add(workType);
                }

                // Resort list
                SortJobGivers();
                return;
            }

            // Add new entry
            _allJobGivers.Add(new JobGiverEntry
            {
                JobGiverType = jobGiverType,
                Priority = priority,
                WorkTypes = new HashSet<string>(workTypes)
            });

            // Sort by priority
            SortJobGivers();
        }

        /// <summary>
        /// Sort job givers by priority (descending)
        /// </summary>
        private static void SortJobGivers()
        {
            _allJobGivers.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }
        
        /// <summary>
        /// Reset the registry
        /// </summary>
        public static void Reset()
        {
            _allJobGivers.Clear();
        }
        
        /// <summary>
        /// Find all job givers that can handle a specific work type
        /// </summary>
        public static List<JobGiverEntry> GetJobGiversForWorkType(string workType)
        {
            return _allJobGivers.Where(e => e.WorkTypes.Contains(workType)).ToList();
        }
    }
    
    /// <summary>
    /// Entry in the JobGiver registry
    /// </summary>
    public class JobGiverEntry
    {
        public Type JobGiverType { get; set; }
        public float Priority { get; set; }
        public HashSet<string> WorkTypes { get; set; } = new HashSet<string>();
    }
}