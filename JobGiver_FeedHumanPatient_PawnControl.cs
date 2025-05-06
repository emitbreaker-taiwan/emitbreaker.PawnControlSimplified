using RimWorld;
using Verse;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver for feeding humanlike non-prisoner patients. Highest priority among feeding jobs.
    /// </summary>
    public class JobGiver_FeedHumanPatient_PawnControl : JobGiver_FeedPatient_PawnControl
    {
        protected override bool FeedHumanlikesOnly => true;
        protected override bool FeedAnimalsOnly => false;
        protected override bool FeedPrisonersOnly => false;
        protected override string WorkTagForJob => "Doctor";
        protected override float JobPriority => 8.5f;  // Highest priority
        protected override string JobDescription => "feed humanlike patient";

        public override string ToString()
        {
            return "JobGiver_FeedHumanPatient_PawnControl";
        }
    }
}