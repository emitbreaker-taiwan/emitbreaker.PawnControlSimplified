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
            // equipment
            if (EquipmentField.GetValue(pawn) == null)
            {
                var eq = new Pawn_EquipmentTracker(pawn);
                EquipmentField.SetValue(pawn, eq);
                eq.Notify_PawnSpawned();
            }

            // apparel
            if (ApparelField.GetValue(pawn) == null)
            {
                var ap = new Pawn_ApparelTracker(pawn);
                ApparelField.SetValue(pawn, ap);
            }

            // inventory
            if (InventoryField.GetValue(pawn) == null)
            {
                var inv = new Pawn_InventoryTracker(pawn);
                InventoryField.SetValue(pawn, inv);
            }
        }

        /// <summary>
        /// Determines if a pawn should have a draft controller injected
        /// </summary>
        public static bool ShouldInjectDrafter(Pawn pawn, NonHumanlikePawnControlExtension modExtension)
        {
            if (pawn == null || pawn.def == null || pawn.RaceProps == null || pawn.drafter != null)
            {
                return false; // Invalid pawn or missing definitions or already has a drafter
            }

            if (modExtension == null)
            {
                return false; // No mod extension found
            }

            // Drafter should be injected if the pawn is valid, has no drafter, and has either
            // the AutoDraftInjection tag or the forceDraftable flag
            return Utility_TagManager.ForceDraftable(pawn.def);
        }

        /// <summary>
        /// Ensures that a pawn's draft controller is properly set up and connected
        /// </summary>
        // In Utility_DrafterManager.cs
        public static void EnsureDrafter(Pawn pawn, NonHumanlikePawnControlExtension modExtension, bool isSpawnSetup = false)
        {
            if (pawn == null || pawn.def == null || pawn.Dead || pawn.Destroyed)
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

                var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
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
                var holdFire = Utility_CacheManager.GetDuty("HoldFire");
                if (holdFire != null)
                {
                    return holdFire;
                }
            }

            if (Utility_TagManager.HasTag(p.def, "Siege_ManTurret"))
            {
                var manTurrets = Utility_CacheManager.GetDuty("ManTurrets");
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

            // Get a reference to the tracker fields using reflection
            var drafterField = AccessTools.Field(typeof(Pawn), "drafter");
            var equipmentField = AccessTools.Field(typeof(Pawn), "equipment");
            var apparelField = AccessTools.Field(typeof(Pawn), "apparel");
            var inventoryField = AccessTools.Field(typeof(Pawn), "inventory");

            // Part 1: Clean up pawns in loaded maps
            if (Find.Maps != null)
            {
                foreach (Map map in Find.Maps)
                {
                    // Safety check for mapPawns
                    if (map?.mapPawns?.AllPawnsSpawned == null) continue;

                    foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                    {
                        try
                        {
                            if (pawn == null || pawn.def != raceDef || pawn.Dead || pawn.Destroyed)
                                continue;

                            cleanedAny = true;

                            // 1. Reset think trees back to vanilla for this pawn
                            Utility_ThinkTreeManager.ResetThinkTreeToVanilla(pawn);

                            // 2. Remove drafter if present
                            if (pawn.drafter != null)
                            {
                                // Force undraft first to prevent issues
                                if (pawn.drafter.Drafted)
                                    pawn.drafter.Drafted = false;

                                // Null out the drafter field
                                drafterField?.SetValue(pawn, null);
                            }

                            // Safety check before handling equipment
                            if (pawn.Map == null) continue;

                            // 3. Handle equipment - drop on ground
                            if (pawn.equipment != null)
                            {
                                List<ThingWithComps> equipmentToMove = pawn.equipment.AllEquipmentListForReading?.ToList() ?? new List<ThingWithComps>();
                                foreach (ThingWithComps eq in equipmentToMove)
                                {
                                    if (eq == null) continue;

                                    pawn.equipment.Remove(eq);
                                    if (!eq.Destroyed && pawn.Map != null)
                                    {
                                        GenDrop.TryDropSpawn(eq, pawn.Position, pawn.Map, ThingPlaceMode.Near, out _);
                                    }
                                }

                                // Now null out the equipment field
                                equipmentField?.SetValue(pawn, null);
                            }

                            // 4. Handle apparel - drop on ground
                            if (pawn.apparel != null)
                            {
                                List<Apparel> apparelToMove = pawn.apparel.WornApparel?.ToList() ?? new List<Apparel>();
                                foreach (Apparel ap in apparelToMove)
                                {
                                    if (ap == null) continue;

                                    pawn.apparel.Remove(ap);
                                    if (!ap.Destroyed && pawn.Map != null)
                                    {
                                        GenDrop.TryDropSpawn(ap, pawn.Position, pawn.Map, ThingPlaceMode.Near, out _);
                                    }
                                }

                                // Now null out the apparel field
                                apparelField?.SetValue(pawn, null);
                            }

                            // 5. Handle inventory - drop on ground
                            if (pawn.inventory != null && pawn.inventory.innerContainer != null)
                            {
                                List<Thing> itemsToMove = pawn.inventory.innerContainer.ToList();
                                foreach (Thing item in itemsToMove)
                                {
                                    if (item == null) continue;

                                    pawn.inventory.innerContainer.Remove(item);
                                    if (!item.Destroyed && pawn.Map != null)
                                    {
                                        GenDrop.TryDropSpawn(item, pawn.Position, pawn.Map, ThingPlaceMode.Near, out _);
                                    }
                                }

                                // Now null out the inventory field
                                inventoryField?.SetValue(pawn, null);
                            }
                        }
                        catch (Exception ex)
                        {
                            Utility_DebugManager.LogError($"Error cleaning up trackers for {pawn?.LabelShort ?? "unknown pawn"}: {ex}");
                        }
                    }
                }
            }

            // Part 2: Clean up world pawns (important for main menu and non-spawned pawns)
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
                        
                        // 1. Reset think trees back to vanilla for this pawn
                        Utility_ThinkTreeManager.ResetThinkTreeToVanilla(pawn);

                        // 2. Remove drafter if present
                        if (pawn.drafter != null)
                        {
                            // Force undraft first to prevent issues
                            if (pawn.drafter.Drafted)
                                pawn.drafter.Drafted = false;

                            // Null out the drafter field
                            drafterField?.SetValue(pawn, null);
                        }

                        // For world pawns not on a map, we just null the trackers
                        // since we can't drop items on the ground
                        // Items will stay in the containers until they're properly nulled

                        // Clear equipment tracker
                        if (pawn.equipment != null)
                        {
                            if (pawn.Spawned && pawn.Map != null)
                            {
                                // If spawned, move items to the map
                                List<ThingWithComps> equipmentToMove = pawn.equipment.AllEquipmentListForReading?.ToList() ?? new List<ThingWithComps>();
                                foreach (ThingWithComps eq in equipmentToMove)
                                {
                                    if (eq == null) continue;
                                    pawn.equipment.Remove(eq);

                                    if (!eq.Destroyed && pawn.Map != null)
                                        GenDrop.TryDropSpawn(eq, pawn.Position, pawn.Map, ThingPlaceMode.Near, out _);
                                }
                            }

                            equipmentField?.SetValue(pawn, null);
                        }

                        // Clear apparel tracker
                        if (pawn.apparel != null)
                        {
                            if (pawn.Spawned && pawn.Map != null)
                            {
                                // If spawned, move items to the map
                                List<Apparel> apparelToMove = pawn.apparel.WornApparel?.ToList() ?? new List<Apparel>();
                                foreach (Apparel ap in apparelToMove)
                                {
                                    if (ap == null) continue;
                                    pawn.apparel.Remove(ap);

                                    if (!ap.Destroyed && pawn.Map != null)
                                        GenDrop.TryDropSpawn(ap, pawn.Position, pawn.Map, ThingPlaceMode.Near, out _);
                                }
                            }

                            apparelField?.SetValue(pawn, null);
                        }

                        // Clear inventory tracker
                        if (pawn.inventory != null && pawn.inventory.innerContainer != null)
                        {
                            if (pawn.Spawned && pawn.Map != null)
                            {
                                // If spawned, move items to the map
                                List<Thing> itemsToMove = pawn.inventory.innerContainer.ToList();
                                foreach (Thing item in itemsToMove)
                                {
                                    if (item == null) continue;
                                    pawn.inventory.innerContainer.Remove(item);

                                    if (!item.Destroyed && pawn.Map != null)
                                        GenDrop.TryDropSpawn(item, pawn.Position, pawn.Map, ThingPlaceMode.Near, out _);
                                }
                            }

                            inventoryField?.SetValue(pawn, null);
                        }
                    }
                    catch (Exception ex)
                    {
                        Utility_DebugManager.LogError($"Error cleaning up world pawn trackers for {pawn?.LabelShort ?? "unknown pawn"}: {ex}");
                    }
                }
            }

            if (cleanedAny)
            {
                Utility_DebugManager.LogNormal($"Cleaned up trackers for race: {raceDef.defName}");
            }
        }
    }
}
