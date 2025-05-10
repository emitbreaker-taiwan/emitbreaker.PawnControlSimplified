using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Plant‐cutting JobGiver with minimal overrides:
    /// uses the standard TryGiveJob wrapper, a cache, and nearest‐plant selection.
    /// </summary>
    public class JobGiver_PlantCutting_PlantsCut_PawnControl : JobGiver_PawnControl
    {
        // 1) Tag used by the base wrapper for eligibility checks
        protected override string WorkTag => "PlantCutting";

        // 2) Cache rebuild interval (~8s) for large plant maps
        protected override int CacheUpdateInterval => 500;

        // 3) Populate cache: all plants that need cutting or harvesting
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            return GetPlantsNeedingCutting(map).Cast<Thing>();
        }

        // 4) From the cached list, pick the nearest valid Plant and return a CutPlant job
        protected override Job ExecuteJobGiverInternal(Pawn pawn, List<Thing> targets)
        {
            Plant bestPlant = null;
            float bestDistSq = float.MaxValue;

            foreach (var thing in targets.OfType<Plant>())
            {
                if (!ValidatePlantTarget(thing, pawn))
                    continue;

                float distSq = (thing.Position - pawn.Position).LengthHorizontalSquared;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestPlant = thing;
                }
            }

            return bestPlant != null
                ? JobMaker.MakeJob(JobDefOf.CutPlant, bestPlant)
                : null;
        }

        #region Plant‐selection helpers

        private IEnumerable<Plant> GetPlantsNeedingCutting(Map map)
        {
            var plants = new List<Plant>();

            // 1) designated Cut/Harvest
            foreach (var des in map.designationManager.AllDesignations)
            {
                if ((des.def == DesignationDefOf.CutPlant || des.def == DesignationDefOf.HarvestPlant)
                    && des.target.Thing is Plant p)
                {
                    plants.Add(p);
                }
            }

            // 2) zone‐based cutting
            foreach (var zone in map.zoneManager.AllZones.OfType<Zone_Growing>())
            {
                if (!zone.allowCut) continue;
                ThingDef growDef = zone.GetPlantDefToGrow();

                foreach (var cell in zone.Cells)
                {
                    var plant = cell.GetPlant(map);
                    if (plant != null
                        && !plants.Contains(plant)
                        && (growDef == null || plant.def != growDef))
                    {
                        plants.Add(plant);
                    }
                }
            }

            // 3) cap to 200 entries
            return plants.Count > 200 ? plants.Take(200) : plants;
        }

        private bool ValidatePlantTarget(Plant plant, Pawn pawn)
        {
            if (plant == null || plant.Destroyed || !plant.Spawned)
                return false;

            bool isDesignated =
                pawn.Map.designationManager.DesignationOn(plant, DesignationDefOf.CutPlant) != null
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

        #endregion
    }
}
