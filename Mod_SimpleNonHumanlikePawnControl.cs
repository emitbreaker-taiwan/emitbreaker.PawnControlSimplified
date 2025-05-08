using RimWorld;
using System.Runtime;
using UnityEngine;
using Verse;

namespace emitbreaker.PawnControl
{

    public class Mod_SimpleNonHumanlikePawnControl : Mod
    {
        public ModSettings_SimpleNonHumanlikePawnControl Settings;

        public override string SettingsCategory()
        {
            return "PawnControl_ModName".Translate();
        }

        public Mod_SimpleNonHumanlikePawnControl(ModContentPack content) : base(content)
        { 
            Settings = GetSettings<ModSettings_SimpleNonHumanlikePawnControl>();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            base.DoSettingsWindowContents(inRect);
            var listing = new Listing_Standard { ColumnWidth = inRect.width };
            listing.Begin(inRect);

            // — Debug mode checkbox —
            listing.CheckboxLabeled("PawnControl_DebugMode".Translate(), ref Settings.debugMode, "PawnControl_DebugMode_Tooltip".Translate());

            listing.Gap(12f);

            //// — Button to open your preset‐manager dialog —
            //if (listing.ButtonText("PawnControl_Button_OpenPresetManager".Translate()))
            //{
            //    // Example: pick the first humanlike race; adjust as you see fit
            //    var firstAnimal = DefDatabase<ThingDef>
            //        .AllDefsListForReading
            //        .FirstOrDefault(td => td.race?.Animal == true);

            //    if (firstAnimal != null)
            //        Find.WindowStack.Add(new Dialog_PawnPresetManager(firstAnimal));
            //    else
            //        Messages.Message("PawnControl_Error_NoAnimalDef".Translate(), MessageTypeDefOf.RejectInput);
            //}

            listing.End();
            WriteSettings(); // persist your debugMode toggle
        }

        // Call this to persist changes when applying presets from main menu
        public void SaveSettings()
        {
            Settings.Write();
        }
    }
}
