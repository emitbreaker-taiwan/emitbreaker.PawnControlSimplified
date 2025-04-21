using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace emitbreaker.PawnControl
{
    public class Dialog_PresetManager : Window
    {
        private Vector2 scrollPos;
        private string search = "";
        private string selectedCategory;
        private string draggingPreset = null;

        private static readonly List<string> allCategories = new List<string>
        {
            "PawnControl_Category_Default".Translate(),
            "PawnControl_Category_Worker".Translate(),
            "PawnControl_Category_Combat".Translate(),
            "PawnControl_Category_Custom".Translate()
        };
        private static List<string> favoritePresets => GameComponent_SimpleNonHumanlikePawnControl.FavoritePresets;
        private static List<string> pinnedPresets => GameComponent_SimpleNonHumanlikePawnControl.PinnedPresets;

        public override Vector2 InitialSize => new Vector2(700f, 620f);

        public Dialog_PresetManager(Action<PawnTagPreset> _ = null)
        {
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            selectedCategory = allCategories.FirstOrDefault();
        }

        public override void DoWindowContents(Rect inRect)
        {
            float tabHeight = 30f;

            // Draw category tabs
            float tabX = inRect.x + 8f;
            float tabY = inRect.y + 6f;
            float curX = tabX;

            Text.Font = GameFont.Small;
            foreach (var cat in allCategories)
            {
                float tabWidthCalc = Text.CalcSize(cat).x + 20f;
                Rect tabRect = new Rect(curX, tabY, tabWidthCalc, 28f);
                bool active = selectedCategory == cat;

                // Visual highlight
                if (Mouse.IsOver(tabRect))
                    Widgets.DrawHighlight(tabRect);
                if (active)
                    Widgets.DrawHighlightSelected(tabRect);

                // Text
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(tabRect, cat);
                Text.Anchor = TextAnchor.UpperLeft;

                // Click detection
                if (Widgets.ButtonInvisible(tabRect))
                {
                    selectedCategory = cat;
                    Event.current.Use();
                }

                curX += tabWidthCalc + 6f;
            }

            Rect searchBox = new Rect(inRect.x + 4f, inRect.y + tabHeight + 4f, inRect.width - 8f, 28f);
            search = Widgets.TextField(searchBox, search);

            float listY = searchBox.yMax + 6f;
            Rect listRect = new Rect(inRect.x + 4f, listY, inRect.width - 8f, inRect.height - listY - 40f);
            DrawPresetList(listRect);

            if (Widgets.ButtonText(new Rect(inRect.x + 4f, inRect.yMax - 34f, 180f, 30f), "PawnControl_ImportFromXML".Translate()))
            {
                string path = Path.Combine(GenFilePaths.SaveDataFolderPath, "PawnControl", "Import", "preset.xml");
                var imported = PawnTagPresetManager.ImportPresetFromFile(path);
                if (imported != null)
                {
                    PawnTagPresetManager.SavePreset(imported);
                    Messages.Message("PawnControl_ImportedPreset".Translate(imported.name), MessageTypeDefOf.TaskCompletion);
                }
            }

            if (Widgets.ButtonText(new Rect(inRect.xMax - 184f, inRect.yMax - 34f, 180f, 30f), "Close".Translate()))
                Close();
        }

        private void DrawPresetList(Rect inRect)
        {
            var presets = PawnTagPresetManager.LoadAllPresets()
                .Where(p => (string.IsNullOrEmpty(search) || p.name.ToLower().Contains(search.ToLower())) &&
                            (selectedCategory == null || selectedCategory == p.category || p.category == null))
                .ToList();

            Rect viewRect = new Rect(0, 0, inRect.width - 16f, presets.Count * 36f);
            Widgets.BeginScrollView(inRect, ref scrollPos, viewRect);

            float y = 0f;
            for (int i = 0; i < presets.Count; i++)
            {
                var p = presets[i];
                Rect row = new Rect(0, y, viewRect.width, 34f);
                Widgets.DrawHighlightIfMouseover(row);

                // === Layout constants ===
                float dragWidth = 24f;
                float starWidth = 24f;
                float labelWidth = 200f;
                float btnSpacing = 4f;

                // === Drag Handle ===
                Rect dragRect = new Rect(row.x, row.y + 5f, dragWidth, 24f);
                GUI.DrawTexture(dragRect, TexButton.ReorderUp);
                if (Mouse.IsOver(dragRect) && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    draggingPreset = p.name;
                    Event.current.Use();
                }
                if (draggingPreset != null && draggingPreset != p.name && Event.current.type == EventType.MouseUp)
                {
                    int oldIndex = pinnedPresets.IndexOf(draggingPreset);
                    pinnedPresets.Remove(draggingPreset);
                    pinnedPresets.Insert(i, draggingPreset);
                    draggingPreset = null;
                    GameComponent_SimpleNonHumanlikePawnControl.Save();
                }

                // === Favorite Star ===
                Rect favRect = new Rect(dragRect.xMax + btnSpacing, row.y + 5f, starWidth, 24f);
                bool isFav = favoritePresets.Contains(p.name);
                if (Widgets.ButtonText(favRect, isFav ? "★" : "☆"))
                {
                    if (isFav) favoritePresets.Remove(p.name); else favoritePresets.Add(p.name);
                    GameComponent_SimpleNonHumanlikePawnControl.Save();
                }

                // === Label + Rename Button ===
                Color catColor = GetCategoryColor(p.category);
                Rect nameRect = new Rect(favRect.xMax + btnSpacing, row.y + 5f, labelWidth - 26f, 24f);
                GUI.color = catColor;
                Widgets.Label(nameRect, p.name);
                GUI.color = Color.white;

                // ✏️ Rename button
                Rect renameRect = new Rect(nameRect.xMax + 2f, row.y + 5f, 24f, 24f);
                if (Widgets.ButtonImage(renameRect, TexButton.Rename))
                {
                    Find.WindowStack.Add(new Dialog_RenamePreset(p.name, newName =>
                    {
                        p.name = newName;
                        PawnTagPresetManager.SavePresetsToFile(); // 🔄 Persist rename
                    }));
                    Event.current.Use();
                }

                // === Buttons (Apply, Export, Delete) ===
                float buttonWidth = 60f;
                float buttonSpacing = 4f;
                float totalButtonWidth = (buttonWidth + buttonSpacing) * 3 - buttonSpacing;
                float xRightStart = row.xMax - totalButtonWidth;

                Rect applyRect = new Rect(xRightStart, row.y + 4f, buttonWidth, 26f);
                Rect exportRect = new Rect(applyRect.xMax + buttonSpacing, row.y + 4f, buttonWidth, 26f);
                Rect deleteRect = new Rect(exportRect.xMax + buttonSpacing, row.y + 4f, buttonWidth, 26f);

                if (Widgets.ButtonText(applyRect, "PawnControl_Apply".Translate()))
                {
                    var targetDef = Utility_NonHumanlikePawnControl.GetActiveTagEditorTarget();

                    if (targetDef != null && Utility_ModExtensionResolver.HasPhysicalModExtension(targetDef))
                    {
                        Messages.Message("PawnControl_CannotEditPhysical".Translate(targetDef.label), MessageTypeDefOf.RejectInput);
                    }
                    else
                    {
                        Utility_NonHumanlikePawnControl.TryApplyPresetToActiveTagEditor(p);
                    }
                }

                if (Widgets.ButtonText(exportRect, "PawnControl_ExportSymbol".Translate()))
                {
                    string path = Path.Combine(GenFilePaths.SaveDataFolderPath, "PawnControl/Exported", p.name + ".xml");
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    PawnTagPresetManager.ExportPresetToFile(path, p);
                }

                if (Widgets.ButtonText(deleteRect, "PawnControl_DeleteSymbol".Translate()))
                {
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "PawnControl_DeletePresetConfirm".Translate(p.name),
                        () => { PawnTagPresetManager.DeletePreset(p.name); }, false));
                }

                // === Tooltip ===
                TooltipHandler.TipRegion(row, "PawnControl_PresetTooltip".Translate(p.tags.Count, p.category ?? "Default"));
                y += 36f;
            }

            Widgets.EndScrollView();
        }

        private Color GetCategoryColor(string cat)
        {
            switch (cat?.ToLowerInvariant())
            {
                case "worker": return new Color(0.6f, 0.8f, 1f);
                case "combat": return new Color(1f, 0.6f, 0.6f);
                case "custom": return new Color(0.9f, 0.9f, 0.6f);
                default: return Color.white;
            }
        }
    }
}