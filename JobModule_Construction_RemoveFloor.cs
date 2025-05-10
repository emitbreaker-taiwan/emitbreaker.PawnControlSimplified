using RimWorld;
using System;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for removing floors with designated for removal
    /// </summary>
    public class JobModule_Construction_RemoveFloor : JobModule_Construction_AffectFloor
    {
        public override string UniqueID => "RemoveFloor";

        // Slightly higher priority than smoothing
        public override float Priority => 5.4f;

        protected override DesignationDef TargetDesignation => DesignationDefOf.RemoveFloor;

        protected override JobDef FloorJobDef => JobDefOf.RemoveFloor;

        /// <summary>
        /// Check if the cell has a removable floor
        /// </summary>
        protected override bool ShouldProcessFloorCell(IntVec3 cell, Map map)
        {
            if (!base.ShouldProcessFloorCell(cell, map))
                return false;

            // Check if the terrain is removable
            TerrainDef terrain = map.terrainGrid.TerrainAt(cell);
            if (terrain == null || !terrain.Removable)
                return false;

            // Skip if cell has a building that would block removal
            Building edifice = cell.GetEdifice(map);
            if (edifice != null && edifice.def.passability == Traversability.Impassable)
                return false;

            return true;
        }

        /// <summary>
        /// Additional validation for floor removal cells
        /// </summary>
        protected override bool ValidateFloorCell(IntVec3 cell, Pawn constructionWorker)
        {
            if (!base.ValidateFloorCell(cell, constructionWorker))
                return false;

            // Check if the terrain is still removable
            TerrainDef terrain = constructionWorker.Map.terrainGrid.TerrainAt(cell);
            if (terrain == null || !terrain.Removable)
                return false;

            // Check if there's a building blocking the removal operation
            Building edifice = cell.GetEdifice(constructionWorker.Map);
            if (edifice != null && edifice.def.passability == Traversability.Impassable)
                return false;

            return true;
        }
    }
}