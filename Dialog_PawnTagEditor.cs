using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using RimWorld;
using Verse;
using static UnityEngine.Scripting.GarbageCollector;
using Verse.AI;
using KTrie;

namespace emitbreaker.PawnControl
{
    public class Dialog_PawnTagEditor : Window
    {
        // === Fields ===
        private readonly ThingDef def;
        public ThingDef SelectedDef => def;

        private Vector2 scrollPos;
        private string newTag = "";
        private string tagSearch = "";

        private bool autoExpandPinned = true;

        private List<WorkTypeDef> allWorkTypes;
        private List<PawnTagDef> allPawnTagDefs;
        private HashSet<string> pinnedCategories = new HashSet<string>();
        private Dictionary<string, bool> categoryCollapseStates = new Dictionary<string, bool>();
        private Dictionary<string, List<PawnTagDef>> groupedTagDefs = new Dictionary<string, List<PawnTagDef>>();

        private float lastScrollViewHeight = -1f;
        //private int lastTagCount = -1;

        private const int MaxPinned = 20;
        private static List<string> pinnedTags => GameComponent_SimpleNonHumanlikePawnControl.PinnedTags;

        //private NonHumanlikePawnControlExtension modExtension;
        private NonHumanlikePawnControlExtension _cachedPhysicalModExtension;
        private VirtualNonHumanlikePawnControlExtension _cachedVirtualModExtension;
        private DefModExtension _cachedSelectedModExtension;

        public NonHumanlikePawnControlExtension PhysicalModExtension
        {
            get
            {
                if (_cachedPhysicalModExtension == null)
                    _cachedPhysicalModExtension = def.GetModExtension<NonHumanlikePawnControlExtension>();
                return _cachedPhysicalModExtension;
            }
        }

        public VirtualNonHumanlikePawnControlExtension VirtualModExtension
        {
            get
            {
                if (_cachedVirtualModExtension == null)
                    _cachedVirtualModExtension = def.GetModExtension<VirtualNonHumanlikePawnControlExtension>();
                return _cachedVirtualModExtension;
            }
        }

        /// <summary>
        /// Returns physical extension if present; otherwise virtual extension.
        /// Used as unified selector for tag read/edit behavior.
        /// </summary>
        public DefModExtension SelectedModExtension
        {
            get
            {
                if (_cachedSelectedModExtension != null)
                {
                    return _cachedSelectedModExtension;
                }

                if (PhysicalModExtension != null)
                {
                    _cachedSelectedModExtension = PhysicalModExtension;
                }
                else if (VirtualModExtension != null)
                {
                    _cachedSelectedModExtension = VirtualModExtension;
                }

                return _cachedSelectedModExtension;
            }
        }

        private List<PawnTagPreset> loadedPresets = new List<PawnTagPreset>();

        private static HashSet<string> ignoredSuggestedTags = new HashSet<string>();

        private PawnTagDef draggedTagDef = null;

        private Rect? deferredSuggestionRect = null;
        private List<string> deferredSuggestions = new List<string>();
        private bool showAddNewButton = false;

        public override Vector2 InitialSize => new Vector2(
            1500f,  // width (Left + Right + Suggestion + AddTag + spacing)
            860f    // height (Tag area + Add tag + Bottom controls)
        );

        // === Constructor ===
        public Dialog_PawnTagEditor(ThingDef def)
        {
            this.def = def;

            forcePause = true;
            doCloseX = true;
            closeOnClickedOutside = true;

            allWorkTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading.OrderBy(w => w.labelShort).ToList();
            if (allPawnTagDefs == null)
            {
                allPawnTagDefs = DefDatabase<PawnTagDef>.AllDefsListForReading.OrderBy(d => d.label).ToList();
            }
            if (PhysicalModExtension != null)
            {
                Utility_CacheManager.RefreshTagCache(def, PhysicalModExtension);
            }

            if (VirtualModExtension != null)
            {
                Utility_CacheManager.RefreshTagCache(def, VirtualModExtension);
            }
            
            var cachedTagNames = Utility_CacheManager.allKnownTagNames
                .Where(tag => Utility_TagCatalog.CanUseWorkTag(tag)) // Optional filtering
                .OrderBy(tag => tag)
                .ToList();

            var definedSet = new HashSet<string>(allPawnTagDefs.Select(p => p.defName));
            foreach (string tag in cachedTagNames)
            {
                Utility_TagCatalog.EnsureTagDefExists(tag, "Auto", "PawnControl_AutoDesc");
            }

            RebuildGroupedDefs();

            if (autoExpandPinned)
            {
                foreach (var cat in groupedTagDefs.Keys)
                {
                    categoryCollapseStates[cat] = false;
                }
            }
        }

        // === Rebuild tag definitions grouped by category ===
        private void RebuildGroupedDefs()
        {
            groupedTagDefs.Clear();
            foreach (var tagDef in allPawnTagDefs)
            {
                var category = tagDef.category ?? "PawnControl_Uncategorized".Translate();
                if (!groupedTagDefs.TryGetValue(category, out var list))
                {
                    list = new List<PawnTagDef>();
                    groupedTagDefs[category] = list;
                }
                list.Add(tagDef);
            }
        }

