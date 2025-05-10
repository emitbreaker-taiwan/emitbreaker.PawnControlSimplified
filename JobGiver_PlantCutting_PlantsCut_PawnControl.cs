using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Plant‐cutting JobGiver now only implements the two hooks
    /// </summary>
    public class JobGiver_PlantCutting_PlantsCut_PawnControl : JobGiver_PawnControl
    {
        //———— Overrides for wrapper —————————————————————————————————————————

        protected override string WorkTag => "PlantCutting";
        protected override int CacheUpdateInterval => 500; // ~8s between scans

        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            return GetPlantsNeedingCutting(map).Cast<Thing>();
        }

        protected override Job ExecuteJobGiverInternal(Pawn pawn, List<Thing> targets)
        {
            Plant best = null;
            float bestDistSqr = float.MaxValue;

            foreach (var plant in targets.OfType<Plant>())
            {
                if (!ValidatePlantTarget(plant, pawn))
                    continue;

                float distSqr = (plant.Position - pawn.Position).LengthHorizontalSquared;
                if (distSqr < bestDistSqr)
                {
                    bestDistSqr = distSqr;
                    best = plant;
                }
            }

            return best != null
                ? JobMaker.MakeJob(JobDefOf.CutPlant, best)
                : null;
        }

        //———— Plant‐selection details ————————————————————————————————————————

        private IEnumerable<Plant> GetPlantsNeedingCutting(Map map)
        {
            var list = new List<Plant>();

            // 1) designated
            foreach (var des in map.designationManager.AllDesignations)
            {
                if ((des.def == DesignationDefOf.CutPlant || des.def == DesignationDefOf.HarvestPlant)
                    && des.target.Thing is Plant p)
                {
                    list.Add(p);
                }
            }

            // 2) growing‐zone defaults
            foreach (var zone in map.zoneManager.AllZones.OfType<Zone_Growing>())
            {
                if (!zone.allowCut) continue;
                var growDef = zone.GetPlantDefToGrow();
                foreach (var cell in zone.Cells)
                {
                    var p = cell.GetPlant(map);
                    if (p != null
                        && !list.Contains(p)
                        && (growDef == null || p.def != growDef))
                    {
                        list.Add(p);
                    }
                }
            }

            // cap for performance
            return list.Count > 200 ? list.Take(200) : list;
        }

        private bool ValidatePlantTarget(Plant plant, Pawn pawn)
        {
            if (plant == null || plant.Destroyed || !plant.Spawned)
                return false;

            bool isDesignated = pawn.Map.designationManager.DesignationOn(plant, DesignationDefOf.CutPlant) != null
                               || pawn.Map.designationManager.DesignationOn(plant, DesignationDefOf.HarvestPlant) != null;
            if (isDesignated)
            {
                if (pawn.Faction != Faction.OfPlayer)
                    return false;
            }
            else
            {
                var zone = pawn.Map.zoneManager.ZoneAt(plant.Position) as Zone_Growing;
                if (zone == null || !zone.allowCut || pawn.Faction != Faction.OfPlayer)
                    return false;
            }

            if (plant.IsForbidden(pawn) || !PlantUtility.PawnWillingToCutPlant_Job(plant, pawn))
                return false;

            return pawn.CanReserve(plant, 1, -1);
        }
    }
}
