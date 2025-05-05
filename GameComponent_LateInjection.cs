using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using static emitbreaker.PawnControl.HarmonyPatches;

namespace emitbreaker.PawnControl
{
    public class GameComponent_LateInjection : GameComponent
    {
        private bool drafterAlreadyInjected = false;

        public GameComponent_LateInjection(Game game) { }

        public override void GameComponentTick()
        {
            if (drafterAlreadyInjected && Utility_IdentityManager.identityFlagsPreloaded)
            {
                return;
            }

            if (Find.TickManager.TicksGame < 200)
            {
                return;
            }

            if (!Utility_IdentityManager.identityFlagsPreloaded)
            {

            }
            if (!drafterAlreadyInjected)
            {
                InjectDraftersSafely();
            }
        }

        /// <summary>
        /// Called when game is being saved.
        /// </summary>
        public override void GameComponentOnGUI()
        {
            base.GameComponentOnGUI();

            // This is a good spot to ensure cache is clean before saving
            JobGiver_WorkNonHumanlike.ClearJobCache();
        }

        /// <summary>
        /// Called when a game is loaded.
        /// </summary>
        public override void FinalizeInit()
        {
            base.FinalizeInit();

            // General job giver caches
            JobGiver_WorkNonHumanlike.ClearJobCache();

            // Plant cutting job givers
            JobGiver_PlantsCut_PawnControl.ResetCache();

            // Growing job givers
            JobGiver_GrowerHarvest_PawnControl.ResetCache();
            JobGiver_GrowerSow_PawnControl.ResetCache();

            // Fire Fighting job givers
            JobGiver_FightFires_PawnControl.ResetCache();

            // Cleaning job givers
            JobGiver_CleanFilth_PawnControl.ResetCache();
            JobGiver_ClearSnow_PawnControl.ResetCache();

            // Basic worker job givers
            JobGiver_Flick_PawnControl.ResetCache();
            JobGiver_Open_PawnControl.ResetCache();
            JobGiver_ExtractSkull_PawnControl.ResetCache();

            // Warden job givers
            JobGiver_Warden_DoExecution_PawnControl.ResetCache();
            JobGiver_Warden_ExecuteGuilty_PawnControl.ResetCache();
            JobGiver_Warden_ReleasePrisoner_PawnControl.ResetCache();
            JobGiver_Warden_TakeToBed_PawnControl.ResetCache();
            JobGiver_Warden_Feed_PawnControl.ResetCache();
            JobGiver_Warden_DeliverFood_PawnControl.ResetCache();
            JobGiver_Warden_Chat_PawnControl.ResetCache();

            // Hauling job givers
            JobGiver_EmptyEggBox_PawnControl.ResetCache();
            JobGiver_Merge_PawnControl.ResetCache();
            JobGiver_ConstructDeliverResourcesToBlueprints_PawnControl.ResetCache();
            JobGiver_ConstructDeliverResourcesToFrames_PawnControl.ResetCache();
            JobGiver_HaulGeneral_PawnControl.ResetCache();
            JobGiver_FillFermentingBarrel_PawnControl.ResetCache();
            JobGiver_TakeBeerOutOfBarrel_PawnControl.ResetCache();
            JobGiver_HaulCampfire_PawnControl.ResetCache();
            JobGiver_Cremate_PawnControl.ResetCache();
            JobGiver_HaulCorpses_PawnControl.ResetCache();
            JobGiver_Strip_PawnControl.ResetCache();
            JobGiver_HaulToPortal_PawnControl.ResetCache();
            JobGiver_LoadTransporters_PawnControl.ResetCache();
            JobGiver_GatherItemsForCaravan_PawnControl.ResetCache();
            JobGiver_UnloadCarriers_PawnControl.ResetCache();
            JobGiver_Refuel_PawnControl.ResetCache();
            JobGiver_Refuel_Turret_PawnControl.ResetCache();

            if (Prefs.DevMode)
            {
                Log.Message("[PawnControl] Cleared job cache on game load");
            }
        }

        private void InjectDraftersSafely()
        {
            List<Map> maps = Find.Maps;
            int mapCount = maps.Count;
            if (mapCount == 0)
            {
                drafterAlreadyInjected = true;
                return;
            }

            for (int i = 0; i < mapCount; i++)
            {
                Map map = maps[i];
                List<Pawn> pawns = map.mapPawns.AllPawns;
                int pawnCount = pawns.Count;
                if (pawnCount == 0)
                    continue;

                for (int j = 0; j < pawnCount; j++)
                {
                    Pawn pawn = pawns[j];
                    if (pawn == null || pawn.def == null || pawn.RaceProps == null || pawn.Dead || pawn.Destroyed)
                        continue;
                    if (!pawn.Spawned || pawn.Faction == null || pawn.drafter != null)
                        continue;
                    //if (Utility_Compatibility.TryGetModExtensionNoDependency(pawn.def, "MIM40kFactions.BodySnatcherExtension") != null)
                    //    continue;
                    if (!Utility_DrafterManager.ShouldInjectDrafter(pawn))
                        continue;
                    if (Utility_VehicleFramework.IsVehiclePawn(pawn))
                        continue;

                    try
                    {
                        pawn.drafter = new Pawn_DraftController(pawn);
                        if (Prefs.DevMode)
                        {
                            Log.Message($"[PawnControl] Late-injected drafter: {pawn.LabelShort}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[PawnControl] Drafter injection failed for {pawn?.LabelCap ?? "unknown pawn"}: {ex.Message}");
                    }
                }
            }

            drafterAlreadyInjected = true; // Correctly set after all maps processed
        }

        // In GameComponent_LateInjection
        public void DiagnoseAllModdedPawns()
        {
            if (!Prefs.DevMode)
                return;

            foreach (var map in Find.Maps)
            {
                foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (pawn?.def != null && Utility_CacheManager.GetModExtension(pawn.def) != null)
                    {
                        Utility_DebugManager.DiagnoseWorkGiversForPawn(pawn);
                    }
                }
            }

            Log.Message("[PawnControl] Completed diagnostic for all modded pawns");
        }

        // Add to GameComponent_LateInjection
        public void ValidateAllModdedPawnThinkTrees()
        {
            if (!Prefs.DevMode)
                return;

            int validated = 0;
            Log.Message("[PawnControl] Starting ThinkTree validation for all modded pawns");

            foreach (var map in Find.Maps)
            {
                foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (pawn?.def == null)
                        continue;

                    var modExt = Utility_CacheManager.GetModExtension(pawn.def);
                    if (modExt != null && Utility_ThinkTreeManager.HasAllowOrBlockWorkTag(pawn.def))
                    {
                        Utility_ThinkTreeManager.ValidateThinkTree(pawn);
                        validated++;
                    }
                }
            }

            Log.Message($"[PawnControl] Completed validation for {validated} modded pawns");
        }
    }
}
