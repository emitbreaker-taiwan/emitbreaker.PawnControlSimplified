using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Verse.AI;
using static emitbreaker.PawnControl.Utility_JobGiverManagerOld;

namespace emitbreaker.PawnControl
{
    public static class Utility_JobGiverManager
    {
        #region Standardized Job Creation

        /// <summary>
        /// Standardized job-giving method wrapper to be used by all JobGiver classes for consistent behavior
        /// </summary>
        public static Job StandardTryGiveJob<T>(
            Pawn pawn,
            string workTag,
            Func<Pawn, bool, Job> jobCreator,
            List<JobEligibilityCheck> additionalChecks = null,
            string debugJobDesc = null,
            bool skipEmergencyCheck = false,
            Type jobGiverType = null) where T : class
        {
            // Start timing for profiling
            var stopwatch = new System.Diagnostics.Stopwatch();
            stopwatch.Start();

            try
            {
                // Early exit if pawn is drafted
                if (pawn?.drafter != null && pawn.drafter.Drafted)
                {
                    Utility_DebugManager.LogNormal($"Injected {pawn.LabelShort} is drafted, skipping {debugJobDesc ?? workTag} job");
                    return null;
                }

                // 1. Emergency check - yield to firefighting if there's a fire (unless we should skip it)
                if (!skipEmergencyCheck && ShouldYieldToEmergencyJob(pawn))
                {
                    Utility_DebugManager.LogNormal($"Injected {pawn.LabelShort} yielding from {debugJobDesc ?? workTag} to handle emergency");
                    return null;
                }

                // 2. Basic eligibility check with the specified work tag
                if (!Utility_JobGiverManager.IsEligibleForSpecializedJobGiver(pawn, workTag))
                    return null;

                // 3. Check global state flags if available
                if (jobGiverType != null && Utility_GlobalStateManager.ShouldSkipJobGiverDueToGlobalState(null, pawn))
                    return null;

                // 4. Check dependencies if available
                if (jobGiverType != null && !AreDependenciesSatisfied(jobGiverType, pawn))
                    return null;

                // 5. Perform any additional checks provided by the specific JobGiver
                if (additionalChecks != null)
                {
                    foreach (var check in additionalChecks)
                    {
                        if (!check(pawn))
                            return null;
                    }
                }

                // 6. Call the job creator function with the pawn and forced=false
                Job job = jobCreator(pawn, false);

                // 7. Debug logging if a job was found
                if (job != null && Prefs.DevMode)
                {
                    Utility_DebugManager.LogNormal($"Injected {pawn.LabelShort} created {debugJobDesc ?? workTag} job: {job.def.defName}");
                }

                return job;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error creating {debugJobDesc ?? workTag} job for {pawn.LabelShort}: {ex}");
                return null;
            }
            finally
            {
                // Stop timing and record metrics
                stopwatch.Stop();
                if (jobGiverType != null)
                {
                    RecordJobGiverExecution(jobGiverType, stopwatch.Elapsed.TotalMilliseconds > 0, (float)stopwatch.Elapsed.TotalMilliseconds);
                }
            }
        }

