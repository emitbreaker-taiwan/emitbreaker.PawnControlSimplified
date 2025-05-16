using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Handles the injection of draft controllers into non-humanlike pawns
    /// </summary>
    public static class Utility_DrafterManager
    {
        private static readonly FieldInfo EquipmentField = AccessTools.Field(typeof(Pawn), "equipment");
        private static readonly FieldInfo ApparelField = AccessTools.Field(typeof(Pawn), "apparel");
        private static readonly FieldInfo InventoryField = AccessTools.Field(typeof(Pawn), "inventory");

        /// <summary>
        /// Injects any missing trackers onto the pawn so Gear/Equip/Apparel UI never NREs.
        /// </summary>
        public static void EnsureAllTrackers(Pawn pawn)
        {
            if (!Utility_Common.RaceDefChecker(pawn.def)) return;

            bool wasTrackerInjected = false;

            // equipment
            if (EquipmentField.GetValue(pawn) == null)
            {
                var eq = new Pawn_EquipmentTracker(pawn);
                EquipmentField.SetValue(pawn, eq);
                eq.Notify_PawnSpawned();
                wasTrackerInjected = true;
            }

            // apparel
            if (ApparelField.GetValue(pawn) == null)
            {
                var ap = new Pawn_ApparelTracker(pawn);
                ApparelField.SetValue(pawn, ap);
                wasTrackerInjected = true;
            }

            // inventory
            if (InventoryField.GetValue(pawn) == null)
            {
                var inv = new Pawn_InventoryTracker(pawn);
                InventoryField.SetValue(pawn, inv);
                wasTrackerInjected = true;
            }

            // Record that this pawn had trackers injected
            if (wasTrackerInjected)
                _pawnsWithInjectedTrackers.Add(pawn.thingIDNumber);
        }

        /// <summary>
        /// Determines if a pawn should have a draft controller injected
        /// </summary>
        public static bool ShouldInjectDrafter(Pawn pawn, NonHumanlikePawnControlExtension modExtension)
        {
            if (!Utility_Common.RaceDefChecker(pawn.def) || pawn.drafter != null)
            {
                return false; // Invalid pawn or missing definitions or already has a drafter
            }

            if (modExtension == null)
            {
                return false; // No mod extension found
            }

            // Drafter should be injected if the pawn is valid, has no drafter, and has either
            // the AutoDraftInjection tag or the forceDraftable flag
            return Utility_TagManager.ForceDraftable(pawn);
        }

        /// <summary>
        /// Ensures that a pawn's draft controller is properly set up and connected
        /// </summary>
        // In Utility_DrafterManager.cs
        public static void EnsureDrafter(Pawn pawn, NonHumanlikePawnControlExtension modExtension, bool isSpawnSetup = false)
        {
            if (!Utility_Common.RaceDefChecker(pawn.def) || pawn.Dead || pawn.Destroyed)
            {
                return;
            }

            // Allow operation even for non-spawned pawns during world load
            if (!isSpawnSetup && !pawn.Spawned)
            {
                return; // Pawn is not spawned, no need to inject drafter
            }

            if (modExtension == null)
            {
                return; // No mod extension found, no drafter needed
            }

            // Always add a drafter if forceDraftable is true - log this decision
            bool shouldAddDrafter = modExtension.forceDraftable;
            if (pawn.Faction == Faction.OfPlayer && Utility_DebugManager.ShouldLogDetailed())
                Utility_DebugManager.LogNormal($"Should add drafter to {pawn.LabelShort}? {shouldAddDrafter}");

            if (!shouldAddDrafter)
            {
                return;
            }

            // Repair any existing controller that lost its `pawn` ref during load
            if (pawn.drafter != null)
            {
                var pawnField = AccessTools.Field(typeof(Pawn_DraftController), "pawn");
                pawnField?.SetValue(pawn.drafter, pawn);
                if (Utility_DebugManager.ShouldLogDetailed())
                    Utility_DebugManager.LogNormal($"Updated existing drafter for {pawn.LabelShort}");
                return;
            }

            try
            {
                // Create and assign a new draft controller
                pawn.drafter = new Pawn_DraftController(pawn);
                Utility_DebugManager.LogNormal($"Injected drafter for {pawn.LabelShort} ({pawn.def.defName})");

                // Make sure the draft controller is properly initialized
                pawn.drafter.ExposeData();
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error injecting drafter for {pawn.LabelShort}: {ex}");
            }
        }

        /// <summary>
        /// Injects draft controllers into all eligible pawns on a map
        /// </summary>
        public static void InjectDraftersIntoMapPawns(Map map)
        {
            if (map?.mapPawns?.AllPawnsSpawned == null)
            {
                return;
            }

            int injected = 0;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn == null || pawn.def == null || pawn.RaceProps == null || pawn.Dead || pawn.Destroyed)
                {
                    continue;
                }

                // Skip humanlike pawns as they already have drafters
                if (pawn.RaceProps.Humanlike)
                {
                    continue;
                }

                // Skip pawns that already have drafters
                if (pawn.drafter != null)
                {
                    continue;
                }

                var modExtension = Utility_UnifiedCache.GetModExtension(pawn.def);
                if (modExtension == null)
                {
                    continue;
                }

                // Check if this pawn should get a drafter
                if (ShouldInjectDrafter(pawn, modExtension))
                {
                    EnsureDrafter(pawn, modExtension);
                    injected++;
                }
            }

            if (injected > 0 && Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Injected drafters into {injected} non-humanlike pawns on map {map.uniqueID}");
            }
        }

        /// <summary>
        /// Determines the appropriate duty for a pawn in a siege scenario
        /// </summary>
        public static DutyDef ResolveSiegeDuty(Pawn p)
        {
            if (Utility_TagManager.HasTag(p.def, "Siege_HoldFire"))
            {
                var holdFire = Utility_UnifiedCache.GetDuty("HoldFire");
                if (holdFire != null)
                {
                    return holdFire;
                }
            }

            if (Utility_TagManager.HasTag(p.def, "Siege_ManTurret"))
            {
                var manTurrets = Utility_UnifiedCache.GetDuty("ManTurrets");
                if (manTurrets != null)
                {
                    return manTurrets;
                }
            }

            return DutyDefOf.Defend;
        }

        /// <summary>
        /// Cleans up trackers for all pawns of the specified race, safely handling items
        /// </summary>
        public static void CleanupTrackersForRace(ThingDef raceDef)
        {
            if (raceDef == null) return;

            // Safety check for Find.Maps
            if (Find.Maps == null) return;

            // Don't clean up trackers for humanlike pawns - they should always have them
            if (raceDef.race != null && raceDef.race.Humanlike)
            {
                Utility_DebugManager.LogNormal($"Skipping cleanup for humanlike race: {raceDef.defName}");
                return;
            }

            bool cleanedAny = false;
            int totalItemsDropped = 0;

            // Part 1: Clean up pawns in loaded maps
            if (Find.Maps != null)
            {
                foreach (Map map in Find.Maps)
                {
                    if (map?.mapPawns?.AllPawnsSpawned == null) continue;

                    foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                    {
                        try
                        {
                            if (pawn == null || pawn.def != raceDef || pawn.Dead || pawn.Destroyed)
                                continue;

                            cleanedAny = true;

                            // 1. First force drop all items - this ensures items get dropped even if there's an error later
                            int dropped = ForceDropAllItems(pawn);
                            totalItemsDropped += dropped;

                            // 2. Reset think trees back to vanilla for this pawn
                            Utility_ThinkTreeManager.ResetThinkTreeToVanilla(pawn);

                            // 3. Clean up and remove trackers
                            SafeCleanupTrackers(pawn);
                        }
                        catch (Exception ex)
                        {
                            Utility_DebugManager.LogError($"Error cleaning up trackers for {pawn?.LabelShort ?? "unknown pawn"}: {ex}");
                        }
                    }
                }
            }

            // Part 2: Clean up world pawns (similar pattern)
            if (Find.World?.worldPawns != null)
            {
                // Get all world pawns of the specified race that are alive
                List<Pawn> worldPawns = Find.World.worldPawns.AllPawnsAliveOrDead
                    .Where(p => p != null && !p.Dead && !p.Destroyed && p.def == raceDef)
                    .ToList();

                foreach (Pawn pawn in worldPawns)
                {
                    try
                    {
                        cleanedAny = true;

                        // 1. First force drop all items
                        if (pawn.Spawned && pawn.Map != null)
                        {
                            int dropped = ForceDropAllItems(pawn);
                            totalItemsDropped += dropped;
                        }

                        // 2. Reset think trees back to vanilla for this pawn
                        Utility_ThinkTreeManager.ResetThinkTreeToVanilla(pawn);

                        // 3. Clean up and remove trackers
                        SafeCleanupTrackers(pawn);
                    }
                    catch (Exception ex)
                    {
                        Utility_DebugManager.LogError($"Error cleaning up world pawn trackers for {pawn?.LabelShort ?? "unknown pawn"}: {ex}");
                    }
                }
            }

            if (cleanedAny)
            {
                Utility_DebugManager.LogNormal($"Cleaned up trackers for race: {raceDef.defName}, dropped {totalItemsDropped} items");
            }
        }

        // Track which pawns have had trackers injected
        private static readonly HashSet<int> _pawnsWithInjectedTrackers = new HashSet<int>();

        /// <summary>
        /// Checks if trackers were previously injected for this pawn
        /// </summary>
        public static bool WasTrackerInjected(Pawn pawn)
        {
            return pawn != null && _pawnsWithInjectedTrackers.Contains(pawn.thingIDNumber);
        }

        /// <summary>
        /// Safely cleans up trackers for a specific pawn, ensuring all inventory is forcibly dropped
        /// </summary>
        public static void SafeCleanupTrackers(Pawn pawn)
        {
            if (pawn == null) return;

            try
            {
                // Track if this pawn had trackers that were injected
                if (pawn.equipment != null || pawn.apparel != null || pawn.inventory != null)
                    _pawnsWithInjectedTrackers.Add(pawn.thingIDNumber);

                // Keep track of total items dropped for logging
                int itemsDropped = 0;
                Map dropMap = pawn.Map;

                // 1. Handle equipment safely
                if (pawn.equipment != null)
                {
                    List<ThingWithComps> equipmentToMove = pawn.equipment.AllEquipmentListForReading?.ToList() ?? new List<ThingWithComps>();

                    foreach (ThingWithComps eq in equipmentToMove)
                    {
                        try
                        {
                            if (eq == null) continue;

                            // Remove from equipment tracker
                            pawn.equipment.Remove(eq);

                            // Drop on ground if possible
                            if (!eq.Destroyed && dropMap != null)
                            {
                                if (GenDrop.TryDropSpawn(eq, pawn.Position, dropMap, ThingPlaceMode.Near, out Thing droppedThing))
                                {
                                    itemsDropped++;
                                    if (Prefs.DevMode)
                                        Utility_DebugManager.LogNormal($"Dropped equipment {eq.LabelCap} from {pawn.LabelShort}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Utility_DebugManager.LogError($"Error removing equipment {eq?.LabelCap ?? "unknown"}: {ex}");
                        }
                    }

                    // Null out the equipment field
                    EquipmentField.SetValue(pawn, null);
                }

                // 2. Handle apparel safely
                if (pawn.apparel != null)
                {
                    List<Apparel> apparelToMove = pawn.apparel.WornApparel?.ToList() ?? new List<Apparel>();

                    foreach (Apparel ap in apparelToMove)
                    {
                        try
                        {
                            if (ap == null) continue;

                            // Remove from apparel tracker
                            pawn.apparel.Remove(ap);

                            // Drop on ground if possible
                            if (!ap.Destroyed && dropMap != null)
                            {
                                if (GenDrop.TryDropSpawn(ap, pawn.Position, dropMap, ThingPlaceMode.Near, out Thing droppedThing))
                                {
                                    itemsDropped++;
                                    if (Prefs.DevMode)
                                        Utility_DebugManager.LogNormal($"Dropped apparel {ap.LabelCap} from {pawn.LabelShort}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Utility_DebugManager.LogError($"Error removing apparel {ap?.LabelCap ?? "unknown"}: {ex}");
                        }
                    }

                    // Null out the apparel field
                    ApparelField.SetValue(pawn, null);
                }

                // 3. Handle inventory safely - ensure all items are dropped
                if (pawn.inventory != null && pawn.inventory.innerContainer != null)
                {
                    List<Thing> itemsToMove = pawn.inventory.innerContainer.ToList();

                    foreach (Thing item in itemsToMove)
                    {
                        try
                        {
                            if (item == null) continue;

                            // Remove from inventory
                            pawn.inventory.innerContainer.Remove(item);

                            // Drop on ground if possible
                            if (!item.Destroyed && dropMap != null)
                            {
                                if (GenDrop.TryDropSpawn(item, pawn.Position, dropMap, ThingPlaceMode.Near, out Thing droppedThing))
                                {
                                    itemsDropped++;
                                    if (Prefs.DevMode)
                                        Utility_DebugManager.LogNormal($"Dropped inventory item {item.LabelCap} from {pawn.LabelShort}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Utility_DebugManager.LogError($"Error removing inventory item {item?.LabelCap ?? "unknown"}: {ex}");
                        }
                    }

                    // Null out the inventory field
                    InventoryField.SetValue(pawn, null);
                }

                // 4. If we also need to remove the drafter
                if (pawn.drafter != null)
                {
                    // Force undraft first to prevent issues
                    try
                    {
                        if (pawn.drafter.Drafted)
                            pawn.drafter.Drafted = false;
                    }
                    catch (Exception ex)
                    {
                        Utility_DebugManager.LogError($"Error unforcing draft state: {ex}");
                    }

                    // Get the drafter field via reflection
                    try
                    {
                        var drafterField = AccessTools.Field(typeof(Pawn), "drafter");
                        drafterField?.SetValue(pawn, null);
                    }
                    catch (Exception ex)
                    {
                        Utility_DebugManager.LogError($"Error removing drafter: {ex}");
                    }
                }

                // Log total items dropped
                if (itemsDropped > 0)
                {
                    Utility_DebugManager.LogNormal($"Successfully dropped {itemsDropped} items from {pawn.LabelShort} during tracker cleanup");
                }
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error in SafeCleanupTrackers for {pawn.LabelShort}: {ex}");
            }
        }

        /// <summary>
        /// Forcibly drops all carried items from a pawn onto the ground, regardless of tracker state
        /// </summary>
        public static int ForceDropAllItems(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed)
                return 0;

            int itemsDropped = 0;
            Map dropMap = pawn.Map;

            // Can't drop items if not on a map
            if (dropMap == null)
                return 0;

            try
            {
                // 1. Drop equipment first
                if (pawn.equipment != null)
                {
                    List<ThingWithComps> equipment = pawn.equipment.AllEquipmentListForReading?.ToList() ?? new List<ThingWithComps>();
                    foreach (ThingWithComps eq in equipment)
                    {
                        if (eq == null) continue;

                        try
                        {
                            pawn.equipment.Remove(eq);
                            if (!eq.Destroyed && GenDrop.TryDropSpawn(eq, pawn.Position, dropMap, ThingPlaceMode.Near, out _))
                                itemsDropped++;
                        }
                        catch { }  // Silently continue on individual item errors
                    }
                }

                // 2. Drop apparel 
                if (pawn.apparel != null)
                {
                    List<Apparel> apparel = pawn.apparel.WornApparel?.ToList() ?? new List<Apparel>();
                    foreach (Apparel ap in apparel)
                    {
                        if (ap == null) continue;

                        try
                        {
                            pawn.apparel.Remove(ap);
                            if (!ap.Destroyed && GenDrop.TryDropSpawn(ap, pawn.Position, dropMap, ThingPlaceMode.Near, out _))
                                itemsDropped++;
                        }
                        catch { }  // Silently continue on individual item errors
                    }
                }

                // 3. Drop inventory items
                if (pawn.inventory?.innerContainer != null)
                {
                    List<Thing> items = pawn.inventory.innerContainer.ToList();
                    foreach (Thing item in items)
                    {
                        if (item == null) continue;

                        try
                        {
                            pawn.inventory.innerContainer.Remove(item);
                            if (!item.Destroyed && GenDrop.TryDropSpawn(item, pawn.Position, dropMap, ThingPlaceMode.Near, out _))
                                itemsDropped++;
                        }
                        catch { }  // Silently continue on individual item errors
                    }
                }
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error in ForceDropAllItems for {pawn.LabelShort}: {ex}");
            }

            return itemsDropped;
        }

        /// <summary>
        /// Forces a specific pawn to drop all items and removes all trackers
        /// For just in case.
        /// </summary>
        public static void PurgeAllTrackersAndDropItems(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed)
                return;

            try
            {
                // 1. Force drop all items first
                int itemsDropped = ForceDropAllItems(pawn);

                // 2. Reset think tree if needed
                Utility_ThinkTreeManager.ResetThinkTreeToVanilla(pawn);

                // 3. Clean up trackers
                SafeCleanupTrackers(pawn);

                // Log result
                Utility_DebugManager.LogNormal($"Purged all trackers and dropped {itemsDropped} items from {pawn.LabelShort}");
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error in PurgeAllTrackersAndDropItems for {pawn.LabelShort}: {ex}");
            }
        }

        /// <summary>
        /// Reset tracker tracking data
        /// </summary>
        public static void ResetTrackerTracking()
        {
            _pawnsWithInjectedTrackers.Clear();
        }
    }
}
