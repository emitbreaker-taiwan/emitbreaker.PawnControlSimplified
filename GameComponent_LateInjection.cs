using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Verse.Noise;
using static emitbreaker.PawnControl.HarmonyPatches;
using static System.Net.Mime.MediaTypeNames;

namespace emitbreaker.PawnControl
{
    public class GameComponent_LateInjection : GameComponent
    {
        private bool drafterAlreadyInjected = false;

        public GameComponent_LateInjection(Game game) 
        {
        }

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
            JobGiver_WorkNonHumanlike.ResetCache();
        }

        /// <summary>
        /// Called when a game is loaded.
        /// </summary>
        public override void FinalizeInit()
        {
            base.FinalizeInit();

            // Ensure all modded pawns have their stat injections applied
            Utility_StatManager.CheckStatHediffDefExists();

            // Reset caches to ensure no stale data is present
            ResetAllCache();
        }

        private void ResetAllCache()
        {
            // Clear work status caches on initialization
            Utility_TagManager.ResetCache();
            Utility_ThinkTreeManager.ResetCache();

            // General job giver caches
            JobGiver_WorkNonHumanlike.ResetCache();

            // Plant cutting job givers
            JobGiver_PlantsCut_PawnControl.ResetCache();

            // Growing job givers
            JobGiver_GrowerHarvest_PawnControl.ResetCache();
            JobGiver_GrowerSow_PawnControl.ResetCache();

            // Fire Fighting job givers
            JobGiver_FightFires_PawnControl.ResetCache();

            // Doctor job givers
            JobGiver_FeedPatient_PawnControl.ResetCache();

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

            // Handling job givers
            JobGiver_Tame_PawnControl.ResetCache();
            JobGiver_Train_PawnControl.ResetCache();
            JobGiver_TakeToPen_PawnControl.ResetCache();
            JobGiver_Slaughter_PawnControl.ResetCache();
            JobGiver_ReleaseAnimalToWild_PawnControl.ResetCache();
            JobGiver_GatherAnimalBodyResources_PawnControl.ResetCache();

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
            foreach (var map in Find.Maps)
            {
                Utility_DrafterManager.InjectDraftersIntoMapPawns(map);
                
                // Now give every non-human pawn its equipment/apparel/inventory
                foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                {
                    var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
                    if (modExtension == null || pawn.RaceProps.Humanlike)
                    {
                        continue;
                    }

                    if (pawn.drafter == null)
                    {
                        continue;
                    }

                    Utility_DrafterManager.EnsureAllTrackers(pawn); 
                }
            }

            drafterAlreadyInjected = true; // Set after all maps processed
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
