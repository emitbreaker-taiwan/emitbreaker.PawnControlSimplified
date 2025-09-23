using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    public class FloatMenuOptionProvider_DraftedRepairNonHumanlike : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => false;
        protected override bool Multiselect => false;
        protected override bool MechanoidCanDo => true;
        protected override bool RequiresManipulation => false; // 비휴먼라이크는 무시

        public override bool SelectedPawnValid(Pawn pawn, FloatMenuContext context)
        {
            if (pawn.RaceProps.Humanlike) return false;
            if (pawn.Faction != Faction.OfPlayer) return false;

            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null) return false;

            return Utility_TagManager.ForceDraftable(pawn);
        }

        protected override bool AppliesInt(FloatMenuContext context)
        {
            var pawn = context.FirstSelectedPawn;

            // 휴먼라이크는 바닐라 체크
            if (pawn.RaceProps.Humanlike)
            {
                if (pawn.skills != null)
                {
                    return !pawn.skills.GetSkill(SkillDefOf.Construction).TotallyDisabled;
                }
                return false;
            }

            // 비휴먼라이크는 태그 기반 체크
            return Utility_TagManager.IsWorkEnabled(pawn, WorkTypeDefOf.Construction);
        }

        protected override FloatMenuOption GetSingleOptionFor(Thing clickedThing, FloatMenuContext context)
        {
            var pawn = context.FirstSelectedPawn;

            // RepairUtility는 폰의 능력을 체크하지 않으므로 안전하게 사용 가능
            if (!RepairUtility.PawnCanRepairNow(pawn, clickedThing))
            {
                return null;
            }

            // 비휴먼라이크의 Construction 작업 가능 여부만 추가로 체크
            if (!pawn.RaceProps.Humanlike && !Utility_TagManager.IsWorkEnabled(pawn, WorkTypeDefOf.Construction))
            {
                return new FloatMenuOption(
                    "CannotRepair".Translate(clickedThing) + ": " + "CannotPrioritizeWorkTypeDisabled".Translate(WorkTypeDefOf.Construction.gerundLabel),
                    null);
            }

            if (!pawn.CanReach(clickedThing, PathEndMode.Touch, Danger.Deadly))
            {
                return new FloatMenuOption(
                    "CannotRepair".Translate(clickedThing) + ": " + "NoPath".Translate().CapitalizeFirst(),
                    null);
            }

            return FloatMenuUtility.DecoratePrioritizedTask(
                new FloatMenuOption("RepairThing".Translate(clickedThing), delegate
                {
                    pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Repair, clickedThing), JobTag.Misc);
                }), pawn, clickedThing);
        }
    }
}