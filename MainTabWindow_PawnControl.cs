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
            raceDefs = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(t => Utility_ModExtensionResolver.HasVirtualModExtension(t))
                .OrderBy(t => t.label)
                .ToList();
        }

        public override void DoWindowContents(Rect inRect)
        {
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f), "PawnControl_Menu_SelectRace".Translate());
            inRect.yMin += 35f;

            Rect scrollOut = new Rect(inRect.x, inRect.y, inRect.width, inRect.height - 10f);
            Rect scrollView = new Rect(0, 0, inRect.width - 16f, raceDefs.Count * 32f);

            Widgets.BeginScrollView(scrollOut, ref scrollPosition, scrollView);

            float y = 0f;
            foreach (var def in raceDefs)
            {
                if (Widgets.ButtonText(new Rect(0, y, scrollView.width - 20f, 30f), def.label))
                {
                    Find.WindowStack.Add(new Dialog_PawnTagEditor(def));
                }

                y += 32f;
            }

            Widgets.EndScrollView();
        }
    }
}
