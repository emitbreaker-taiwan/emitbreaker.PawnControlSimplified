using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Abstract base class for animal interaction job modules.
    /// Adapts the functionality from RimWorld's WorkGiver_InteractAnimal.
    /// </summary>
    public abstract class JobModule_Handling_InteractAnimal : JobModule_Handling
    {
        // Translation strings from WorkGiver_InteractAnimal
        protected static string NoUsableFoodTrans;
        protected static string AnimalInteractedTooRecentlyTrans;
        protected static string CantInteractAnimalDownedTrans;
        protected static string CantInteractAnimalAsleepTrans;
        protected static string CantInteractAnimalBusyTrans;

        // Flag to determine if the animal interaction can happen while the animal is sleeping
        protected bool canInteractWhileSleeping;
        
        // Flag to determine if the animal interaction can happen while the animal is roaming
        protected bool canInteractWhileRoaming;

        /// <summary>
        /// Reset static translation strings
        /// </summary>
        public override void ResetStaticData()
        {
            base.ResetStaticData();
            
            NoUsableFoodTrans = "NoUsableFood".Translate();
            AnimalInteractedTooRecentlyTrans = "AnimalInteractedTooRecently".Translate();
            CantInteractAnimalDownedTrans = "CantInteractAnimalDowned".Translate();
            CantInteractAnimalAsleepTrans = "CantInteractAnimalAsleep".Translate();
            CantInteractAnimalBusyTrans = "CantInteractAnimalBusy".Translate();
        }

        /// <summary>
        /// Common validation logic for animal interaction jobs
        /// </summary>
        public override bool ValidateHandlingJob(Pawn animal, Pawn handler)
        {
            if (!IsValidHandlingTarget(animal, handler))
                return false;
                
            // Additional animal interaction-specific checks
            string jobFailReason;
            if (!CanInteractWithAnimal(handler, animal, out jobFailReason, forced: false, 
                canInteractWhileSleeping, ignoreSkillRequirements: false, canInteractWhileRoaming))
            {
                if (jobFailReason != null)
                {
                    JobFailReason.Is(jobFailReason);
                }
                return false;
            }
            
            return true;
        }

        /// <summary>
        /// Common CanInteractWithAnimal logic adapted from WorkGiver_InteractAnimal
        /// </summary>
        protected bool CanInteractWithAnimal(Pawn pawn, Pawn animal, out string jobFailReason, 
            bool forced, bool ignoreSkillRequirements = false)
        {
            return CanInteractWithAnimal(pawn, animal, out jobFailReason, forced, 
                canInteractWhileSleeping, ignoreSkillRequirements, canInteractWhileRoaming);
        }

        /// <summary>
        /// Static method adapted from WorkGiver_InteractAnimal for checking animal interaction validity
        /// </summary>
        public static bool CanInteractWithAnimal(Pawn pawn, Pawn animal, out string jobFailReason, 
            bool forced, bool canInteractWhileSleeping = false, bool ignoreSkillRequirements = false, 
            bool canInteractWhileRoaming = false)
        {
            jobFailReason = null;
            
            if (!pawn.CanReserve(animal, 1, -1, null, forced))
                return false;

            if (animal.Downed)
            {
                jobFailReason = CantInteractAnimalDownedTrans;
                return false;
            }

            if (!animal.Awake() && !canInteractWhileSleeping)
            {
                jobFailReason = CantInteractAnimalAsleepTrans;
                return false;
            }

            if (!animal.CanCasuallyInteractNow(twoWayInteraction: false, canInteractWhileSleeping, canInteractWhileRoaming))
            {
                jobFailReason = CantInteractAnimalBusyTrans;
                return false;
            }

            int minimumHandlingSkill = TrainableUtility.MinimumHandlingSkill(animal);
            if (!ignoreSkillRequirements && minimumHandlingSkill > pawn.skills.GetSkill(SkillDefOf.Animals).Level)
            {
                jobFailReason = "AnimalsSkillTooLow".Translate(minimumHandlingSkill);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Check if the handler has food usable for animal interaction
        /// </summary>
        protected bool HasFoodToInteractAnimal(Pawn pawn, Pawn animal)
        {
            ThingOwner<Thing> innerContainer = pawn.inventory.innerContainer;
            int fullPortionCount = 0;
            float requiredNutrition = JobDriver_InteractAnimal.RequiredNutritionPerFeed(animal);
            float accumulatedNutrition = 0f;
            
            for (int i = 0; i < innerContainer.Count; i++)
            {
                Thing thing = innerContainer[i];
                if (!animal.WillEat(thing, pawn) || 
                    (int)thing.def.ingestible.preferability > (int)FoodPreferability.RawTasty || 
                    thing.def.IsDrug)
                {
                    continue;
                }

                for (int j = 0; j < thing.stackCount; j++)
                {
                    accumulatedNutrition += thing.GetStatValue(StatDefOf.Nutrition);
                    if (accumulatedNutrition >= requiredNutrition)
                    {
                        fullPortionCount++;
                        accumulatedNutrition = 0f;
                    }

                    if (fullPortionCount >= 2)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Creates a job for the pawn to take food specifically for animal interaction
        /// </summary>
        protected Job TakeFoodForAnimalInteractJob(Pawn pawn, Pawn animal)
        {
            ThingDef foodDef;
            float requiredNutrition = JobDriver_InteractAnimal.RequiredNutritionPerFeed(animal) * 2f * 4f;
            
            Thing bestFood = FoodUtility.BestFoodSourceOnMap(
                pawn, animal, desperate: false, out foodDef, 
                FoodPreferability.RawTasty, allowPlant: false, allowDrug: false, allowCorpse: false,
                allowDispenserFull: false, allowDispenserEmpty: false, allowForbidden: false,
                allowSociallyImproper: false, allowHarvest: false, forceScanWholeMap: false,
                ignoreReservations: false, calculateWantedStackCount: false,
                FoodPreferability.Undefined, requiredNutrition);
                
            if (bestFood == null)
                return null;

            float nutrition = FoodUtility.GetNutrition(animal, bestFood, foodDef);
            int count = FoodUtility.StackCountForNutrition(requiredNutrition, nutrition);
            
            Job job = JobMaker.MakeJob(JobDefOf.TakeInventory, bestFood);
            job.count = count;
            
            return job;
        }
    }
}