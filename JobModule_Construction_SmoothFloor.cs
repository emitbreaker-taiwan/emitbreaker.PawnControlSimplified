using RimWorld;
using System;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for smoothing floors with designated for smoothing
    /// </summary>
    public class JobModule_Construction_SmoothFloor : JobModule_Construction_AffectFloor
    {
        public override string UniqueID => "SmoothFloor";

        // Slightly lower priority than other construction tasks
        public override float Priority => 5.3f;

        protected override DesignationDef TargetDesignation => DesignationDefOf.SmoothFloor;

        protected override JobDef FloorJobDef => JobDefOf.SmoothFloor;

        /// <summary>
        /// Check if the cell has a smoothable terrain
        /// </summary>
        protected override bool ShouldProcessFloorCell(IntVec3 cell, Map map)
        {
            if (!base.ShouldProcessFloorCell(cell, map))
                return false;

            // Check if the cell has a smoothable terrain
            TerrainDef terrain = map.terrainGrid.TerrainAt(cell);
            if (terrain == null || terrain.smoothedTerrain == null)
                return false;

            // Skip if cell already has a building that would block smoothing
            Building edifice = cell.GetEdifice(map);
            if (edifice != null && edifice.def.passability == Traversability.Impassable)
                return false;

            return true;
        }

        /// <summary>
        /// Additional validation for smooth floor cells
        /// </summary>
        protected override bool ValidateFloorCell(IntVec3 cell, Pawn constructionWorker)
        {
            if (!base.ValidateFloorCell(cell, constructionWorker))
                return false;

            // Check if the terrain is still smoothable
            TerrainDef terrain = constructionWorker.Map.terrainGrid.TerrainAt(cell);
            if (terrain == null || terrain.smoothedTerrain == null)
                return false;

            // Check if there's a building blocking the smoothing operation
            Building edifice = cell.GetEdifice(constructionWorker.Map);
            if (edifice != null && edifice.def.passability == Traversability.Impassable)
                return false;

            return true;
        }
    }
}