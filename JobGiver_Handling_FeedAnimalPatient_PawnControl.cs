using RimWorld;
using Verse;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver for feeding animal patients with the Handling work tag.
    /// </summary>
    public class JobGiver_Handling_FeedAnimalPatient_PawnControl : JobGiver_Common_FeedPatient_PawnControl
    {
        protected override bool FeedHumanlikesOnly => false;
        protected override bool FeedAnimalsOnly => true;
        protected override bool FeedPrisonersOnly => false;
        protected override string WorkTagForJob => "Handling";
        protected override float JobPriority => 7.5f;  // Lowest priority among feeding jobs
        protected override string JobDescription => "feed animal patient (handling)";

        public override string ToString()
        {
            return "JobGiver_FeedAnimalPatientHandling_PawnControl";
        }
    }
}