        public override void PreOpen()
        {
            base.PreOpen();
            RefreshPresets();

            // Ensure modExtension is bound
            if (Utility_ModExtensionResolver.HasPhysicalModExtension(def))
            {
                Utility_CacheManager.RefreshTagCache(def, PhysicalModExtension);
            }
            else if (Utility_ModExtensionResolver.HasVirtualModExtension(def))
            {
                Utility_CacheManager.RefreshTagCache(def, VirtualModExtension);
            }

            // Refresh known tags
            if (Utility_CacheManager.allKnownTagNames == null)
            {
                Utility_CacheManager.allKnownTagNames = new HashSet<string>();
            }
            Utility_CacheManager.allKnownTagNames = new HashSet<string>(DefDatabase<PawnTagDef>.AllDefsListForReading
                    .Select(d => d.defName)
                    .Where(name => !string.IsNullOrEmpty(name))
                    );
            Utility_TagCatalog.InjectEnumTags(Utility_CacheManager.allKnownTagNames); // ← Inject enum tags for suggestions

            RebuildGroupedDefs(); // 🔁 must rebuild UI after sync
            Utility_NonHumanlikePawnControl.SetActiveTagEditorTarget(def);
        }

        // Optional: build suggestion UI list on demand
        private List<string> GetAutocompleteSuggestions(string input)
        {
            if (Utility_CacheManager.allKnownTagNames == null)
            {
                Utility_CacheManager.allKnownTagNames = new HashSet<string>();
            }
            return Utility_CacheManager.allKnownTagNames
                .Where(s => s.IndexOf(input, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(s => s)
                .ToList();
        }

        private void RefreshPresets()
        {
            loadedPresets = PawnTagPresetManager.LoadAllPresets();
        }

        // === Main Window Content ===
        public override void DoWindowContents(Rect inRect)
        {
            float margin = 10f;
            float suggestionPaneWidth = 200f; // original suggestion width
            float addTagPaneWidth = 280f;     // assume current or target AddTag width

            float bottomPaneHeight = 260f;
            float topSectionHeight = inRect.height - bottomPaneHeight - margin * 2;

            // Left pane stays same
            float leftPaneWidth = inRect.width * 0.5f - margin;

            // Compute average width per your formula
            float sharedHalfWidth = (suggestionPaneWidth + addTagPaneWidth) / 2f;

            // Total right pane space
            float rightPaneTotalWidth = inRect.width - leftPaneWidth - margin * 3;

            // Final widths (shared between center and right)
            float centerPaneWidth = sharedHalfWidth;
            float rightPaneWidth = rightPaneTotalWidth - centerPaneWidth;

            // Title
            Rect titleRect = new Rect(inRect.x + margin, inRect.y, inRect.width - margin * 2, 40f);
            Widgets.Label(titleRect, "PawnControl_EditTag".Translate() + " " + def.label);
            inRect.yMin += 45f;

            // Virtual preview banner
            //if (!Utility_ModExtensionResolver.HasPhysicalModExtension(def))
            //{
            //    if (!Utility_ModExtensionResolver.HasVirtualModExtension(def))
            //    {
            //        Rect banner = new Rect(inRect.x, inRect.y, inRect.width, 32f);
            //        Widgets.DrawBoxSolid(banner, new Color(0.3f, 0.3f, 0.05f, 0.25f));
            //        Widgets.Label(banner, "PawnControl_VirtualPreviewLabel".Translate());
            //        inRect.yMin += 36f;

            //        Rect injectButton = new Rect(banner.x + 520f, banner.y + 4f, 200f, 24f);
            //        if (Widgets.ButtonText(injectButton, "PawnControl_InjectThisRaceNow".Translate()))
            //        {
            //            // Immediately inject before confirmation (to update UI right away)
            //            if (def.modExtensions == null)
            //            {
            //                def.modExtensions = new List<DefModExtension>();
            //            }

            //            if (!def.modExtensions.OfType<VirtualNonHumanlikePawnControlExtension>().Any())
            //            {
            //                var modExtension = new VirtualNonHumanlikePawnControlExtension();
            //                def.modExtensions.Add(modExtension);
            //            }

            //            // Then show confirmation (does not undo the above — you could revert if needed)
            //            List<string> list;
            //            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
            //                "PawnControl_InjectThisRaceNow".Translate(def.label),
            //                () =>
            //                {
            //                    list = Utility_ModExtensionResolver.GetEffectiveTags(def);
            //                    VirtualTagStorageService.Instance.Set(def, list);

            //                    Messages.Message("PawnControl_ThisRaceNowInjected".Translate(def.label), MessageTypeDefOf.TaskCompletion);

            //                    // Now rebuild the UI with updated virtual state
            //                    LongEventHandler.ExecuteWhenFinished(() =>
            //                    {
            //                        Close();
            //                        Find.WindowStack.Add(new Dialog_PawnTagEditor(def));
            //                    });
            //                }));
            //        }
            //    }
            //}

            if (SelectedModExtension == null)
            {
                if (Utility_NonHumanlikePawnControl.DebugMode())
                {
                    Messages.Message($"[PawnControl] No modExtension detected.", MessageTypeDefOf.TaskCompletion);
                }
                return;
            }

            // === Layout Rects ===
            Rect topArea = new Rect(inRect.x, inRect.y, inRect.width, topSectionHeight);
            float topX = topArea.x + margin;
            float topY = topArea.y;

            Rect leftPane = new Rect(topX, topY, leftPaneWidth, topSectionHeight - margin);
            Rect centerPane = new Rect(leftPane.xMax + margin, topY, centerPaneWidth, topSectionHeight - margin);
            Rect rightPane = new Rect(centerPane.xMax + margin, topY, rightPaneWidth, topSectionHeight - margin);
            Rect bottomPane = new Rect(inRect.x + margin, topArea.yMax + margin, inRect.width - margin * 2, bottomPaneHeight - margin);

            // === Draw Panels ===
            DrawTagListScrollView(leftPane);
            DrawSuggestionTagPane(centerPane);
            DrawAddTagPane(rightPane);
            DrawBottomControlPane(bottomPane);

            // === Floating tag visual (dragging)
            if (draggedTagDef != null && Event.current.type == EventType.Repaint)
            {
                Vector2 mouse = Event.current.mousePosition;
                Rect floatRect = new Rect(mouse.x + 15f, mouse.y + 5f, 120f, 24f);
                GUI.color = Color.yellow;
                Widgets.DrawBoxSolid(floatRect, new Color(0f, 0f, 0f, 0.4f));
                Widgets.Label(floatRect.ContractedBy(2f), draggedTagDef.label);
                GUI.color = Color.white;
            }

            // === Autocomplete dropdown for Add Tag field (aligned under AddTag textbox)
            if (!string.IsNullOrWhiteSpace(newTag))
            {
                deferredSuggestions = GetAutocompleteSuggestions(newTag).Take(10).ToList();
                if (Utility_CacheManager.allKnownTagNames == null)
                    Utility_CacheManager.allKnownTagNames = new HashSet<string>();
                showAddNewButton = !Utility_CacheManager.allKnownTagNames.Contains(newTag);

                float dropdownHeight = deferredSuggestions.Count * 24f + (showAddNewButton ? 30f : 0f);
                float dropdownX = rightPane.x + 120f;             // Same X offset as AddTag textbox
                float dropdownY = rightPane.y + 38f;              // Just below AddTag textbox
                float dropdownWidth = rightPane.width - 160f;

                deferredSuggestionRect = new Rect(dropdownX, dropdownY, dropdownWidth, dropdownHeight);
                DrawSuggestionDropdown(deferredSuggestionRect.Value, deferredSuggestions, ref newTag, showAddNewButton);
            }
        }

        /// <summary>
        /// Draws a dropdown list of suggestions at the given rect.
        /// Each entry is selectable and updates the newTag value.
        /// </summary>
        private void DrawSuggestionDropdown(Rect rect, List<string> matches, ref string input, bool allowAdd)
        {
            Widgets.DrawMenuSection(rect);

            float y = rect.y + 4f;
            foreach (var match in matches)
            {
                Rect option = new Rect(rect.x + 4f, y, rect.width - 8f, 24f);
                if (Widgets.ButtonText(option, match, false))
                {
                    input = match; // ✅ Set the field to the selected suggestion
                    GUI.FocusControl(null); // ensure refresh of Add button

                    // Don’t add immediately — let user click "Add" button to confirm
                    // Optionally, you could auto-fill AND auto-add here if desired

                    deferredSuggestions.Clear();
                    deferredSuggestionRect = null;
                    if (Utility_CacheManager.allKnownTagNames == null)
                        Utility_CacheManager.allKnownTagNames = new HashSet<string>();
                    showAddNewButton = !Utility_CacheManager.allKnownTagNames.Contains(input); // refresh button state
                    return;
                }
                y += 24f;
            }

            if (allowAdd)
            {
                Rect addNewRect = new Rect(rect.x + 4f, y, rect.width - 8f, 24f);
                GUI.color = Color.yellow;
                if (Widgets.ButtonText(addNewRect, "PawnControl_AddAsNew".Translate(input), false))
                {
                    if (DefDatabase<PawnTagDef>.GetNamedSilentFail(input) == null)
                        CreateTagDefFrom(input);

                    AddTag(input);
                    if (Utility_CacheManager.allKnownTagNames == null)
                        Utility_CacheManager.allKnownTagNames = new HashSet<string>();
                    Utility_CacheManager.allKnownTagNames.Add(input);
                    RebuildGroupedDefs();

                    input = ""; // ✅ Clear only after confirmed add
                    deferredSuggestions.Clear();
                    deferredSuggestionRect = null;
                    showAddNewButton = false;
                    GUI.FocusControl(null);
                }
                GUI.color = Color.white;
            }
        }

        /// <summary>
        /// Checks whether a tag is present in the active tag list.
        /// </summary>
        private bool HasTag(string tag)
        {
            return Utility_ModExtensionResolver.HasTag(def, tag);
        }

        /// <summary>
        /// Adds a tag to the appropriate storage, refreshes cache and UI.
        /// </summary>
        private void AddTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return;

            if (Utility_ModExtensionResolver.HasPhysicalModExtension(def))
            {
                Messages.Message("PawnControl_CannotEditPhysical".Translate(def.label), MessageTypeDefOf.RejectInput);
                return;
            }

            Utility_ModExtensionResolver.AddTag(def, tag);
            Utility_CacheManager.InvalidateTagCachesFor(def); // ✅ new line
            RebuildGroupedDefs();
            NotifyRestartRequired();
        }

        /// <summary>
        /// Removes a tag from the appropriate storage, refreshes cache and UI.
        /// </summary>
        private void RemoveTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return;

            if (Utility_ModExtensionResolver.HasPhysicalModExtension(def))
            {
                Messages.Message("PawnControl_CannotEditPhysical".Translate(def.label), MessageTypeDefOf.RejectInput);
                return;
            }

            Utility_ModExtensionResolver.RemoveTag(def, tag);
            Utility_CacheManager.InvalidateTagCachesFor(def); // ✅ new line
            RebuildGroupedDefs();
            NotifyRestartRequired();
        }

