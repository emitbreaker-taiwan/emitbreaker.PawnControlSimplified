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
            if (!IsEligibleForSpecializedJobGiver(pawn, workTag))
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
        public static bool IsEligibleForSpecializedJobGiver(Pawn pawn, string workTag)
        {
            if (pawn == null || !pawn.Spawned || pawn.Dead)
                return false;
            
            // Using ThinkTreeManager for consistent tagging across codebase
            if (!Utility_ThinkTreeManager.HasAllowWorkTag(pawn.def))
                return false;
            
            // Check for specific work tag - must be explicitly enabled for this pawn
            if (!string.IsNullOrEmpty(workTag) && !Utility_TagManager.WorkEnabled(pawn.def, workTag))
                return false;
            
            // Skip pawns without mod extension - let vanilla handle them
            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null)
                return false;

            // CRITICAL FIX: Check actual work settings for humanlike and modded pawns with workSettings
            if (!string.IsNullOrEmpty(workTag) && pawn.workSettings != null)
            {
                // Try to find the corresponding WorkTypeDef based on tag name
                WorkTypeDef workTypeDef = null;

                // First try exact match
                workTypeDef = DefDatabase<WorkTypeDef>.GetNamedSilentFail(workTag);

                // If that fails, try case-insensitive match
                if (workTypeDef == null)
                {
                    foreach (WorkTypeDef def in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                    {
                        if (def.defName.EqualsIgnoreCase(workTag))
                        {
                            workTypeDef = def;
                            break;
                        }
                    }
                }

                // If work type exists and is disabled in pawn's settings, don't give job
                if (workTypeDef != null && !pawn.workSettings.WorkIsActive(workTypeDef))
                {
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} has work type {workTypeDef.defName} disabled in work settings, skipping job");
                    return false;
                }
            }

            return true;
        }
        
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
            if (!IsEligibleForSpecializedJobGiver(pawn, workTypeTag))
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
        /// Creates a job for warden to interact with a prisoner
        /// </summary>
        public static Job TryCreatePrisonerInteractionJob(
            Pawn warden,
            Dictionary<int, List<Pawn>> prisonerCache,
            Dictionary<int, Dictionary<Pawn, bool>> reachabilityCache,
            Func<Pawn, Pawn, bool> validator,
            Func<Pawn, Pawn, Job> jobCreator,
            float[] distanceThresholds = null)
        {
            if (warden?.Map == null || warden.Faction == null) return null;

            int mapId = warden.Map.uniqueID;
            if (!prisonerCache.ContainsKey(mapId) || prisonerCache[mapId].Count == 0)
                return null;

            // Use distance bucketing
            var buckets = CreateDistanceBuckets(
                warden,
                prisonerCache[mapId],
                prisoner => (prisoner.Position - warden.Position).LengthHorizontalSquared,
                distanceThresholds ?? new float[] { 100f, 400f, 900f }
            );

            // Find best prisoner to interact with
            Pawn targetPrisoner = FindFirstValidTargetInBuckets(
                buckets,
                warden,
                (prisoner, p) => {
                    // Base validation checks
                    if (!IsValidFactionInteraction(prisoner, p, requiresDesignator: false))
                        return false;

                    if (prisoner?.guest == null || !prisoner.IsPrisoner)
                        return false;

                    // Custom validation from caller
                    return validator(prisoner, p);
                },
                reachabilityCache
            );

            // Create job if we found a valid target
            if (targetPrisoner != null)
            {
                return jobCreator(warden, targetPrisoner);
            }

            return null;
        }

        /// <summary>
        /// Updates a cache of prisoners matching specific criteria
        /// </summary>
        public static void UpdatePrisonerCache(
            Map map,
            ref int lastUpdateTick,
            int updateInterval,
            Dictionary<int, List<Pawn>> prisonerCache,
            Dictionary<int, Dictionary<Pawn, bool>> reachabilityCache,
            Func<Pawn, bool> prisonerFilter)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > lastUpdateTick + updateInterval ||
                !prisonerCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (prisonerCache.ContainsKey(mapId))
                    prisonerCache[mapId].Clear();
                else
                    prisonerCache[mapId] = new List<Pawn>();

                // Clear reachability cache too
                if (reachabilityCache.ContainsKey(mapId))
                    reachabilityCache[mapId].Clear();
                else
                    reachabilityCache[mapId] = new Dictionary<Pawn, bool>();

                // Find all matching prisoners using the provided filter
                foreach (Pawn prisoner in map.mapPawns.PrisonersOfColonySpawned)
                {
                    if (prisoner == null || prisoner.Destroyed || !prisoner.Spawned)
                        continue;

                    if (prisonerFilter(prisoner))
                    {
                        prisonerCache[mapId].Add(prisoner);
                    }
                }

                lastUpdateTick = currentTick;
            }
        }

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
    }
}