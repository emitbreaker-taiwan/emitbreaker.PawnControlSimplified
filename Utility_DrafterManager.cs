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
        public static void EnsureDrafter(Pawn pawn, NonHumanlikePawnControlExtension modExtension, bool isSpawnSetup = false)
        {
            if (pawn == null || pawn.def == null || pawn.Dead || pawn.Destroyed)
            {
                return;
            }

            if (!isSpawnSetup && !pawn.Spawned)
            {
                return; // Pawn is not spawned, no need to inject drafter
            }

            if (modExtension == null)
            {                 
                return; // No mod extension found, no drafter needed
            }

            // Check if this pawn should get a drafter
            if (!Utility_TagManager.ForceDraftable(pawn.def))
            {
                return;
            }

            // Repair any existing controller that lost its `pawn` ref during load
            if (pawn.drafter != null)
            {
                var pawnField = AccessTools.Field(typeof(Pawn_DraftController), "pawn");
                pawnField.SetValue(pawn.drafter, pawn);
                return;
            }

            try
            {
                // Create and assign a new draft controller
                pawn.drafter = new Pawn_DraftController(pawn);

                if (Prefs.DevMode && modExtension.debugMode)
                {
                    Log.Message($"[PawnControl] Injected drafter for {pawn.LabelShort} ({pawn.def.defName})");
                }

                // Make sure the draft controller is properly initialized
                // This ensures the controller's internal state is properly set up
                pawn.drafter.ExposeData();
            }
            catch (Exception ex)
            {
                Log.Error($"[PawnControl] Error injecting drafter for {pawn.LabelShort}: {ex}");
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
                Log.Message($"[PawnControl] Injected drafters into {injected} non-humanlike pawns on map {map.uniqueID}");
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
    }
}
