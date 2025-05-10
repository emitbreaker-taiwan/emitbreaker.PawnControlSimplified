using RimWorld;
using System;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for shearing animals
    /// </summary>
    public class JobModule_Handling_Shear : JobModule_Handling_GatherAnimalBodyResources
    {
        public override string UniqueID => "ShearAnimals";
        public override float Priority => 5.1f; // Slightly lower priority than milking

        /// <summary>
        /// JobDef for shearing animals
        /// </summary>
        protected override JobDef JobDef => JobDefOf.Shear;

        /// <summary>
        /// Gets the shearable comp from the animal
        /// </summary>
        protected override CompHasGatherableBodyResource GetComp(Pawn animal)
        {
            return animal?.TryGetComp<CompShearable>();
        }

        /// <summary>
        /// Get a human-readable name for wool
        /// </summary>
        protected override string GetResourceName(Pawn animal)
        {
            return "wool";
        }
    }
}