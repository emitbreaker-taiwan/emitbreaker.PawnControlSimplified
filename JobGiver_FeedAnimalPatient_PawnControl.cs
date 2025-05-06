using RimWorld;
using Verse;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver for feeding animal patients with the Doctor work tag.
    /// </summary>
    public class JobGiver_FeedAnimalPatient_PawnControl : JobGiver_FeedPatient_PawnControl
    {
        protected override bool FeedHumanlikesOnly => false;
        protected override bool FeedAnimalsOnly => true;
        protected override bool FeedPrisonersOnly => false;
        protected override string WorkTagForJob => "Doctor";
        protected override float JobPriority => 7.8f;  // Lower priority than feeding humanlike patients
        protected override string JobDescription => "feed animal patient (doctor)";

        public override string ToString()
        {
            return "JobGiver_FeedAnimalPatient_PawnControl";
        }
    }
}