        /// <summary>
        /// Unified helper to add or remove a tag depending on toggle state.
        /// </summary>
        private void SetTagState(string tag, bool enabled)
        {
            if (string.IsNullOrWhiteSpace(tag)) return;

            if (Utility_ModExtensionResolver.HasPhysicalModExtension(def))
            {
                Messages.Message("PawnControl_CannotEditPhysical".Translate(def.label), MessageTypeDefOf.RejectInput);
                return;
            }

            Utility_ModExtensionResolver.SetTagState(def, tag, enabled);
            Utility_CacheManager.InvalidateTagCachesFor(def); // ✅ new line
            RebuildGroupedDefs();
            NotifyRestartRequired();
        }

        private void DrawPinnedTagRow(ref float y, Rect viewRect, string tag, int index)
        {
            Rect label = new Rect(0, y, viewRect.width - 100f, 30f);
            Widgets.Label(label, "PawnControl_Star_Pinned".Translate() + " " + tag);

            Rect up = new Rect(viewRect.width - 75f, y + 5f, 20f, 20f);
            Rect down = new Rect(viewRect.width - 50f, y + 5f, 20f, 20f);
            Rect del = new Rect(viewRect.width - 25f, y + 5f, 20f, 20f);

            if (index > 0 && Widgets.ButtonImage(up, TexButton.ReorderUp))
                pinnedTags.Swap(index, index - 1);
            if (index < pinnedTags.Count - 1 && Widgets.ButtonImage(down, TexButton.ReorderDown))
                pinnedTags.Swap(index, index + 1);
            if (Widgets.ButtonImage(del, TexButton.CloseXSmall))
                pinnedTags.RemoveAt(index);

            y += 34f;
        }

