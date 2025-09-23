using RimWorld;
using System;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    public class FloatMenuOptionProvider_ArrestNonHumanlike : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;
        protected override bool RequiresManipulation => false; // 비휴먼라이크는 무시

        public override bool SelectedPawnValid(Pawn pawn, FloatMenuContext context)
        {
            if (pawn.RaceProps.Humanlike) return false;
            if (pawn.Faction != Faction.OfPlayer) return false;

            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null) return false;

            return Utility_TagManager.ForceDraftable(pawn);
        }

        protected override FloatMenuOption GetSingleOptionFor(Pawn clickedPawn, FloatMenuContext context)
        {
            var arrester = context.FirstSelectedPawn;

            if (!clickedPawn.CanBeArrestedBy(arrester))
                return null;

            if (clickedPawn.Downed && clickedPawn.guilt.IsGuilty)
                return null;

            if (!arrester.Drafted && (!clickedPawn.IsWildMan() || clickedPawn.IsPrisonerOfColony))
                return null;

            if (arrester.InSameExtraFaction(clickedPawn, ExtraFactionType.HomeFaction) ||
                arrester.InSameExtraFaction(clickedPawn, ExtraFactionType.MiniFaction))
            {
                return new FloatMenuOption(
                    "CannotArrest".Translate() + ": " + "SameFaction".Translate(clickedPawn),
                    null);
            }

            // 비휴먼라이크의 Warden 작업 가능 여부 체크
            if (!arrester.RaceProps.Humanlike && !Utility_TagManager.IsWorkEnabled(arrester, WorkTypeDefOf.Warden))
            {
                return new FloatMenuOption(
                    "CannotArrest".Translate() + ": " + "CannotPrioritizeWorkTypeDisabled".Translate(WorkTypeDefOf.Warden.gerundLabel),
                    null);
            }

            if (!arrester.CanReach(clickedPawn, PathEndMode.OnCell, Danger.Deadly))
            {
                return new FloatMenuOption(
                    "CannotArrest".Translate() + ": " + "NoPath".Translate().CapitalizeFirst(),
                    null);
            }

            Action action = () =>
            {
                Building_Bed targetB = RestUtility.FindBedFor(clickedPawn, arrester, false, guestStatus: GuestStatus.Prisoner) ??
                                      RestUtility.FindBedFor(clickedPawn, arrester, false, true, GuestStatus.Prisoner);
                if (targetB == null)
                {
                    Messages.Message(
                        "CannotArrest".Translate() + ": " + "NoPrisonerBed".Translate(),
                        clickedPawn, MessageTypeDefOf.RejectInput, false);
                }
                else
                {
                    Job job = JobMaker.MakeJob(JobDefOf.Arrest, clickedPawn, targetB);
                    job.count = 1;
                    arrester.jobs.TryTakeOrderedJob(job);

                    if (clickedPawn.Faction != null &&
                        clickedPawn.Faction != Faction.OfPlayer &&
                        !clickedPawn.Faction.Hidden &&
                        !clickedPawn.IsQuestLodger())
                    {
                        TutorUtility.DoModalDialogIfNotKnown(
                            ConceptDefOf.ArrestingCreatesEnemies,
                            clickedPawn.GetAcceptArrestChance(arrester).ToStringPercent());
                    }
                }
            };

            return FloatMenuUtility.DecoratePrioritizedTask(
                new FloatMenuOption(
                    "TryToArrest".Translate(clickedPawn.LabelCap, clickedPawn, clickedPawn.GetAcceptArrestChance(arrester).ToStringPercent()),
                    action, MenuOptionPriority.High, revalidateClickTarget: clickedPawn),
                arrester, clickedPawn);
        }
    }
}