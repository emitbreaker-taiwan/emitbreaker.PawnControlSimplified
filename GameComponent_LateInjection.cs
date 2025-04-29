using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace emitbreaker.PawnControl
{
    public class GameComponent_LateInjection : GameComponent
    {
        private bool drafterAlreadyInjected = false;
        //private bool reinjected;

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
    }
}