        /// <summary>
        /// Draw scrollable list of tags assigned to the selected pawn def, grouped by category.
        /// </summary>
        private void DrawTagListScrollView(Rect outRect)
        {
            var definedSet = new HashSet<string>(DefDatabase<PawnTagDef>.AllDefsListForReading.Select(p => p.defName));
            var effectiveTags = Utility_ModExtensionResolver.GetEffectiveTags(def);
            var unmatched = effectiveTags
                .Where(tag => DefDatabase<PawnTagDef>.GetNamedSilentFail(tag) == null &&
                              !Enum.GetNames(typeof(PawnEnumTags)).Contains(tag))
                .ToList();

            unmatched.Clear();

            float estimatedHeight = 24f + (pinnedTags.Count * 34f);
            int enumCount = Enum.GetNames(typeof(PawnEnumTags)).Length;
            estimatedHeight += enumCount * 34f + 24f;

            var nonEnumTagDefs = allPawnTagDefs.Where(p => !Utility_TagCatalog.IsEnumTag(p.defName)).ToList();
            foreach (var pair in nonEnumTagDefs.GroupBy(p => p.category ?? "PawnControl_Uncategorized".Translate()))
            {
                estimatedHeight += 24f + pair.Count() * 34f;
            }

            lastScrollViewHeight = estimatedHeight + 90f;

            Rect searchRect = new Rect(outRect.x, outRect.y, outRect.width, 30f);
            tagSearch = Widgets.TextField(searchRect, tagSearch);
            outRect.yMin += 35f;

            Rect viewRect = new Rect(0, 0, outRect.width - 16f, lastScrollViewHeight);
            Widgets.BeginScrollView(outRect, ref scrollPos, viewRect);

            float y = 0f;

            // === Pinned Tags ===
            if (pinnedTags.Count > 0)
            {
                Widgets.Label(new Rect(0, y, viewRect.width, 24f), "PawnControl_FavoritesHeader".Translate());
                y += 24f;

                for (int i = 0; i < pinnedTags.Count; i++)
                {
                    string tag = pinnedTags[i];
                    if (!string.IsNullOrEmpty(tagSearch) && !tag.ToLower().Contains(tagSearch.ToLower())) continue;
                    DrawPinnedTagRow(ref y, viewRect, tag, i);
                }
            }

            // === Enum-backed Tags ===
            Widgets.Label(new Rect(0, y, viewRect.width, 24f), "PawnControl_EnumTagsHeader".Translate());
            y += 24f;

            foreach (var enumName in Enum.GetNames(typeof(PawnEnumTags)))
            {
                string resolved = Utility_CacheManager.ResolveTagPriority(enumName);
                if (!string.IsNullOrEmpty(tagSearch) && !enumName.ToLower().Contains(tagSearch.ToLower())) continue;

                Rect tagRect = new Rect(20f, y, viewRect.width - 140f, 30f);
                Widgets.Label(tagRect, "[E] " + enumName);

                Rect buttonRect = new Rect(viewRect.width - 100f, y, 90f, 30f);
                bool active = Utility_ModExtensionResolver.HasTag(def, resolved);
                string label = active ? "PawnControl_Remove".Translate() : "PawnControl_AddTag".Translate();

                if (Widgets.ButtonText(buttonRect, label))
                {
                    ToggleResolvedTag(enumName);
                }
                y += 34f;
            }

            // === PawnTagDefs not from Enum ===
            foreach (var group in nonEnumTagDefs.GroupBy(p => p.category ?? "PawnControl_Uncategorized".Translate()))
            {
                string category = group.Key;
                if (!categoryCollapseStates.ContainsKey(category))
                    categoryCollapseStates[category] = !pinnedCategories.Contains(category);

                Rect catRect = new Rect(0, y, viewRect.width, 24f);
                Widgets.DrawHighlight(catRect);
                string labelWithPin = pinnedCategories.Contains(category) ? "📌 " : "   ";
                if (Widgets.ButtonText(catRect, labelWithPin + (categoryCollapseStates[category] ? "[+] " : "[-] ") + category))
                {
                    if (Event.current.button == 1)
                    {
                        if (pinnedCategories.Contains(category)) pinnedCategories.Remove(category);
                        else pinnedCategories.Add(category);
                    }
                    else
                    {
                        categoryCollapseStates[category] = !categoryCollapseStates[category];
                    }
                }

                y += 24f;
                if (categoryCollapseStates[category]) continue;

                foreach (var tagDef in group)
                {
                    if (!string.IsNullOrEmpty(tagSearch) && !tagDef.label.ToLower().Contains(tagSearch.ToLower())) continue;

                    Rect tagRect = new Rect(20f, y, viewRect.width - 140f, 30f);
                    string prefix = tagDef.category == "Auto" ? "[A] " : "";
                    GUI.color = tagDef.color ?? Color.white;
                    Widgets.Label(tagRect, prefix + tagDef.label);
                    GUI.color = Color.white;

                    Rect buttonRect = new Rect(viewRect.width - 100f, y, 90f, 30f);
                    bool active = Utility_ModExtensionResolver.HasTag(def, tagDef.defName);
                    string label = active ? "PawnControl_Remove".Translate() : "PawnControl_AddTag".Translate();

                    if (Widgets.ButtonText(buttonRect, label))
                    {
                        ToggleResolvedTag(tagDef.defName);
                    }

                    y += 34f;
                }
            }

            // === Unmatched string-only tags section [S] ===
            if (unmatched.Count > 0)
            {
                y += 10f;
                Widgets.Label(new Rect(0, y, viewRect.width, 24f), "[S] " + "PawnControl_StringOnlyTags".Translate());
                y += 24f;

                foreach (string tag in unmatched)
                {
                    if (!string.IsNullOrEmpty(tagSearch) && !tag.ToLower().Contains(tagSearch.ToLower()))
                        continue;

                    Rect tagRect = new Rect(20f, y, viewRect.width - 132f, 30f);
                    GUI.color = Color.gray;
                    Widgets.Label(tagRect, "[S] " + tag);
                    GUI.color = Color.white;
                    TooltipHandler.TipRegion(tagRect, "PawnControl_UnmatchedStringTooltip".Translate());

                    Rect actionRect = new Rect(viewRect.width - 100f, y, 100f, 30f);
                    string removeText = "PawnControl_Remove".CanTranslate() ? (string)"PawnControl_Remove".Translate() : "Remove";

                    if (Widgets.ButtonText(actionRect, removeText))
                    {
                        RemoveTag(tag);
                    }

                    y += 34f;
                }
            }

            Widgets.EndScrollView();
        }

