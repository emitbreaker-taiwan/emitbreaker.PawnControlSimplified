using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime;
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

        // Race selection
        private RaceTypeFlag? selectedRaceType = null;
        private string raceDefSearch = "";
        private List<string> raceDefSuggestions = new List<string>();
        private Rect? raceDefSuggestionRect = null;

        // Window sizing
        private const float FooterHeight = 50f;
        private const float TitleHeight = 40f;
        private const float LineHeight = 24f;
        private const float ButtonHeight = 30f;

        public Dialog_PawnPresetManager(ThingDef def)
        {
            this._def = def;

            // Double the default window size
            this.windowRect = new Rect(100f, 100f, 1000f, 700f);
            this.doCloseX = true;
            this.doCloseButton = true;
            this.forcePause = true;
            this.absorbInputAroundWindow = true;

            // Load available presets for this race type
            RaceTypeFlag raceFlag = GetRaceTypeFlag(def);
            this.availablePresets = DefDatabase<PawnTagDef>
                .AllDefsListForReading
                .Where(pt => pt.targetRaceType == raceFlag)
                .ToList();
        }

        public override Vector2 InitialSize => new Vector2(1000f, 700f);

        public override void DoWindowContents(Rect inRect)
        {
            const float margin = 10f;
            float contentWidth = inRect.width - (margin * 2);

            // Title
            var titleRect = new Rect(inRect.x, inRect.y, contentWidth, TitleHeight);
            Text.Font = GameFont.Medium;
            Widgets.Label(titleRect, "PawnControl_Title_PresetManager".Translate());
            Text.Font = GameFont.Small;

            // Content area (below title)
            var contentRect = new Rect(
                inRect.x,
                inRect.y + TitleHeight + margin,
                contentWidth,
                inRect.height - TitleHeight - FooterHeight - (margin * 2)
            );

            // Split into two panels (each doubled in size)
            float panelWidth = contentRect.width / 2f - margin;

            // Left panel: Race selection
            var leftPanelRect = new Rect(
                contentRect.x,
                contentRect.y,
                panelWidth,
                contentRect.height
            );
            DrawRaceSelectionPanel(leftPanelRect);

            // Right panel: Preset management
            var rightPanelRect = new Rect(
                contentRect.x + panelWidth + (margin * 2),
                contentRect.y,
                panelWidth,
                contentRect.height
            );
            DrawPresetManagementPanel(rightPanelRect);
        }

        private void DrawRaceSelectionPanel(Rect rect)
        {
            const float buttonH = ButtonHeight;
            const float fieldH = ButtonHeight;
            const float lineH = LineHeight;
            const float gap = 8f;

            GUI.color = new Color(1f, 1f, 1f, 0.05f);
            Widgets.DrawBox(rect);
            GUI.color = Color.white;

            float curY = rect.y + gap;

            // Race type dropdown button
            var typeRect = new Rect(rect.x + gap, curY, rect.width - (gap * 2), buttonH);
            string typeLabel;
            if (selectedRaceType.HasValue)
            {
                typeLabel = selectedRaceType.Value.ToString();
            }
            else
            {
                typeLabel = "PawnControl_Button_SelectRaceType".Translate().ToString();
            }

            if (Widgets.ButtonText(typeRect, typeLabel))
            {
                var options = new List<FloatMenuOption>();
                foreach (RaceTypeFlag flag in Enum.GetValues(typeof(RaceTypeFlag)))
                {
                    options.Add(new FloatMenuOption(flag.ToString(), () =>
                    {
                        selectedRaceType = flag;
                        raceDefSearch = "";
                        raceDefSuggestions.Clear();
                        raceDefSuggestionRect = null;
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            curY += buttonH + gap;

            // Race search field (only if race type is selected)
            if (selectedRaceType.HasValue)
            {
                var searchRect = new Rect(rect.x + gap, curY, rect.width - (gap * 2), fieldH);
                GUI.SetNextControlName("RaceSearchField");
                raceDefSearch = Widgets.TextField(searchRect, raceDefSearch);
                curY += fieldH + gap;

                // Filter race definitions and sort by label
                var allDefs = DefDatabase<ThingDef>.AllDefsListForReading
                    .Where(td => td.race != null
                              && td.thingClass != typeof(Corpse)
                              && GetRaceTypeFlag(td) == selectedRaceType.Value)
                    .OrderBy(td => td.LabelCap.ToString());  // Sort by label

                // Create a dictionary to quickly map defNames back to ThingDefs
                Dictionary<string, ThingDef> defNameToThingDef = allDefs.ToDictionary(d => d.defName);

                // Generate suggestions with proper filtering
                if (string.IsNullOrWhiteSpace(raceDefSearch))
                {
                    // If no search, show all sorted by label
                    raceDefSuggestions = allDefs.Select(td => td.defName).ToList();
                }
                else
                {
                    // If searching, filter by both defName and label
                    raceDefSuggestions = allDefs
                        .Where(td => td.defName.IndexOf(raceDefSearch, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     td.LabelCap.ToString().IndexOf(raceDefSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                        .Select(td => td.defName)
                        .ToList();
                }

                // Show suggestions in a scrolling list with expanded height
                if (raceDefSuggestions.Any())
                {
                    // Use the remaining height for the suggestions (minus footer padding)
                    float remainingHeight = rect.yMax - curY - gap * 2;
                    float listHeight = Mathf.Min(remainingHeight, lineH * raceDefSuggestions.Count);

                    raceDefSuggestionRect = new Rect(
                        rect.x + gap,
                        curY,
                        rect.width - (gap * 2),
                        listHeight
                    );

                    // Draw suggestion background
                    Widgets.DrawMenuSection(raceDefSuggestionRect.Value);

                    // Calculate scrollview content height
                    float contentHeight = raceDefSuggestions.Count * lineH;
                    var viewRect = new Rect(
                        raceDefSuggestionRect.Value.x,
                        raceDefSuggestionRect.Value.y,
                        raceDefSuggestionRect.Value.width - 16f, // Account for scrollbar
                        contentHeight
                    );

                    Widgets.BeginScrollView(raceDefSuggestionRect.Value, ref raceDefScroll, viewRect);

                    // Draw each race suggestion with label
                    float y = viewRect.y;
                    foreach (string defName in raceDefSuggestions)
                    {
                        var itemRect = new Rect(viewRect.x, y, viewRect.width, lineH);
                        ThingDef raceDef = defNameToThingDef[defName];

                        // Show label and defName in the dropdown
                        string displayText = raceDef.LabelCap + " (" + defName + ")";

                        if (Widgets.ButtonText(itemRect, displayText, false))
                        {
                            raceDefSearch = defName;
                            GUI.FocusControl(null);
                            raceDefSuggestions.Clear();
                            raceDefSuggestionRect = null;

                            // If there's a ThingDef with this name, update our def
                            ThingDef selectedDef = defNameToThingDef[defName]; // Use our dictionary instead of lookup
                            if (selectedDef != null)
                            {
                                SelectRace(selectedDef);
                            }
                            break;
                        }

                        y += lineH;
                    }

                    Widgets.EndScrollView();
                }
            }

            // Show info about currently selected def (if any)
            if (!string.IsNullOrWhiteSpace(raceDefSearch))
            {
                ThingDef raceDef = DefDatabase<ThingDef>.GetNamedSilentFail(raceDefSearch);
                if (raceDef != null)
                {
                    float infoY = rect.yMax - (LineHeight * 3) - gap;

                    GUI.color = new Color(0.8f, 0.8f, 0.8f);
                    Widgets.Label(new Rect(rect.x + gap, infoY, rect.width - (gap * 2), LineHeight),
                        "Selected Race: " + raceDef.LabelCap);

                    Widgets.Label(new Rect(rect.x + gap, infoY + LineHeight, rect.width - (gap * 2), LineHeight),
                        "Type: " + GetRaceTypeFlag(raceDef));

                    GUI.color = Color.white;
                }
            }
        }

        private void SelectRace(ThingDef raceDef)
        {
            if (raceDef == null) return;

            // Update our current def reference
            this._def = raceDef;

            // Update available presets for this race
            RaceTypeFlag raceFlag = GetRaceTypeFlag(raceDef);

            // Log the race flag for debugging
            Utility_DebugManager.LogNormal($"Selected race {raceDef.defName} with type flag: {raceFlag}");

            // Get all possible presets
            var allPresets = DefDatabase<PawnTagDef>.AllDefsListForReading;

            // Filter for matching race type
            this.availablePresets = allPresets
                .Where(pt => pt.targetRaceType == raceFlag)
                .ToList();

            // Log the filtering results
            Utility_DebugManager.LogNormal($"Found {availablePresets.Count} matching presets out of {allPresets.Count} total presets");
            foreach (var preset in availablePresets)
            {
                Utility_DebugManager.LogNormal($"  Matching preset: {preset.defName} (targetRaceType: {preset.targetRaceType})");
            }

            // Clear preset selection
            selectedPreset = null;

            // Force cache refresh to ensure we're getting the latest mod extension state
            Utility_CacheManager.ClearModExtensionCachePerInstance(raceDef);
        }

        private void DrawPresetManagementPanel(Rect rect)
        {
            const float buttonH = ButtonHeight;
            const float lineH = LineHeight;
            const float gap = 8f;

            float curY = rect.y + gap;

            // Get current mod extension (if any)
            var modExtension = Utility_CacheManager.GetModExtension(_def);

            // Show preset selection button
            var selectPresetRect = new Rect(rect.x + gap, curY, rect.width - (gap * 2), buttonH);
            string presetButtonLabel = selectedPreset != null
                ? selectedPreset.LabelCap
                : "PawnControl_Button_SelectPreset".Translate();

            if (Widgets.ButtonText(selectPresetRect, presetButtonLabel))
            {
                var options = new List<FloatMenuOption>();

                // Add "none" option
                options.Add(new FloatMenuOption("(None)", () =>
                {
                    selectedPreset = null;
                }));

                // Add available presets
                foreach (var preset in availablePresets)
                {
                    options.Add(new FloatMenuOption(preset.LabelCap, () =>
                    {
                        selectedPreset = preset;
                    }));
                }

                Find.WindowStack.Add(new FloatMenu(options));
            }
            curY += buttonH + gap * 2;

            // Determine what to display in the content area
            NonHumanlikePawnControlExtension extensionToShow = null;
            string labelToShow = "";
            string descriptionToShow = "";

            // Case 1: Show existing modExtension if present
            if (modExtension != null)
            {
                extensionToShow = modExtension;

                // Try to find the associated PawnTagDef if it exists
                var associatedDef = DefDatabase<PawnTagDef>.AllDefsListForReading
                    .FirstOrDefault(d => d.modExtension == modExtension);

                if (associatedDef != null)
                {
                    labelToShow = associatedDef.LabelCap;
                    descriptionToShow = associatedDef.description;
                }
            }
            // Case 2: Show selected preset if available
            else if (selectedPreset != null)
            {
                extensionToShow = selectedPreset.modExtension;
                labelToShow = selectedPreset.LabelCap;
                descriptionToShow = selectedPreset.description;
            }

            // Content area for extension details
            var contentArea = new Rect(
                rect.x + gap,
                curY,
                rect.width - (gap * 2),
                rect.height - curY - buttonH - (gap * 3) // Leave room for buttons at bottom
            );

            // Calculate content height based on extension details
            float contentHeight = gap * 2; // Initial padding

            if (!string.IsNullOrEmpty(labelToShow))
                contentHeight += lineH;

            if (!string.IsNullOrEmpty(descriptionToShow))
                contentHeight += lineH * 2;

            if (extensionToShow != null)
            {
                contentHeight += extensionToShow.tags.Count * lineH;
                if (extensionToShow.forceIdentity != null) contentHeight += lineH;
                if (extensionToShow.forceDraftable) contentHeight += lineH;
                if (extensionToShow.forceEquipWeapon) contentHeight += lineH;
                if (extensionToShow.forceWearApparel) contentHeight += lineH;
                if (!string.IsNullOrEmpty(extensionToShow.mainWorkThinkTreeDefName)) contentHeight += lineH;
                if (!string.IsNullOrEmpty(extensionToShow.constantThinkTreeDefName)) contentHeight += lineH;
            }

            var viewRect = new Rect(
                contentArea.x,
                contentArea.y,
                contentArea.width,
                contentHeight
            );

            Widgets.BeginScrollView(contentArea, ref extPaneScroll, viewRect);

            float y = viewRect.y + gap;

            // Display label if available
            if (!string.IsNullOrEmpty(labelToShow))
            {
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(viewRect.x, y, viewRect.width, lineH), labelToShow);
                Text.Font = GameFont.Small;
                y += lineH * 2;
            }

            // Display description if available
            if (!string.IsNullOrEmpty(descriptionToShow))
            {
                GUI.color = new Color(0.9f, 0.9f, 0.9f);
                Widgets.Label(new Rect(viewRect.x, y, viewRect.width, lineH * 2), descriptionToShow);
                GUI.color = Color.white;
                y += lineH * 2;
            }

            // Add a small gap
            y += gap;

            // Display extension details
            if (extensionToShow != null)
            {
                foreach (var tag in extensionToShow.tags)
                {
                    Widgets.Label(new Rect(viewRect.x, y, viewRect.width, lineH), tag);
                    y += lineH;
                }

                if (extensionToShow.forceIdentity != null)
                {
                    Widgets.Label(new Rect(viewRect.x, y, viewRect.width, lineH),
                        "PawnControl_Button_ForceIdentity".Translate() + " " + extensionToShow.forceIdentity);
                    y += lineH;
                }
                if (extensionToShow.forceDraftable)
                {
                    Widgets.Label(new Rect(viewRect.x, y, viewRect.width, lineH),
                        PawnEnumTags.ForceDraftable.ToString());
                    y += lineH;
                }
                if (extensionToShow.forceEquipWeapon)
                {
                    Widgets.Label(new Rect(viewRect.x, y, viewRect.width, lineH),
                        PawnEnumTags.ForceEquipWeapon.ToString());
                    y += lineH;
                }
                if (extensionToShow.forceWearApparel)
                {
                    Widgets.Label(new Rect(viewRect.x, y, viewRect.width, lineH),
                        PawnEnumTags.ForceWearApparel.ToString());
                    y += lineH;
                }
                if (!string.IsNullOrEmpty(extensionToShow.mainWorkThinkTreeDefName))
                {
                    Widgets.Label(new Rect(viewRect.x, y, viewRect.width, lineH),
                        "PawnControl_Item_MainWorkThinkTree".Translate() + " " + extensionToShow.mainWorkThinkTreeDefName);
                    y += lineH;
                }
                if (!string.IsNullOrEmpty(extensionToShow.constantThinkTreeDefName))
                {
                    Widgets.Label(new Rect(viewRect.x, y, viewRect.width, lineH),
                        "PawnControl_Item_ConstantWorkThinkTree".Translate() + " " + extensionToShow.constantThinkTreeDefName);
                    y += lineH;
                }
            }
            else if (availablePresets.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(viewRect.x, y, viewRect.width, lineH),
                    "PawnControl_Label_NoPresets".Translate());
                GUI.color = Color.white;
            }
            else if (selectedPreset == null)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(viewRect.x, y, viewRect.width, lineH),
                    "PawnControl_Label_NoPresetSelected".Translate());
                GUI.color = Color.white;
            }

            Widgets.EndScrollView();

            // Footer buttons
            if (modExtension == null && selectedPreset != null)
            {
                // Apply button - only show when a preset is selected and there's no current mod extension
                var applyRect = new Rect(rect.x + gap, rect.yMax - buttonH - gap, 120f, buttonH);
                if (Widgets.ButtonText(applyRect, "PawnControl_Button_Apply".Translate()))
                {
                    ApplyModExtensionPreset(selectedPreset);
                }
            }
            // In DrawPresetManagementPanel method within Dialog_PawnPresetManager.cs
            else if (modExtension != null && modExtension.fromXML == false)
            {
                // Remove button - only show for injected extensions (not XML-defined ones)
                var removeRect = new Rect(rect.x + gap, rect.yMax - buttonH - gap, 120f, buttonH);

                // Change button label if already marked for removal
                string buttonText = modExtension.toBeRemoved
                    ? "PawnControl_Button_RemovalPending".Translate()
                    : "PawnControl_Button_Remove".Translate();

                // Disable button if already marked for removal
                if (Widgets.ButtonText(removeRect, buttonText, !modExtension.toBeRemoved))
                {
                    // Mark for removal instead of removing immediately
                    modExtension.toBeRemoved = true;

                    // Update cache
                    Utility_CacheManager.UpdateModExtensionCache(_def, modExtension);
                    // Also remove from the current game component
                    if (Current.Game != null)
                    {
                        var gameComponent = Current.Game.GetComponent<GameComponent_LateInjection>();
                        if (gameComponent != null)
                        {
                            gameComponent.RemoveRuntimeModExtension(_def);
                        }
                    }

                    Messages.Message("PawnControl_Message_ModExtensionMarkedForRemoval".Translate(),MessageTypeDefOf.TaskCompletion);
                }
            }
            if (Utility_DebugManager.ShouldLog())
            {
                var debugRect = new Rect(rect.x + rect.width - 120f - gap, rect.yMax - buttonH - gap, 120f, buttonH);
                if (Widgets.ButtonText(debugRect, "Debug Status"))
                {
                    // Check mod extension and race properties
                    Utility_DebugManager.LogNormal($"{_def.defName} status:");
                    Utility_DebugManager.LogNormal($"- Has mod extension: {Utility_CacheManager.GetModExtension(_def) != null}");
                    if (_def.modExtensions != null)
                    {
                        foreach (var ext in _def.modExtensions)
                        {
                            Utility_DebugManager.LogNormal($"- Ext type: {ext.GetType().Name}");
                            if (ext is NonHumanlikePawnControlExtension pcExt)
                            {
                                Utility_DebugManager.LogNormal($"  - forceDraftable: {pcExt.forceDraftable}");
                                Utility_DebugManager.LogNormal($"  - mainThinkTree: {pcExt.mainWorkThinkTreeDefName}");
                            }
                        }
                    }

                    // Check living pawns
                    if (Find.Maps != null)
                    {
                        foreach (Map map in Find.Maps)
                        {
                            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                            {
                                if (pawn?.def == _def && !pawn.Dead)
                                {
                                    Utility_DebugManager.LogNormal($"Pawn {pawn.LabelShort} status:");
                                    Utility_DebugManager.LogNormal($"- Has drafter: {pawn.drafter != null}");
                                    Utility_DebugManager.LogNormal($"- HasEquipment: {pawn.equipment != null}");
                                    Utility_DebugManager.LogNormal($"- HasApparel: {pawn.apparel != null}");
                                    Utility_DebugManager.LogNormal($"- ThinkTree: {pawn.thinker?.MainThinkTree?.defName ?? "null"}");
                                    Utility_DebugManager.LogNormal($"- ThinkTree: {pawn.thinker?.ConstantThinkTree?.defName ?? "null"}");
                                }
                            }
                        }
                    }
                }
            }
        }

        private void ApplyModExtensionPreset(PawnTagDef presetDef)
        {
            // Detailed null checks
            if (presetDef == null)
            {
                Utility_DebugManager.LogError("Cannot apply null preset definition");
                Messages.Message("PawnControl_Error_NoPreset".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            if (presetDef.modExtension == null)
            {
                Utility_DebugManager.LogError($"Preset '{presetDef.defName}' has a null modExtension");
                Messages.Message("PawnControl_Error_NoModExtension".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            // Log the preset we're applying for debugging
            Utility_DebugManager.LogNormal($"Applying preset '{presetDef.defName}' to race {_def.defName}");
            Utility_DebugManager.LogNormal($" - forceDraftable: {presetDef.modExtension.forceDraftable}");
            Utility_DebugManager.LogNormal($" - tags count: {presetDef.modExtension.tags.Count}");
            Utility_DebugManager.LogNormal($" - mainThinkTree: {presetDef.modExtension.mainWorkThinkTreeDefName}");

            try
            {
                // If we're replacing an existing extension, clean up trackers first
                var currentExt = Utility_CacheManager.GetModExtension(_def);
                if (currentExt != null)
                {
                    Utility_DebugManager.LogWarning($"Race {_def.defName} already has a mod extension - will not replace");
                    Messages.Message("PawnControl_Error_ModExtensionExists".Translate(), MessageTypeDefOf.RejectInput);
                    return;
                }

                // Create a deep copy of the preset's extension instead of using the reference directly
                var newExtension = CloneModExtension(presetDef.modExtension);

                // Mark as runtime-added (not from XML)
                newExtension.fromXML = false;

                var settings = LoadedModManager.GetMod<Mod_SimpleNonHumanlikePawnControl>().GetSettings<ModSettings_SimpleNonHumanlikePawnControl>();
                if (settings.debugMode)
                {
                    newExtension.debugMode = true;
                }

                // Log the cloned extension
                Utility_DebugManager.LogNormal($"Cloned extension has forceDraftable={newExtension.forceDraftable}");

                // Ensure the def has a modExtensions list
                if (_def.modExtensions == null)
                {
                    _def.modExtensions = new List<DefModExtension>();
                }

                // Ensure caches are populated
                newExtension.CacheSimulatedSkillLevels();
                newExtension.CacheSkillPassions();

                // Add the cloned extension
                _def.modExtensions.Add(newExtension);

                // Clear only the specific race's cache entry
                Utility_CacheManager.ClearModExtensionCachePerInstance(_def);

                // CRITICAL FIX: Instead of calling global ResetCache methods that affect all races,
                // clear only the cache entries for THIS race
                Utility_TagManager.ClearCacheForRace(_def);

                // Reload into cache for just this race
                Utility_CacheManager.PreloadModExtensionForRace(_def);

                // Apply to existing pawns of THIS race only
                ApplyExtensionToExistingPawns(_def, newExtension);

                // IMPORTANT: Save this extension to the current game's component
                if (Current.Game != null)
                {
                    var gameComponent = Current.Game.GetComponent<GameComponent_LateInjection>();
                    if (gameComponent != null)
                    {
                        gameComponent.SaveRuntimeModExtension(_def, newExtension);
                    }
                }

                Messages.Message("PawnControl_Error_PresetApplied".Translate() + " " + presetDef.LabelCap, MessageTypeDefOf.TaskCompletion);
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogError($"Error applying preset '{presetDef.defName}': {ex}");
                Messages.Message("PawnControl_Error_PresetApplyError".Translate() + " " + ex.Message, MessageTypeDefOf.RejectInput);
            }
        }

        /// <summary>
        /// After adding a mod extension to a race, apply relevant changes to all existing pawns of that race
        /// </summary>
        private void ApplyExtensionToExistingPawns(ThingDef raceDef, NonHumanlikePawnControlExtension modExtension)
        {
            if (raceDef == null || modExtension == null) return;
            if (Find.Maps == null) return;

            int updatedCount = 0;

            // First, apply race-level changes to ensure the right think trees are set
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
                            // Apply drafter
                            Utility_DrafterManager.EnsureDrafter(pawn, modExtension);
                            Utility_DebugManager.LogNormal($"Added drafter to {pawn.LabelShort}, has drafter now: {pawn.drafter != null}");
                        }

                        // Ensure other trackers exist
                        if (modExtension.forceEquipWeapon || modExtension.forceWearApparel)
                        {
                            Utility_DrafterManager.EnsureAllTrackers(pawn);
                        }

                        // CRITICAL FIX: Apply the think trees directly to the pawn
                        if (pawn.mindState != null)
                        {
                            // Apply main think tree
                            if (!string.IsNullOrEmpty(modExtension.mainWorkThinkTreeDefName))
                            {
                                var thinkTree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(modExtension.mainWorkThinkTreeDefName);
                                if (thinkTree != null)
                                {
                                    // Use reflection to set the think tree
                                    Type mindStateType = pawn.mindState.GetType();
                                    FieldInfo thinkTreeField = AccessTools.Field(mindStateType, "thinkTree");
                                    if (thinkTreeField != null)
                                    {
                                        thinkTreeField.SetValue(pawn.mindState, thinkTree);
                                        Utility_DebugManager.LogNormal($"Set main think tree {thinkTree.defName} for {pawn.LabelShort}");
                                    }
                                }
                            }

                            // Apply constant think tree if needed
                            if (!string.IsNullOrEmpty(modExtension.constantThinkTreeDefName))
                            {
                                var constantTree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(modExtension.constantThinkTreeDefName);
                                if (constantTree != null)
                                {
                                    // Use reflection to set the constant think tree
                                    Type mindStateType = pawn.mindState.GetType();
                                    FieldInfo constThinkTreeField = AccessTools.Field(mindStateType, "thinkTreeConstant");
                                    if (constThinkTreeField != null)
                                    {
                                        constThinkTreeField.SetValue(pawn.mindState, constantTree);
                                        Utility_DebugManager.LogNormal($"Set constant think tree {constantTree.defName} for {pawn.LabelShort}");
                                    }
                                }
                            }

                            // Force think node rebuild
                            if (pawn.thinker != null)
                            {
                                Type thinkerType = pawn.thinker.GetType();
                                FieldInfo thinkRootField = AccessTools.Field(thinkerType, "thinkRoot");
                                if (thinkRootField != null)
                                {
                                    // Null out the think root to force a rebuild
                                    thinkRootField.SetValue(pawn.thinker, null);
                                }
                            }
                        }

                        updatedCount++;
                    }
                    catch (Exception ex)
                    {
                        Utility_DebugManager.LogError($"Error updating pawn {pawn.LabelShort}: {ex}");
                    }
                }
            }

            // Process world pawns too
            if (Find.World?.worldPawns?.AllPawnsAliveOrDead != null)
            {
                List<Pawn> worldPawns = Find.World.worldPawns.AllPawnsAliveOrDead
                    .Where(p => p != null && !p.Dead && p.def == raceDef)
                    .ToList();

                foreach (Pawn pawn in worldPawns)
                {
                    try
                    {
                        // Apply similar changes to world pawns
                        if (modExtension.forceDraftable && pawn.drafter == null)
                        {
                            Utility_DrafterManager.EnsureDrafter(pawn, modExtension, true);
                        }

                        // Apply think trees for world pawns too
                        if (pawn.mindState != null)
                        {
                            if (!string.IsNullOrEmpty(modExtension.mainWorkThinkTreeDefName))
                            {
                                var thinkTree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail(modExtension.mainWorkThinkTreeDefName);
                                if (thinkTree != null)
                                {
                                    Type mindStateType = pawn.mindState.GetType();
                                    FieldInfo thinkTreeField = AccessTools.Field(mindStateType, "thinkTree");
                                    if (thinkTreeField != null)
                                    {
                                        thinkTreeField.SetValue(pawn.mindState, thinkTree);
                                    }
                                }
                            }
                        }

                        updatedCount++;
                    }
                    catch (Exception ex)
                    {
                        Utility_DebugManager.LogError($"Error updating world pawn {pawn.LabelShort}: {ex}");
                    }
                }
            }

            if (updatedCount > 0)
            {
                Utility_DebugManager.LogNormal($"Updated {updatedCount} existing pawns of race {raceDef.defName}");
            }
        }

        /// <summary>
        /// Creates a deep copy of a NonHumanlikePawnControlExtension to avoid reference issues
        /// </summary>
        private NonHumanlikePawnControlExtension CloneModExtension(NonHumanlikePawnControlExtension source)
        {
            if (source == null)
                return null;

            var clone = new NonHumanlikePawnControlExtension
            {
                // Copy all fields
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

            // Deep copy lists
            if (source.tags != null)
                clone.tags = new List<string>(source.tags);

            if (source.allowedBodyTypes != null)
                clone.allowedBodyTypes = new List<BodyTypeDef>(source.allowedBodyTypes);

            // Deep copy injected skills
            if (source.injectedSkills != null)
            {
                clone.injectedSkills = new List<SkillLevelEntry>();
                foreach (var skill in source.injectedSkills)
                {
                    clone.injectedSkills.Add(new SkillLevelEntry
                    {
                        skill = skill.skill,
                        level = skill.level
                    });
                }
            }

            // Deep copy injected passions
            if (source.injectedPassions != null)
            {
                clone.injectedPassions = new List<SkillPassionEntry>();
                foreach (var passion in source.injectedPassions)
                {
                    clone.injectedPassions.Add(new SkillPassionEntry
                    {
                        skill = passion.skill,
                        passion = passion.passion
                    });
                }
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