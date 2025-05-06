using RimWorld;
using System;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to shear animals for wool.
    /// Uses the Handler work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Shear_PawnControl : JobGiver_GatherAnimalBodyResources_PawnControl
    {
        /// <summary>
        /// The JobDef to use for shearing animals
        /// </summary>
        protected override JobDef JobDef => JobDefOf.Shear;

        /// <summary>
        /// Gets the shearable component from the animal
        /// </summary>
        protected override CompHasGatherableBodyResource GetComp(Pawn animal)
        {
            return animal.TryGetComp<CompShearable>();
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static new void ResetCache()
        {
            JobGiver_GatherAnimalBodyResources_PawnControl.ResetCache();
        }

        public override string ToString()
        {
            return "JobGiver_Shear_PawnControl";
        }
    }
}