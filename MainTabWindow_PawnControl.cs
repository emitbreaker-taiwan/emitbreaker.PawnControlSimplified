using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace emitbreaker.PawnControl
{
    public class MainTabWindow_PawnControl : MainTabWindow
    {
        private Vector2 scrollPosition;
        private List<ThingDef> raceDefs;

        public override void PreOpen()
        {
            base.PreOpen();
            // Refresh the cache when the tab is opened
            Utility_CacheManager.RefreshEligibleNonHumanlikeRacesCache();
            raceDefs = Utility_CacheManager.GetEligibleNonHumanlikeRaces();
        }

        public override void DoWindowContents(Rect inRect)
        {
            //Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f), "PawnControl_Menu_SelectRace".Translate());
            //inRect.yMin += 35f;

            //Rect scrollOut = new Rect(inRect.x, inRect.y, inRect.width, inRect.height - 10f);
            //Rect scrollView = new Rect(0, 0, inRect.width - 16f, raceDefs.Count * 32f);

            //Widgets.BeginScrollView(scrollOut, ref scrollPosition, scrollView);

            //if (Widgets.ButtonText(new Rect(inRect.x, inRect.y, inRect.width, 30f), "PawnControl_ModSettings_OpenSelector".Translate()))
            //{
            //    Find.WindowStack.Add(new Dialog_SelectNonHumanlikeRace());
            //}

            //Widgets.EndScrollView();
            float y = 0f;
            float spacing = 35f;

            y += spacing;

            // Refresh the raceDefs cache
            Utility_CacheManager.RefreshEligibleNonHumanlikeRacesCache();
            raceDefs = Utility_CacheManager.GetEligibleNonHumanlikeRaces();

            int injected = raceDefs.Count(def => def.modExtensions?.OfType<VirtualNonHumanlikePawnControlExtension>().Any() == true);

            // === Injected Races Summary ===
            Widgets.Label(new Rect(inRect.x, y, 420f, 30f), "PawnControl_ModSettings_AutoInjectSummary".Translate(injected, raceDefs.Count));
            y += spacing;

            // === Inject Button ===
            if (Widgets.ButtonText(new Rect(inRect.x, y, 420f, 30f), "PawnControl_ModSettings_InjectNow".Translate()))
            {
                Utility_NonHumanlikePawnControl.InjectVirtualExtensionsForEligibleRaces();
            }
            y += spacing;

            // === Remove Button ===
            if (Widgets.ButtonText(new Rect(inRect.x, y, 420f, 30f), "PawnControl_ModSettings_RemoveInjected".Translate()))
            {
                Utility_NonHumanlikePawnControl.RemoveVirtualExtensionsForEligibleRaces();
                Utility_CacheManager.RefreshEligibleNonHumanlikeRacesCache();
            }
            y += spacing;

            // === Open Selector Button ===
            if (Widgets.ButtonText(new Rect(inRect.x, y, 420f, 30f), "PawnControl_ModSettings_OpenSelector".Translate()))
            {
                Find.WindowStack.Add(new Dialog_SelectNonHumanlikeRace());
            }
            y += spacing;
        }
    }
}
