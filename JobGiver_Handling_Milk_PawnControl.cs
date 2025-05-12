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
    public class JobGiver_Handling_Milk_PawnControl : JobGiver_Handling_GatherAnimalBodyResources_PawnControl
    {
        #region Overrides

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        protected override bool RequiresMapZoneorArea => false;

        /// <summary>
        /// The JobDef to use for milking animals
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.Milk;

        /// <summary>
        /// Gets the milkable component from the animal
        /// </summary>
        protected override CompHasGatherableBodyResource GetComp(Pawn animal)
        {
            return animal.TryGetComp<CompMilkable>();
        }

        /// <summary>
        /// Overrides the TryGiveJob method from the base class
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            return base.TryGiveJob(pawn);
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return "JobGiver_Handling_Milk_PawnControl";
        }

        #endregion
    }
}