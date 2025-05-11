using RimWorld;
using Verse;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver specialized for handling roaming animals specifically
    /// </summary>
    public class JobGiver_Handling_TakeRoamingAnimalsToPen_PawnControl : JobGiver_Handling_TakeToPen_PawnControl
    {
        public JobGiver_Handling_TakeRoamingAnimalsToPen_PawnControl()
        {
            this.targetRoamingAnimals = true;  // Specifically target roaming animals
            this.allowUnenclosedPens = true;   // Allow taking them to unenclosed pens
            this.ropingPriority = RopingPriority.Closest;  // Use additional priority
        }

        protected override float GetBasePriority(string workTag)
        {
            // Higher priority than regular animal handling
            return 6.0f;
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static new void ResetCache()
        {
            JobGiver_Handling_TakeToPen_PawnControl.ResetCache();
        }

        public override string ToString()
        {
            return "JobGiver_Handling_TakeRoamingAnimalsToPen_PawnControl";
        }
    }
}