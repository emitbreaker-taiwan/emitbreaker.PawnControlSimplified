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
    public class JobGiver_PlantCutting_PlantsCut_PawnControl : JobGiver_Scan_PawnControl
    {
        #region Overrides

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        protected override bool RequiresMapZoneorArea => true;

        protected override JobDef WorkJobDef => JobDefOf.CutPlant;

        protected override string WorkTag => "PlantCutting";
        protected override int CacheUpdateInterval => 500;

        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            return GetPlantsNeedingCutting(map).Cast<Thing>();
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Use the StandardTryGiveJob pattern directly
            return Utility_JobGiverManager.StandardTryGiveJob<Plant>(
                pawn,
                "PlantCutting",  // Make sure this matches the name in the WorkTypeDef
                (p, forced) => {
                    // Get plants that need cutting
                    List<Thing> targets = GetTargets(p.Map).ToList();
                    if (targets.Count == 0) return null;

                    // Find the best plant to cut (nearest one that passes validation)
                    Plant best = null;
                    float bestDistSq = float.MaxValue;

                    foreach (var plant in targets.OfType<Plant>())
                    {
                        if (!ValidatePlantTarget(plant, p))
                            continue;

                        float distSq = (plant.Position - p.Position).LengthHorizontalSquared;
                        if (distSq < bestDistSq)
                        {
                            bestDistSq = distSq;
                            best = plant;
                        }
                    }

                    return best != null
                        ? JobMaker.MakeJob(WorkJobDef, best)
                        : null;
                },
                debugJobDesc: "plant cutting assignment");
        }

        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // Find the best plant to cut (nearest one that passes validation)
            Plant best = null;
            float bestDistSq = float.MaxValue;

            foreach (var plant in targets.OfType<Plant>())
            {
                if (!ValidatePlantTarget(plant, pawn))
                    continue;

                float distSq = (plant.Position - pawn.Position).LengthHorizontalSquared;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = plant;
                }
            }

            return best != null
                ? JobMaker.MakeJob(WorkJobDef, best)
                : null;
        }

        #endregion

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

        #region Utility

        public override string ToString()
        {
            return "JobGiver_PlantCutting_PlantsCut_PawnControl";
        }

        #endregion
    }
}