        // Also update the simplified version
        public static Job StandardTryGiveJob<T>(
            Pawn pawn,
            string workTag,
            Func<Pawn, bool, Job> jobCreator,
            JobEligibilityCheck additionalCheck,
            string debugJobDesc = null,
            bool skipEmergencyCheck = false) where T : class
        {
            try
            {
                if (pawn?.Map == null || pawn.Dead || !pawn.Spawned || pawn?.Faction == null)
                {
                    return null;
                }
                return StandardTryGiveJob<T>(
                pawn,
                workTag,
                jobCreator,
                additionalCheck != null ? new List<JobEligibilityCheck> { additionalCheck } : null,
                debugJobDesc,
                skipEmergencyCheck);
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error in StandardTryGiveJob for {pawn?.LabelShort ?? "null"}, tag {workTag}, error: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Check if pawn should yield to emergency jobs like firefighting
        /// </summary>
        public static bool ShouldYieldToEmergencyJob(Pawn pawn)
        {
            if (pawn?.Map == null)
                return false;

            // Manually scan for spawned fires in the home area
            List<Thing> fires = pawn.Map.listerThings.ThingsOfDef(ThingDefOf.Fire);
            for (int i = 0, c = fires.Count; i < c; i++)
            {
                var f = fires[i] as Fire;
                if (f != null && f.Spawned && pawn.Map.areaManager.Home[f.Position])
                {
                    // only yield if pawn can do firefighting work
                    if (Utility_TagManager.WorkEnabled(pawn.def, "Firefighter"))
                        return true;
                    break;
                }
            }

            return false;
        }

        #endregion

        #region Validation

        /// <summary>
        /// Checks if a pawn is eligible for specialized JobGiver processing
        /// </summary>
        public static bool IsEligibleForSpecializedJobGiver(Pawn pawn, string workTypeName)
        {
            if (pawn == null || !pawn.Spawned || pawn.Dead)
                return false;

            // Skip pawns without mod extension - let vanilla handle them
            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null)
                return false;

            // Using ThinkTreeManager for consistent tagging across codebase
            if (!Utility_ThinkTreeManager.HasAllowWorkTag(pawn.def))
                return false;

            if (!Utility_TagManager.WorkTypeSettingEnabled(pawn, workTypeName))
            {
                if (Prefs.DevMode)
                {
                    Utility_DebugManager.LogNormal(
                        $"{pawn.LabelShort} (non-humanlike bypass = False) is not eligible for {workTypeName} work, skipping job");
                }
                return false;
            }

            return true;
        }

        /// <summary>
        /// Checks if a pawn is allowed to perform work on a target based on faction rules
        /// </summary>
        public static bool IsValidFactionInteraction(Thing target, Pawn pawn, bool requiresDesignator = false)
        {
            // Handle null faction case first
            if (pawn?.Faction == null)
            {
                return false;
            }

            // For designator-oriented tasks, only player pawns can perform them
            if (requiresDesignator && pawn.Faction != Faction.OfPlayer)
            {
                return false;
            }

            Pawn targetPawn = target as Pawn;

            // Skip faction check for dead or downed pawns with strip designation
            // This is consistent with vanilla behavior where pawns can strip anyone
            if (requiresDesignator && targetPawn != null &&
                (targetPawn.Dead || targetPawn.Downed) &&
                targetPawn.Map?.designationManager.DesignationOn(targetPawn, DesignationDefOf.Strip) != null)
            {
                return true;
            }

            // For corpses - allow stripping regardless of faction if designated
            if (requiresDesignator && target is Corpse corpse &&
                corpse.Map?.designationManager.DesignationOn(corpse, DesignationDefOf.Strip) != null)
            {
                return true;
            }

            // Check if target has a different non-null faction
            if (target.Faction != null && target.Faction != pawn.Faction)
            {
                if (targetPawn == null)
                {
                    return false;
                }

                // Special handling for prisoners - wardens can interact with prisoner pawns
                if ((targetPawn.IsPrisoner || targetPawn.IsSlave) && targetPawn.HostFaction == pawn.Faction)
                {
                    return true;
                }

                // Special handling for patients - doctors can treat patients from other factions
                if (FeedPatientUtility.ShouldBeFed(targetPawn) || targetPawn.health.HasHediffsNeedingTend())
                {
                    Building_Bed bed = targetPawn.CurrentBed();
                    if (bed != null && bed.Medical)
                        return true;
                }

                return false;
            }

            return true;
        }

        #endregion

        #region Bucketing

        /// <summary>
        /// Creates a distance-based buckets system for optimized job target selection
        /// </summary>
        public static List<T>[] CreateDistanceBuckets<T>(Pawn pawn, IEnumerable<T> candidates,
            Func<T, float> distanceSquaredFunc, float[] distanceThresholds) where T : Thing
        {
            if (pawn == null || candidates == null || distanceThresholds == null)
                return null;

            // Initialize buckets
            List<T>[] buckets = new List<T>[distanceThresholds.Length + 1];
            for (int i = 0; i < buckets.Length; i++)
                buckets[i] = new List<T>();

            foreach (T target in candidates)
            {
                // Skip invalid targets
                if (target == null || target.Destroyed || !target.Spawned)
                    continue;

                // Get distance 
                float distSq = distanceSquaredFunc(target);

                // Assign to appropriate bucket
                int bucketIndex = distanceThresholds.Length; // Default to last bucket (furthest)
                for (int i = 0; i < distanceThresholds.Length; i++)
                {
                    if (distSq < distanceThresholds[i])
                    {
                        bucketIndex = i;
                        break;
                    }
                }

                buckets[bucketIndex].Add(target);
            }

            return buckets;
        }

        /// <summary>
        /// Process buckets from closest to farthest, finding first valid target
        /// </summary>
        public static T FindFirstValidTargetInBuckets<T>(
            List<T>[] buckets,
            Pawn pawn,
            Func<T, Pawn, bool> validationFunc,
            Dictionary<int, Dictionary<T, bool>> reachabilityCache = null) where T : Thing
        {
            if (buckets == null || pawn == null)
                return null;

            // Process buckets from closest to farthest
            for (int b = 0; b < buckets.Length; b++)
            {
                if (buckets[b].Count == 0)
                    continue;

                // Randomize within each distance band for better distribution
                buckets[b].Shuffle();

                // Check each thing in this distance band
                foreach (T thing in buckets[b])
                {
                    bool canUse = false;

                    // Use cache if available
                    if (reachabilityCache != null)
                    {
                        int mapId = pawn.Map.uniqueID;
                        var reachDict = Utility_CacheManager.GetOrNewReachabilityDict(reachabilityCache, mapId);

                        if (!reachDict.TryGetValue(thing, out canUse))
                        {
                            canUse = validationFunc(thing, pawn);

                            // Cache result but limit cache size
                            if (reachDict.Count < 1000) // Max cache size
                                reachDict[thing] = canUse;
                        }
                    }
                    else
                    {
                        canUse = validationFunc(thing, pawn);
                    }

                    if (canUse)
                        return thing;
                }
            }

            return null;
        }


        #endregion
    }
}
