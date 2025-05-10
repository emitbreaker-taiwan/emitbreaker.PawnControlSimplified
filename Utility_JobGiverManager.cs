using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Provides helper methods for creating and managing specialized JobGivers more efficiently
    /// </summary>
    public static class Utility_JobGiverManager
    {
        // Add WorkTypeDef cache to improve lookup performance
        private static readonly Dictionary<string, WorkTypeDef> _workTypeCache = DefDatabase<WorkTypeDef>.AllDefsListForReading.ToDictionary(w => w.defName, w => w);

        // Add bucket cache to reduce allocations
        private static readonly Dictionary<int, string> _bucketHashCache = new Dictionary<int, string>();
        private static readonly Dictionary<int, object> _cachedBuckets = new Dictionary<int, object>();
        private static readonly Dictionary<int, int> _bucketCacheLastTick = new Dictionary<int, int>();

        // Add this to Utility_JobGiverManager class
        private static readonly Dictionary<int, HashSet<string>> _disabledWorkTypeCache = new Dictionary<int, HashSet<string>>();

        // Register for work settings change events
        static Utility_JobGiverManager()
        {
            // Hook up to the work settings changed event
            Utility_WorkSettingsManager.OnWorkSettingsChanged += ClearDisabledWorkTypeCache;
        }

        /// <summary>
        /// A delegate type for check functions that may set JobFailReason
        /// </summary>
        /// <param name="pawn">The pawn to check</param>
        /// <param name="setFailReason">Whether to set a JobFailReason if the check fails</param>
        /// <returns>True if the check passes, false otherwise</returns>
        public delegate bool JobEligibilityCheck(Pawn pawn, bool setFailReason = true);

        /// <summary>
        /// Standardized job-giving method wrapper to be used by all JobGiver classes for consistent behavior
        /// </summary>
        /// <typeparam name="T">The type of things being processed (e.g., Plant, Thing, Pawn)</typeparam>
        /// <param name="pawn">The pawn trying to get a job</param>
        /// <param name="workTag">Work tag required for this job (e.g., "PlantCutting", "Warden")</param>
        /// <param name="jobCreator">Function that creates the actual job if eligibility checks pass</param>
        /// <param name="additionalChecks">Optional list of additional conditions that must be satisfied, with ability to set JobFailReasons</param>
        /// <param name="debugJobDesc">Description of the job type for logging (e.g., "plant cutting")</param>
        /// <param name="skipEmergencyCheck">Whether to skip the emergency check (set to true for emergency JobGivers)</param>
        /// <returns>A Job if one can be created, otherwise null</returns>
        public static Job StandardTryGiveJob<T>(
            Pawn pawn,
            string workTag,
            Func<Pawn, bool, Job> jobCreator,
            List<JobEligibilityCheck> additionalChecks = null,
            string debugJobDesc = null,
            bool skipEmergencyCheck = false) where T : class
        {
            // Early exit if pawn is drafted
            if (pawn.drafter != null && pawn.drafter.Drafted)
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

            // The rest of the method remains the same...
            // 2. Basic eligibility check with the specified work tag
            if (!PawnCanDoWorkType(pawn, workTag))
                return null;

            // 3. Perform any additional checks provided by the specific JobGiver
            if (additionalChecks != null)
            {
                foreach (var check in additionalChecks)
                {
                    if (!check(pawn))
                        return null;
                }
            }

            // 4. Call the job creator function with the pawn and forced=false
            try
            {
                Job job = jobCreator(pawn, false);

                // 5. Debug logging if a job was found
                if (job != null)
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

                // FIXED: Pass skipEmergencyCheck parameter properly
                return StandardTryGiveJob<T>(
                    pawn,
                    workTag,
                    jobCreator,
                    additionalCheck != null ? new List<JobEligibilityCheck> { additionalCheck } : null,
                    debugJobDesc,
                    skipEmergencyCheck);  // Now correctly passes skipEmergencyCheck
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
            {
                return false;
            }

            // Check for fires in home area - yield to firefighting JobGiver
            if (pawn.Map.listerThings.ThingsOfDef(ThingDefOf.Fire).Any(f => f.Spawned && pawn.Map.areaManager.Home[f.Position]))
            {
                // Also check if pawn can do firefighting work
                if (Utility_TagManager.WorkEnabled(pawn.def, "Firefighter"))
                {
                    return true;
                }
            }

            // Add more emergency checks if needed (e.g., threats, medical emergencies)

            return false;
        }

        /// <summary>
        /// Checks if a pawn is eligible for specialized JobGiver processing
        /// </summary>
        public static bool PawnCanDoWorkType(Pawn pawn, string workTypeName)
        {
            if (pawn == null || !pawn.Spawned || pawn.Dead || string.IsNullOrEmpty(workTypeName))
                return false;

            try
            {
                // Check the pawn-specific disabled cache first
                int pawnId = pawn.thingIDNumber;
                if (_disabledWorkTypeCache.TryGetValue(pawnId, out var disabledWorkTypes) &&
                    disabledWorkTypes.Contains(workTypeName))
                {
                    return false;
                }

                // Use cached WorkTypeDef instead of fetching from DefDatabase every time
                if (!_workTypeCache.TryGetValue(workTypeName, out WorkTypeDef workTypeDef))
                {
                    // Fallback to database lookup if not in cache (should be rare)
                    workTypeDef = DefDatabase<WorkTypeDef>.GetNamedSilentFail(workTypeName);

                    // Cache the result if found
                    if (workTypeDef != null)
                        _workTypeCache[workTypeName] = workTypeDef;
                    else
                        return false; // Exit early if no such work type exists
                }

                // Use WorkTypeSettingEnabled for the actual check
                bool result = Utility_TagManager.WorkTypeSettingEnabled(pawn, workTypeDef);

                // Update the pawn-specific disabled cache if needed
                if (!result)
                {
                    if (!_disabledWorkTypeCache.TryGetValue(pawnId, out disabledWorkTypes))
                    {
                        disabledWorkTypes = new HashSet<string>();
                        _disabledWorkTypeCache[pawnId] = disabledWorkTypes;
                    }
                    disabledWorkTypes.Add(workTypeName);

                    // Log the disabled work type
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} has work type {workTypeDef.defName} disabled in work settings, skipping job");
                }

                return result;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Critical error in PawnCanDoWorkType for {pawn?.LabelShort ?? "unknown pawn"}: {ex}");
                return false; // Default to not allowing work if there's an exception
            }
        }

        /// <summary>
        /// Creates a distance-based buckets system for optimized job target selection
        /// Now properly uses tick tracking to avoid redundant calculations
        /// </summary>
        public static List<T>[] CreateDistanceBuckets<T>(Pawn pawn, IEnumerable<T> candidates,
            Func<T, float> distanceSquaredFunc, float[] distanceThresholds) where T : Thing
        {
            if (pawn == null || candidates == null || distanceThresholds == null)
                return null;

            int mapId = pawn.Map.uniqueID;
            int currentTick = Find.TickManager.TicksGame;

            // Store hash of candidates collection to detect changes in content
            int candidateHash = 0;
            int candidateCount = 0;

            // Calculate a simple hash of the candidates collection
            foreach (T candidate in candidates)
            {
                if (candidate != null && !candidate.Destroyed)
                {
                    candidateHash = candidateHash * 31 + candidate.thingIDNumber;
                    candidateCount++;
                }
            }

            // Generate a bucket cache key that includes the hash
            string cacheKey = $"{currentTick}_{candidateCount}_{candidateHash}";

            // Check if we've already processed this exact set of candidates this tick
            if (_bucketCacheLastTick.TryGetValue(mapId, out int lastTick) &&
                lastTick == currentTick &&
                candidateCount > 0 &&
                _bucketHashCache.TryGetValue(mapId, out string lastHash) &&
                lastHash == cacheKey)
            {
                // We've already built buckets for this exact set of candidates this tick
                // Return the cached buckets if available
                if (_cachedBuckets.TryGetValue(mapId, out object cached) &&
                    cached is List<T>[] typedBuckets)
                {
                    // We found valid cached buckets, return them
                    Utility_DebugManager.LogNormal($"Reused distance buckets for map {mapId}, saved {candidateCount} allocations");
                    return typedBuckets;
                }
            }

            // Initialize buckets
            List<T>[] buckets = new List<T>[distanceThresholds.Length + 1];
            for (int i = 0; i < buckets.Length; i++)
                buckets[i] = new List<T>();

            // Process candidates into buckets
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

            // Update cache for future reuse
            _bucketCacheLastTick[mapId] = currentTick;
            _bucketHashCache[mapId] = cacheKey;
            _cachedBuckets[mapId] = buckets;

            return buckets;
        }

        /// <summary>
        /// Process buckets from closest to farthest, finding first valid target
        /// </summary>
        public static T FindFirstValidTargetInBuckets<T>(
            List<T>[] buckets,
            Pawn pawn,
            Func<T, Pawn, bool> validationFunc,
            object reachabilityCache = null) where T : Thing
        {
            if (buckets == null || pawn == null)
                return null;

            Dictionary<T, bool> reachDict = null;

            // Prepare reachability cache once outside the loop
            if (reachabilityCache != null)
            {
                // Extract the appropriate reachability dictionary
                if (reachabilityCache is Dictionary<int, Dictionary<T, bool>> nestedCache)
                {
                    int mapId = pawn.Map.uniqueID;
                    reachDict = Utility_CacheManager.GetOrNewReachabilityDict(nestedCache, mapId);
                }
                else if (reachabilityCache is Dictionary<T, bool> directCache)
                {
                    reachDict = directCache;
                }
            }

            // Process buckets from closest to farthest
            for (int b = 0; b < buckets.Length; b++)
            {
                var bucket = buckets[b];
                int bucketSize = bucket.Count;

                if (bucketSize == 0)
                    continue;

                // Randomize within each distance band for better distribution
                bucket.Shuffle();

                // Check each thing in this distance band
                for (int i = 0; i < bucketSize; i++)
                {
                    T thing = bucket[i];
                    bool canUse;

                    // Use cached reachability if available
                    if (reachDict != null)
                    {
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
                        // No cache provided, just evaluate
                        canUse = validationFunc(thing, pawn);
                    }

                    if (canUse)
                        return thing;
                }
            }

            return null;
        }

        /// <summary>
        /// Executes a generic designation-based job creation pattern
        /// </summary>
        public static Job TryCreateDesignatedJob<T>(
            Pawn pawn,
            Dictionary<int, List<T>> designationCache,
            Dictionary<int, Dictionary<T, bool>> reachabilityCache,
            string workTypeTag,
            DesignationDef designationDef,
            JobDef jobDef,
            Func<T, bool> extraValidation = null,
            Func<T, Pawn, bool> reachabilityFunc = null,
            float[] distanceThresholds = null) where T : Thing
        {
            // Basic eligibility check
            if (!PawnCanDoWorkType(pawn, workTypeTag))
                return null;

            // IMPORTANT: For designation-based jobs, ONLY player faction pawns or pawns slaved to player faction can perform them
            if (pawn.Faction != Faction.OfPlayer &&
                !(pawn.IsSlave && pawn.HostFaction == Faction.OfPlayer))
                return null;

            // Quick skip check - if no designations on map
            if (!pawn.Map.designationManager.AnySpawnedDesignationOfDef(designationDef))
                return null;
                
            // Default distance thresholds if not provided
            if (distanceThresholds == null)
                distanceThresholds = new float[] { 625f, 2500f, 10000f }; // 25, 50, 100 tiles
                
            // Default reachability function if not provided
            if (reachabilityFunc == null)
                reachabilityFunc = (T t, Pawn p) => !t.IsForbidden(p) && p.CanReserveAndReach(t, PathEndMode.Touch, p.NormalMaxDanger());
                
            int mapId = pawn.Map.uniqueID;
            if (!designationCache.ContainsKey(mapId) || designationCache[mapId].Count == 0)
                return null;
                
            // Create distance buckets
            var buckets = CreateDistanceBuckets(
                pawn, 
                designationCache[mapId],
                (T t) => (t.Position - pawn.Position).LengthHorizontalSquared,
                distanceThresholds
            );

            // Apply extra validation if provided
            Func<T, Pawn, bool> finalValidation = (T t, Pawn p) =>
            {
                // IMPORTANT: Check faction interaction validity first since this is a designator-based job
                if (!IsValidFactionInteraction(t, p, requiresDesignator: true))
                    return false;

                if (t.Destroyed || !t.Spawned ||
                    pawn.Map.designationManager.DesignationOn(t, designationDef) == null)
                    return false;

                if (extraValidation != null && !extraValidation(t))
                    return false;

                return reachabilityFunc(t, p);
            };

            // Find target
            T target = FindFirstValidTargetInBuckets(buckets, pawn, finalValidation, reachabilityCache);
            
            // Create job if target found
            if (target != null)
            {
                Job job = JobMaker.MakeJob(jobDef, target);
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job {jobDef.defName} for {target.Label}");                
                return job;
            }
            
            return null;
        }

        /// <summary>
        /// Attempts to create a job for a pawn to work on bills at a work station.
        /// Works with any kind of bill giver (campfires, crematoriums, etc.)
        /// </summary>
        public static Job TryCreateBillGiverJob<T>(
            Pawn pawn,
            Dictionary<int, List<T>> billGiversCache,
            Dictionary<int, Dictionary<T, bool>> reachabilityCache,
            float[] distanceThresholds = null) where T : Thing, IBillGiver
        {
            if (pawn?.Map == null) return null;

            // Use default distance thresholds if not provided
            if (distanceThresholds == null)
                distanceThresholds = new float[] { 225f, 625f, 1600f }; // 15, 25, 40 tiles

            int mapId = pawn.Map.uniqueID;
            if (!billGiversCache.ContainsKey(mapId) || billGiversCache[mapId].Count == 0)
                return null;

            // Use distance bucketing
            var buckets = CreateDistanceBuckets(
                pawn,
                billGiversCache[mapId],
                (building) => (building.Position - pawn.Position).LengthHorizontalSquared,
                distanceThresholds
            );

            // Find the best bill giver to use
            T targetBillGiver = FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (building, p) => {
                    // Skip if no longer valid
                    if (building == null || building.Destroyed || !building.Spawned)
                        return false;

                    // Skip if not usable as a bill giver
                    if (building.BillStack == null || !building.BillStack.AnyShouldDoNow || !building.UsableForBillsAfterFueling())
                        return false;

                    // Check if refueling is needed first
                    CompRefuelable refuelable = building.TryGetComp<CompRefuelable>();
                    if (refuelable != null && !refuelable.HasFuel)
                    {
                        if (!RefuelWorkGiverUtility.CanRefuel(p, building, false))
                            return false;

                        // Skip - a refueling job will be created instead
                        return false;
                    }

                    // Skip if forbidden or unreachable
                    if (building.IsForbidden(p) || building.IsBurning() ||
                        !p.CanReserve(building))
                        return false;

                    // Check for interaction cell
                    if (building.def.hasInteractionCell &&
                        !p.CanReserve(building.InteractionCell))
                        return false;

                    return true;
                },
                reachabilityCache
            );

            // Create job if target found
            if (targetBillGiver != null)
            {
                // Determine if we need to refuel first
                CompRefuelable refuelable = targetBillGiver.TryGetComp<CompRefuelable>();
                if (refuelable != null && !refuelable.HasFuel)
                {
                    if (RefuelWorkGiverUtility.CanRefuel(pawn, targetBillGiver, false))
                    {
                        Job refuelJob = RefuelWorkGiverUtility.RefuelJob(pawn, targetBillGiver, false);
                        if (refuelJob != null)
                        {
                            Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to refuel {targetBillGiver.def.label} first");
                            return refuelJob;
                        }
                    }
                }

                // Find a valid bill to work on
                targetBillGiver.BillStack.RemoveIncompletableBills();

                for (int i = 0; i < targetBillGiver.BillStack.Count; i++)
                {
                    Bill bill = targetBillGiver.BillStack[i];
                    if (bill.ShouldDoNow() && bill.PawnAllowedToStartAnew(pawn))
                    {
                        // Check skill requirements
                        SkillRequirement skillReq = bill.recipe?.FirstSkillRequirementPawnDoesntSatisfy(pawn);
                        if (skillReq != null)
                            continue;

                        if (bill is Bill_ProductionWithUft billProduction)
                        {
                            // Check for unfinished things to work on
                            if (billProduction.BoundUft != null)
                            {
                                if (billProduction.BoundWorker == pawn &&
                                    pawn.CanReserveAndReach(billProduction.BoundUft, PathEndMode.Touch, Danger.Deadly) &&
                                    !billProduction.BoundUft.IsForbidden(pawn))
                                {
                                    // Work on the existing unfinished thing
                                    Job finishJob = FinishUnfinishedThingJob(pawn, billProduction.BoundUft, billProduction);
                                    if (finishJob != null)
                                    {
                                        Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to finish {bill.recipe?.defName ?? "unfinished task"}");
                                        return finishJob;
                                    }
                                }
                                continue;
                            }

                            // Look for unfinished tasks
                            UnfinishedThing uft = FindClosestUnfinishedThingForBill(pawn, billProduction);
                            if (uft != null)
                            {
                                Job finishJob = FinishUnfinishedThingJob(pawn, uft, billProduction);
                                if (finishJob != null)
                                {
                                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to finish {bill.recipe?.defName ?? "unfinished task"}");
                                    return finishJob;
                                }
                            }
                        }

                        // Try to find ingredients for the job
                        List<ThingCount> chosenIngredients = new List<ThingCount>();
                        if (TryFindBestBillIngredients(bill, pawn, targetBillGiver, chosenIngredients))
                        {
                            Job billJob = CreateStartBillJob(pawn, bill, targetBillGiver, chosenIngredients);
                            if (billJob != null)
                            {
                                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to work on {bill.recipe?.defName ?? "bill"} at {targetBillGiver.def.label}");
                                return billJob;
                            }
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Creates a job for a pawn to work on bills at a specific work table
        /// </summary>
        /// <param name="pawn">The pawn who will perform the job</param>
        /// <param name="workTable">The work table with bills to process</param>
        /// <returns>A Job if one can be created, otherwise null</returns>
        public static Job TryCreateBillJob(Pawn pawn, Building_WorkTable workTable)
        {
            try
            {
                if (pawn == null || workTable == null || !workTable.Spawned)
                    return null;

                // Check if the work table needs refueling first
                CompRefuelable refuelable = workTable.TryGetComp<CompRefuelable>();
                if (refuelable != null && !refuelable.HasFuel)
                {
                    if (RefuelWorkGiverUtility.CanRefuel(pawn, workTable, false))
                    {
                        Job refuelJob = RefuelWorkGiverUtility.RefuelJob(pawn, workTable, false);
                        if (refuelJob != null)
                        {
                            Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to refuel {workTable.def.label} first");
                            return refuelJob;
                        }
                    }
                }

                // Find a valid bill to work on
                if (workTable.BillStack == null)
                    return null;

                workTable.BillStack.RemoveIncompletableBills();

                for (int i = 0; i < workTable.BillStack.Count; i++)
                {
                    Bill bill = workTable.BillStack[i];
                    if (bill.ShouldDoNow() && bill.PawnAllowedToStartAnew(pawn))
                    {
                        // Check skill requirements
                        SkillRequirement skillReq = bill.recipe?.FirstSkillRequirementPawnDoesntSatisfy(pawn);
                        if (skillReq != null)
                            continue;

                        if (bill is Bill_ProductionWithUft billProduction)
                        {
                            // Check for unfinished things to work on
                            if (billProduction.BoundUft != null)
                            {
                                if (billProduction.BoundWorker == pawn &&
                                    pawn.CanReserveAndReach(billProduction.BoundUft, PathEndMode.Touch, Danger.Deadly) &&
                                    !billProduction.BoundUft.IsForbidden(pawn))
                                {
                                    // Work on the existing unfinished thing
                                    Job finishJob = FinishUnfinishedThingJob(pawn, billProduction.BoundUft, billProduction);
                                    if (finishJob != null)
                                    {
                                        Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to finish {bill.recipe?.defName ?? "unfinished task"}");
                                        return finishJob;
                                    }
                                }
                                continue;
                            }

                            // Look for unfinished tasks
                            UnfinishedThing uft = FindClosestUnfinishedThingForBill(pawn, billProduction);
                            if (uft != null)
                            {
                                Job finishJob = FinishUnfinishedThingJob(pawn, uft, billProduction);
                                if (finishJob != null)
                                {
                                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to finish {bill.recipe?.defName ?? "unfinished task"}");
                                    return finishJob;
                                }
                            }
                        }

                        // Try to find ingredients for the job
                        List<ThingCount> chosenIngredients = new List<ThingCount>();
                        if (TryFindBestBillIngredients(bill, pawn, workTable, chosenIngredients))
                        {
                            Job billJob = CreateStartBillJob(pawn, bill, workTable, chosenIngredients);
                            if (billJob != null)
                            {
                                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to work on {bill.recipe?.defName ?? "bill"} at {workTable.def.label}");
                                return billJob;
                            }
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating bill job for {pawn?.LabelShort ?? "null"}: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Creates a job for a pawn to deliver resources to a construction frame or blueprint
        /// </summary>
        /// <param name="pawn">The pawn who will deliver resources</param>
        /// <param name="constructible">The construction target (frame or blueprint)</param>
        /// <returns>A job to deliver resources, or null if not possible</returns>
        public static Job ResourceDeliveryJobFor(Pawn pawn, IConstructible constructible)
        {
            if (pawn == null || constructible == null || !(constructible is Thing))
                return null;

            try
            {
                // Special case for installing blueprints
                if (constructible is Blueprint_Install install)
                {
                    // Create install job directly
                    Thing thing = install.MiniToInstallOrBuildingToReinstall;
                    if (thing == null)
                        return null;

                    Job job = JobMaker.MakeJob(JobDefOf.HaulToContainer);
                    job.targetA = thing;
                    job.targetB = install;
                    job.count = 1;
                    job.haulMode = HaulMode.ToContainer;
                    return job;
                }

                // Track missing resources for failure reasons
                Dictionary<ThingDef, int> missingResources = new Dictionary<ThingDef, int>();
                List<Thing> resourcesAvailable = new List<Thing>();

                // Get materials needed for construction
                foreach (ThingDefCountClass need in constructible.TotalMaterialCost())
                {
                    // Check how many items are needed
                    int countNeeded = constructible.ThingCountNeeded(need.thingDef);
                    if (countNeeded <= 0)
                        continue;

                    // See if required materials exist on the map
                    if (!pawn.Map.itemAvailability.ThingsAvailableAnywhere(need.thingDef, countNeeded, pawn))
                    {
                        missingResources.Add(need.thingDef, countNeeded);
                        continue;
                    }

                    Thing foundResource = null;

                    // Check if pawn is already carrying the resource
                    if (pawn.carryTracker?.CarriedThing != null &&
                        pawn.carryTracker.CarriedThing.def == need.thingDef &&
                        !pawn.carryTracker.CarriedThing.IsForbidden(pawn))
                    {
                        foundResource = pawn.carryTracker.CarriedThing;
                    }
                    else
                    {
                        // Find closest valid resource
                        foundResource = GenClosest.ClosestThingReachable(
                            pawn.Position,
                            pawn.Map,
                            ThingRequest.ForDef(need.thingDef),
                            PathEndMode.ClosestTouch,
                            TraverseParms.For(pawn),
                            9999f,
                            (Thing r) => IsValidResource(pawn, need, r)
                        );
                    }

                    // If no valid resource found, track as missing
                    if (foundResource == null)
                    {
                        missingResources.Add(need.thingDef, countNeeded);
                        continue;
                    }

                    // Find nearby resources of same type that could be hauled together
                    resourcesAvailable.Clear();
                    resourcesAvailable.Add(foundResource);
                    int totalAvailable = foundResource.stackCount;

                    // Add additional resources if needed and available
                    if (countNeeded > totalAvailable)
                    {
                        foreach (Thing thing in pawn.Map.listerThings.ThingsOfDef(need.thingDef))
                        {
                            if (thing != foundResource &&
                                !thing.IsForbidden(pawn) &&
                                thing.IsInValidStorage() &&
                                pawn.CanReserve(thing) &&
                                pawn.CanReach(thing, PathEndMode.Touch, Danger.Deadly))
                            {
                                resourcesAvailable.Add(thing);
                                totalAvailable += thing.stackCount;

                                if (totalAvailable >= countNeeded)
                                    break;
                            }
                        }
                    }

                    // Create delivery job
                    Thing targetThing = constructible as Thing;
                    if (targetThing != null)
                    {
                        Job job = JobMaker.MakeJob(JobDefOf.HaulToContainer);
                        job.targetA = foundResource;
                        job.targetC = targetThing;
                        job.targetB = targetThing; // Primary target in this case

                        // Queue additional resources if available
                        if (resourcesAvailable.Count > 1)
                        {
                            job.targetQueueA = new List<LocalTargetInfo>();
                            for (int i = 1; i < resourcesAvailable.Count; i++)
                            {
                                job.targetQueueA.Add(resourcesAvailable[i]);
                            }
                        }

                        // Set count to the minimum of what's needed and what's available
                        job.count = Math.Min(countNeeded, totalAvailable);
                        job.haulMode = HaulMode.ToContainer;

                        Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to deliver {need.thingDef.label} to {targetThing.Label}");
                        return job;
                    }
                }

                // Report missing resources for UI feedback
                if (missingResources.Count > 0 && FloatMenuMakerMap.makingFor == pawn)
                {
                    JobFailReason.Is("MissingMaterials".Translate(missingResources.Select(kvp =>
                        $"{kvp.Value}x {kvp.Key.label}").ToCommaList()));
                }

                return null;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating resource delivery job: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Validates if a resource can be used for construction
        /// </summary>
        private static bool IsValidResource(Pawn pawn, ThingDefCountClass need, Thing resource)
        {
            return resource.def == need.thingDef &&
                   !resource.IsForbidden(pawn) &&
                   pawn.CanReserve(resource) &&
                   resource.stackCount > 0;
        }

        ///// <summary>
        ///// Creates a job for warden to interact with a prisoner
        ///// </summary>
        //public static Job TryCreatePrisonerInteractionJob(
        //    Pawn warden,
        //    Dictionary<int, List<Pawn>> prisonerCache,
        //    Dictionary<int, Dictionary<Pawn, bool>> reachabilityCache,
        //    Func<Pawn, Pawn, bool> validator,
        //    Func<Pawn, Pawn, Job> jobCreator,
        //    float[] distanceThresholds = null)
        //{
        //    if (warden?.Map == null || warden.Faction == null) return null;

        //    int mapId = warden.Map.uniqueID;
        //    if (!prisonerCache.ContainsKey(mapId) || prisonerCache[mapId].Count == 0)
        //        return null;

        //    // Use distance bucketing
        //    var buckets = CreateDistanceBuckets(
        //        warden,
        //        prisonerCache[mapId],
        //        prisoner => (prisoner.Position - warden.Position).LengthHorizontalSquared,
        //        distanceThresholds ?? new float[] { 100f, 400f, 900f }
        //    );

        //    // Find best prisoner to interact with
        //    Pawn targetPrisoner = FindFirstValidTargetInBuckets(
        //        buckets,
        //        warden,
        //        (prisoner, p) => {
        //            // Base validation checks
        //            if (!IsValidFactionInteraction(prisoner, p, requiresDesignator: false))
        //                return false;

        //            if (prisoner?.guest == null || !prisoner.IsPrisoner)
        //                return false;

        //            // Custom validation from caller
        //            return validator(prisoner, p);
        //        },
        //        reachabilityCache
        //    );

        //    // Create job if we found a valid target
        //    if (targetPrisoner != null)
        //    {
        //        return jobCreator(warden, targetPrisoner);
        //    }

        //    return null;
        //}

        ///// <summary>
        ///// Updates a cache of prisoners matching specific criteria
        ///// </summary>
        //public static void UpdatePrisonerCache(
        //    Map map,
        //    ref int lastUpdateTick,
        //    int updateInterval,
        //    Dictionary<int, List<Pawn>> prisonerCache,
        //    Dictionary<int, Dictionary<Pawn, bool>> reachabilityCache,
        //    Func<Pawn, bool> prisonerFilter)
        //{
        //    if (map == null) return;

        //    int currentTick = Find.TickManager.TicksGame;
        //    int mapId = map.uniqueID;

        //    if (currentTick > lastUpdateTick + updateInterval ||
        //        !prisonerCache.ContainsKey(mapId))
        //    {
        //        // Clear outdated cache
        //        if (prisonerCache.ContainsKey(mapId))
        //            prisonerCache[mapId].Clear();
        //        else
        //            prisonerCache[mapId] = new List<Pawn>();

        //        // Clear reachability cache too
        //        if (reachabilityCache.ContainsKey(mapId))
        //            reachabilityCache[mapId].Clear();
        //        else
        //            reachabilityCache[mapId] = new Dictionary<Pawn, bool>();

        //        // Find all matching prisoners using the provided filter
        //        foreach (Pawn prisoner in map.mapPawns.PrisonersOfColonySpawned)
        //        {
        //            if (prisoner == null || prisoner.Destroyed || !prisoner.Spawned)
        //                continue;

        //            if (prisonerFilter(prisoner))
        //            {
        //                prisonerCache[mapId].Add(prisoner);
        //            }
        //        }

        //        lastUpdateTick = currentTick;
        //    }
        //}

        /// <summary>
        /// Creates a job to finish an unfinished thing
        /// </summary>
        public static Job FinishUnfinishedThingJob(Pawn pawn, UnfinishedThing uft, Bill_ProductionWithUft bill)
        {
            if (uft.Creator != pawn)
            {
                Utility_DebugManager.LogError("Tried to get FinishUftJob for " + pawn + " finishing " + uft + " but its creator is " + uft.Creator);
                return null;
            }

            Thing billGiverThing = bill.billStack.billGiver as Thing;
            if (billGiverThing == null)
                return null;

            Job job = JobMaker.MakeJob(JobDefOf.DoBill, billGiverThing);
            job.bill = bill;
            job.targetQueueB = new List<LocalTargetInfo>() { uft };
            job.countQueue = new List<int>() { 1 };
            job.haulMode = HaulMode.ToCellNonStorage;

            return job;
        }

        /// <summary>
        /// Finds the closest unfinished thing for a bill
        /// </summary>
        public static UnfinishedThing FindClosestUnfinishedThingForBill(Pawn pawn, Bill_ProductionWithUft bill)
        {
            return (UnfinishedThing)GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForDef(bill.recipe.unfinishedThingDef),
                PathEndMode.InteractionCell,
                TraverseParms.For(pawn),
                validator: t =>
                    !t.IsForbidden(pawn) &&
                    ((UnfinishedThing)t).Recipe == bill.recipe &&
                    ((UnfinishedThing)t).Creator == pawn &&
                    ((UnfinishedThing)t).ingredients.TrueForAll(x => bill.IsFixedOrAllowedIngredient(x.def)) &&
                    pawn.CanReserve(t)
            );
        }

        /// <summary>
        /// Creates a job to start working on a bill
        /// </summary>
        public static Job CreateStartBillJob(Pawn pawn, Bill bill, Thing billGiver, List<ThingCount> chosenIngredients)
        {
            IBillGiver giver = billGiver as IBillGiver;
            if (giver == null)
                return null;

            Job haulOffJob = WorkGiverUtility.HaulStuffOffBillGiverJob(pawn, giver, null);
            if (haulOffJob != null)
                return haulOffJob;

            Job job = JobMaker.MakeJob(JobDefOf.DoBill, billGiver);
            job.targetQueueB = new List<LocalTargetInfo>(chosenIngredients.Count);
            job.countQueue = new List<int>(chosenIngredients.Count);

            for (int i = 0; i < chosenIngredients.Count; i++)
            {
                job.targetQueueB.Add(chosenIngredients[i].Thing);
                job.countQueue.Add(chosenIngredients[i].Count);
            }

            job.haulMode = HaulMode.ToCellNonStorage;
            job.bill = bill;

            return job;
        }

        /// <summary>
        /// Tries to find the best ingredients for a bill
        /// </summary>
        public static bool TryFindBestBillIngredients(Bill bill, Pawn pawn, Thing billGiver, List<ThingCount> chosen)
        {
            chosen.Clear();
            float searchRadius = bill.ingredientSearchRadius;

            // For a bill, we're looking for ingredients that match the bill filter
            List<Thing> availableThings = new List<Thing>();

            // Use the same search logic as in WorkGiver_DoBill's TryFindBestIngredientsHelper
            Predicate<Thing> thingValidator = (t => IsUsableIngredient(t, bill));
            float radiusSq = searchRadius * searchRadius;

            // Custom search implementation - simpler than the vanilla version for performance
            foreach (Thing t in pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
            {
                if (t.Spawned &&
                    thingValidator(t) &&
                    (t.Position - billGiver.Position).LengthHorizontalSquared < radiusSq &&
                    !t.IsForbidden(pawn) &&
                    pawn.CanReserve(t))
                {
                    availableThings.Add(t);
                }
            }

            if (availableThings.Count == 0)
                return false;

            // Sort by distance for better efficiency
            availableThings.Sort((t1, t2) =>
                (t1.Position - pawn.Position).LengthHorizontalSquared.CompareTo(
                (t2.Position - pawn.Position).LengthHorizontalSquared));

            // Pick the right ingredients based on the bill type
            if (bill.recipe.allowMixingIngredients)
            {
                return TryFindBestBillIngredientsInSet_AllowMix(availableThings, bill, chosen, pawn.Position);
            }
            else
            {
                return TryFindBestBillIngredientsInSet_NoMix(availableThings, bill, chosen, pawn.Position);
            }
        }

        /// <summary>
        /// Checks if a thing is a usable ingredient for a bill
        /// </summary>
        public static bool IsUsableIngredient(Thing t, Bill bill)
        {
            if (!bill.IsFixedOrAllowedIngredient(t))
                return false;

            foreach (IngredientCount ingredient in bill.recipe.ingredients)
            {
                if (ingredient.filter.Allows(t))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Tries to find the best ingredients for a bill that doesn't allow mixing
        /// </summary>
        public static bool TryFindBestBillIngredientsInSet_NoMix(List<Thing> availableThings, Bill bill, List<ThingCount> chosen, IntVec3 rootCell)
        {
            chosen.Clear();

            // Simple implementation - just pick the first valid ingredient for each requirement
            foreach (IngredientCount ingredient in bill.recipe.ingredients)
            {
                bool foundIngredient = false;

                foreach (Thing thing in availableThings)
                {
                    if (ingredient.filter.Allows(thing) && bill.ingredientFilter.Allows(thing))
                    {
                        // Use the ingredient's CountRequiredOfFor method instead of accessing it through recipe
                        int countToAdd = Mathf.Min(ingredient.CountRequiredOfFor(thing.def, bill.recipe, bill), thing.stackCount);
                        if (countToAdd > 0)
                        {
                            chosen.Add(new ThingCount(thing, countToAdd));
                            foundIngredient = true;
                            break;
                        }
                    }
                }

                if (!foundIngredient)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Tries to find the best ingredients for a bill that allows mixing
        /// </summary>
        public static bool TryFindBestBillIngredientsInSet_AllowMix(List<Thing> availableThings, Bill bill, List<ThingCount> chosen, IntVec3 rootCell)
        {
            chosen.Clear();

            // Sort by distance only - we can't use ValuePerUnitOf from an ingredientValueGetter that doesn't exist
            availableThings.Sort((t1, t2) =>
                (t1.Position - rootCell).LengthHorizontalSquared.CompareTo(
                (t2.Position - rootCell).LengthHorizontalSquared));

            foreach (IngredientCount ingredient in bill.recipe.ingredients)
            {
                float baseCount = ingredient.GetBaseCount();

                foreach (Thing thing in availableThings)
                {
                    if (ingredient.filter.Allows(thing) && bill.ingredientFilter.Allows(thing))
                    {
                        // We need to calculate based on the count required
                        int countRequired = ingredient.CountRequiredOfFor(thing.def, bill.recipe, bill);
                        // Instead of using a non-existent ValuePerUnitOf method, we'll use 1.0 as default value per unit
                        float valuePerUnit = 1f;
                        int countToAdd = Mathf.Min(Mathf.CeilToInt(baseCount / valuePerUnit), thing.stackCount);

                        if (countToAdd > 0)
                        {
                            chosen.Add(new ThingCount(thing, countToAdd));
                            baseCount -= countToAdd * valuePerUnit;

                            if (baseCount <= 0.0001f)
                                break;
                        }
                    }
                }

                if (baseCount > 0.0001f)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Checks if there's an active emergency that should override normal work priorities
        /// </summary>
        public static bool IsEmergencyActive(Pawn pawn, string emergencyType)
        {
            if (pawn?.Map == null) return false;

            switch (emergencyType)
            {
                case "Fire":
                    return pawn.Map.listerThings.ThingsOfDef(ThingDefOf.Fire).Any(f =>
                        f.Spawned && pawn.Map.areaManager.Home[f.Position]);

                case "MedicalEmergency":
                    // Check for colonists needing urgent medical care
                    return pawn.Map.mapPawns.FreeColonists.Any(p =>
                        p != pawn && p.health.HasHediffsNeedingTend());

                // Add more emergency types as needed

                default:
                    return false;
            }
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

        /// <summary>
        /// Updates the target caches for all specified job modules
        /// </summary>
        /// <summary>
        /// Updates the target caches for all specified job modules
        /// </summary>
        public static void UpdateAllModuleTargetCaches<TModule, TTarget>(
            Map map,
            List<TModule> modules,
            Dictionary<int, Dictionary<string, List<TTarget>>> targetCache)
            where TModule : JobModule<TTarget>
            where TTarget : Thing
        {
            if (map == null || modules == null || modules.Count == 0) return;

            int mapId = map.uniqueID;

            // Initialize cache structures if needed
            if (!targetCache.ContainsKey(mapId))
            {
                targetCache[mapId] = new Dictionary<string, List<TTarget>>();
            }

            // Update each module's cache and track results
            foreach (var module in modules)
            {
                try
                {
                    string moduleId = module.UniqueID;

                    // Initialize module's target list if needed
                    if (!targetCache[mapId].ContainsKey(moduleId))
                    {
                        targetCache[mapId][moduleId] = new List<TTarget>();
                    }
                    else
                    {
                        targetCache[mapId][moduleId].Clear();
                    }

                    // Get the target list
                    List<TTarget> targets = targetCache[mapId][moduleId];

                    // Call the module's public UpdateCache method
                    module.UpdateCache(map, targets);

                    // Update the module's target status
                    bool hasTargets = targets.Count > 0;
                    module.SetHasTargets(map, hasTargets);

                    if (hasTargets && Prefs.DevMode)
                    {
                        Utility_DebugManager.LogNormal($"Module {moduleId} found {targets.Count} targets on map {mapId}");
                    }
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"Error updating cache for module {module.UniqueID}: {ex}");
                }
            }
        }

        /// <summary>
        /// Finds the first job module that can provide a job for the given pawn
        /// </summary>
        public static Job TryGetJobFromModules<TModule, TTarget>(
            Pawn pawn,
            List<TModule> modules,
            Dictionary<int, Dictionary<string, List<TTarget>>> targetCache,
            Dictionary<int, Dictionary<TTarget, bool>> reachabilityCache,
            float[] distanceThresholds = null)
            where TModule : JobModule<TTarget>
            where TTarget : Thing
        {
            if (pawn?.Map == null || modules == null || modules.Count == 0) return null;

            int mapId = pawn.Map.uniqueID;

            // Debug why a pawn isn't finding targets - this will help immensely!
            if (Prefs.DevMode && pawn.Name != null && modules.Count > 0)
            {
                bool hasEmptyCaches = false;

                if (!targetCache.ContainsKey(mapId))
                {
                    Utility_DebugManager.LogWarning($"No target cache for map {mapId} (pawn: {pawn.LabelShort})");
                    hasEmptyCaches = true;
                }
                else if (targetCache[mapId].Count == 0)
                {
                    Utility_DebugManager.LogWarning($"Target cache for map {mapId} is empty (pawn: {pawn.LabelShort})");
                    hasEmptyCaches = true;
                }
                else
                {
                    // Count modules with no targets
                    int emptyModuleCount = modules.Count(m =>
                        !targetCache[mapId].ContainsKey(m.UniqueID) ||
                        targetCache[mapId][m.UniqueID].Count == 0);

                    if (emptyModuleCount == modules.Count)
                    {
                        Utility_DebugManager.LogWarning($"All {modules.Count} modules have empty target caches for {pawn.LabelShort}");
                        Utility_DebugManager.LogJobModuleTargetCaches(modules, targetCache, mapId);
                        hasEmptyCaches = true;
                    }
                }

                if (hasEmptyCaches)
                {
                    // Force update target caches for all modules
                    UpdateAllModuleTargetCaches(pawn.Map, modules, targetCache);
                    Utility_DebugManager.LogNormal($"Forced target cache update for {pawn.LabelShort}");
                }
            }

            // Use default distance thresholds if not provided
            if (distanceThresholds == null)
                distanceThresholds = new float[] { 100f, 400f, 900f }; // 10, 20, 30 tiles

            // Track modules we'll try
            HashSet<string> attemptedModuleIds = new HashSet<string>();

            // First check if this pawn had a successful module previously
            string lastSuccessfulModuleId = JobModuleCore.GetLastSuccessfulModule(pawn);
            if (!string.IsNullOrEmpty(lastSuccessfulModuleId))
            {
                var lastSuccessfulModule = modules.FirstOrDefault(m => m.UniqueID == lastSuccessfulModuleId);

                // If we found the module and it still applies, try it first
                if (lastSuccessfulModule != null &&
                    lastSuccessfulModule.WorkTypeApplies(pawn) &&
                    lastSuccessfulModule.QuickFilterCheck(pawn) &&
                    lastSuccessfulModule.HasTargets(pawn.Map))
                {
                    attemptedModuleIds.Add(lastSuccessfulModule.UniqueID);

                    Job job = TryGetJobFromSingleModule(
                        pawn, lastSuccessfulModule, targetCache, reachabilityCache, distanceThresholds);

                    if (job != null)
                        return job;
                }
            }

            // Then try all remaining modules in priority order
            foreach (var module in modules)
            {
                // Skip modules we've already tried or that won't work for this pawn
                if (attemptedModuleIds.Contains(module.UniqueID) ||
                    !module.WorkTypeApplies(pawn) ||
                    !module.QuickFilterCheck(pawn) ||
                    !module.HasTargets(pawn.Map))
                    continue;

                attemptedModuleIds.Add(module.UniqueID);

                Job job = TryGetJobFromSingleModule(pawn, module, targetCache, reachabilityCache, distanceThresholds);

                if (job != null)
                {
                    // Record successful module for future optimization
                    module.RecordSuccessfulJobCreation(pawn);
                    return job;
                }
            }

            // If we tried all modules and found nothing, consider this a failure state
            if (attemptedModuleIds.Count > 0)
            {
                // Only clear local caches for this pawn
                if (reachabilityCache.ContainsKey(mapId))
                    reachabilityCache[mapId].Clear();

                // Remove this pawn from last successful module tracking
                JobModuleCore.ClearLastSuccessfulModule(pawn);

                Utility_DebugManager.LogNormal($"All job modules failed for {pawn.LabelShort} - resetting caches");
            }
            // FIXED: Only clear cache if the pawn previously had a successful module
            else if (modules.Count > 0 && !string.IsNullOrEmpty(lastSuccessfulModuleId))
            {
                JobModuleCore.ClearLastSuccessfulModule(pawn);
                Utility_DebugManager.LogNormal($"No job modules were eligible for {pawn.LabelShort} - resetting last successful module cache");
            }

            return null;
        }

        /// <summary>
        /// Tries to get a job from a specific module for a pawn
        /// </summary>
        private static Job TryGetJobFromSingleModule<TModule, TTarget>(
            Pawn pawn,
            TModule module,
            Dictionary<int, Dictionary<string, List<TTarget>>> targetCache,
            Dictionary<int, Dictionary<TTarget, bool>> reachabilityCache,
            float[] distanceThresholds)
            where TModule : JobModule<TTarget>
            where TTarget : Thing
        {
            int mapId = pawn.Map.uniqueID;
            string moduleId = module.UniqueID;

            // Check if we have a valid target cache
            bool hasValidCache = targetCache.ContainsKey(mapId) &&
                                 targetCache[mapId].ContainsKey(moduleId) &&
                                 targetCache[mapId][moduleId].Count > 0;

            // If no cache exists, try to force update it
            if (!hasValidCache && Prefs.DevMode)
            {
                // Make sure the map cache exists
                if (!targetCache.ContainsKey(mapId))
                    targetCache[mapId] = new Dictionary<string, List<TTarget>>();

                // Make sure the module cache exists
                if (!targetCache[mapId].ContainsKey(moduleId))
                    targetCache[mapId][moduleId] = new List<TTarget>();

                // Force update this module's cache directly
                List<TTarget> targets = targetCache[mapId][moduleId];
                targets.Clear();
                module.UpdateCache(pawn.Map, targets);

                // Check if we now have targets
                hasValidCache = targets.Count > 0;

                if (hasValidCache)
                    Utility_DebugManager.LogNormal($"Force-updated cache for module {moduleId}, found {targets.Count} targets");
                else
                    Utility_DebugManager.LogWarning($"Force-updated cache for module {moduleId}, still no targets found");
            }

            // Skip if no targets cached for this module
            if (!targetCache.ContainsKey(mapId) ||
                !targetCache[mapId].ContainsKey(moduleId) ||
                targetCache[mapId][moduleId].Count == 0)
                return null;

            // Create distance buckets for efficient target selection
            var buckets = CreateDistanceBuckets(
                pawn,
                targetCache[mapId][moduleId],
                t => (t.Position - pawn.Position).LengthHorizontalSquared,
                distanceThresholds);

            // Get reachability cache dictionary
            Dictionary<TTarget, bool> reachDict = null;
            if (reachabilityCache.ContainsKey(mapId))
                reachDict = reachabilityCache[mapId];
            else
            {
                reachDict = new Dictionary<TTarget, bool>();
                reachabilityCache[mapId] = reachDict;
            }

            // Find first valid target using bucketing
            TTarget target = FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (t, p) => module.ValidateJob(t, p),
                reachDict);

            // Create job if target found
            if (target != null)
            {
                try
                {
                    return module.CreateJob(pawn, target);
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"Error creating job from module {moduleId}: {ex}");
                }
            }

            return null;
        }

        // Add this method to clear the cache when work settings change
        public static void ClearDisabledWorkTypeCache(Pawn pawn)
        {
            if (pawn != null)
            {
                _disabledWorkTypeCache.Remove(pawn.thingIDNumber);
                Utility_DebugManager.LogNormal($"Cleared disabled work type cache for {pawn.LabelShort}");
            }
        }

        /// <summary>
        /// Resets job module-specific caches
        /// </summary>
        public static void ResetModuleCaches()
        {
            // Reset the job module tracking systems
            JobModuleCore.ResetAllTargetCaches();
            JobModuleCore.ResetLastSuccessfulModuleCache();

            // Clear bucket cache
            _bucketCacheLastTick.Clear();
            _bucketHashCache.Clear(); // Clear hash cache
            _cachedBuckets.Clear();   // Clear cached buckets
            _disabledWorkTypeCache.Clear();

            Utility_DebugManager.LogNormal("Cleared all JobGiverManager caches");
        }

        /// <summary>
        /// Checks if a pawn is currently doing a meaningful job
        /// (not idle, not wandering, not satisfying needs)
        /// </summary>
        /// <param name="pawn">The pawn to check</param>
        /// <returns>True if the pawn is actively working</returns>
        public static bool IsPawnActivelyWorking(Pawn pawn)
        {
            // Basic checks
            if (pawn == null || pawn.Dead || !pawn.Spawned)
                return false;

            // Check if pawn has a current job
            if (pawn.CurJob == null)
                return false;

            // Check if pawn is drafted (controlled manually)
            if (pawn.Drafted)
                return false;

            // Check for common non-work job defs
            JobDef currentJobDef = pawn.CurJob.def;

            // Skip basic needs and idle behaviors
            if (currentJobDef == JobDefOf.Wait_Wander ||
                currentJobDef == JobDefOf.GotoWander ||
                currentJobDef == JobDefOf.Wait ||
                currentJobDef == JobDefOf.Wait_Downed ||
                currentJobDef == JobDefOf.Wait_SafeTemperature ||
                currentJobDef == JobDefOf.Wait_Wander ||
                currentJobDef == JobDefOf.SocialFight ||
                currentJobDef == JobDefOf.FleeAndCower ||
                currentJobDef == JobDefOf.Flee ||
                currentJobDef == JobDefOf.GotoSafeTemperature ||
                currentJobDef == JobDefOf.Vomit ||
                currentJobDef == JobDefOf.Wait_AsleepDormancy ||
                currentJobDef == JobDefOf.Wait_Asleep)
                return false;

            // Skip needs satisfaction jobs
            if (currentJobDef == JobDefOf.Ingest ||
                currentJobDef == JobDefOf.LayDown ||
                //currentJobDef == JobDefOf.UseThing ||
                currentJobDef == JobDefOf.UseCommsConsole)
                return false;

            // FIXED LINE: Removed DesiredJobEndMode which doesn't exist
            // Only check if the job has already ended
            if (pawn.jobs?.curDriver?.ended == true || pawn.jobs?.jobQueue?.Count == 0)
                return false;

            // If none of the above conditions are met, the pawn is actively working
            return true;
        }

    }
}