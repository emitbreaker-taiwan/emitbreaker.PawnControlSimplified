using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Base class for basic worker job givers with specialized cache management.
    /// Handles common designation-based target finding and caching.
    /// </summary>
    public abstract class JobGiver_BasicWorker_PawnControl : JobGiver_Scan_PawnControl
    {
        #region Configuration
        
        /// <summary>
        /// All BasicWorker job givers share this work tag
        /// </summary>
        protected override string WorkTag => "BasicWorker";
        
        /// <summary>
        /// Default distance thresholds for bucketing (20, 40, 50 tiles)
        /// </summary>
        protected virtual float[] DistanceThresholds => new float[] { 400f, 1600f, 2500f };
        
        /// <summary>
        /// The designation type this job giver handles
        /// </summary>
        protected abstract DesignationDef TargetDesignation { get; }
        
        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected abstract JobDef WorkJobDef { get; }
        
        /// <summary>
        /// Update cache every 5 seconds by default
        /// </summary>
        protected override int CacheUpdateInterval => 300;
        
        #endregion

        #region Caching
        
        /// <summary>
        /// Domain-specific caches for BasicWorker jobs
        /// </summary>
        protected static readonly Dictionary<int, List<Thing>> _designationCache = new Dictionary<int, List<Thing>>();
        protected static readonly Dictionary<int, Dictionary<Thing, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Thing, bool>>();
        protected static readonly Dictionary<int, int> _lastDesignationCacheUpdate = new Dictionary<int, int>();
        
        #endregion

        #region Target selection
        
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // Return all things with the specified designation
            if (map?.designationManager != null)
            {
                foreach (Designation designation in map.designationManager.SpawnedDesignationsOfDef(TargetDesignation))
                {
                    if (designation?.target.Thing != null && designation.target.Thing.Spawned)
                    {
                        yield return designation.target.Thing;
                    }
                }
            }
        }
        
        /// <summary>
        /// Determines if a target is valid for the job
        /// </summary>
        protected virtual bool IsValidTarget(Thing thing, Pawn worker)
        {
            return !thing.IsForbidden(worker) && 
                   worker.CanReserveAndReach(thing, PathEndMode.ClosestTouch, Danger.Deadly);
        }
        
        /// <summary>
        /// Creates a job for the specified target
        /// </summary>
        protected virtual Job CreateJobForTarget(Thing target)
        {
            return JobMaker.MakeJob(WorkJobDef, target);
        }
        
        #endregion

        #region Cache management
        
        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetBasicWorkerCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_designationCache, _reachabilityCache);
            _lastDesignationCacheUpdate.Clear();
        }
        
        #endregion
    }
}