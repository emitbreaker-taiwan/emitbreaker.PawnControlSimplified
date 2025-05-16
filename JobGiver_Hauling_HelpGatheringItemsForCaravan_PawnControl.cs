using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to help gather items for forming caravans.
    /// Uses the Hauling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Hauling_HelpGatheringItemsForCaravan_PawnControl : JobGiver_Hauling_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        public override bool RequiresMapZoneorArea => false;

        /// <summary>
        /// Human-readable name for debug logging
        /// </summary>
        protected override string DebugName => "HelpGatheringItemsForCaravan";

        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.PrepareCaravan_GatherItems;

        // Use higher priority since caravan forming is important
        protected override float GetBasePriority(string workTag)
        {
            return 6.0f;
        }

        // Use shorter cache update interval for more responsive caravan formation
        protected override int CacheUpdateInterval => base.CacheUpdateInterval;

        #endregion

        #region Core flow

        /// <summary>
        /// Pre-filter pawns to ensure only valid candidates attempt caravan jobs
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            // IMPORTANT: Only player pawns and slaves owned by player should gather items for caravans
            if (pawn.Faction != Faction.OfPlayer &&
                !(pawn.IsSlave && pawn.HostFaction == Faction.OfPlayer))
                return null;

            if (ShouldSkip(pawn))
            {
                return null;
            }

            // Don't assign caravan gathering jobs to pawns already forming caravans
            if (pawn.IsFormingCaravan())
            {
                return null;
            }

            return base.TryGiveJob(pawn);
        }

        /// <summary>
        /// Process cached targets to create jobs for gathering caravan items
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            if (pawn?.Map == null || targets == null || targets.Count == 0)
                return null;

            // Process each target (which contains a Lord)
            foreach (Thing target in targets)
            {
                // We need to use a LordWrapper object here
                LordWrapper wrapper = target as LordWrapper;
                if (wrapper == null) continue;

                // Get the lord from the wrapper
                Lord lord = wrapper.Lord;
                if (lord == null) continue;

                // Find a thing to haul for this caravan
                Thing thingToHaul = FindThingToHaul(pawn, lord);
                if (thingToHaul != null)
                {
                    // Check if there's a reachable carrier or colonist
                    if (AnyReachableCarrierOrColonist(pawn, lord))
                    {
                        Job job = JobMaker.MakeJob(WorkJobDef, thingToHaul);
                        job.lord = lord;

                        Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to gather {thingToHaul.LabelCap} for caravan");
                        return job;
                    }
                }
            }

            // Return null if no valid job was created
            return null;
        }

        #endregion

        #region Target selection

        /// <summary>
        /// Gets all active caravan lords that are in the gathering phase as targets
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map?.lordManager?.lords == null)
                yield break;

            // Find all lords that are forming caravans and gathering items
            foreach (Lord lord in map.lordManager.lords)
            {
                if (lord.LordJob is LordJob_FormAndSendCaravan caravanLordJob &&
                    caravanLordJob.GatheringItemsNow)
                {
                    // Create a wrapper that implements Thing to store the Lord
                    yield return new LordWrapper(lord);
                }
            }
        }

        #endregion

        #region Lord wrapper class

        /// <summary>
        /// Wrapper class to make Lords compatible with the Thing-based job system
        /// </summary>
        private class LordWrapper : Thing
        {
            public Lord Lord { get; private set; }

            public LordWrapper(Lord lord)
            {
                this.Lord = lord;
            }
        }

        #endregion

        #region Caravan-specific methods

        /// <summary>
        /// Find a thing to haul for the caravan
        /// </summary>
        private Thing FindThingToHaul(Pawn pawn, Lord lord)
        {
            // Use the utility method if available
            try
            {
                Thing utilityResult = GatherItemsForCaravanUtility.FindThingToHaul(pawn, lord);
                if (utilityResult != null)
                    return utilityResult;
            }
            catch (Exception ex)
            {
                // Fallback to our own implementation if the utility method fails
                Utility_DebugManager.LogWarning($"Error using GatherItemsForCaravanUtility.FindThingToHaul: {ex.Message}");
            }

            // Fallback implementation
            LordJob_FormAndSendCaravan caravanLordJob = lord.LordJob as LordJob_FormAndSendCaravan;
            if (caravanLordJob == null) return null;

            // Look for things to haul among the transferables
            foreach (TransferableOneWay transferable in caravanLordJob.transferables)
            {
                if (transferable.CountToTransfer <= 0) continue;

                int leftToTransfer = transferable.CountToTransfer;
                foreach (Thing alreadyTransferred in lord.ownedPawns
                    .Where(p => p.inventory != null)
                    .SelectMany(p => p.inventory.innerContainer)
                    .Where(t => t.GetInnerIfMinified().def == transferable.ThingDef))
                {
                    leftToTransfer -= alreadyTransferred.stackCount;
                }

                if (leftToTransfer <= 0) continue;

                // Find a thing matching the transferable that can be hauled
                foreach (Thing thing in transferable.things)
                {
                    if (!thing.Spawned || thing.IsForbidden(pawn)) continue;

                    if (pawn.CanReserve(thing) && pawn.CanReach(thing, PathEndMode.Touch, Danger.Deadly))
                    {
                        return thing;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Check if there's any reachable carrier or colonist in the lord
        /// </summary>
        private bool AnyReachableCarrierOrColonist(Pawn forPawn, Lord lord)
        {
            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                Pawn ownedPawn = lord.ownedPawns[i];
                if (IsUsableCarrier(ownedPawn, forPawn, false) &&
                    !ownedPawn.IsForbidden(forPawn) &&
                    forPawn.CanReach(ownedPawn, PathEndMode.Touch, Danger.Deadly))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Check if a pawn can be used as a carrier
        /// </summary>
        private bool IsUsableCarrier(Pawn p, Pawn forPawn, bool allowColonists)
        {
            if (!p.IsFormingCaravan())
                return false;

            if (p == forPawn)
                return true;

            if (p.DestroyedOrNull() || !p.Spawned || p.inventory.UnloadEverything ||
                !forPawn.CanReach(p, PathEndMode.Touch, Danger.Deadly))
                return false;

            if (allowColonists && p.IsColonist)
                return true;

            return (p.RaceProps.packAnimal || p.HostFaction == Faction.OfPlayer) &&
                   !p.IsBurning() && !p.Downed && !MassUtility.IsOverEncumbered(p);
        }

        #endregion

        #region Reset

        /// <summary>
        /// Reset the cache - implements IResettableCache
        /// </summary>
        public override void Reset()
        {
            // Call the parent class's Reset method to use the centralized cache system
            base.Reset();

            // Log the reset for debugging
            Utility_DebugManager.LogNormal($"Reset {GetType().Name} cache");
        }

        /// <summary>
        /// Static method to reset the caravan cache
        /// </summary>
        public static void ResetCache()
        {
            // Any additional cache clearing logic specific to this class
        }

        #endregion

        #region Utility

        /// <summary>
        /// Returns a descriptive string for debugging
        /// </summary>
        public override string ToString()
        {
            return "JobGiver_Hauling_HelpGatheringItemsForCaravan_PawnControl";
        }

        #endregion
    }
}