        /// <summary>
        /// Resolves tag name and toggles it between selected and unselected state.
        /// </summary>
        private void ToggleResolvedTag(string rawTag)
        {
            string resolved = Utility_CacheManager.ResolveTagPriority(rawTag);
            if (HasTag(resolved))
                RemoveTag(resolved);
            else
                AddTag(resolved);
        }

        /// <summary>
        /// Determines if a given tagDef should appear as selected (active) in the UI,
        /// resolving potential conflicts between enum-defined tags and XML-based ones.
        /// </summary>
        private bool IsTagActiveForDisplay(PawnTagDef tagDef)
        {
            return Utility_ModExtensionResolver.HasTag(def, tagDef.defName);
        }

        /// <summary>
        /// Draw the bottom control pane with checkboxes (Force flags, AllowAllWork, BlockAllWork, AutoDraftInjection).
        /// Positioned at the bottom of the dialog.
        /// </summary>
        private void DrawBottomControlPane(Rect rect)
        {
            if (Utility_ModExtensionResolver.HasVirtualModExtension(def) || SelectedModExtension == null || SelectedModExtension != VirtualModExtension)
            {
                return; // 🛡 Skip for physical previews or invalid state
            }

            var modExtension = VirtualModExtension;

            Widgets.DrawBoxSolid(rect, new Color(0.18f, 0.18f, 0.18f, 0.35f));

            float spacing = 12f;
            float lineH = 26f;
            float checkW = 280f;

            float x = rect.x + 20f;
            float y = rect.y + 10f;

            // === First Row: Force Flags ===
            bool forceAnimal = modExtension.forceAnimal ?? false;
            Widgets.CheckboxLabeled(new Rect(x, y, checkW, lineH), "PawnControl_ForceAnimal".Translate(), ref forceAnimal);
            modExtension.forceAnimal = forceAnimal;

            x += checkW + spacing;
            bool forceDraftable = modExtension.forceDraftable ?? false;
            Widgets.CheckboxLabeled(new Rect(x, y, checkW, lineH), "PawnControl_ForceDraftable".Translate(), ref forceDraftable);
            modExtension.forceDraftable = forceDraftable;

            x += checkW + spacing;
            bool forceTrainerTab = modExtension.forceTrainerTab ?? false;
            Widgets.CheckboxLabeled(new Rect(x, y, checkW, lineH), "PawnControl_ForceTrainerTab".Translate(), ref forceTrainerTab);
            modExtension.forceTrainerTab = forceTrainerTab;

            x += checkW + spacing;
            bool forceWork = modExtension.forceWork ?? false;
            Widgets.CheckboxLabeled(new Rect(x, y, checkW, lineH), "PawnControl_ForceWork".Translate(), ref forceWork);
            modExtension.forceWork = forceWork;

            // === Second Row: Logical Tags (requires restart message) ===
            x = rect.x + 20f;
            y += lineH + spacing;

            bool allowAllWork = HasTag(ManagedTags.AllowAllWork);
            bool beforeAllow = allowAllWork;
            Widgets.CheckboxLabeled(new Rect(x, y, checkW, lineH), "PawnControl_AllowAllWork".Translate(), ref allowAllWork);
            if (allowAllWork != beforeAllow)
            {
                SetTagState(ManagedTags.AllowAllWork, allowAllWork);
                Messages.Message("PawnControl_TagChange_RequiresRestart".Translate(), MessageTypeDefOf.NeutralEvent);
            }

            x += checkW + spacing;
            bool blockAllWork = HasTag(ManagedTags.BlockAllWork);
            bool beforeBlock = blockAllWork;
            Widgets.CheckboxLabeled(new Rect(x, y, checkW, lineH), "PawnControl_BlockAllWork".Translate(), ref blockAllWork);
            if (blockAllWork != beforeBlock)
            {
                SetTagState(ManagedTags.BlockAllWork, blockAllWork);
                Messages.Message("PawnControl_TagChange_RequiresRestart".Translate(), MessageTypeDefOf.NeutralEvent);
            }

            x += checkW + spacing;
            bool autoDraft = HasTag(ManagedTags.AutoDraftInjection);
            bool beforeDraft = autoDraft;
            Widgets.CheckboxLabeled(new Rect(x, y, checkW, lineH), "PawnControl_AutoDraftInjection".Translate(), ref autoDraft);
            if (autoDraft != beforeDraft)
            {
                SetTagState(ManagedTags.AutoDraftInjection, autoDraft);
                Messages.Message("PawnControl_TagChange_RequiresRestart".Translate(), MessageTypeDefOf.NeutralEvent);
            }

            // === Horizontal Row: Import / Export / Export CE Patch ===
            x = rect.x + 20f;
            y += lineH + spacing * 2; // only advance once for the row

            float buttonWidth = 200f;
            float buttonHeight = 30f;
            float spacingH = 10f; // horizontal spacing between buttons

            // Import
            if (Widgets.ButtonText(new Rect(x, y, buttonWidth, buttonHeight), "PawnControl_ImportVirtualTags".Translate()))
            {
                string path = Path.Combine(GenFilePaths.SaveDataFolderPath, "ExportedPawnTags.xml");
                if (File.Exists(path))
                {
                    emitbreaker.PawnControl.VirtualTagStorage.Instance.LoadFromXmlFile(path);
                    Messages.Message("PawnControl_ImportedVirtualTags".Translate(path), MessageTypeDefOf.TaskCompletion);
                    Messages.Message("PawnControl_TagChange_RequiresRestart".Translate(), MessageTypeDefOf.NeutralEvent);
                }
                else
                {
                    Messages.Message("PawnControl_ImportFailed_NoFile".Translate(path), MessageTypeDefOf.RejectInput);
                }
            }

            x += buttonWidth + spacingH;

            // Export
            if (Widgets.ButtonText(new Rect(x, y, buttonWidth, buttonHeight), "PawnControl_ExportVirtualTags".Translate()))
            {
                string path = Path.Combine(GenFilePaths.SaveDataFolderPath, "ExportedPawnTags.xml");
                emitbreaker.PawnControl.VirtualTagStorage.Instance.SaveToXmlFile(path);
                Messages.Message("PawnControl_ExportedTo".Translate(path), MessageTypeDefOf.TaskCompletion);
            }

            x += buttonWidth + spacingH;

            // Export CE Patch
            if (Widgets.ButtonText(new Rect(x, y, buttonWidth, buttonHeight), "PawnControl_ExportCEPatch".Translate()))
            {
                string path = Path.Combine(GenFilePaths.SaveDataFolderPath, "PawnControl_CE_Patch.xml");
                Utility_CEPatchExporter.ExportToCEPatchFormat(def, Utility_ModExtensionResolver.GetEffectiveTags(def).ToList(), path);
                Messages.Message("PawnControl_ExportedTo".Translate(path), MessageTypeDefOf.TaskCompletion);
            }
        }

