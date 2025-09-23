using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    public class FloatMenuOptionProvider_DraftedMoveNonHumanlike : FloatMenuOptionProvider
    {
        private static List<Pawn> tmpPawns = new List<Pawn>();

        protected override bool Drafted => true;
        protected override bool Undrafted => false;
        protected override bool Multiselect => true;
        protected override bool MechanoidCanDo => true;
        protected override bool IgnoreFogged => false;

        public override bool SelectedPawnValid(Pawn pawn, FloatMenuContext context)
        {
            // 휴먼라이크는 바닐라 프로바이더 사용
            if (pawn.RaceProps.Humanlike) return false;

            // 플레이어 팩션만
            if (pawn.Faction != Faction.OfPlayer) return false;

            // 드래프트 상태 확인
            if (!pawn.Drafted) return false;

            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null) return false;

            return Utility_TagManager.ForceDraftable(pawn);
        }

        protected override FloatMenuOption GetSingleOption(FloatMenuContext context)
        {
            IntVec3 curLoc = CellFinder.StandableCellNear(context.ClickedCell, context.map, 2.9f);
            if (!curLoc.IsValid)
            {
                return null;
            }

            FloatMenuOption floatMenuOption;

            if (!context.IsMultiselect)
            {
                // 단일 선택
                var pawn = context.FirstSelectedPawn;
                if (curLoc == pawn.Position)
                {
                    return null;
                }

                AcceptanceReport acceptanceReport = PawnCanGoto(pawn, curLoc);
                if (!acceptanceReport.Accepted)
                {
                    return new FloatMenuOption(acceptanceReport.Reason, null);
                }

                floatMenuOption = new FloatMenuOption("GoHere".Translate(), delegate
                {
                    PawnGotoAction(context.ClickedCell, pawn, RCellFinder.BestOrderedGotoDestNear(curLoc, pawn));
                }, MenuOptionPriority.GoHere);
            }
            else
            {
                // 다중 선택
                tmpPawns.Clear();
                foreach (Pawn validSelectedPawn in context.ValidSelectedPawns)
                {
                    if (PawnCanGoto(validSelectedPawn, curLoc).Accepted)
                    {
                        tmpPawns.Add(validSelectedPawn);
                    }
                }

                if (tmpPawns.Count == 0)
                {
                    return null;
                }

                floatMenuOption = new FloatMenuOption("GoHere".Translate(), delegate
                {
                    Find.Selector.gotoController.StartInteraction(curLoc);
                    foreach (Pawn tmpPawn in tmpPawns)
                    {
                        Find.Selector.gotoController.AddPawn(tmpPawn);
                    }
                    Find.Selector.gotoController.FinalizeInteraction();
                }, MenuOptionPriority.GoHere);
            }

            floatMenuOption.isGoto = true;
            floatMenuOption.autoTakeable = true;
            floatMenuOption.autoTakeablePriority = 10f;
            return floatMenuOption;
        }

        private static AcceptanceReport PawnCanGoto(Pawn pawn, IntVec3 gotoLoc)
        {
            // 메카니터 범위 체크 (비휴먼라이크도 적용)
            if (ModsConfig.BiotechActive && pawn.IsColonyMech &&
                !MechanitorUtility.InMechanitorCommandRange(pawn, gotoLoc))
            {
                return "CannotGoOutOfRange".Translate() + ": " + "OutOfCommandRange".Translate();
            }

            if (!pawn.CanReach(gotoLoc, PathEndMode.OnCell, Danger.Deadly))
            {
                return "CannotGoNoPath".Translate();
            }

            return true;
        }

        private static void PawnGotoAction(IntVec3 clickCell, Pawn pawn, IntVec3 gotoLoc)
        {
            bool flag;
            if (pawn.Position == gotoLoc)
            {
                flag = true;
                if (pawn.CurJobDef == JobDefOf.Goto)
                {
                    pawn.jobs.EndCurrentJob(JobCondition.Succeeded);
                }
            }
            else if (pawn.CurJobDef == JobDefOf.Goto && pawn.CurJob.targetA.Cell == gotoLoc)
            {
                flag = true;
            }
            else
            {
                Job job = JobMaker.MakeJob(JobDefOf.Goto, gotoLoc);
                if (pawn.Map.exitMapGrid.IsExitCell(clickCell))
                {
                    job.exitMapOnArrival = !pawn.IsColonyMech;
                }
                else if (!pawn.Map.IsPlayerHome && !pawn.Map.exitMapGrid.MapUsesExitGrid &&
                         CellRect.WholeMap(pawn.Map).IsOnEdge(clickCell, 3) &&
                         pawn.Map.Parent.GetComponent<FormCaravanComp>() != null &&
                         MessagesRepeatAvoider.MessageShowAllowed("MessagePlayerTriedToLeaveMapViaExitGrid-" + pawn.Map.uniqueID, 60f))
                {
                    Messages.Message(
                        pawn.Map.Parent.GetComponent<FormCaravanComp>().CanFormOrReformCaravanNow
                            ? "MessagePlayerTriedToLeaveMapViaExitGrid_CanReform".Translate()
                            : "MessagePlayerTriedToLeaveMapViaExitGrid_CantReform".Translate(),
                        pawn.Map.Parent, MessageTypeDefOf.RejectInput, historical: false);
                }

                flag = pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }

            if (flag)
            {
                FleckMaker.Static(gotoLoc, pawn.Map, FleckDefOf.FeedbackGoto);
            }
        }
    }
}
