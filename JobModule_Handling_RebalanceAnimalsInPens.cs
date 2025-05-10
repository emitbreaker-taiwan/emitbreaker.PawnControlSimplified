using RimWorld;
using System;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module specifically for rebalancing animals between pens
    /// Uses the balancing algorithm from the animal pen system
    /// </summary>
    public class JobModule_Handling_RebalanceAnimalsInPens : JobModule_Handling_TakeToPen
    {
        public override string UniqueID => "RebalanceAnimalsInPens";
        public override float Priority => 5.5f; // Slightly lower than roaming animals, higher than regular pen assignment
        public override string Category => "AnimalHandling";
        public override int CacheUpdateInterval => 400; // Every ~6.7 seconds - slightly less frequent than base class

        /// <summary>
        /// Constructor to initialize specialized settings for pen rebalancing
        /// </summary>
        public JobModule_Handling_RebalanceAnimalsInPens() : base()
        {
            // Configure the module for optimal pen balancing
            ropingPriority = RopingPriority.Balanced; // Key difference - use balanced algorithm instead of closest
            allowUnenclosedPens = false; // Only rebalance animals in enclosed pens
            targetRoamingAnimals = false; // Don't target roaming animals with this module
        }

        /// <summary>
        /// Override to only include animals that would benefit from rebalancing
        /// </summary>
        public override bool ShouldProcessAnimal(Pawn animal, Map map)
        {
            try
            {
                // Basic checks from parent class
                if (animal == null || !animal.Spawned || animal.Dead || !animal.IsNonMutantAnimal)
                    return false;

                // Must be player's animal
                if (animal.Faction != Faction.OfPlayer)
                    return false;

                // Skip animals that are roaming or in other mental states
                if (animal.MentalStateDef != null)
                    return false;

                // Skip animals marked for release
                if (map.designationManager.DesignationOn(animal, DesignationDefOf.ReleaseAnimalToWild) != null)
                    return false;

                // Skip animals not in enclosed pens
                // Get the animal's current pen - using correct AnimalPenUtility method
                CompAnimalPenMarker currentPen = AnimalPenUtility.GetCurrentPenOf(animal, false);
                if (currentPen == null || !currentPen.PenState.Enclosed)
                    return false;

                // Update balance calculators for future validation
                UpdateBalanceCalculators(null);

                return true;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in ShouldProcessAnimal for rebalancing: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Provide more descriptive logging for rebalancing jobs
        /// </summary>
        protected override Job CreateHandlingJob(Pawn handler, Pawn animal)
        {
            Job job = base.CreateHandlingJob(handler, animal);

            // Add specialized logging for rebalancing
            if (job != null)
            {
                CompAnimalPenMarker currentPen = AnimalPenUtility.GetCurrentPenOf(animal, false);
                string penName = currentPen?.parent?.Label ?? "unknown pen";
                Utility_DebugManager.LogNormal($"REBALANCING: {handler.LabelShort} moving {animal.LabelShort} from {penName} for better pen balance");
            }

            return job;
        }

        /// <summary>
        /// Override to add specialized validation for rebalancing operations
        /// </summary>
        public override bool ValidateHandlingJob(Pawn animal, Pawn handler)
        {
            // First check if the basic pen movement is valid
            if (!base.ValidateHandlingJob(animal, handler))
                return false;

            try
            {
                // Get the animal's current pen using the correct method
                CompAnimalPenMarker currentPen = AnimalPenUtility.GetCurrentPenOf(animal, false);
                if (currentPen == null || !currentPen.PenState.Enclosed)
                    return false;

                // Update balance calculators for accurate decision making
                UpdateBalanceCalculators(handler);

                // Check if there's a better pen to move to
                string failReason;
                AnimalPenBalanceCalculator balanceCalculator = GetBalanceCalculator(handler.Map);

                // Use the RopingPriority.Balanced algorithm to determine if the animal should be moved
                CompAnimalPenMarker targetPen = AnimalPenUtility.GetPenAnimalShouldBeTakenTo(
                    handler, animal, out failReason, false, canInteractWhileSleeping, false, true,
                    RopingPriority.Balanced, balanceCalculator);

                // Check if we're actually moving to a different pen (otherwise it's not rebalancing)
                if (targetPen == null || targetPen == currentPen)
                {
                    if (failReason != null)
                        JobFailReason.Is(failReason);
                    return false;
                }

                // Only proceed if the target pen is a reasonable distance away
                if ((targetPen.parent.Position - animal.Position).LengthHorizontalSquared > 400) // 20 tiles
                {
                    JobFailReason.Is("Target pen too far for rebalancing");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in ValidateHandlingJob for rebalancing: {ex.Message}");
                return false;
            }
        }
    }
}