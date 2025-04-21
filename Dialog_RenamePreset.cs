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
    public class Dialog_RenamePreset : Window
    {
        private string newName;
        private readonly Action<string> onRename;

        public override Vector2 InitialSize => new Vector2(400f, 150f);

        public Dialog_RenamePreset(string currentName, Action<string> onRename)
        {
            this.newName = currentName;
            this.onRename = onRename;
            forcePause = true;
            closeOnAccept = false;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f), "PawnControl_RenamePresetTitle".Translate());

            Text.Font = GameFont.Small;
            newName = Widgets.TextField(new Rect(inRect.x, inRect.y + 35f, inRect.width, 30f), newName);

            if (Widgets.ButtonText(new Rect(inRect.x, inRect.y + 80f, inRect.width, 30f), "Confirm".Translate()))
            {
                if (!string.IsNullOrWhiteSpace(newName))
                {
                    onRename?.Invoke(newName.Trim());
                    Close();
                }
                else
                {
                    Messages.Message("PawnControl_RenameErrorEmpty".Translate(), MessageTypeDefOf.RejectInput, false);
                }
            }
        }
    }
}