        /// <summary>
        /// Draws the suggestion tag list (rightmost vertical pane).
        /// Supports tag selection, hover, drag, and right-click actions.
        /// </summary>
        private Vector2 scrollPositionSuggestion;
        private void DrawSuggestionTagPane(Rect rect)
        {
            if (Utility_ModExtensionResolver.HasPhysicalModExtension(def))
            {
                Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.1f, 0.4f));
                Widgets.Label(rect.ContractedBy(4f), "PawnControl_CannotEditPhysical".Translate());
                return;
            }

            Widgets.DrawBoxSolid(rect, new Color(0.15f, 0.15f, 0.15f, 0.3f)); // background

            Rect inner = rect.ContractedBy(6f);
            Rect viewRect = new Rect(0f, 0f, inner.width - 16f, 10000f); // Arbitrary large height
            float curY = 0f;

            List<string> tagsToAdd = new List<string>();
            List<string> tagsToRemove = new List<string>();

            List<string> activeTags = Utility_ModExtensionResolver.GetEffectiveTags(def).ToList();
            List<string> enumTags = Enum.GetNames(typeof(PawnEnumTags)).OrderBy(t => t).ToList();
            List<PawnTagDef> suggested = Utility_CacheManager.GetSuggestedTags(def);

            // Calculate full height
            int tagCount = enumTags.Count + suggested.Count(tag => !enumTags.Contains(tag?.defName));
            float fullHeight = 30f + tagCount * 26f; // 30f for title + 26f per tag row
            viewRect.height = fullHeight;

            // Begin scrollable area
            Widgets.BeginScrollView(inner, ref scrollPositionSuggestion, viewRect);

            // Title
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(new Rect(0f, curY, viewRect.width, 24f), "PawnControl_SuggestedTags".Translate());
            curY += 30f;

            // === ENUM TAGS (always shown)
            foreach (string enumTag in enumTags)
            {
                string resolved = Utility_CacheManager.ResolveTagPriority(enumTag);
                bool isActive = Utility_ModExtensionResolver.HasTag(def, resolved);
                Color color = isActive ? Color.green : Color.gray;

                Rect tagRect = new Rect(0f, curY, viewRect.width, 24f);
                if (curY + 26f >= scrollPositionSuggestion.y && curY <= scrollPositionSuggestion.y + inner.height) // Visible?
                {
                    GUI.color = color;
                    Widgets.Label(tagRect, "[E] " + enumTag);
                    GUI.color = Color.white;

                    if (Widgets.ButtonInvisible(tagRect))
                    {
                        if (Utility_ModExtensionResolver.HasPhysicalModExtension(def))
                        {
                            Messages.Message("PawnControl_CannotEditPhysical".Translate(def.label), MessageTypeDefOf.RejectInput);
                        }
                        else
                        {
                            if (isActive)
                                tagsToRemove.Add(resolved);
                            else
                                tagsToAdd.Add(resolved);
                        }
                    }
                }
                curY += 26f;
            }

