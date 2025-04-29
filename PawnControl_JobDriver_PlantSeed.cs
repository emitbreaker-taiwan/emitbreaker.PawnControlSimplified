using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse.AI;
using Verse;

namespace emitbreaker.PawnControl
{
    public class PawnControl_JobDriver_PlantSeed : JobDriver
    {
        private float sowWorkDone;
        private const TargetIndex SeedIndex = TargetIndex.A;
        private const TargetIndex PlantCellIndex = TargetIndex.B;
        private const TargetIndex OldSeedStackIndex = TargetIndex.C;

        private IntVec3 PlantCell => job.GetTarget(TargetIndex.B).Cell;

        private CompPlantable PlantableComp => job.GetTarget(TargetIndex.A).Thing.TryGetComp<CompPlantable>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, 1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // === Fail Conditions ===
            this.FailOn(() => PlantUtility.AdjacentSowBlocker(job.plantDefToSow, PlantCell, base.Map) != null
                || !job.plantDefToSow.CanEverPlantAt(PlantCell, base.Map)
                || !PlantableComp.PlantCells.Contains(PlantCell));

            // === Move to seed ===
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            // === Mark original stack ===
            yield return Toils_General.Do(delegate
            {
                LocalTargetInfo target2 = job.GetTarget(TargetIndex.A);
                if (target2.HasThing && target2.Thing.stackCount > job.count)
                {
                    job.SetTarget(TargetIndex.C, target2.Thing);
                }
            });

            // === Pickup seed ===
            yield return Toils_Haul.StartCarryThing(TargetIndex.A);

            // === Notify PlantableComp of removal ===
            yield return Toils_General.Do(delegate
            {
                LocalTargetInfo target = job.GetTarget(TargetIndex.C);
                if (target.IsValid && target.HasThing)
                {
                    target.Thing.TryGetComp<CompPlantable>()?.Notify_SeedRemovedFromStackForPlantingAt(PlantCell);
                }
                PlantableComp?.Notify_RemovedFromStackForPlantingAt(PlantCell);
            });

            // === Carry to planting location ===
            yield return Toils_Haul.CarryHauledThingToCell(TargetIndex.B, PathEndMode.Touch);

            // === Simulate planting toil (no XP) ===
            Toil toil = ToilMaker.MakeToil("NonHumanlike_Planting");
            toil.tickAction = delegate
            {
                sowWorkDone += pawn.GetStatValue(StatDefOf.PlantWorkSpeed, true);
                if (sowWorkDone >= job.plantDefToSow.plant.sowWork)
                {
                    ReadyForNextToil();
                }
            };
            toil.defaultCompleteMode = ToilCompleteMode.Never;
            toil.WithEffect(EffecterDefOf.Sow, TargetIndex.B);
            toil.PlaySustainerOrSound(() => SoundDefOf.Interact_Sow);
            toil.WithProgressBar(TargetIndex.B, () => sowWorkDone / job.plantDefToSow.plant.sowWork, true);
            // 🔻 NO XP gain, NO activeSkill assignment
            yield return toil;

            // === Perform actual planting ===
            yield return Toils_General.Do(delegate
            {
                PlantableComp.DoPlant(pawn, PlantCell, pawn.Map);
            });
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref sowWorkDone, "sowWorkDone", 0f);
        }
    }
}
