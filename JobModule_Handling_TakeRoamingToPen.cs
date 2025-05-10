using RimWorld;
using System;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module specifically for taking roaming animals to pens
    /// This is specialized from the regular TakeToPen module with different priorities and settings
    /// </summary>
    public class JobModule_Handling_TakeRoamingToPen : JobModule_Handling_TakeToPen
    {
        public override string UniqueID => "TakeRoamingToPen";
        public override float Priority => 6.0f; // Higher priority than regular take to pen
        public override string Category => "AnimalHandling";
        public override int CacheUpdateInterval => 150; // Update more frequently (every 2.5 seconds)

        /// <summary>
        /// Constructor to initialize specialized settings for roaming animals
        /// </summary>
        public JobModule_Handling_TakeRoamingToPen() : base()
        {
            // Override the base settings to specifically target roaming animals
            targetRoamingAnimals = true;  // Specifically target roaming animals
            allowUnenclosedPens = true;   // Allow taking them to unenclosed pens
            ropingPriority = RopingPriority.Closest;
        }

        /// <summary>
        /// Override to specifically target only roaming animals
        /// </summary>
        public override bool ShouldProcessAnimal(Pawn animal, Map map)
        {
            try
            {
                if (animal == null || !animal.Spawned || animal.Dead || !animal.IsNonMutantAnimal)
                    return false;

                // Must be roaming
                if (animal.MentalStateDef != MentalStateDefOf.Roaming)
                    return false;

                // Skip animals that are marked for release to wild
                if (animal.Faction == Faction.OfPlayer &&
                    map.designationManager.DesignationOn(animal, DesignationDefOf.ReleaseAnimalToWild) != null)
                    return false;

                // Animal needs to be from player faction
                if (animal.Faction != Faction.OfPlayer)
                    return false;

                return true;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in ShouldProcessAnimal for taking roaming to pen: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Provide more descriptive logging for roaming animal handling
        /// </summary>
        protected override Job CreateHandlingJob(Pawn handler, Pawn animal)
        {
            Job job = base.CreateHandlingJob(handler, animal);
            
            // Add extra logging specific to handling roaming animals
            if (job != null)
            {
                Utility_DebugManager.LogNormal($"Handling ROAMING animal: {handler.LabelShort} taking {animal.LabelShort} ({job.def})");
            }
            
            return job;
        }
    }
}