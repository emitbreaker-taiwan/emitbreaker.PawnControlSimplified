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
    public class FloatMenuOptionProvider_DraftedTendNonHumanlike : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => false;
        protected override bool Multiselect => false;
        protected override bool MechanoidCanDo => true;
        protected override bool RequiresManipulation => false; // 비휴먼라이크는 무시
        protected override bool CanSelfTarget => true;

        public override bool SelectedPawnValid(Pawn pawn, FloatMenuContext context)
        {
            if (pawn.RaceProps.Humanlike) return false;
            if (pawn.Faction != Faction.OfPlayer) return false;

            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null) return false;

            // 뮤턴트 치료 가능 체크
            if (pawn.IsMutant && !pawn.mutant.Def.canTend) return false;

            return Utility_TagManager.ForceDraftable(pawn);
        }

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Pawn clickedPawn, FloatMenuContext context)
        {
            var doctor = context.FirstSelectedPawn;

            if (!IsValidTendTarget(doctor, clickedPawn))
            {
                yield break;
            }

            if (!clickedPawn.health.HasHediffsNeedingTend())
            {
                if (doctor != clickedPawn)
                {
                    yield return new FloatMenuOption(
                        "CannotTend".Translate(clickedPawn) + ": " + "TendingNotRequired".Translate(clickedPawn),
                        null);
                }
                yield break;
            }

            // Doctor 작업 가능 여부 체크 - 비휴먼라이크는 태그 기반, 휴먼라이크는 바닐라
            if (!doctor.RaceProps.Humanlike)
            {
                if (!Utility_TagManager.IsWorkEnabled(doctor, WorkTypeDefOf.Doctor))
                {
                    yield return new FloatMenuOption(
                        "CannotTend".Translate(clickedPawn) + ": " + "CannotPrioritizeWorkTypeDisabled".Translate(WorkTypeDefOf.Doctor.gerundLabel),
                        null);
                    yield break;
                }
            }
            else if (doctor.WorkTypeIsDisabled(WorkTypeDefOf.Doctor))
            {
                yield return new FloatMenuOption(
                    "CannotTend".Translate(clickedPawn) + ": " + "CannotPrioritizeWorkTypeDisabled".Translate(WorkTypeDefOf.Doctor.gerundLabel),
                    null);
                yield break;
            }

            if (clickedPawn == doctor && doctor.playerSettings != null && !doctor.playerSettings.selfTend)
            {
                yield return new FloatMenuOption(
                    "CannotTend".Translate(clickedPawn) + ": " + "SelfTendDisabled".Translate().CapitalizeFirst(),
                    null);
                yield break;
            }

            if (clickedPawn.InAggroMentalState && !clickedPawn.health.hediffSet.HasHediff(HediffDefOf.Scaria))
            {
                yield return new FloatMenuOption(
                    "CannotTend".Translate(clickedPawn) + ": " + "PawnIsInAggroMentalState".Translate(clickedPawn).CapitalizeFirst(),
                    null);
                yield break;
            }

            Thing medicine = HealthAIUtility.FindBestMedicine(doctor, clickedPawn, onlyUseInventory: true);
            TaggedString taggedString = "Tend".Translate(clickedPawn);
            if (medicine == null)
            {
                taggedString += " (" + "WithoutMedicine".Translate() + ")";
            }

            yield return FloatMenuUtility.DecoratePrioritizedTask(
                new FloatMenuOption(taggedString, () =>
                {
                    Job job = JobMaker.MakeJob(JobDefOf.TendPatient, clickedPawn, medicine);
                    job.count = 1;
                    job.draftedTend = true;
                    doctor.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                }), doctor, clickedPawn);

            if (medicine != null && doctor.CanReserve(clickedPawn) && clickedPawn.Spawned)
            {
                yield return new FloatMenuOption(
                    "Tend".Translate(clickedPawn) + " (" + "WithoutMedicine".Translate() + ")",
                    delegate
                    {
                        Job job2 = JobMaker.MakeJob(JobDefOf.TendPatient, clickedPawn, null);
                        job2.count = 1;
                        job2.draftedTend = true;
                        doctor.jobs.TryTakeOrderedJob(job2, JobTag.Misc);
                    });
            }
        }

        private bool IsValidTendTarget(Pawn doctor, Pawn patient)
        {
            if (!doctor.Drafted && patient != doctor)
            {
                return false;
            }

            if (patient.Downed)
            {
                return true;
            }

            if (patient.HostileTo(doctor.Faction))
            {
                return false;
            }

            if (patient.IsColonist || patient.IsQuestLodger() || patient.IsPrisonerOfColony ||
                patient.IsSlaveOfColony || (patient.Faction == Faction.OfPlayer && patient.IsAnimal))
            {
                return true;
            }

            if (patient.IsColonySubhuman && patient.mutant.Def.entitledToMedicalCare)
            {
                return true;
            }

            return false;
        }
    }
}
