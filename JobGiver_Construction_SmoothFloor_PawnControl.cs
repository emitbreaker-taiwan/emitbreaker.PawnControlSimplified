using RimWorld;
using Verse;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that allows pawns to smooth floors in designated areas.
    /// Uses the Construction work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Construction_SmoothFloor_PawnControl : JobGiver_Common_ConstructAffectFloor_PawnControl
    {
        protected override DesignationDef DesDef => DesignationDefOf.SmoothFloor;
        protected override JobDef JobDef => JobDefOf.SmoothFloor;
    }
}