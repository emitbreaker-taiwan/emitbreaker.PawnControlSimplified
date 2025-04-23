using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace emitbreaker.PawnControl
{
    public static class PawnTagPresetManager
    {
        private static readonly string PresetFolder = Path.Combine(GenFilePaths.SaveDataFolderPath, "PawnControl/Presets");

        // Apply tags to selected def in Dialog_PawnTagEditor
        public static void ApplyPresetToSelected(PawnTagPreset preset)
        {
            if (Find.WindowStack.Windows.FirstOrDefault(w => w is Dialog_PawnTagEditor) is Dialog_PawnTagEditor editor)
            {
                var modExtension = editor.SelectedModExtension;
                if (modExtension == null)
                {
                    Messages.Message("[PawnControl] No target mod extension selected.", MessageTypeDefOf.RejectInput, false);
                    return;
                }

                if (modExtension is NonHumanlikePawnControlExtension physicalModExtension)
                {
                    foreach (string tag in preset.tags)
                    {
                        if (!physicalModExtension.tags.Contains(tag))
                        {
                            physicalModExtension.tags.Add(tag);
                        }
                    }
                }
                else if (modExtension is VirtualNonHumanlikePawnControlExtension virtualModExtension)
                {
                    foreach (string tag in preset.tags)
                    {
                        if (!virtualModExtension.tags.Contains(tag))
                        {
                            virtualModExtension.tags.Add(tag);
                        }
                    }
                }
                // Ensure tag cache and PawnTagDefs sync
                if (modExtension != null && editor != null)
                {
                    Utility_CacheManager.RefreshTagCache(editor.SelectedDef, modExtension);
                }

                Messages.Message("PawnControl_AppliedPreset".Translate(preset.name), MessageTypeDefOf.TaskCompletion, false);
            }
            else
            {
                Messages.Message("PawnControl_NoEditorOpen".Translate(), MessageTypeDefOf.RejectInput, false);
            }
        }

        public static void SavePreset(PawnTagPreset preset)
        {
            Directory.CreateDirectory(PresetFolder);
            string path = Path.Combine(PresetFolder, preset.name + ".xml");

            try
            {
                Scribe.saver.InitSaving(path, "PawnTagPreset");
                Scribe_Deep.Look(ref preset, "preset");
                Scribe.saver.FinalizeSaving();
            }
            catch (Exception ex)
            {
                Log.Error("[PawnControl] Failed to save preset: " + ex);
            }
        }

        public static List<PawnTagPreset> LoadAllPresets()
        {
            List<PawnTagPreset> list = new List<PawnTagPreset>();
            if (!Directory.Exists(PresetFolder)) return list;

            foreach (var file in Directory.GetFiles(PresetFolder, "*.xml"))
            {
                try
                {
                    PawnTagPreset preset = null;
                    Scribe.loader.InitLoading(file);
                    Scribe_Deep.Look(ref preset, "preset");
                    Scribe.loader.FinalizeLoading();
                    if (preset != null)
                        list.Add(preset);
                }
                catch (Exception ex)
                {
                    Log.Error("[PawnControl] Failed to load preset from " + file + ": " + ex);
                }
            }

            return list;
        }

        public static void DeletePreset(string name)
        {
            string path = Path.Combine(PresetFolder, name + ".xml");
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        public static void RenamePreset(string oldName, string newName)
        {
            string oldPath = Path.Combine(PresetFolder, oldName + ".xml");
            string newPath = Path.Combine(PresetFolder, newName + ".xml");

            // ⚠️ Check if target file already exists
            if (File.Exists(newPath))
            {
                Messages.Message("PawnControl_RenameExistsWarning".Translate(newName), MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (File.Exists(oldPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(newPath));
                File.Move(oldPath, newPath);
            }

            // ✅ Optional: remove old cached preset, if tracked in memory
            //if (_loadedPresets.TryGetValue(oldName, out var preset))
            //{
            //    _loadedPresets.Remove(oldName);
            //    preset.name = newName;
            //    _loadedPresets[newName] = preset;
            //}
        }

        public static void SavePresetsToFile()
        {
            // Save all current presets to disk (overwrite existing file)
            try
            {
                string path = Path.Combine(GenFilePaths.SaveDataFolderPath, "PawnControl", "presets.xml");

                // ✅ Ensure directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                List<PawnTagPreset> allPresets = LoadAllPresets();

                ScribeSaver saver = Scribe.saver;
                saver.InitSaving(path, "Presets");
                Scribe_Collections.Look(ref allPresets, "Presets", LookMode.Deep);
                saver.FinalizeSaving();
            }
            catch (Exception ex)
            {
                Log.Error("PawnControl :: Failed to save presets: " + ex);
            }
        }

        public static void ExportPresetToFile(string filePath, PawnTagPreset preset)
        {
            // ✅ Ensure export directory exists
            string directory = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            try
            {
                Scribe.saver.InitSaving(filePath, "Preset");
                Scribe_Deep.Look(ref preset, "Preset");
                Scribe.saver.FinalizeSaving();
            }
            catch (Exception ex)
            {
                Log.Error("Exception while saving preset: " + ex);
            }
        }

        public static PawnTagPreset ImportPresetFromFile(string filePath)
        {
            // ✅ Ensure directory exists before loading
            string directory = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            Scribe.loader.InitLoading(filePath);
            PawnTagPreset preset = null;
            try
            {
                Scribe_Deep.Look(ref preset, "Preset");
            }
            catch (Exception ex)
            {
                Log.Error("Exception while loading preset: " + ex);
            }
            finally
            {
                Scribe.loader.FinalizeLoading();
            }
            return preset;
        }
    }
}
