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
    /// Common base class for all crafting-related job givers.
    /// Handles bill processing at workbenches with efficient caching.
    /// </summary>
    public abstract class JobGiver_Crafting_PawnControl : JobGiver_PawnControl
    {
        #region Configuration

        /// <summary>
        /// All crafting job givers use this work tag
        /// </summary>
        public override string WorkTag => "Crafting";

        /// <summary>
        /// Human-readable name for debug logging
        /// </summary>
        protected override string DebugName => "Crafting";

        /// <summary>
        /// Whether this job giver requires a designator to operate
        /// </summary>
        protected override bool RequiresDesignator => false;

        /// <summary>
        /// Whether this job giver requires zone or area designation
        /// </summary>
        protected override bool RequiresMapZoneorArea => true;

        /// <summary>
        /// Whether this job giver requires player faction specifically
        /// </summary>
        protected override bool RequiresPlayerFaction => true;

        /// <summary>
        /// Whether this crafting job requires specific tag for non-humanlike pawns
        /// </summary>
        public override PawnEnumTags RequiredTag => PawnEnumTags.AllowWork_Crafting;

        /// <summary>
        /// Most crafting jobs have no specific designation
        /// </summary>
        protected override DesignationDef TargetDesignation => null;

        /// <summary>
        /// Standard job def for crafting jobs
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.DoBill;

        /// <summary>
        /// Update interval - bills don't change that often
        /// </summary>
        protected override int CacheUpdateInterval => 180;

        /// <summary>
        /// Default distance thresholds for workbenches (15, 30, 60 tiles)
        /// </summary>
        protected virtual float[] DistanceThresholds => new float[] { 225f, 900f, 3600f };

        /// <summary>
        /// How often to recheck a failed bill search in ticks
        /// </summary>
        protected static readonly IntRange ReCheckFailedBillTicksRange = new IntRange(500, 600);

        /// <summary>
        /// The fixed bill giver defs this job giver can work with, if any
        /// </summary>
        protected virtual List<ThingDef> FixedBillGiverDefs => null;

        /// <summary>
        /// Should this job check for pawns as bill givers?
        /// </summary>
        protected virtual bool BillGiversAllHumanlikes => false;

        /// <summary>
        /// Should this job check for mechanoids as bill givers?
        /// </summary>
        protected virtual bool BillGiversAllMechanoids => false;

        /// <summary>
        /// Should this job check for animals as bill givers?
        /// </summary>
        protected virtual bool BillGiversAllAnimals => false;

        /// <summary>
        /// Should this job check for humanlike corpses as bill givers?
        /// </summary>
        protected virtual bool BillGiversAllHumanlikesCorpses => false;

        /// <summary>
        /// Should this job check for mechanoid corpses as bill givers?
        /// </summary>
        protected virtual bool BillGiversAllMechanoidsCorpses => false;

        /// <summary>
        /// Should this job check for animal corpses as bill givers?
        /// </summary>
        protected virtual bool BillGiversAllAnimalsCorpses => false;

        #endregion

        #region Caching

        // Cache for ingredient selection
        protected readonly Dictionary<Bill, Dictionary<Pawn, int>> _billFailTicksCache = new Dictionary<Bill, Dictionary<Pawn, int>>();
        protected readonly List<IngredientCount> _missingIngredients = new List<IngredientCount>();
        protected readonly List<ThingCount> _chosenIngThings = new List<ThingCount>();
        protected readonly DefCountList _availableCounts = new DefCountList();

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_Crafting_PawnControl() : base()
        {
            InitializeCache<Thing>();
        }

        #endregion

        #region Faction Validation

        /// <summary>
        /// Common implementation for ShouldSkip that enforces faction requirements
        /// </summary>
        public override bool ShouldSkip(Pawn pawn)
        {
            if (base.ShouldSkip(pawn))
                return true;

            // Check faction validation
            if (!IsValidFactionForCrafting(pawn))
                return true;

            // Check capability validation for non-humanlike pawns
            if (ShouldSkipByCapability(pawn))
                return true;

            return false;
        }

        /// <summary>
        /// Determines if a pawn's faction is allowed to perform crafting work
        /// </summary>
        protected virtual bool IsValidFactionForCrafting(Pawn pawn)
        {
            // Only player pawns and player's slaves can process bills by default
            return pawn != null && (pawn.Faction == Faction.OfPlayer ||
                   (pawn.IsSlave && pawn.HostFaction == Faction.OfPlayer));
        }

        /// <summary>
        /// Checks if a pawn should skip this job giver due to capability restrictions
        /// </summary>
        protected virtual bool ShouldSkipByCapability(Pawn pawn)
        {
            // If it's a humanlike pawn, no need for additional capability checks
            if (pawn.RaceProps.Humanlike)
                return false;

            // For non-humanlike pawns, check if they have the required capabilities
            // or if they're allowed to ignore capability restrictions
            NonHumanlikePawnControlExtension modExtension =
                pawn.def.GetModExtension<NonHumanlikePawnControlExtension>();

            if (modExtension != null && modExtension.ignoreCapability)
                return false; // Skip capability check if extension says to ignore

            // Otherwise apply whatever capability checks are needed
            return !HasRequiredCapabilities(pawn);
        }

        /// <summary>
        /// Checks if a non-humanlike pawn has the required capabilities for this job giver
        /// </summary>
        protected override bool HasRequiredCapabilities(Pawn pawn)
        {
            if (pawn == null || pawn.RaceProps.Humanlike)
                return true;

            // Check for crafting capability tag in mod extension
            NonHumanlikePawnControlExtension modExtension = 
                pawn.def.GetModExtension<NonHumanlikePawnControlExtension>();

            if (modExtension == null)
                return false;

            return modExtension.tags.Contains(RequiredTag.ToStringSafe());
        }

        #endregion

        #region Core flow

        /// <summary>
        /// Process the cached targets to create a job
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            if (pawn == null || targets == null || targets.Count == 0)
                return null;

            // Filter bill givers to those that are valid for this pawn
            var validBillGivers = targets
                .Where(thing => IsValidBillGiver(thing, pawn, forced))
                .ToList();

            if (validBillGivers.Count == 0)
                return null;

            // Find the best bill giver
            return FindBestBillGiverJob(pawn, validBillGivers, forced);
        }

        /// <summary>
        /// Finds the best bill giver and creates a job
        /// </summary>
        protected virtual Job FindBestBillGiverJob(Pawn pawn, List<Thing> validBillGivers, bool forced)
        {
            // Use distance bucketing to find the closest valid bill giver
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                validBillGivers,
                (thing) => (thing.Position - pawn.Position).LengthHorizontalSquared,
                DistanceThresholds);

            // Find bill giver with work
            Thing targetBillGiver = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (thing, worker) => HasWorkForPawn(thing, worker, forced));

            if (targetBillGiver == null)
                return null;

            // Create and return a job for the bill giver
            return StartOrResumeBillJob(pawn, targetBillGiver as IBillGiver, forced);
        }

        #endregion

        #region Target selection

        /// <summary>
        /// Gets all bill givers on the map
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // This implementation mirrors WorkGiver_DoBill.PotentialWorkThingRequest
            if (map == null)
                yield break;

            // First check fixed bill giver defs
            if (FixedBillGiverDefs != null && FixedBillGiverDefs.Count > 0)
            {
                foreach (ThingDef def in FixedBillGiverDefs)
                {
                    foreach (Thing thing in map.listerThings.ThingsOfDef(def))
                    {
                        if (thing is IBillGiver && ThingIsUsableBillGiver(thing))
                        {
                            yield return thing;
                        }
                    }
                }
            }
            else
            {
                // If no fixed defs, check all potential bill givers
                foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.PotentialBillGiver))
                {
                    if (thing is IBillGiver && ThingIsUsableBillGiver(thing))
                    {
                        yield return thing;
                    }
                }
            }
        }

        /// <summary>
        /// Checks if a thing is a valid bill giver for this job giver
        /// </summary>
        protected virtual bool ThingIsUsableBillGiver(Thing thing)
        {
            Pawn pawn = thing as Pawn;
            Corpse corpse = thing as Corpse;
            Pawn innerPawn = null;

            if (corpse != null)
            {
                innerPawn = corpse.InnerPawn;
            }

            if (FixedBillGiverDefs != null && FixedBillGiverDefs.Contains(thing.def))
            {
                return true;
            }

            if (pawn != null)
            {
                if (BillGiversAllHumanlikes && pawn.RaceProps.Humanlike)
                {
                    return true;
                }

                if (BillGiversAllMechanoids && pawn.RaceProps.IsMechanoid)
                {
                    return true;
                }

                if (BillGiversAllAnimals && pawn.RaceProps.Animal)
                {
                    return true;
                }
            }

            if (corpse != null && innerPawn != null)
            {
                if (BillGiversAllHumanlikesCorpses && innerPawn.RaceProps.Humanlike)
                {
                    return true;
                }

                if (BillGiversAllMechanoidsCorpses && innerPawn.RaceProps.IsMechanoid)
                {
                    return true;
                }

                if (BillGiversAllAnimalsCorpses && innerPawn.RaceProps.Animal)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks if a bill giver is valid for a specific pawn
        /// </summary>
        protected virtual bool IsValidBillGiver(Thing thing, Pawn pawn, bool forced = false)
        {
            // Basic validity checks
            if (!(thing is IBillGiver billGiver) ||
                !ThingIsUsableBillGiver(thing) ||
                !billGiver.BillStack.AnyShouldDoNow ||
                !billGiver.UsableForBillsAfterFueling() ||
                !pawn.CanReserve(thing, 1, -1, null, forced) ||
                thing.IsBurning() ||
                thing.IsForbidden(pawn))
            {
                return false;
            }

            // Check interaction cell
            if (thing.def.hasInteractionCell && !pawn.CanReserveSittableOrSpot_NewTemp(thing.InteractionCell, thing, forced))
            {
                return false;
            }

            // Check if it needs refueling first
            CompRefuelable refuelable = thing.TryGetComp<CompRefuelable>();
            if (refuelable != null && !refuelable.HasFuel)
            {
                return RefuelWorkGiverUtility.CanRefuel(pawn, thing, forced);
            }

            return true;
        }

        /// <summary>
        /// Checks if a bill giver has valid work for a specific pawn
        /// </summary>
        protected virtual bool HasWorkForPawn(Thing thing, Pawn pawn, bool forced = false)
        {
            if (!(thing is IBillGiver billGiver))
                return false;

            // Remove bills that can't be completed
            billGiver.BillStack.RemoveIncompletableBills();

            // Check if there's a valid job to do
            return StartOrResumeBillJob(pawn, billGiver, forced) != null;
        }

        #endregion

        #region Bill processing

        /// <summary>
        /// Starts or resumes a bill job for a pawn at a bill giver
        /// </summary>
        protected virtual Job StartOrResumeBillJob(Pawn pawn, IBillGiver giver, bool forced = false)
        {
            bool isFloatMenuCheck = FloatMenuMakerMap.makingFor == pawn;

            for (int i = 0; i < giver.BillStack.Count; i++)
            {
                Bill bill = giver.BillStack[i];

                // Skip bills that don't match our work type or aren't ready
                if ((bill.recipe.requiredGiverWorkType != null && bill.recipe.requiredGiverWorkType != Utility_WorkTypeManager.Named(WorkTag)) ||
                    (Find.TickManager.TicksGame <= GetNextBillStartTick(bill, pawn) && !isFloatMenuCheck) ||
                    !bill.ShouldDoNow() ||
                    !bill.PawnAllowedToStartAnew(pawn))
                {
                    continue;
                }

                // Check skill requirements - respect ignoreCapability from mod extension
                bool skipSkillCheck = false;
                if (!pawn.RaceProps.Humanlike)
                {
                    NonHumanlikePawnControlExtension modExt = pawn.def.GetModExtension<NonHumanlikePawnControlExtension>();
                    if (modExt != null && modExt.ignoreCapability)
                    {
                        skipSkillCheck = true;
                    }
                }

                if (!skipSkillCheck)
                {
                    SkillRequirement failedReq = bill.recipe.FirstSkillRequirementPawnDoesntSatisfy(pawn);
                    if (failedReq != null)
                    {
                        if (isFloatMenuCheck)
                        {
                            JobFailReason.Is("UnderRequiredSkill".Translate(failedReq.minLevel), bill.Label);
                        }
                        continue;
                    }
                }

                // Handle medical bills
                if (bill is Bill_Medical billMedical)
                {
                    if (billMedical.IsSurgeryViolationOnExtraFactionMember(pawn))
                    {
                        if (isFloatMenuCheck)
                        {
                            JobFailReason.Is("SurgeryViolationFellowFactionMember".Translate());
                        }
                        continue;
                    }

                    if (!pawn.CanReserve(billMedical.GiverPawn, 1, -1, null, forced))
                    {
                        if (isFloatMenuCheck)
                        {
                            Pawn reserver = pawn.MapHeld.reservationManager.FirstRespectedReserver(billMedical.GiverPawn, pawn);
                            JobFailReason.Is("IsReservedBy".Translate(billMedical.GiverPawn.LabelShort, reserver.LabelShort));
                        }
                        continue;
                    }
                }

                // Handle unfinished things
                if (bill is Bill_ProductionWithUft billProductionWithUft)
                {
                    if (billProductionWithUft.BoundUft != null)
                    {
                        if (billProductionWithUft.BoundWorker == pawn &&
                            pawn.CanReserveAndReach(billProductionWithUft.BoundUft, PathEndMode.Touch, Danger.Deadly) &&
                            !billProductionWithUft.BoundUft.IsForbidden(pawn))
                        {
                            return FinishUftJob(pawn, billProductionWithUft.BoundUft, billProductionWithUft);
                        }
                        continue;
                    }

                    UnfinishedThing unfinishedThing = ClosestUnfinishedThingForBill(pawn, billProductionWithUft);
                    if (unfinishedThing != null)
                    {
                        return FinishUftJob(pawn, unfinishedThing, billProductionWithUft);
                    }
                }

                // Handle autonomous bills
                if (bill is Bill_Autonomous billAutonomous && billAutonomous.State != FormingState.Gathering)
                {
                    return WorkOnFormedBill((Thing)giver, billAutonomous);
                }

                // Try to find ingredients for the bill
                _missingIngredients.Clear();
                if (!TryFindBestBillIngredients(bill, pawn, (Thing)giver, _chosenIngThings, isFloatMenuCheck ? _missingIngredients : null))
                {
                    if (!isFloatMenuCheck)
                    {
                        // Set next time to check this bill
                        SetNextBillStartTick(bill, pawn, Find.TickManager.TicksGame + ReCheckFailedBillTicksRange.RandomInRange);
                    }
                    else if (isFloatMenuCheck)
                    {
                        // Show reason for menu
                        if (_missingIngredients.Count > 0)
                        {
                            if (IsMedicineRestrictionProblem(giver, bill, _missingIngredients))
                            {
                                JobFailReason.Is("NoMedicineMatchingCategory".Translate(
                                    GetMedicalCareCategory((Thing)giver).GetLabel().Named("CATEGORY")), bill.Label);
                            }
                            else
                            {
                                string missingItemsList = _missingIngredients
                                    .Select(missing => missing.Summary)
                                    .ToCommaList();

                                JobFailReason.Is("MissingMaterials".Translate(missingItemsList), bill.Label);
                            }
                        }
                    }

                    _chosenIngThings.Clear();
                    continue;
                }

                // Add any unique required ingredients for medical bills
                Bill_Medical billMedical2 = bill as Bill_Medical;
                if (billMedical2?.uniqueRequiredIngredients?.NullOrEmpty() == false)
                {
                    bool missingUniqueIngredient = false;
                    foreach (Thing uniqueRequiredIngredient in billMedical2.uniqueRequiredIngredients)
                    {
                        if (uniqueRequiredIngredient.IsForbidden(pawn) ||
                            !pawn.CanReserveAndReach(uniqueRequiredIngredient, PathEndMode.OnCell, Danger.Deadly))
                        {
                            missingUniqueIngredient = true;
                            if (isFloatMenuCheck)
                            {
                                JobFailReason.Is("MissingMaterials".Translate(uniqueRequiredIngredient.Label), bill.Label);
                            }
                            break;
                        }

                        _chosenIngThings.Add(new ThingCount(uniqueRequiredIngredient, 1));
                    }

                    if (missingUniqueIngredient)
                    {
                        _chosenIngThings.Clear();
                        continue;
                    }
                }

                // Create the job
                Job haulOffJob;
                Job result = TryStartNewDoBillJob(pawn, bill, giver, _chosenIngThings, out haulOffJob);
                _chosenIngThings.Clear();

                if (haulOffJob != null)
                {
                    return haulOffJob;
                }

                if (result != null)
                {
                    return result;
                }
            }

            _chosenIngThings.Clear();
            return null;
        }

        /// <summary>
        /// Creates a job to finish an unfinished thing
        /// </summary>
        protected virtual Job FinishUftJob(Pawn pawn, UnfinishedThing uft, Bill_ProductionWithUft bill)
        {
            if (uft.Creator != pawn)
            {
                Utility_DebugManager.LogError($"Tried to get FinishUftJob for {pawn} finishing {uft} but its creator is {uft.Creator}");
                return null;
            }

            Job haulJob = WorkGiverUtility.HaulStuffOffBillGiverJob(pawn, bill.billStack.billGiver, uft);
            if (haulJob != null && haulJob.targetA.Thing != uft)
            {
                return haulJob;
            }

            Job job = JobMaker.MakeJob(JobDefOf.DoBill, (Thing)bill.billStack.billGiver);
            job.bill = bill;
            job.targetQueueB = new List<LocalTargetInfo> { uft };
            job.countQueue = new List<int> { 1 };
            job.haulMode = HaulMode.ToCellNonStorage;
            return job;
        }

        /// <summary>
        /// Creates a job to work on a formed bill
        /// </summary>
        protected virtual Job WorkOnFormedBill(Thing giver, Bill_Autonomous bill)
        {
            Job job = JobMaker.MakeJob(JobDefOf.DoBill, giver);
            job.bill = bill;
            return job;
        }

        /// <summary>
        /// Creates a job to work on a bill with the chosen ingredients
        /// </summary>
        protected virtual Job TryStartNewDoBillJob(Pawn pawn, Bill bill, IBillGiver giver, List<ThingCount> chosenIngredients, out Job haulOffJob)
        {
            haulOffJob = WorkGiverUtility.HaulStuffOffBillGiverJob(pawn, giver, null);
            if (haulOffJob != null)
            {
                return null; // Need to haul things off first
            }

            Job job = JobMaker.MakeJob(JobDefOf.DoBill, (Thing)giver);
            job.targetQueueB = new List<LocalTargetInfo>(chosenIngredients.Count);
            job.countQueue = new List<int>(chosenIngredients.Count);

            for (int i = 0; i < chosenIngredients.Count; i++)
            {
                job.targetQueueB.Add(chosenIngredients[i].Thing);
                job.countQueue.Add(chosenIngredients[i].Count);
            }

            if (bill.xenogerm != null)
            {
                job.targetQueueB.Add(bill.xenogerm);
                job.countQueue.Add(1);
            }

            job.haulMode = HaulMode.ToCellNonStorage;
            job.bill = bill;
            return job;
        }

        /// <summary>
        /// Finds the closest unfinished thing for a bill
        /// </summary>
        protected virtual UnfinishedThing ClosestUnfinishedThingForBill(Pawn pawn, Bill_ProductionWithUft bill)
        {
            Predicate<Thing> validator = (Thing t) =>
                !t.IsForbidden(pawn) &&
                t is UnfinishedThing unfinished &&
                unfinished.Recipe == bill.recipe &&
                unfinished.Creator == pawn &&
                unfinished.ingredients.TrueForAll(x => bill.IsFixedOrAllowedIngredient(x.def)) &&
                pawn.CanReserve(t);

            return (UnfinishedThing)GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForDef(bill.recipe.unfinishedThingDef),
                PathEndMode.InteractionCell,
                TraverseParms.For(pawn, pawn.NormalMaxDanger()),
                9999f,
                validator);
        }

        #endregion

        #region Ingredient selection

        /// <summary>
        /// Attempts to find the best ingredients for a bill
        /// </summary>
        protected virtual bool TryFindBestBillIngredients(Bill bill, Pawn pawn, Thing billGiver, List<ThingCount> chosen, List<IngredientCount> missingIngredients)
        {
            chosen.Clear();
            missingIngredients?.Clear();

            if (bill.recipe.ingredients.Count == 0)
            {
                return true;
            }

            IntVec3 rootCell = GetBillGiverRootCell(billGiver, pawn);
            Region rootRegion = rootCell.GetRegion(pawn.Map);
            if (rootRegion == null)
            {
                return false;
            }

            // Predicate for usable ingredients
            Predicate<Thing> baseValidator = thing =>
                thing.Spawned &&
                IsUsableIngredient(thing, bill) &&
                (float)(thing.Position - billGiver.Position).LengthHorizontalSquared < bill.ingredientSearchRadius * bill.ingredientSearchRadius &&
                !thing.IsForbidden(pawn) &&
                pawn.CanReserve(thing);

            // Special handling for medical bills with pawns
            bool billGiverIsPawn = billGiver is Pawn;
            List<Thing> relevantThings = new List<Thing>();

            // For medical operations on pawns, prioritize medicine
            if (billGiverIsPawn)
            {
                // Special medicine handling
                MedicalCareCategory medicalCare = GetMedicalCareCategory(billGiver);
                List<Thing> availableMedicine = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Medicine)
                    .Where(med =>
                        medicalCare.AllowsMedicine(med.def) &&
                        baseValidator(med) &&
                        pawn.CanReach(med, PathEndMode.OnCell, Danger.Deadly))
                    .OrderByDescending(med => med.GetStatValue(StatDefOf.MedicalPotency))
                    .ThenBy(med => med.Position.DistanceToSquared(billGiver.Position))
                    .ToList();

                relevantThings.AddRange(availableMedicine);

                if (bill.recipe.ingredients.Any(ing => ing.filter.Allows(ThingDefOf.MedicineIndustrial)) &&
                    TryFindBestBillIngredientsInSet(availableMedicine, bill, chosen, rootCell, true, missingIngredients))
                {
                    return true;
                }
            }

            // For autonomous work tables
            if (billGiver is Building_WorkTableAutonomous workTable && workTable.innerContainer.Count > 0)
            {
                relevantThings.AddRange(workTable.innerContainer);
                if (TryFindBestBillIngredientsInSet(relevantThings, bill, chosen, rootCell, false, missingIngredients))
                {
                    return true;
                }
            }

            // Need to search for ingredients
            TraverseParms traverseParams = TraverseParms.For(pawn);
            HashSet<Thing> processedThings = new HashSet<Thing>(relevantThings);
            List<Thing> newRelevantThings = new List<Thing>();
            RegionEntryPredicate regionEntryPredicate;

            // Set up region search conditions
            if (Math.Abs(999f - bill.ingredientSearchRadius) >= 1f)
            {
                float radiusSq = bill.ingredientSearchRadius * bill.ingredientSearchRadius;
                regionEntryPredicate = (Region from, Region r) =>
                {
                    if (!r.Allows(traverseParams, isDestination: false))
                        return false;

                    CellRect extentsClose = r.extentsClose;
                    int dx = Math.Abs(billGiver.Position.x - Math.Max(extentsClose.minX, Math.Min(billGiver.Position.x, extentsClose.maxX)));
                    int dz = Math.Abs(billGiver.Position.z - Math.Max(extentsClose.minZ, Math.Min(billGiver.Position.z, extentsClose.maxZ)));

                    return (float)(dx * dx + dz * dz) <= radiusSq;
                };
            }
            else
            {
                regionEntryPredicate = (Region from, Region r) => r.Allows(traverseParams, isDestination: false);
            }

            // Search for ingredients by traversing regions
            int adjacentRegionsAvailable = rootRegion.Neighbors.Count(region => regionEntryPredicate(rootRegion, region));
            int regionsProcessed = 0;
            bool foundAllIngredients = false;

            // Always check the starting set
            if (TryFindBestBillIngredientsInSet(relevantThings, bill, chosen, rootCell, false, missingIngredients))
            {
                foundAllIngredients = true;
            }

            // Process regions to find ingredients
            RegionProcessor regionProcessor = delegate (Region r)
            {
                // Find relevant things in this region
                List<Thing> regionThings = r.ListerThings.ThingsMatching(ThingRequest.ForGroup(ThingRequestGroup.HaulableEver));
                foreach (Thing thing in regionThings)
                {
                    if (!processedThings.Contains(thing) &&
                        ReachabilityWithinRegion.ThingFromRegionListerReachable(thing, r, PathEndMode.ClosestTouch, pawn) &&
                        baseValidator(thing) &&
                        !(thing.def.IsMedicine && billGiverIsPawn))
                    {
                        newRelevantThings.Add(thing);
                        processedThings.Add(thing);
                    }
                }

                regionsProcessed++;

                // Check if we've found enough ingredients after processing some regions
                if (newRelevantThings.Count > 0 && regionsProcessed > adjacentRegionsAvailable)
                {
                    relevantThings.AddRange(newRelevantThings);
                    newRelevantThings.Clear();

                    if (TryFindBestBillIngredientsInSet(relevantThings, bill, chosen, rootCell, false, missingIngredients))
                    {
                        foundAllIngredients = true;
                        return true; // Stop traversal
                    }
                }

                return false;
            };

            // Traverse regions looking for ingredients
            RegionTraverser.BreadthFirstTraverse(rootRegion, regionEntryPredicate, regionProcessor, 99999);
            return foundAllIngredients;
        }

        /// <summary>
        /// Attempts to find the best ingredients for a bill from a set of things
        /// </summary>
        protected virtual bool TryFindBestBillIngredientsInSet(
            List<Thing> availableThings,
            Bill bill,
            List<ThingCount> chosen,
            IntVec3 rootCell,
            bool alreadySorted,
            List<IngredientCount> missingIngredients)
        {
            if (bill.recipe.allowMixingIngredients)
            {
                return TryFindBestBillIngredientsInSet_AllowMix(
                    availableThings, bill, chosen, rootCell, missingIngredients);
            }

            return TryFindBestBillIngredientsInSet_NoMix(
                availableThings, bill, chosen, rootCell, alreadySorted, missingIngredients);
        }

        /// <summary>
        /// Attempts to find the best ingredients for a bill from a set of things, without mixing ingredients
        /// </summary>
        protected virtual bool TryFindBestBillIngredientsInSet_NoMix(
            List<Thing> availableThings,
            Bill bill,
            List<ThingCount> chosen,
            IntVec3 rootCell,
            bool alreadySorted,
            List<IngredientCount> missingIngredients)
        {
            // Sort by distance if needed
            if (!alreadySorted)
            {
                availableThings.Sort((t1, t2) =>
                    (t1.Position - rootCell).LengthHorizontalSquared.CompareTo((t2.Position - rootCell).LengthHorizontalSquared));
            }

            // Clear prior results
            chosen.Clear();
            _availableCounts.Clear();
            missingIngredients?.Clear();

            // Generate count list from available things
            _availableCounts.GenerateFrom(availableThings);

            // Try to satisfy each ingredient
            foreach (IngredientCount ingredientNeeded in bill.recipe.ingredients)
            {
                bool foundIngredient = false;

                // Look for matching ingredients
                for (int i = 0; i < _availableCounts.Count; i++)
                {
                    ThingDef def = _availableCounts.GetDef(i);
                    float countAvailable = _availableCounts.GetCount(i);
                    float countNeeded = bill.recipe.ignoreIngredientCountTakeEntireStacks ?
                        1 : ingredientNeeded.CountRequiredOfFor(def, bill.recipe, bill);

                    // Skip if not enough or not allowed
                    if ((countNeeded > countAvailable && !bill.recipe.ignoreIngredientCountTakeEntireStacks) ||
                        !ingredientNeeded.filter.Allows(def) ||
                        (!ingredientNeeded.IsFixedIngredient && !bill.ingredientFilter.Allows(def)))
                    {
                        continue;
                    }

                    // Find matching things
                    for (int j = 0; j < availableThings.Count; j++)
                    {
                        Thing thing = availableThings[j];
                        if (thing.def != def)
                            continue;

                        int numAlreadyChosen = ThingCountUtility.CountOf(chosen, thing);
                        int numAvailable = thing.stackCount - numAlreadyChosen;

                        if (numAvailable <= 0)
                            continue;

                        if (bill.recipe.ignoreIngredientCountTakeEntireStacks)
                        {
                            ThingCountUtility.AddToList(chosen, thing, numAvailable);
                            return true;
                        }

                        int numToTake = Mathf.Min(Mathf.FloorToInt(countNeeded), numAvailable);
                        ThingCountUtility.AddToList(chosen, thing, numToTake);
                        countNeeded -= numToTake;

                        if (countNeeded < 0.001f)
                        {
                            foundIngredient = true;
                            _availableCounts[def] -= countNeeded;
                            break;
                        }
                    }

                    if (foundIngredient)
                        break;
                }

                // If we couldn't find this ingredient, record it as missing
                if (!foundIngredient)
                {
                    if (missingIngredients == null)
                        return false;

                    missingIngredients.Add(ingredientNeeded);
                }
            }

            // Report if any ingredients are missing
            if (missingIngredients != null)
                return missingIngredients.Count == 0;

            return true;
        }

        /// <summary>
        /// Attempts to find the best ingredients for a bill from a set of things, allowing mixed ingredients
        /// </summary>
        protected virtual bool TryFindBestBillIngredientsInSet_AllowMix(
            List<Thing> availableThings,
            Bill bill,
            List<ThingCount> chosen,
            IntVec3 rootCell,
            List<IngredientCount> missingIngredients)
        {
            chosen.Clear();
            missingIngredients?.Clear();

            // Sort by value per unit and distance
            availableThings.SortBy(
                t => bill.recipe.IngredientValueGetter.ValuePerUnitOf(t.def),
                t => (t.Position - rootCell).LengthHorizontalSquared);

            // Process each ingredient
            foreach (IngredientCount ingredientNeeded in bill.recipe.ingredients)
            {
                float countNeeded = ingredientNeeded.GetBaseCount();

                // Try to satisfy with available things
                foreach (Thing thing in availableThings)
                {
                    if (!ingredientNeeded.filter.Allows(thing) ||
                        (!ingredientNeeded.IsFixedIngredient && !bill.ingredientFilter.Allows(thing)))
                        continue;

                    float valuePerUnit = bill.recipe.IngredientValueGetter.ValuePerUnitOf(thing.def);
                    int numToTake = Mathf.Min(Mathf.CeilToInt(countNeeded / valuePerUnit), thing.stackCount);

                    ThingCountUtility.AddToList(chosen, thing, numToTake);
                    countNeeded -= numToTake * valuePerUnit;

                    if (countNeeded <= 0.0001f)
                        break;
                }

                // If we couldn't satisfy this ingredient
                if (countNeeded > 0.0001f)
                {
                    if (missingIngredients == null)
                        return false;

                    missingIngredients.Add(ingredientNeeded);
                }
            }

            // Report if any ingredients are missing
            if (missingIngredients != null)
                return missingIngredients.Count == 0;

            return true;
        }

        /// <summary>
        /// Gets the bill giver root cell for pathfinding
        /// </summary>
        protected virtual IntVec3 GetBillGiverRootCell(Thing billGiver, Pawn forPawn)
        {
            if (billGiver is Building building)
            {
                if (building.def.hasInteractionCell)
                {
                    return building.InteractionCell;
                }

                Utility_DebugManager.LogError($"Tried to find bill ingredients for {billGiver} which has no interaction cell.");
                return forPawn.Position;
            }

            return billGiver.Position;
        }

        /// <summary>
        /// Gets the medical care category for a thing
        /// </summary>
        protected virtual MedicalCareCategory GetMedicalCareCategory(Thing billGiver)
        {
            if (billGiver is Pawn pawn && pawn.playerSettings != null)
            {
                return pawn.playerSettings.medCare;
            }

            return MedicalCareCategory.Best;
        }

        /// <summary>
        /// Checks if an ingredient is usable for a bill
        /// </summary>
        protected virtual bool IsUsableIngredient(Thing thing, Bill bill)
        {
            if (!bill.IsFixedOrAllowedIngredient(thing))
                return false;

            foreach (IngredientCount ingredient in bill.recipe.ingredients)
            {
                if (ingredient.filter.Allows(thing))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if there's a medicine restriction problem
        /// </summary>
        protected virtual bool IsMedicineRestrictionProblem(IBillGiver giver, Bill bill, List<IngredientCount> missingIngredients)
        {
            if (!(giver is Pawn pawn))
                return false;

            bool needsMedicine = missingIngredients.Any(i => i.filter.Allows(ThingDefOf.MedicineIndustrial));
            if (!needsMedicine)
                return false;

            MedicalCareCategory medicalCare = GetMedicalCareCategory(pawn);
            return !pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Medicine)
                .Any(med => IsUsableIngredient(med, bill) && medicalCare.AllowsMedicine(med.def));
        }

        /// <summary>
        /// Gets the next tick to start a bill for a pawn
        /// </summary>
        protected int GetNextBillStartTick(Bill bill, Pawn pawn)
        {
            if (!_billFailTicksCache.TryGetValue(bill, out Dictionary<Pawn, int> pawnTicks))
                return -999;

            if (!pawnTicks.TryGetValue(pawn, out int tick))
                return -999;

            return tick;
        }

        /// <summary>
        /// Sets the next tick to start a bill for a pawn
        /// </summary>
        protected void SetNextBillStartTick(Bill bill, Pawn pawn, int tick)
        {
            if (!_billFailTicksCache.TryGetValue(bill, out Dictionary<Pawn, int> pawnTicks))
            {
                pawnTicks = new Dictionary<Pawn, int>();
                _billFailTicksCache[bill] = pawnTicks;
            }

            pawnTicks[pawn] = tick;
        }

        #endregion

        #region Cache management

        /// <summary>
        /// Reset the cache - implements IResettableCache
        /// </summary>
        public override void Reset()
        {
            base.Reset();
            _billFailTicksCache.Clear();
        }

        /// <summary>
        /// Utility class for tracking available ingredients
        /// </summary>
        protected class DefCountList
        {
            private List<ThingDef> defs = new List<ThingDef>();
            private List<float> counts = new List<float>();

            public int Count => defs.Count;

            public float this[ThingDef def]
            {
                get
                {
                    int index = defs.IndexOf(def);
                    return index < 0 ? 0f : counts[index];
                }
                set
                {
                    int index = defs.IndexOf(def);
                    if (index < 0)
                    {
                        defs.Add(def);
                        counts.Add(value);
                    }
                    else
                    {
                        counts[index] = value;
                        CheckRemove(index);
                    }
                }
            }

            public float GetCount(int index) => counts[index];

            public void SetCount(int index, float value)
            {
                counts[index] = value;
                CheckRemove(index);
            }

            public ThingDef GetDef(int index) => defs[index];

            public void Clear()
            {
                defs.Clear();
                counts.Clear();
            }

            public void GenerateFrom(List<Thing> things)
            {
                Clear();
                foreach (Thing thing in things)
                {
                    this[thing.def] += thing.stackCount;
                }
            }

            private void CheckRemove(int index)
            {
                if (counts[index] == 0f)
                {
                    counts.RemoveAt(index);
                    defs.RemoveAt(index);
                }
            }
        }

        #endregion
    }
}