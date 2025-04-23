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
        private bool alreadyInjected = false;

        public GameComponent_LateInjection(Game game) { }

        public override void GameComponentTick()
        {
            if (alreadyInjected)
            {
                return;
            }

            if (Find.TickManager.TicksGame < 200)
            {
                return;
            }

            foreach (Map map in Find.Maps)
            {
                foreach (Pawn pawn in map.mapPawns.AllPawns)
                {
                    // === Safeguards against half-initialized pawns ===
                    if (pawn == null || pawn.def == null || pawn.RaceProps == null || pawn.Dead || pawn.Destroyed)
                        continue;
                    if (!pawn.Spawned || pawn.Faction == null || pawn.drafter != null)
                        continue;
                    if (Utility_Compatibility.TryGetModExtensionNoDependency(pawn.def, "MIM40kFactions.BodySnatcherExtension") != null)
                        continue;
                    if (!Utility_DrafterManager.ShouldInjectDrafter(pawn))
                        continue;
                    if (Utility_VehicleFramework.IsVehiclePawn(pawn))
                        continue;

                    // === Safe to inject drafter ===
                    try
                    {
                        pawn.drafter = new Pawn_DraftController(pawn);
                        // Log.Message($"[PawnControl] Late-injected drafter: {pawn.LabelShort}");
                    }
                    catch (System.Exception ex)
                    {
                        Log.Warning($"[PawnControl] Drafter injection failed for {pawn?.LabelCap ?? "unknown pawn"}: {ex.Message}");
                    }
                }
            }

            alreadyInjected = true; // Prevent future ticks
        }
    }
}
