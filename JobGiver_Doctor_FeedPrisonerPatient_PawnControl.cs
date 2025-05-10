using RimWorld;
using Verse;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver for feeding humanlike prisoner patients.
    /// </summary>
    public class JobGiver_Doctor_FeedPrisonerPatient_PawnControl : JobGiver_Common_FeedPatient_PawnControl
    {
        protected override bool FeedHumanlikesOnly => true;
        protected override bool FeedAnimalsOnly => false;
        protected override bool FeedPrisonersOnly => true;
        protected override string WorkTagForJob => "Doctor";
        protected override float JobPriority => 8.0f;  // Slightly lower priority than feeding colonists
        protected override string JobDescription => "feed prisoner patient";

        public override string ToString()
        {
            return "JobGiver_FeedPrisonerPatient_PawnControl";
        }
    }
}