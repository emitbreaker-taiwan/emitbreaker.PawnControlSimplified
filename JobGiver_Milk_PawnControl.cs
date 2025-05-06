using RimWorld;
using System;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to milk animals.
    /// Uses the Handler work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Milk_PawnControl : JobGiver_GatherAnimalBodyResources_PawnControl
    {
        /// <summary>
        /// The JobDef to use for milking animals
        /// </summary>
        protected override JobDef JobDef => JobDefOf.Milk;

        /// <summary>
        /// Gets the milkable component from the animal
        /// </summary>
        protected override CompHasGatherableBodyResource GetComp(Pawn animal)
        {
            return animal.TryGetComp<CompMilkable>();
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
            return "JobGiver_Milk_PawnControl";
        }
    }
}