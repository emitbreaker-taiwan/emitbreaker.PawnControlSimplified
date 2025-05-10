using RimWorld;
using System;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Module for milking animals
    /// </summary>
    public class JobModule_Handling_Milk : JobModule_Handling_GatherAnimalBodyResources
    {
        public override string UniqueID => "MilkAnimals";
        public override float Priority => 5.2f; // Same priority as the original JobGiver

        /// <summary>
        /// JobDef for milking animals
        /// </summary>
        protected override JobDef JobDef => JobDefOf.Milk;

        /// <summary>
        /// Gets the milk comp from the animal
        /// </summary>
        protected override CompHasGatherableBodyResource GetComp(Pawn animal)
        {
            return animal?.TryGetComp<CompMilkable>();
        }

        /// <summary>
        /// Get a human-readable name for milk
        /// </summary>
        protected override string GetResourceName(Pawn animal)
        {
            return "milk";
        }
    }
}