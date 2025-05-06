using RimWorld;
using Verse;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver specialized for handling roaming animals specifically
    /// </summary>
    public class JobGiver_TakeRoamingToPen_PawnControl : JobGiver_TakeToPen_PawnControl
    {
        public JobGiver_TakeRoamingToPen_PawnControl()
        {
            this.targetRoamingAnimals = true;  // Specifically target roaming animals
            this.allowUnenclosedPens = true;   // Allow taking them to unenclosed pens
            this.ropingPriority = RopingPriority.Closest;  // Use additional priority
        }

        public override float GetPriority(Pawn pawn)
        {
            // Higher priority than regular animal handling
            return 6.0f;
        }

        public override string ToString()
        {
            return "JobGiver_TakeRoamingToPen_PawnControl";
        }
    }
}