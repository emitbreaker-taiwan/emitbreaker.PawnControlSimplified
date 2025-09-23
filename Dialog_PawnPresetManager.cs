using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace emitbreaker.PawnControl
{
    public class Dialog_PawnPresetManager : Window
    {
        private ThingDef _def;
        public ThingDef SelectedDef => _def;

        // Scrollview positions
        private Vector2 extPaneScroll;
        private Vector2 raceDefScroll;

        // Preset management
        private List<PawnTagDef> availablePresets = new List<PawnTagDef>();
        private PawnTagDef selectedPreset;

        // Race selection - cached data
        private RaceTypeFlag? selectedRaceType = null;
        private string raceDefSearch = "";
        private List<string> raceDefSuggestions = new List<string>();
        private Dictionary<string, ThingDef> cachedRaceDefLookup = new Dictionary<string, ThingDef>();
        private List<ThingDef> cachedRaceDefs = new List<ThingDef>();
        private bool raceDefsNeedUpdate = true;

        // Window sizing constants
        private const float FooterHeight = 50f;
        private const float TitleHeight = 40f;
        private const float LineHeight = 24f;
        private const float ButtonHeight = 30f;
        private const float WindowMargin = 10f;
        private const float Gap = 8f;

        // UI constants for better maintainability
        private const float MaxSuggestionItems = 15f; // Limit visible suggestions for performance
        private const int MaxSearchResults = 50; // Limit search results

        public Dialog_PawnPresetManager(ThingDef def)
        {
            // IMPORTANT: Clear cached mod extension for this race to prevent showing extensions from previous saves
            Utility_CacheManager.ClearModExtension(def);

            this._def = def;
            this.windowRect = new Rect(100f, 100f, 1000f, 700f);
            this.doCloseX = true;
            this.doCloseButton = true;
            this.forcePause = true;
            this.absorbInputAroundWindow = true;

            // Load available presets for this race type
            RefreshPresetsForRace(def);
        }

        public override Vector2 InitialSize => new Vector2(1000f, 700f);

        public override void DoWindowContents(Rect inRect)
        {
            float contentWidth = inRect.width - (WindowMargin * 2);

            // Title
            var titleRect = new Rect(inRect.x, inRect.y, contentWidth, TitleHeight);
            DrawTitle(titleRect);

            // Content area (below title)
            var contentRect = new Rect(
                inRect.x,
                inRect.y + TitleHeight + WindowMargin,
                contentWidth,
                inRect.height - TitleHeight - FooterHeight - (WindowMargin * 2)
            );

            // Split into two panels
            float panelWidth = contentRect.width / 2f - WindowMargin;

            // Left panel: Race selection
            var leftPanelRect = new Rect(contentRect.x, contentRect.y, panelWidth, contentRect.height);
            DrawRaceSelectionPanel(leftPanelRect);

            // Right panel: Preset management
            var rightPanelRect = new Rect(
                contentRect.x + panelWidth + (WindowMargin * 2),
                contentRect.y,
                panelWidth,
                contentRect.height
            );
            DrawPresetManagementPanel(rightPanelRect);
        }

        private void DrawTitle(Rect titleRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(titleRect, "PawnControl_Title_PresetManager".Translate());
            Text.Font = GameFont.Small;
        }

        private void DrawRaceSelectionPanel(Rect rect)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.05f);
            Widgets.DrawBox(rect);
            GUI.color = Color.white;

            float curY = rect.y + Gap;

            // Race type dropdown button
            curY = DrawRaceTypeSelector(rect, curY);

            // Race search field (only if race type is selected)
            if (selectedRaceType.HasValue)
            {
                curY = DrawRaceSearchField(rect, curY);
                curY = DrawRaceSuggestions(rect, curY);
            }

            // Show info about currently selected def (if any)
            DrawSelectedRaceInfo(rect);
        }

        private float DrawRaceTypeSelector(Rect rect, float curY)
        {
            var typeRect = new Rect(rect.x + Gap, curY, rect.width - (Gap * 2), ButtonHeight);
            string typeLabel = selectedRaceType?.ToString() ?? "PawnControl_Button_SelectRaceType".Translate().ToString();

            if (Widgets.ButtonText(typeRect, typeLabel))
            {
                ShowRaceTypeMenu();
            }
            return curY + ButtonHeight + Gap;
        }

        private void ShowRaceTypeMenu()
        {
            var options = new List<FloatMenuOption>();
            foreach (RaceTypeFlag flag in Enum.GetValues(typeof(RaceTypeFlag)))
            {
                options.Add(new FloatMenuOption(flag.ToString(), () => SelectRaceType(flag)));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void SelectRaceType(RaceTypeFlag flag)
        {
            selectedRaceType = flag;
            raceDefSearch = "";
            raceDefSuggestions.Clear();
            raceDefsNeedUpdate = true; // Mark for refresh
        }

        private float DrawRaceSearchField(Rect rect, float curY)
        {
            var searchRect = new Rect(rect.x + Gap, curY, rect.width - (Gap * 2), ButtonHeight);
            GUI.SetNextControlName("RaceSearchField");

            string newSearch = Widgets.TextField(searchRect, raceDefSearch);
            if (newSearch != raceDefSearch)
            {
                raceDefSearch = newSearch;
                raceDefsNeedUpdate = true;
            }

            return curY + ButtonHeight + Gap;
        }

        private float DrawRaceSuggestions(Rect rect, float curY)
        {
            // Update race definitions cache if needed
            if (raceDefsNeedUpdate)
            {
                UpdateRaceDefCache();
                raceDefsNeedUpdate = false;
            }

            if (!raceDefSuggestions.Any()) return curY;

            // Calculate suggestion list size with limits
            float remainingHeight = rect.yMax - curY - Gap * 2;
            int maxItems = Mathf.Min(raceDefSuggestions.Count, (int)MaxSuggestionItems);
            float listHeight = Mathf.Min(remainingHeight, LineHeight * maxItems);

            var suggestionRect = new Rect(rect.x + Gap, curY, rect.width - (Gap * 2), listHeight);
            Widgets.DrawMenuSection(suggestionRect);

            // Scrollable list
            float contentHeight = raceDefSuggestions.Count * LineHeight;
            var viewRect = new Rect(suggestionRect.x, suggestionRect.y, suggestionRect.width - 16f, contentHeight);

            Widgets.BeginScrollView(suggestionRect, ref raceDefScroll, viewRect);

            float y = viewRect.y;
            foreach (string defName in raceDefSuggestions)
            {
                var itemRect = new Rect(viewRect.x, y, viewRect.width, LineHeight);
                if (cachedRaceDefLookup.TryGetValue(defName, out ThingDef raceDef))
                {
                    string displayText = $"{raceDef.LabelCap} ({defName})";

                    if (Widgets.ButtonText(itemRect, displayText, false))
                    {
                        SelectRaceFromSuggestion(defName, raceDef);
                        break;
                    }
                }
                y += LineHeight;
            }

            Widgets.EndScrollView();
            return curY + listHeight + Gap;
        }

        private void UpdateRaceDefCache()
        {
            if (!selectedRaceType.HasValue) return;

            // Cache race definitions for the selected type
            cachedRaceDefs = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(td => td.race != null
                          && td.thingClass != typeof(Corpse)
                          && GetRaceTypeFlag(td) == selectedRaceType.Value)
                .OrderBy(td => td.LabelCap.ToString())
                .Take(MaxSearchResults) // Limit results for performance
                .ToList();

            // Create lookup dictionary
            cachedRaceDefLookup = cachedRaceDefs.ToDictionary(d => d.defName);

            // Update suggestions based on search
            UpdateSuggestions();
        }

        private void UpdateSuggestions()
        {
            if (string.IsNullOrWhiteSpace(raceDefSearch))
            {
                raceDefSuggestions = cachedRaceDefs.Select(td => td.defName).ToList();
            }
            else
            {
                // Case-insensitive search on both defName and label
                string searchLower = raceDefSearch.ToLower();
                raceDefSuggestions = cachedRaceDefs
                    .Where(td => td.defName.ToLower().Contains(searchLower) ||
                                td.LabelCap.ToString().ToLower().Contains(searchLower))
                    .Select(td => td.defName)
                    .Take(MaxSearchResults)
                    .ToList();
            }
        }

        private void SelectRaceFromSuggestion(string defName, ThingDef raceDef)
        {
            raceDefSearch = defName;
            GUI.FocusControl(null);
            raceDefSuggestions.Clear();
            SelectRace(raceDef);
        }

        private void DrawSelectedRaceInfo(Rect rect)
        {
            if (string.IsNullOrWhiteSpace(raceDefSearch)) return;

            ThingDef raceDef = DefDatabase<ThingDef>.GetNamedSilentFail(raceDefSearch);
            if (raceDef == null) return;

            float infoY = rect.yMax - (LineHeight * 3) - Gap;

            GUI.color = new Color(0.8f, 0.8f, 0.8f);
            Widgets.Label(new Rect(rect.x + Gap, infoY, rect.width - (Gap * 2), LineHeight),
                $"Selected Race: {raceDef.LabelCap}");

            Widgets.Label(new Rect(rect.x + Gap, infoY + LineHeight, rect.width - (Gap * 2), LineHeight),
                $"Type: {GetRaceTypeFlag(raceDef)}");

            GUI.color = Color.white;
        }

        private void RefreshPresetsForRace(ThingDef raceDef)
        {
            RaceTypeFlag raceFlag = GetRaceTypeFlag(raceDef);
            this.availablePresets = DefDatabase<PawnTagDef>
                .AllDefsListForReading
                .Where(pt => pt.targetRaceType == raceFlag)
                .ToList();
        }

        private void SelectRace(ThingDef raceDef)
        {
            if (raceDef == null) return;

            this._def = raceDef;
            RefreshPresetsForRace(raceDef);
            selectedPreset = null;

            // Force cache refresh
            Utility_CacheManager.ClearModExtension(raceDef);

            Utility_DebugManager.LogNormal($"Selected race {raceDef.defName} with {availablePresets.Count} available presets");
        }

        private void DrawPresetManagementPanel(Rect rect)
        {
            float curY = rect.y + Gap;
            var modExtension = Utility_CacheManager.GetModExtension(_def);

            // Preset selection dropdown
            curY = DrawPresetSelector(rect, curY);

            // Content area
            var contentArea = new Rect(
                rect.x + Gap,
                curY,
                rect.width - (Gap * 2),
                rect.height - curY - ButtonHeight - (Gap * 3)
            );

            DrawExtensionDetails(contentArea, modExtension);
            DrawActionButtons(rect, modExtension);
        }

        private float DrawPresetSelector(Rect rect, float curY)
        {
            var selectPresetRect = new Rect(rect.x + Gap, curY, rect.width - (Gap * 2), ButtonHeight);
            string presetButtonLabel = selectedPreset?.LabelCap ?? "PawnControl_Button_SelectPreset".Translate();

            if (Widgets.ButtonText(selectPresetRect, presetButtonLabel))
            {
                ShowPresetMenu();
            }
            return curY + ButtonHeight + Gap * 2;
        }

        private void ShowPresetMenu()
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("(None)", () => selectedPreset = null)
            };

            options.AddRange(availablePresets.Select(preset =>
                new FloatMenuOption(preset.LabelCap, () => selectedPreset = preset)));

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void DrawExtensionDetails(Rect contentArea, NonHumanlikePawnControlExtension modExtension)
        {
            // Determine what to display
            var (extensionToShow, labelToShow, descriptionToShow) = GetDisplayContent(modExtension);

            // Calculate content height
            float contentHeight = CalculateContentHeight(extensionToShow, labelToShow, descriptionToShow);

            var viewRect = new Rect(contentArea.x, contentArea.y, contentArea.width, contentHeight);
            Widgets.BeginScrollView(contentArea, ref extPaneScroll, viewRect);

            float y = DrawExtensionContent(viewRect, extensionToShow, labelToShow, descriptionToShow);

            Widgets.EndScrollView();
        }

        private (NonHumanlikePawnControlExtension extension, string label, string description) GetDisplayContent(
            NonHumanlikePawnControlExtension modExtension)
        {
            // Case 1: Show existing modExtension if present
            if (modExtension != null)
            {
                var associatedDef = DefDatabase<PawnTagDef>.AllDefsListForReading
                    .FirstOrDefault(d => d.modExtension == modExtension);

                return (modExtension, associatedDef?.LabelCap ?? "", associatedDef?.description ?? "");
            }

            // Case 2: Show selected preset if available
            if (selectedPreset != null)
            {
                return (selectedPreset.modExtension, selectedPreset.LabelCap, selectedPreset.description);
            }

            return (null, "", "");
        }

        private float CalculateContentHeight(NonHumanlikePawnControlExtension extension, string label, string description)
        {
            float height = Gap * 4; // Initial padding

            if (!string.IsNullOrEmpty(label)) height += LineHeight + 16f; // Title spacing
            if (!string.IsNullOrEmpty(description)) height += LineHeight * 2 + Gap;

            if (extension != null)
            {
                height += extension.tags.Count * LineHeight;
                if (extension.forceIdentity != null) height += LineHeight;
                if (extension.forceDraftable) height += LineHeight;
                if (extension.forceEquipWeapon) height += LineHeight;
                if (extension.forceWearApparel) height += LineHeight;
                if (!string.IsNullOrEmpty(extension.mainWorkThinkTreeDefName)) height += LineHeight;
                if (!string.IsNullOrEmpty(extension.constantThinkTreeDefName)) height += LineHeight;
            }

            return height;
        }

        private float DrawExtensionContent(Rect viewRect, NonHumanlikePawnControlExtension extension,
            string label, string description)
        {
            float y = viewRect.y + Gap * 2;

            // Draw title if available
            if (!string.IsNullOrEmpty(label))
            {
                y = DrawSectionTitle(viewRect, y, label);
            }

            // Draw description if available
            if (!string.IsNullOrEmpty(description))
            {
                y = DrawDescription(viewRect, y, description);
            }

            // Draw extension details
            if (extension != null)
            {
                y = DrawExtensionProperties(viewRect, y, extension);
            }
            else
            {
                y = DrawNoContentMessage(viewRect, y);
            }

            return y;
        }

        private float DrawSectionTitle(Rect viewRect, float y, string label)
        {
            // Visual separator
            GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            Widgets.DrawLineHorizontal(viewRect.x, y - Gap, viewRect.width);
            GUI.color = Color.white;

            // Title with highlight background
            var titleRect = new Rect(viewRect.x, y, viewRect.width, LineHeight);
            Widgets.DrawBoxSolid(titleRect.ExpandedBy(4f), new Color(0.3f, 0.3f, 0.3f, 0.1f));

            Text.Font = GameFont.Medium;
            GUI.color = new Color(1f, 0.9f, 0.7f);
            Widgets.Label(titleRect, label);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            return y + LineHeight + 16f;
        }

        private float DrawDescription(Rect viewRect, float y, string description)
        {
            GUI.color = new Color(0.9f, 0.9f, 0.9f);
            Widgets.Label(new Rect(viewRect.x, y, viewRect.width, LineHeight * 2), description);
            GUI.color = Color.white;

            // Separator after description
            y += LineHeight * 2 + Gap;
            GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.3f);
            Widgets.DrawLineHorizontal(viewRect.x, y, viewRect.width);
            GUI.color = Color.white;

            return y + Gap * 2;
        }

        private float DrawExtensionProperties(Rect viewRect, float y, NonHumanlikePawnControlExtension extension)
        {
            // Tags section
            GUI.color = new Color(0.85f, 0.85f, 0.85f);
            Widgets.Label(new Rect(viewRect.x, y, viewRect.width, LineHeight), "Tags and Capabilities:");
            GUI.color = Color.white;
            y += LineHeight + Gap;

            // Draw tags
            foreach (var tag in extension.tags)
            {
                Widgets.Label(new Rect(viewRect.x, y, viewRect.width, LineHeight), tag);
                y += LineHeight;
            }

            // Draw boolean properties
            y = DrawBooleanProperties(viewRect, y, extension);

            // Draw think tree properties
            y = DrawThinkTreeProperties(viewRect, y, extension);

            return y;
        }

        private float DrawBooleanProperties(Rect viewRect, float y, NonHumanlikePawnControlExtension extension)
        {
            if (extension.forceIdentity != null)
            {
                Widgets.Label(new Rect(viewRect.x, y, viewRect.width, LineHeight),
                    $"{extension.forceIdentity}");
                y += LineHeight;
            }

            var booleanProperties = new[]
            {
                (extension.forceDraftable, PawnEnumTags.ForceDraftable.ToString()),
                (extension.forceEquipWeapon, PawnEnumTags.ForceEquipWeapon.ToString()),
                (extension.forceWearApparel, PawnEnumTags.ForceWearApparel.ToString())
            };

            foreach (var (condition, label) in booleanProperties)
            {
                if (condition)
                {
                    Widgets.Label(new Rect(viewRect.x, y, viewRect.width, LineHeight), label);
                    y += LineHeight;
                }
            }

            return y;
        }

        private float DrawThinkTreeProperties(Rect viewRect, float y, NonHumanlikePawnControlExtension extension)
        {
            bool hasThinkTrees = !string.IsNullOrEmpty(extension.mainWorkThinkTreeDefName) ||
                               !string.IsNullOrEmpty(extension.constantThinkTreeDefName);

            if (!hasThinkTrees) return y;

            // Separator and header
            y += Gap;
            GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.3f);
            Widgets.DrawLineHorizontal(viewRect.x, y, viewRect.width);
            GUI.color = Color.white;
            y += Gap;

            GUI.color = new Color(0.85f, 0.85f, 0.85f);
            Widgets.Label(new Rect(viewRect.x, y, viewRect.width, LineHeight), "AI Behavior:");
            GUI.color = Color.white;
            y += LineHeight + Gap / 2;

            // Draw think tree entries
            if (!string.IsNullOrEmpty(extension.mainWorkThinkTreeDefName))
            {
                Widgets.Label(new Rect(viewRect.x, y, viewRect.width, LineHeight),
                    $"{"PawnControl_Item_MainWorkThinkTree".Translate()} {extension.mainWorkThinkTreeDefName}");
                y += LineHeight;
            }

            if (!string.IsNullOrEmpty(extension.constantThinkTreeDefName))
            {
                Widgets.Label(new Rect(viewRect.x, y, viewRect.width, LineHeight),
                    $"{"PawnControl_Item_ConstantWorkThinkTree".Translate()} {extension.constantThinkTreeDefName}");
                y += LineHeight;
            }

            return y;
        }

        private float DrawNoContentMessage(Rect viewRect, float y)
        {
            GUI.color = Color.gray;
            string message = availablePresets.Count == 0
                ? "PawnControl_Label_NoPresets".Translate()
                : "PawnControl_Label_NoPresetSelected".Translate();

            Widgets.Label(new Rect(viewRect.x, y, viewRect.width, LineHeight), message);
            GUI.color = Color.white;
            return y + LineHeight;
        }

        private void DrawActionButtons(Rect rect, NonHumanlikePawnControlExtension modExtension)
        {
            var buttonRect = new Rect(rect.x + Gap, rect.yMax - ButtonHeight - Gap, 120f, ButtonHeight);

            if (modExtension == null && selectedPreset != null)
            {
                // Apply button
                if (Widgets.ButtonText(buttonRect, "PawnControl_Button_Apply".Translate()))
                {
                    ApplyModExtensionPreset(selectedPreset);
                }
            }
            else if (modExtension != null && !modExtension.fromXML)
            {
                // Remove button
                string buttonText = modExtension.toBeRemoved
                    ? "PawnControl_Button_RemovalPending".Translate()
                    : "PawnControl_Button_Remove".Translate();

                if (Widgets.ButtonText(buttonRect, buttonText, !modExtension.toBeRemoved))
                {
                    RemoveModExtension(modExtension);
                }
            }

            // Debug button (if enabled)
            if (Utility_DebugManager.ShouldLog())
            {
                var debugRect = new Rect(rect.x + rect.width - 120f - Gap, rect.yMax - ButtonHeight - Gap, 120f, ButtonHeight);
                if (Widgets.ButtonText(debugRect, "Debug Status"))
                {
                    // Debug functionality can be added here
                    Utility_DebugManager.LogNormal($"Debug: Current def = {_def?.defName}, Selected preset = {selectedPreset?.defName}");
                }
            }
        }

        private void RemoveModExtension(NonHumanlikePawnControlExtension modExtension)
        {
            string originalMainTree = modExtension.originalMainWorkThinkTreeDefName;
            string originalConstantTree = modExtension.originalConstantThinkTreeDefName;

            modExtension.toBeRemoved = true;
            Utility_CacheManager.UpdateModExtension(_def, modExtension);

            // GameComponent에서 런타임 확장 제거
            if (Current.Game?.GetComponent<GameComponent_LateInjection>() is var gameComponent && gameComponent != null)
            {
                gameComponent.RemoveRuntimeModExtension(_def);
            }

            // DefModExtensions에서 완전히 제거
            if (_def.modExtensions != null)
            {
                _def.modExtensions.RemoveAll(ext => ext == modExtension);
                if (_def.modExtensions.Count == 0)
                {
                    _def.modExtensions = null; // 빈 리스트 제거
                }
            }

            // 기존 폰들의 WorkSettings 정리
            CleanupWorkSettingsForRace(_def, originalMainTree, originalConstantTree);

            Messages.Message("PawnControl_Message_ModExtensionMarkedForRemoval".Translate(), MessageTypeDefOf.TaskCompletion);
        }

        private void CleanupWorkSettingsForRace(ThingDef raceDef, string originalMainTree = null, string originalConstantTree = null)
        {
            if (raceDef == null) return;

            var modExtension = Utility_CacheManager.GetModExtension(raceDef);

            RestoreOriginalThinkTrees(raceDef, originalMainTree, originalConstantTree);
            Utility_TagManager.ClearCacheForRace(raceDef);
            Utility_IdentityManager.BuildIdentityFlagCache(true);

            int cleanedCount = 0;

            // 맵의 모든 폰들 처리
            foreach (Map map in Find.Maps)
            {
                if (map?.mapPawns?.AllPawnsSpawned == null) continue;

                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (pawn?.def != raceDef || pawn.Dead || pawn.Destroyed) continue;
                    if (pawn.RaceProps.Humanlike) continue; // 휴먼라이크는 건드리지 않음

                    try
                    {
                        // WorkSettings 제거
                        if (pawn.workSettings != null)
                        {
                            pawn.workSettings = null;
                            cleanedCount++;
                            Utility_DebugManager.LogNormal($"Removed WorkSettings from {pawn.LabelShort}");
                        }

                        // Drafter 제거
                        if (pawn.drafter != null && !pawn.RaceProps.Humanlike)
                        {
                            pawn.drafter = null;
                            Utility_DebugManager.LogNormal($"Removed Drafter from {pawn.LabelShort}");
                        }

                        // Equipment/Apparel Tracker 정리 (안전한 방법)
                        if (modExtension != null)
                        {
                            if (modExtension.forceEquipWeapon && pawn.equipment != null && !pawn.RaceProps.Humanlike)
                            {
                                // 장비만 제거하고 tracker는 유지 (다른 모드가 사용할 수 있음)
                                if (pawn.equipment.Primary != null)
                                {
                                    pawn.equipment.TryDropEquipment(pawn.equipment.Primary, out ThingWithComps droppedWeapon, pawn.Position);
                                }
                            }

                            if (modExtension.forceWearApparel && pawn.apparel != null && !pawn.RaceProps.Humanlike)
                            {
                                // 의복만 제거하고 tracker는 유지
                                pawn.apparel.DestroyAll();
                            }
                        }

                        // 캐시에서 제거
                        Utility_WorkSettingsManager.RemovePawnFromCache(pawn);
                    }
                    catch (Exception ex)
                    {
                        Utility_DebugManager.LogError($"Error cleaning up {pawn.LabelShort}: {ex}");
                    }
                }
            }

            // 월드 폰들도 처리
            if (Find.World?.worldPawns?.AllPawnsAliveOrDead != null)
            {
                var worldPawns = Find.World.worldPawns.AllPawnsAliveOrDead
                    .Where(p => p != null && !p.Dead && p.def == raceDef && !p.RaceProps.Humanlike)
                    .ToList();

                foreach (Pawn pawn in worldPawns)
                {
                    try
                    {
                        // WorkSettings 제거
                        if (pawn.workSettings != null)
                        {
                            pawn.workSettings = null;
                            cleanedCount++;
                            Utility_DebugManager.LogNormal($"Removed WorkSettings from {pawn.LabelShort}");
                        }

                        // Drafter 제거
                        if (pawn.drafter != null && !pawn.RaceProps.Humanlike)
                        {
                            pawn.drafter = null;
                            Utility_DebugManager.LogNormal($"Removed Drafter from {pawn.LabelShort}");
                        }

                        // Equipment/Apparel Tracker 정리 (안전한 방법)
                        if (modExtension != null)
                        {
                            if (modExtension.forceEquipWeapon && pawn.equipment != null && !pawn.RaceProps.Humanlike)
                            {
                                // 장비만 제거하고 tracker는 유지 (다른 모드가 사용할 수 있음)
                                if (pawn.equipment.Primary != null)
                                {
                                    pawn.equipment.TryDropEquipment(pawn.equipment.Primary, out ThingWithComps droppedWeapon, pawn.Position);
                                }
                            }

                            if (modExtension.forceWearApparel && pawn.apparel != null && !pawn.RaceProps.Humanlike)
                            {
                                // 의복만 제거하고 tracker는 유지
                                pawn.apparel.DestroyAll();
                            }
                        }

                        // 캐시에서 제거
                        Utility_WorkSettingsManager.RemovePawnFromCache(pawn);
                    }
                    catch (Exception ex)
                    {
                        Utility_DebugManager.LogError($"Error cleaning up world pawn {pawn.LabelShort}: {ex}");
                    }
                }
            }

            if (cleanedCount > 0)
            {
                Utility_DebugManager.LogNormal($"Cleaned up WorkSettings for {cleanedCount} pawns of race {raceDef.defName}");
            }
        }

        private void RestoreOriginalThinkTrees(ThingDef raceDef, string originalMainTree = null, string originalConstantTree = null)
        {
            // 매개변수로 받은 값이 있으면 우선 사용, 없으면 modExtension에서 가져오기
            string mainTreeToRestore = originalMainTree;
            string constantTreeToRestore = originalConstantTree;

            if (string.IsNullOrEmpty(mainTreeToRestore) || string.IsNullOrEmpty(constantTreeToRestore))
            {
                var modExtension = Utility_CacheManager.GetModExtension(raceDef);
                if (modExtension != null)
                {
                    mainTreeToRestore = mainTreeToRestore ?? modExtension.originalMainWorkThinkTreeDefName;
                    constantTreeToRestore = constantTreeToRestore ?? modExtension.originalConstantThinkTreeDefName;
                }
            }

            try
            {
                // 원본 메인 Think Tree 복원
                if (!string.IsNullOrEmpty(mainTreeToRestore))
                {
                    var originalMainTreeDef = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(mainTreeToRestore);
                    if (originalMainTreeDef != null)
                    {
                        raceDef.race.thinkTreeMain = originalMainTreeDef;
                        Utility_DebugManager.LogNormal($"Restored original main think tree {originalMainTreeDef.defName} for {raceDef.defName}");
                    }
                }

                // 원본 상수 Think Tree 복원
                if (!string.IsNullOrEmpty(constantTreeToRestore))
                {
                    var originalConstantTreeDef = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(constantTreeToRestore);
                    if (originalConstantTreeDef != null)
                    {
                        raceDef.race.thinkTreeConstant = originalConstantTreeDef;
                        Utility_DebugManager.LogNormal($"Restored original constant think tree {originalConstantTreeDef.defName} for {raceDef.defName}");
                    }
                }
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error restoring original think trees for {raceDef.defName}: {ex}");
            }
        }

        // Keeping the existing ApplyModExtensionPreset, ApplyExtensionToExistingPawns, and CloneModExtension methods
        // as they contain complex business logic that shouldn't be changed without careful testing

        private void ApplyModExtensionPreset(PawnTagDef presetDef)
        {
            if (presetDef?.modExtension == null)
            {
                Utility_DebugManager.LogError($"Cannot apply preset: {(presetDef == null ? "null preset" : "null modExtension")}");
                Messages.Message("PawnControl_Error_NoPreset".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            var currentExt = Utility_CacheManager.GetModExtension(_def);
            if (currentExt != null)
            {
                Utility_DebugManager.LogWarning($"Race {_def.defName} already has a mod extension - will not replace");
                Messages.Message("PawnControl_Error_ModExtensionExists".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            try
            {
                var newExtension = CloneModExtension(presetDef.modExtension);
                newExtension.fromXML = false;

                // Ensure the def has a modExtensions list
                if (_def.modExtensions == null)
                    _def.modExtensions = new List<DefModExtension>();

                newExtension.CacheSimulatedSkillLevels();
                newExtension.CacheSkillPassions();
                _def.modExtensions.Add(newExtension);

                // Clear caches and reload
                Utility_CacheManager.ClearModExtension(_def);
                Utility_TagManager.ClearCacheForRace(_def);
                Utility_CacheManager.PreloadModExtensionForRace(_def);

                ApplyExtensionToExistingPawns(_def, newExtension);

                // Save to game component
                Current.Game?.GetComponent<GameComponent_LateInjection>()?.SaveRuntimeModExtension(_def, newExtension);

                Messages.Message($"{"PawnControl_Error_PresetApplied".Translate()} {presetDef.LabelCap}", MessageTypeDefOf.TaskCompletion);
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error applying preset '{presetDef.defName}': {ex}");
                Messages.Message($"{"PawnControl_Error_PresetApplyError".Translate()} {ex.Message}", MessageTypeDefOf.RejectInput);
            }
        }

        // [Keep existing ApplyExtensionToExistingPawns and CloneModExtension methods unchanged]
        // These methods contain complex game logic and reflection operations that should not be modified
        // without extensive testing

        private void ApplyExtensionToExistingPawns(ThingDef raceDef, NonHumanlikePawnControlExtension modExtension)
        {
            // [Original implementation preserved for safety]
            if (raceDef == null || modExtension == null) return;
            if (Find.Maps == null) return;

            int updatedCount = 0;

            // Store original think trees in the extension before changing them
            modExtension.originalMainWorkThinkTreeDefName = raceDef.race.thinkTreeMain?.defName;
            modExtension.originalConstantThinkTreeDefName = raceDef.race.thinkTreeConstant?.defName;

            Utility_DebugManager.LogNormal($"Stored original think trees for {raceDef.defName}: Main={modExtension.originalMainWorkThinkTreeDefName}, Constant={modExtension.originalConstantThinkTreeDefName}");

            // Apply static think tree changes at race level if specified
            if (!string.IsNullOrEmpty(modExtension.mainWorkThinkTreeDefName))
            {
                var thinkTree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(modExtension.mainWorkThinkTreeDefName);
                if (thinkTree != null)
                {
                    raceDef.race.thinkTreeMain = thinkTree;
                    Utility_DebugManager.LogNormal($"Applied main think tree {thinkTree.defName} to race {raceDef.LabelCap}");
                }
                else
                {
                    Utility_DebugManager.LogWarning($"Think tree {modExtension.mainWorkThinkTreeDefName} not found!");
                }
            }

            // Apply constant think tree if specified
            if (!string.IsNullOrEmpty(modExtension.constantThinkTreeDefName))
            {
                var constantTree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(modExtension.constantThinkTreeDefName);
                if (constantTree != null)
                {
                    raceDef.race.thinkTreeConstant = constantTree;
                    Utility_DebugManager.LogNormal($"Applied constant think tree {constantTree.defName} to race {raceDef.LabelCap}");
                }
                else
                {
                    Utility_DebugManager.LogWarning($"Constant think tree {modExtension.constantThinkTreeDefName} not found!");
                }
            }

            // Process all maps
            foreach (Map map in Find.Maps)
            {
                if (map?.mapPawns?.AllPawnsSpawned == null) continue;

                // Find all spawned pawns of the given race
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (pawn?.def != raceDef || pawn.Dead || pawn.Destroyed) continue;

                    try
                    {
                        // Apply drafters if needed
                        if (modExtension.forceDraftable && pawn.drafter == null)
                        {
                            Utility_DrafterManager.EnsureDrafter(pawn, modExtension);
                            Utility_DebugManager.LogNormal($"Added drafter to {pawn.LabelShort}, has drafter now: {pawn.drafter != null}");
                        }

                        // Ensure other trackers exist
                        if (modExtension.forceEquipWeapon || modExtension.forceWearApparel)
                        {
                            Utility_DrafterManager.EnsureAllTrackers(pawn);
                        }

                        // Apply think trees directly to the pawn
                        ApplyThinkTreesToPawn(pawn, modExtension);

                        updatedCount++;
                    }
                    catch (Exception ex)
                    {
                        Utility_DebugManager.LogError($"Error updating pawn {pawn.LabelShort}: {ex}");
                    }
                }
            }

            // Process world pawns
            updatedCount += ProcessWorldPawns(raceDef, modExtension);

            if (updatedCount > 0)
            {
                Utility_DebugManager.LogNormal($"Updated {updatedCount} existing pawns of race {raceDef.defName}");
            }
        }

        private void ApplyThinkTreesToPawn(Pawn pawn, NonHumanlikePawnControlExtension modExtension)
        {
            if (pawn.mindState == null) return;

            // Apply main think tree
            if (!string.IsNullOrEmpty(modExtension.mainWorkThinkTreeDefName))
            {
                var thinkTree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(modExtension.mainWorkThinkTreeDefName);
                if (thinkTree != null)
                {
                    var thinkTreeField = AccessTools.Field(pawn.mindState.GetType(), "thinkTree");
                    thinkTreeField?.SetValue(pawn.mindState, thinkTree);
                    Utility_DebugManager.LogNormal($"Set main think tree {thinkTree.defName} for {pawn.LabelShort}");
                }
            }

            // Apply constant think tree
            if (!string.IsNullOrEmpty(modExtension.constantThinkTreeDefName))
            {
                var constantTree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(modExtension.constantThinkTreeDefName);
                if (constantTree != null)
                {
                    var constThinkTreeField = AccessTools.Field(pawn.mindState.GetType(), "thinkTreeConstant");
                    constThinkTreeField?.SetValue(pawn.mindState, constantTree);
                    Utility_DebugManager.LogNormal($"Set constant think tree {constantTree.defName} for {pawn.LabelShort}");
                }
            }

            // Force think node rebuild
            if (pawn.thinker != null)
            {
                var thinkRootField = AccessTools.Field(pawn.thinker.GetType(), "thinkRoot");
                thinkRootField?.SetValue(pawn.thinker, null); // Null out to force rebuild
            }
        }

        private int ProcessWorldPawns(ThingDef raceDef, NonHumanlikePawnControlExtension modExtension)
        {
            if (Find.World?.worldPawns?.AllPawnsAliveOrDead == null) return 0;

            var worldPawns = Find.World.worldPawns.AllPawnsAliveOrDead
                .Where(p => p != null && !p.Dead && p.def == raceDef)
                .ToList();

            int count = 0;
            foreach (Pawn pawn in worldPawns)
            {
                try
                {
                    if (modExtension.forceDraftable && pawn.drafter == null)
                    {
                        Utility_DrafterManager.EnsureDrafter(pawn, modExtension, true);
                    }

                    ApplyThinkTreesToPawn(pawn, modExtension);
                    count++;
                }
                catch (Exception ex)
                {
                    Utility_DebugManager.LogError($"Error updating world pawn {pawn.LabelShort}: {ex}");
                }
            }

            return count;
        }

        private NonHumanlikePawnControlExtension CloneModExtension(NonHumanlikePawnControlExtension source)
        {
            if (source == null) return null;

            var clone = new NonHumanlikePawnControlExtension
            {
                mainWorkThinkTreeDefName = source.mainWorkThinkTreeDefName,
                constantThinkTreeDefName = source.constantThinkTreeDefName,
                baseSkillLevelOverride = source.baseSkillLevelOverride,
                skillLevelToolUser = source.skillLevelToolUser,
                skillLevelAnimalAdvanced = source.skillLevelAnimalAdvanced,
                skillLevelAnimalIntermediate = source.skillLevelAnimalIntermediate,
                skillLevelAnimalBasic = source.skillLevelAnimalBasic,
                forceIdentity = source.forceIdentity,
                forceDraftable = source.forceDraftable,
                forceEquipWeapon = source.forceEquipWeapon,
                forceWearApparel = source.forceWearApparel,
                restrictApparelByBodyType = source.restrictApparelByBodyType,
                debugMode = source.debugMode,
                fromXML = false // Always mark runtime copies as non-XML
            };

            // Deep copy collections
            clone.tags = source.tags != null ? new List<string>(source.tags) : new List<string>();
            clone.allowedBodyTypes = source.allowedBodyTypes != null ? new List<BodyTypeDef>(source.allowedBodyTypes) : new List<BodyTypeDef>();

            // Deep copy injected skills
            if (source.injectedSkills != null)
            {
                clone.injectedSkills = source.injectedSkills.Select(skill => new SkillLevelEntry
                {
                    skill = skill.skill,
                    level = skill.level
                }).ToList();
            }

            // Deep copy injected passions
            if (source.injectedPassions != null)
            {
                clone.injectedPassions = source.injectedPassions.Select(passion => new SkillPassionEntry
                {
                    skill = passion.skill,
                    passion = passion.passion
                }).ToList();
            }

            return clone;
        }

        private RaceTypeFlag GetRaceTypeFlag(ThingDef def)
        {
            var rp = def.race;
            if (rp == null) return RaceTypeFlag.Animal;
            if (rp.Humanlike) return RaceTypeFlag.Humanlike;
            if (rp.Animal) return RaceTypeFlag.Animal;
            if (rp.IsMechanoid) return RaceTypeFlag.Mechanoid;
            if (rp.ToolUser) return RaceTypeFlag.ToolUser;
            return RaceTypeFlag.Mutant;
        }
    }
}