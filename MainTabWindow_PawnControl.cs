using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace emitbreaker.PawnControl
{
    public class MainTabWindow_PawnControl : MainTabWindow
    {
        private List<ThingDef> cachedNonHumanlikeRaces;

        public override Vector2 InitialSize => new Vector2(300f, 200f);

        public override void PreOpen()
        {
            base.PreOpen();
            // Refresh the race cache when opening the window
            RefreshRaceCache();
        }

        public override void DoWindowContents(Rect inRect)
        {
            // Title section
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x + 10f, inRect.y + 10f, inRect.width - 20f, 35f),
                "PawnControl_TabTitle".Translate());
            Text.Font = GameFont.Small;

            // Description text
            Widgets.Label(new Rect(inRect.x + 10f, inRect.y + 50f, inRect.width - 20f, 50f),
                "PawnControl_TabDescription".Translate());

            // Button to open the preset manager dialog
            const float buttonHeight = 35f;
            var buttonRect = new Rect(
                inRect.x + 10f,
                inRect.y + inRect.height - buttonHeight - 10f,
                inRect.width - 20f,
                buttonHeight);

            if (Widgets.ButtonText(buttonRect, "PawnControl_Menu_OpenPresetManager".Translate()))
            {
                OpenPresetManager();
            }
        }

        private void RefreshRaceCache()
        {
            // Get all ThingDefs that are non-humanlike pawns
            cachedNonHumanlikeRaces = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(def => def.race != null && !def.race.Humanlike && def.thingClass != typeof(Corpse))
                .ToList();
        }

        private List<ThingDef> GetNonHumanlikeRaces()
        {
            if (cachedNonHumanlikeRaces == null)
            {
                RefreshRaceCache();
            }
            return cachedNonHumanlikeRaces;
        }

        private void OpenPresetManager()
        {
            var races = GetNonHumanlikeRaces();
            if (races != null && races.Count > 0)
            {
                // Find the first animal race as default selection
                ThingDef firstRace = races.FirstOrDefault(r => r.race?.Animal == true) ?? races.First();

                // Open dialog with initial race selection
                Find.WindowStack.Add(new Dialog_PawnPresetManager(firstRace));
            }
            else
            {
                Messages.Message("PawnControl_Error_NoEligibleRaces".Translate(), MessageTypeDefOf.RejectInput);
            }
        }
    }
}