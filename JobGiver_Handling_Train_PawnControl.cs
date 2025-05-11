using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to train animals owned by the player's faction.
    /// Uses the Handler work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Handling_Train_PawnControl : JobGiver_Handling_InteractAnimal_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "Train";

        /// <summary>
        /// Maximum cache size to control memory usage
        /// </summary>
        private const int MAX_CACHE_SIZE = 1000;

        /// <summary>
        /// Training doesn't require the animal to be awake
        /// </summary>
        protected override bool CanInteractWhileSleeping => false;

        /// <summary>
        /// Training can be done while animal is roaming
        /// </summary>
        protected override bool CanInteractWhileRoaming => false;

        /// <summary>
        /// Training requires appropriate skill levels
        /// </summary>
        protected override bool IgnoreSkillRequirements => false;

        /// <summary>
        /// Training usually requires food
        /// </summary>
        protected override bool NeedsFoodForInteraction() => true;

        #endregion

        #region Core flow

        protected override float GetBasePriority(string workTag)
        {
            // Training has moderate priority among work tasks
            return 5.0f;
        }

        #endregion

        #region Target processing

        /// <summary>
        /// Override to get only animals that need training
        /// </summary>
        protected override bool CanInteractWithAnimalInPrinciple(Pawn animal)
        {
            // Basic animal check from base class
            if (!base.CanInteractWithAnimalInPrinciple(animal))
                return false;

            // Training-specific checks
            return animal.training != null &&
                animal.training.NextTrainableToTrain() != null &&
                animal.RaceProps.animalType != AnimalType.Dryad &&
                !TrainableUtility.TrainedTooRecently(animal);
        }

        /// <summary>
        /// Additional criteria for training
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null) yield break;

            Faction playerFaction = Faction.OfPlayer;
            if (playerFaction == null) yield break;

            // Get animals from the faction that need training
            foreach (Pawn animal in map.mapPawns.SpawnedPawnsInFaction(playerFaction))
            {
                if (CanInteractWithAnimalInPrinciple(animal))
                {
                    yield return animal;
                }
            }
        }

        /// <summary>
        /// Training-specific checks for animal interaction
        /// </summary>
        protected override bool IsValidForSpecificInteraction(Pawn handler, Pawn animal, bool forced)
        {
            // Skip if not of same faction
            if (animal.Faction != handler.Faction)
                return false;

            // Skip if no trainable left or trained too recently
            if (animal.training == null ||
                animal.training.NextTrainableToTrain() == null ||
                TrainableUtility.TrainedTooRecently(animal))
                return false;

            // Skip if animals marked venerated
            if (ModsConfig.IdeologyActive &&
                handler.Ideo != null &&
                handler.Ideo.IsVeneratedAnimal(animal))
                return false;

            return true;
        }

        /// <summary>
        /// Create job for training the animal
        /// </summary>
        protected override Job MakeInteractionJob(Pawn handler, Pawn animal, bool forced)
        {
            if (animal?.training?.NextTrainableToTrain() == null)
                return null;

            // Create the training job
            return JobMaker.MakeJob(JobDefOf.Train, animal);
        }

        /// <summary>
        /// Custom food check for training animals
        /// </summary>
        protected override bool HasFoodToInteractAnimal(Pawn pawn, Pawn animal)
        {
            // Some animals don't need food for training
            if (!animal.RaceProps.EatsFood || animal.needs?.food == null)
                return true;

            // Use base implementation for standard food check
            return base.HasFoodToInteractAnimal(pawn, animal);
        }

        #endregion

        #region Utility

        /// <summary>
        /// For debugging purposes
        /// </summary>
        public override string ToString()
        {
            return "JobGiver_Train_PawnControl";
        }

        #endregion
    }
}