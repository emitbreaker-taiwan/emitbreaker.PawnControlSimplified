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
    public class PawnControl_JobDriver_PlantSow : JobDriver
    {
        private float sowWorkDone;

        private Plant Plant => (Plant)job.GetTarget(TargetIndex.A).Thing;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref sowWorkDone, "sowWorkDone", 0f);
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // Move to cell with validation
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.Touch)
                .FailOn((Toil toil) => PlantUtility.AdjacentSowBlocker(job.plantDefToSow, TargetA.Cell, Map) != null)
                .FailOn((Toil toil) => !job.plantDefToSow.CanNowPlantAt(TargetLocA, Map))
                .FailOn((Toil toil) =>
                {
                    List<Thing> thingList = TargetA.Cell.GetThingList(Map);
                    for (int i = 0; i < thingList.Count; i++)
                    {
                        if (thingList[i].def == job.plantDefToSow)
                            return true;
                    }
                    return false;
                });

            // Sow Toil
            Toil sowToil = ToilMaker.MakeToil("SowToil");
            sowToil.initAction = delegate
            {
                TargetThingA = GenSpawn.Spawn(job.plantDefToSow, TargetLocA, Map);
                pawn.Reserve(TargetThingA, sowToil.actor.CurJob);
                Plant plant = Plant;
                plant.Growth = 0f;
                plant.sown = true;
            };

            sowToil.tickAction = delegate
            {
                var actor = sowToil.actor;
                int simulatedSkill = Utility_SkillManager.SetInjectedSkillLevel(actor);
                float speedFactor = actor.GetStatValue(StatDefOf.PlantWorkSpeed);

                // Learning disabled — replaced with simulation only
                Plant plant = Plant;
                if (plant.LifeStage != PlantLifeStage.Sowing)
                {
                    Log.Error(this + " getting sowing work while not in Sowing life stage.");
                }

                sowWorkDone += speedFactor;

                if (sowWorkDone >= plant.def.plant.sowWork)
                {
                    plant.Growth = 0.0001f;
                    Map.mapDrawer.MapMeshDirty(plant.Position, MapMeshFlagDefOf.Things);
                    actor.records.Increment(RecordDefOf.PlantsSown);

                    Find.HistoryEventsManager.RecordEvent(
                        new HistoryEvent(HistoryEventDefOf.SowedPlant, actor.Named(HistoryEventArgsNames.Doer)));

                    if (plant.def.plant.humanFoodPlant)
                    {
                        Find.HistoryEventsManager.RecordEvent(
                            new HistoryEvent(HistoryEventDefOf.SowedHumanFoodPlant, actor.Named(HistoryEventArgsNames.Doer)));
                    }

                    ReadyForNextToil();
                }
            };

            sowToil.defaultCompleteMode = ToilCompleteMode.Never;
            sowToil.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            sowToil.FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);
            sowToil.WithEffect(EffecterDefOf.Sow, TargetIndex.A);
            sowToil.WithProgressBar(TargetIndex.A, () => sowWorkDone / Plant.def.plant.sowWork, interpolateBetweenActorAndTarget: true);
            sowToil.PlaySustainerOrSound(() => SoundDefOf.Interact_Sow);
            sowToil.AddFinishAction(delegate
            {
                if (TargetThingA != null && !TargetThingA.Destroyed)
                {
                    var plant = (Plant)job.GetTarget(TargetIndex.A).Thing;
                    if (sowWorkDone < plant.def.plant.sowWork)
                    {
                        TargetThingA.Destroy();
                    }
                }
            });

            // Use simulated skill, but skip actual assignment since this only controls job tooltip skill icon
            sowToil.activeSkill = () => SkillDefOf.Plants;

            yield return sowToil;
        }
    }
}