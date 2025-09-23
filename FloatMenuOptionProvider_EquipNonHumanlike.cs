using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    public class FloatMenuOptionProvider_EquipNonHumanlike : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;
        protected override bool MechanoidCanDo => true;
        protected override bool RequiresManipulation => false; // 비휴먼라이크는 무시

        public override bool SelectedPawnValid(Pawn pawn, FloatMenuContext context)
        {
            if (pawn.RaceProps.Humanlike) return false;

            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null) return false;

            return Utility_TagManager.ForceEquipWeapon(pawn);
        }

        protected override bool AppliesInt(FloatMenuContext context)
        {
            var pawn = context.FirstSelectedPawn;

            // 장비 트래커 보장
            if (pawn.equipment == null)
            {
                pawn.equipment = new Pawn_EquipmentTracker(pawn);
                if (pawn.Spawned)
                {
                    pawn.equipment.Notify_PawnSpawned();
                }
            }

            return pawn.equipment != null;
        }

        protected override FloatMenuOption GetSingleOptionFor(Thing clickedThing, FloatMenuContext context)
        {
            if (!clickedThing.HasComp<CompEquippable>()) return null;

            var pawn = context.FirstSelectedPawn;
            string labelShort = clickedThing.LabelShort;

            // Violent 작업 체크 - 비휴먼라이크는 태그 기반
            if (clickedThing.def.IsWeapon)
            {
                if (!pawn.RaceProps.Humanlike)
                {
                    // 태그 시스템으로 Violent 작업 가능 여부 체크
                    if (!Utility_TagManager.ForceEquipWeapon(pawn))
                    {
                        return new FloatMenuOption("CannotEquip".Translate(labelShort) + ": " + "IsIncapableOfViolenceLower".Translate(pawn.LabelShort, pawn), null);
                    }
                }
                else if (pawn.WorkTagIsDisabled(WorkTags.Violent))
                {
                    return new FloatMenuOption("CannotEquip".Translate(labelShort) + ": " + "IsIncapableOfViolenceLower".Translate(pawn.LabelShort, pawn), null);
                }
            }

            // 원거리 무기 체크
            if (clickedThing.def.IsRangedWeapon)
            {
                if (!pawn.RaceProps.Humanlike)
                {
                    string workType = "Shooting";
                    if (!Utility_TagManager.IsWorkEnabled(pawn, workType))
                    {
                        return new FloatMenuOption("CannotEquip".Translate(labelShort) + ": " + "IsIncapableOfShootingLower".Translate(pawn), null);
                    }
                }
                else if (pawn.WorkTagIsDisabled(WorkTags.Shooting))
                {
                    return new FloatMenuOption("CannotEquip".Translate(labelShort) + ": " + "IsIncapableOfShootingLower".Translate(pawn), null);
                }
            }

            // 최소한의 체크만
            if (!pawn.CanReach(clickedThing, PathEndMode.ClosestTouch, Danger.Deadly))
            {
                return new FloatMenuOption(
                    "CannotEquip".Translate(labelShort) + ": " + "NoPath".Translate().CapitalizeFirst(),
                    null);
            }

            if (clickedThing.IsBurning())
            {
                return new FloatMenuOption(
                    "CannotEquip".Translate(labelShort) + ": " + "BurningLower".Translate(),
                    null);
            }

            string text = "Equip".Translate(labelShort);
            return FloatMenuUtility.DecoratePrioritizedTask(
                new FloatMenuOption(text, () =>
                {
                    clickedThing.SetForbidden(false);
                    var job = JobMaker.MakeJob(JobDefOf.Equip, clickedThing);
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                    FleckMaker.Static(clickedThing.DrawPos, clickedThing.MapHeld, FleckDefOf.FeedbackEquip);
                    PlayerKnowledgeDatabase.KnowledgeDemonstrated(ConceptDefOf.EquippingWeapons, KnowledgeAmount.Total);
                }, MenuOptionPriority.High),
                pawn, clickedThing
            );
        }
    }
}
