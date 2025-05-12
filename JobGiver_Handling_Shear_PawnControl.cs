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
    public class JobGiver_Handling_Shear_PawnControl : JobGiver_Handling_GatherAnimalBodyResources_PawnControl
    {
        #region Overrides

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        protected override bool RequiresMapZoneorArea => false;

        /// <summary>
        /// The JobDef to use for shearing animals
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.Shear;

        /// <summary>
        /// Gets the shearable component from the animal
        /// </summary>
        protected override CompHasGatherableBodyResource GetComp(Pawn animal)
        {
            return animal.TryGetComp<CompShearable>();
        }

        /// <summary>
        /// Override TryGiveJob to implement shearing-specific logic if needed
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            // Leverage the base class implementation which already uses Utility_JobGiverManager
            return base.TryGiveJob(pawn);
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return "JobGiver_Handling_Shear_PawnControl";
        }

        #endregion
    }
}