using RimWorld;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Bypasses HaulAIUtility's humanlike & manipulation checks so animals and mechanoids can haul.
    /// </summary>
    public static class Utility_HaulAIManager
    {
        /// <summary>
        /// Like HaulAIUtility.PawnCanAutomaticallyHaulFast, but skips the Manipulation-capacity test.
        /// </summary>
        public static bool PawnCanAutomaticallyHaulFast(Pawn pawn, Thing t, bool forced)
        {
            if (Utility_DebugManager.ShouldLogDetailed())
                Utility_DebugManager.LogNormal($"PawnCanAutomaticallyHaulFast check for {pawn.LabelShort} on {t.Label} (forced: {forced})");

            // Unfinished bills must still obey the vanilla bound-bill logic
            if (t is UnfinishedThing ut && ut.BoundBill != null)
            {
                var giver = ut.BoundBill.billStack.billGiver as Building;
                if (giver != null)
                {
                    if (!giver.Spawned || !giver.OccupiedRect().ExpandedBy(1).Contains(ut.Position))
                    {
                        if (Utility_DebugManager.ShouldLog())
                            Utility_DebugManager.LogNormal($"Cannot haul unfinished thing {t.Label}: Bill giver not spawned or too far from unfinished thing");
                        return false;
                    }
                }

                if (Utility_DebugManager.ShouldLogDetailed())
                    Utility_DebugManager.LogNormal($"Unfinished thing {t.Label} bound bill check passed");
            }

            // Must be reachable & reservable
            if (!pawn.CanReach(t, PathEndMode.ClosestTouch, pawn.NormalMaxDanger()))
            {
                if (Utility_DebugManager.ShouldLog())
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} cannot reach {t.Label} at {t.Position}");
                return false;
            }

            if (!pawn.CanReserve(t, ignoreOtherReservations: forced))
            {
                if (Utility_DebugManager.ShouldLog())
                {
                    string reservedBy = "";
                    if (pawn.Map?.reservationManager != null)
                    {
                        Pawn reserver = pawn.Map.reservationManager.FirstRespectedReserver(t, pawn);
                        if (reserver != null)
                            reservedBy = $" (reserved by {reserver.LabelShort})";
                    }

                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} cannot reserve {t.Label}{reservedBy}");
                }
                return false;
            }

            if (Utility_DebugManager.ShouldLogDetailed())
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} can reach and reserve {t.Label}");

            // Skip the Manipulation capacity test entirely
            if (Utility_DebugManager.ShouldLogDetailed())
                Utility_DebugManager.LogNormal($"Skipping manipulation capacity check for {pawn.LabelShort}");

            // Prisoner food logic still applies
            if (t.def.IsNutritionGivingIngestible && t.def.ingestible.HumanEdible
                && !t.IsSociallyProper(pawn, false, true))
            {
                if (Utility_DebugManager.ShouldLog())
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} cannot haul food {t.Label}: not socially proper");
                return false;
            }

            // No hauling burning things
            if (t.IsBurning())
            {
                if (Utility_DebugManager.ShouldLog())
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} cannot haul burning thing {t.Label}");
                return false;
            }

            if (Utility_DebugManager.ShouldLogDetailed())
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} CAN automatically haul {t.Label}");

            return true;
        }

        /// <summary>
        /// Passes through to vanilla HaulToStorageJob (no additional guards).
        /// </summary>
        public static Job HaulToStorageJob(Pawn pawn, Thing t)
        {
            if (Utility_DebugManager.ShouldLog())
                Utility_DebugManager.LogNormal($"Creating haul job for {pawn.LabelShort} to haul {t.Label} ({t.def.defName})");

            Job job = HaulAIUtility.HaulToStorageJob(pawn, t);

            if (Utility_DebugManager.ShouldLog())
            {
                if (job != null)
                {
                    IntVec3 storeCell = job.targetB.Cell;
                    Utility_DebugManager.LogNormal($"Created haul job for {pawn.LabelShort}: {job.def.defName} for {t.Label} to {storeCell}");

                    // Get storage building info if possible
                    if (storeCell.IsValid && pawn.Map != null)
                    {
                        SlotGroup slotGroup = pawn.Map.haulDestinationManager.SlotGroupAt(storeCell);
                        if (slotGroup != null && slotGroup.parent != null)
                        {
                            Utility_DebugManager.LogNormal($"Storage destination: {slotGroup.parent}");
                        }
                    }
                }
                else
                {
                    Utility_DebugManager.LogWarning($"Failed to create haul job for {pawn.LabelShort} to haul {t.Label}");

                    // Try to provide more context on why the job creation failed
                    if (StoreUtility.TryFindBestBetterStoreCellFor(t, pawn, pawn.Map,
                        StoreUtility.CurrentStoragePriorityOf(t), pawn.Faction, out IntVec3 _))
                    {
                        Utility_DebugManager.LogNormal("Better storage location exists but job creation failed");
                    }
                    else
                    {
                        Utility_DebugManager.LogNormal("No better storage location found");
                    }
                }
            }

            return job;
        }

        /// <summary>
        /// Diagnostic method to check storage options for a thing
        /// </summary>
        public static void DiagnoseStorageOptions(Pawn pawn, Thing t)
        {
            if (!Utility_DebugManager.ShouldLog()) return;

            Utility_DebugManager.LogNormal($"=== Storage diagnosis for {t.Label} ===");
            Utility_DebugManager.LogNormal($"Current cell: {t.Position}");
            Utility_DebugManager.LogNormal($"Current storage priority: {StoreUtility.CurrentStoragePriorityOf(t)}");

            // Check forbidden status
            Utility_DebugManager.LogNormal($"Is forbidden: {t.IsForbidden(pawn)}");

            // Check if better storage exists
            bool betterStoreFound = StoreUtility.TryFindBestBetterStoreCellFor(t, pawn, pawn.Map,
                StoreUtility.CurrentStoragePriorityOf(t), pawn.Faction, out IntVec3 foundCell);

            Utility_DebugManager.LogNormal($"Better storage found: {betterStoreFound} {(betterStoreFound ? "at " + foundCell : "")}");

            // Check storage groups that accept this thing
            int storageCount = 0;
            foreach (var slotGroup in pawn.Map.haulDestinationManager.AllGroupsListForReading)
            {
                if (slotGroup.parent.Accepts(t))
                {
                    storageCount++;
                    if (storageCount <= 5) // Limit detailed reporting to 5 storage options
                    {
                        Utility_DebugManager.LogNormal($"Accepting storage: {slotGroup.parent} (priority {slotGroup.Settings.Priority})");
                    }
                }
            }

            Utility_DebugManager.LogNormal($"Total accepting storage options: {storageCount}");
            Utility_DebugManager.LogNormal("=== End storage diagnosis ===");
        }
    }
}