            // === AUTO / NON-ENUM TAGS
            foreach (var tagDef in suggested)
            {
                if (tagDef == null) continue;
                if (enumTags.Contains(tagDef.defName)) continue;

                string resolved = Utility_CacheManager.ResolveTagPriority(tagDef.defName);
                bool isActive = Utility_ModExtensionResolver.HasTag(def, resolved);

                Rect tagRect = new Rect(0f, curY, viewRect.width, 24f);
                if (curY + 26f >= scrollPositionSuggestion.y && curY <= scrollPositionSuggestion.y + inner.height) // Visible?
                {
                    GUI.color = isActive ? Color.green : (tagDef.color ?? Color.white);
                    Widgets.Label(tagRect, "[A] " + tagDef.label);
                    GUI.color = Color.white;

                    if (Widgets.ButtonInvisible(tagRect))
                    {
                        if (isActive)
                            tagsToRemove.Add(resolved);
                        else
                            tagsToAdd.Add(resolved);
                    }
                }
                curY += 26f;
            }

            Widgets.EndScrollView();
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            // === Apply batched changes
            foreach (var tag in tagsToAdd)
            {
                AddTag(tag); // ✅ centralized, respects priority and saves
            }

            foreach (var tag in tagsToRemove)
            {
                RemoveTag(tag); // ✅ centralized, respects priority and saves
            }
        }

        /// <summary>
        /// New fourth column: Add Tag input + autocomplete + confirm
        /// </summary>
        private void DrawAddTagPane(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.2f, 0.2f, 0.2f, 0.3f));

            float margin = 8f;
            float rowH = 30f;
            float labelW = 100f;

            float y = rect.y + margin;
            float x = rect.x + margin;
            float inputW = rect.width - labelW - margin * 3 - 100f; // space for button

            // === Label
            Widgets.Label(new Rect(x, y, labelW, rowH), "PawnControl_NewTag".Translate());

            // === Text Field
            Rect inputRect = new Rect(x + labelW + margin, y, inputW, rowH);
            newTag = Widgets.TextField(inputRect, newTag);

            // === Add Button
            Rect addBtn = new Rect(x + labelW + margin + inputW + margin, y, 90f, rowH);
            bool canAdd = !string.IsNullOrWhiteSpace(newTag) && !Utility_ModExtensionResolver.HasTag(def, newTag);

            if (canAdd && Widgets.ButtonText(addBtn, "PawnControl_AddTag".Translate()))
            {
                CreateTagDefFrom(newTag); // ✅ handles duplication safely now

                AddTag(newTag);
                if (Utility_CacheManager.allKnownTagNames == null)
                    Utility_CacheManager.allKnownTagNames = new HashSet<string>();
                Utility_CacheManager.allKnownTagNames.Add(newTag);
                RebuildGroupedDefs();

                newTag = ""; // ✅ Clear only after confirmed add
                deferredSuggestions.Clear();
                deferredSuggestionRect = null;
                showAddNewButton = false;
                GUI.FocusControl(null);
            }

            // === Autocomplete Dropdown
            if (!string.IsNullOrWhiteSpace(newTag))
            {
                deferredSuggestions = GetAutocompleteSuggestions(newTag).Take(10).ToList();
                if (Utility_CacheManager.allKnownTagNames == null)
                    Utility_CacheManager.allKnownTagNames = new HashSet<string>();
                showAddNewButton = !Utility_CacheManager.allKnownTagNames.Contains(newTag);

                float dropH = deferredSuggestions.Count * 24f + (showAddNewButton ? 30f : 0f);
                deferredSuggestionRect = new Rect(inputRect.x, inputRect.yMax, inputRect.width, dropH);

                DrawSuggestionDropdown(deferredSuggestionRect.Value, deferredSuggestions, ref newTag, showAddNewButton);
            }
        }

        // Opens right-click context menu for suggestion tags
        private void HandleTagContextMenu(Rect tagRect, PawnTagDef tagDef)
        {
            if (Mouse.IsOver(tagRect) && Event.current.type == EventType.MouseDown && Event.current.button == 1)
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>
                {
                    new FloatMenuOption("PawnControl_Context_Remove".Translate(), () => RemoveTag(tagDef.defName)),
                    new FloatMenuOption("PawnControl_Context_Revert".Translate(), () => RemoveTag(tagDef.defName)) // can be extended
                };

                Find.WindowStack.Add(new FloatMenu(options));
                Event.current.Use(); // consume event
            }
        }

        /// <summary>
        /// Add or remove a tag from the modExtension.tags and selectedTags.
        /// Also updates tag selection cache and ensures suggestion pane reflects changes.
        /// </summary>
        /// <summary>
        /// Add or remove a tag from the provided tag list (usually modExtension.tags) and selectedTags.
        /// Also updates cache and ensures consistent UI and storage.
        /// </summary>
        private void DrawImportExportTools(ref float rx, ref float ry)
        {
            string exportPath = Path.Combine(GenFilePaths.SaveDataFolderPath, "ExportedPawnTags.xml");

            if (Widgets.ButtonText(new Rect(rx, ry, 200f, 30f), "PawnControl_ExportVirtualTags".Translate()))
            {
                emitbreaker.PawnControl.VirtualTagStorage.Instance.SaveToXmlFile(exportPath);
                Messages.Message("PawnControl_ExportedVirtualTags".Translate(exportPath), MessageTypeDefOf.TaskCompletion);
            }

            if (Widgets.ButtonText(new Rect(rx + 210f, ry, 200f, 30f), "PawnControl_ImportVirtualTags".Translate()))
            {
                if (File.Exists(exportPath))
                {
                    emitbreaker.PawnControl.VirtualTagStorage.Instance.LoadFromXmlFile(exportPath);
                    Messages.Message("PawnControl_ImportedVirtualTags".Translate(exportPath), MessageTypeDefOf.TaskCompletion);
                }
                else
                {
                    Messages.Message("PawnControl_ImportFailed_NoFile".Translate(exportPath), MessageTypeDefOf.RejectInput);
                }
            }

            ry += 40f;
        }

        /// <summary>
        /// Draws the preset dropdown and buttons, using unified AddTag logic for both injected and virtual.
        /// </summary>
        private void DrawPresetsAndClipboard(ref float rx, ref float ry)
        {
            Rect presetRect = new Rect(rx, ry, 420f, 30f);
            Widgets.Dropdown<object, string>(
                presetRect,
                "Preset",
                _ => "PawnControl_SelectPreset".Translate(),
                _ => new List<Widgets.DropdownMenuElement<string>> {
            new Widgets.DropdownMenuElement<string> {
                option = new FloatMenuOption("PawnControl_Button_ApplyWorkerPreset".Translate(), () => {
                    if (Utility_ModExtensionResolver.HasPhysicalModExtension(def))
                    {
                        Messages.Message("PawnControl_CannotEditPhysical".Translate(def.label), MessageTypeDefOf.RejectInput);
                        return;
                    }

                    foreach (string tag in NonHumanlikePawnRolePresets.Worker)
                    {
                        if (!HasTag(tag))
                            AddTag(tag);
                    }

                    Utility_CacheManager.InvalidateTagCachesFor(def);
                    Messages.Message("PawnControl_TagChange_RequiresRestart".Translate(), MessageTypeDefOf.NeutralEvent);
                }),
                payload = "Worker"
            },
            new Widgets.DropdownMenuElement<string> {
                option = new FloatMenuOption("PawnControl_Button_ApplyCombatPreset".Translate(), () => {
                    if (Utility_ModExtensionResolver.HasPhysicalModExtension(def))
                    {
                        Messages.Message("PawnControl_CannotEditPhysical".Translate(def.label), MessageTypeDefOf.RejectInput);
                        return;
                    }

                    foreach (string tag in NonHumanlikePawnRolePresets.CombatServitor)
                    {
                        if (!HasTag(tag))
                            AddTag(tag);
                    }

                    Utility_CacheManager.InvalidateTagCachesFor(def);
                    Messages.Message("PawnControl_TagChange_RequiresRestart".Translate(), MessageTypeDefOf.NeutralEvent);
                }),
                payload = "Combat"
            }
                }.Concat(PawnTagPresetManager.LoadAllPresets()
                    .Select(p => new Widgets.DropdownMenuElement<string>
                    {
                        option = new FloatMenuOption("PawnControl_Apply".Translate() + " " + p.name, () =>
                        {
                            if (Utility_ModExtensionResolver.HasPhysicalModExtension(def))
                            {
                                Messages.Message("PawnControl_CannotEditPhysical".Translate(def.label), MessageTypeDefOf.RejectInput);
                                return;
                            }

                            var tagsToApply = p.tags.Where(t => !Utility_ModExtensionResolver.HasTag(def, t)).ToList();
                            foreach (var tag in tagsToApply)
                                AddTag(tag);

                            Utility_CacheManager.InvalidateTagCachesFor(def);
                            Messages.Message("PawnControl_TagChange_RequiresRestart".Translate(), MessageTypeDefOf.NeutralEvent);
                        }),
                        payload = p.name
                    })
                ).ToList(),
                "PawnControl_PresetLabel".Translate()
            );

            ry += 40f;

            if (Widgets.ButtonText(new Rect(rx, ry, 200f, 30f), "PawnControl_OpenPresetManager".Translate()))
            {
                Find.WindowStack.Add(new Dialog_PresetManager(preset =>
                {
                    Utility_NonHumanlikePawnControl.TryApplyPresetToActiveTagEditor(preset);
                }));
            }

            ry += 40f;
        }

        private void NotifyRestartRequired()
        {
            Messages.Message("PawnControl_TagChange_RequiresRestart".Translate(), MessageTypeDefOf.NeutralEvent);
        }

        // === Create tag def manually in dev mode ===
        private void CreateTagDefFrom(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return;

            PawnTagDef existing = DefDatabase<PawnTagDef>.GetNamedSilentFail(tag);

            if (existing != null)
            {
                // 🚫 Skip if it's an enum or autogenerated tag
                if (existing.category == "Enum" || existing.category == "Auto")
                {
                    Log.Message($"[PawnControl] Skipped creating tag '{tag}' because it's from category '{existing.category}'.");
                    return;
                }

                // ✅ Already exists and not enum/auto — don't override
                Log.Message($"[PawnControl] TagDef '{tag}' already exists in DefDatabase (category: {existing.category}). Skipped.");
                return;
            }

            PawnTagDef newDef = new PawnTagDef
            {
                defName = tag,
                label = tag,
                category = "Imported"
            };

            DefDatabase<PawnTagDef>.Add(newDef);
            Log.Message($"[PawnControl] Registered new PawnTagDef: {tag}");

            // Refresh display
            allPawnTagDefs = DefDatabase<PawnTagDef>.AllDefsListForReading.OrderBy(d => d.label).ToList();
            RebuildGroupedDefs();
        }
    }
}