using RimWorld;
using Verse;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that allows pawns to remove floors in designated areas.
    /// Uses the Construction work tag for eligibility checking.
    /// </summary>
    public class JobGiver_RemoveFloor_PawnControl : JobGiver_ConstructAffectFloor_PawnControl
    {
        protected override DesignationDef DesDef => DesignationDefOf.RemoveFloor;
        protected override JobDef JobDef => JobDefOf.RemoveFloor;
    }
}