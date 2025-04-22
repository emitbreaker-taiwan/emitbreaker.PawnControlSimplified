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
    public class Dialog_SelectNonHumanlikeRace : Window
    {
        private Vector2 scrollPosition = Vector2.zero;
        private string searchText = "";

        public override Vector2 InitialSize => new Vector2(700f, 700f);

        public Dialog_SelectNonHumanlikeRace()
        {
            forcePause = true;
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            
            // Refresh the cache when the dialog is created
            Utility_CacheManager.RefreshEligibleNonHumanlikeRacesCache();
        }

        public override void DoWindowContents(Rect inRect)
        {
            float top = 0f;
            float spacing = 6f;

            // === Search Bar ===
            Rect searchRect = new Rect(inRect.x, inRect.y, inRect.width - 40f, 30f);

            GUI.SetNextControlName("SearchRaceInput");
            searchText = Widgets.TextField(searchRect, searchText);

            // Placeholder label (drawn AFTER text field if it's empty and not focused)
            if (string.IsNullOrEmpty(searchText) && GUI.GetNameOfFocusedControl() != "SearchRaceInput")
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = Color.gray;
                Widgets.Label(searchRect.ContractedBy(4f), "PawnControl_SearchRace".Translate());
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
            }

            // Clear button
            Rect clearRect = new Rect(searchRect.xMax + 4f, searchRect.y, 30f, 30f);
            if (!string.IsNullOrWhiteSpace(searchText) && Widgets.ButtonText(clearRect, "×"))
            {
                searchText = "";
                GUI.FocusControl(null);
            }

            top += searchRect.height + spacing;

            // === Title ===
            Rect titleRect = new Rect(inRect.x, top, inRect.width, 30f);
            Text.Font = GameFont.Medium;
            Widgets.Label(titleRect, "PawnControl_SelectRaceToEdit".Translate());
            top += titleRect.height + spacing;
            Text.Font = GameFont.Small;

            //// === Scrollable list of races ===
            //Rect scrollOutRect = new Rect(inRect.x, top, inRect.width, inRect.height - top - spacing);
            //float viewHeight = 9999f;
            //Rect scrollViewRect = new Rect(0f, 0f, scrollOutRect.width - 16f, viewHeight);

            // === Scrollable list of races ===
            Rect scrollOutRect = new Rect(inRect.x, top, inRect.width, inRect.height - top - spacing);

            // Calculate total height for all race rows
            IEnumerable<ThingDef> raceDefs = Utility_CacheManager.GetEligibleNonHumanlikeRaces(
                searchText: searchText,
                additionalFilter: def => Utility_NonHumanlikePawnControl.IsValidRaceCandidate(def)
            );

            // Height of each row
            float rowHeight = 36f;
            // Total height of all rows
            float totalHeight = raceDefs.Count() * rowHeight;

            // Scroll view height
            Rect scrollViewRect = new Rect(0f, 0f, scrollOutRect.width - 16f, totalHeight);

            Widgets.BeginScrollView(scrollOutRect, ref scrollPosition, scrollViewRect);
            float curY = 0f;
            foreach (ThingDef def in raceDefs)
            {
                bool hasPhysical = Utility_ModExtensionResolver.HasPhysicalModExtension(def);
                bool hasVirtual = Utility_ModExtensionResolver.HasVirtualModExtension(def);
                string label = hasPhysical ? $"[★] {def.label}" : hasVirtual ? $"[V] {def.label}" : def.label;

                Rect row = new Rect(0f, curY, scrollViewRect.width, 32f);
                if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
                if (Widgets.ButtonText(row, label))
                {
                    Close();
                    Find.WindowStack.Add(new Dialog_PawnTagEditor(def));
                }
                curY += 36f;
            }

            Widgets.EndScrollView();
        }
    }
}
