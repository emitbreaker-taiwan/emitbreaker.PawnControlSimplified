using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Base abstract class for all hauling job modules
    /// </summary>
    public abstract class JobModule_Hauling : JobModule<Thing>
    {
        // Simple fixed priority (higher number = higher priority)
        public override float Priority => 3.9f;

        /// <summary>
        /// Gets the work type this module is associated with
        /// </summary>
        public override string WorkTypeName => "Hauling";

        /// <summary>
        /// Fast filter check for haulers
        /// </summary>
        public override bool QuickFilterCheck(Pawn pawn)
        {
            return !pawn.WorkTagIsDisabled(WorkTags.Hauling);
        }

        public override bool WorkTypeApplies(Pawn pawn)
        {
            // Quick check based on work settings
            return pawn?.workSettings?.WorkIsActive(WorkTypeDefOf.Hauling) == true;
        }

        /// <summary>
        /// Filter function to identify items for hauling (specially named for hauling jobs)
        /// </summary>
        public abstract bool ShouldHaulItem(Thing item, Map map);

        /// <summary>
        /// Filter function implementation that calls the hauling-specific method
        /// </summary>
        public override bool ShouldProcessTarget(Thing target, Map map) => ShouldHaulItem(target, map);

        /// <summary>
        /// Validates if the pawn can perform this hauling job on the target
        /// </summary>
        public abstract bool ValidateHaulingJob(Thing target, Pawn hauler);

        /// <summary>
        /// Validates job implementation that calls the hauling-specific method
        /// </summary>
        public override bool ValidateJob(Thing target, Pawn actor) => ValidateHaulingJob(target, actor);

        /// <summary>
        /// Creates the hauling job
        /// </summary>
        protected abstract Job CreateHaulingJob(Pawn hauler, Thing target);

        /// <summary>
        /// Creates job implementation that calls the hauling-specific method
        /// </summary>
        public override Job CreateJob(Pawn actor, Thing target) => CreateHaulingJob(actor, target);

        /// <summary>
        /// Helper method to check fuel availability for refuelable things
        /// </summary>
        protected bool CheckFuelAvailability(CompRefuelable refuelable, Pawn hauler)
        {
            if (refuelable == null || hauler?.Map == null) return false;

            // First check resource counter (fast)
            foreach (var resourceCount in hauler.Map.resourceCounter.AllCountedAmounts)
            {
                if (refuelable.Props.fuelFilter.Allows(resourceCount.Key) && resourceCount.Value > 0)
                    return true;
            }

            // Then check specific items (slower)
            return hauler.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver)
                .Any(fuel =>
                    refuelable.Props.fuelFilter.Allows(fuel) &&
                    !fuel.IsForbidden(hauler) &&
                    hauler.CanReserve(fuel) &&
                    hauler.CanReach(fuel, PathEndMode.ClosestTouch, hauler.NormalMaxDanger())
                );
        }

        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups { get; } =
            new HashSet<ThingRequestGroup> { ThingRequestGroup.HaulableEver };
    }
}