using System;
using System.Collections.Generic;
using Verse;

[StaticConstructorOnStartup]
public static class Startup_GlobalPresetApplier
{
    static Startup_GlobalPresetApplier()
    {
        // This only runs once at startup, not per save
        try
        {
            ApplyDefaultPresets();
        }
        catch (Exception ex)
        {
            Log.Error($"[PawnControl] Error applying default presets: {ex}");
        }
    }

    // Apply defaults defined in XML, not runtime additions
    private static void ApplyDefaultPresets()
    {
        // Any default application logic here
        Log.Message("[PawnControl] Applied default presets");
    }
}

//using System;
//using System.Collections.Generic;
//using Verse;

//namespace emitbreaker.PawnControl
//{
//    [StaticConstructorOnStartup]
//    public static class Startup_GlobalPresetApplier
//    {
//        // Add a static reference to the mod instance
//        private static Mod_SimpleNonHumanlikePawnControl modInstance;

//        static Startup_GlobalPresetApplier()
//        {
//            // Get a reference to our mod instance
//            modInstance = LoadedModManager.GetMod<Mod_SimpleNonHumanlikePawnControl>();

//            // This runs when the game starts, before any game is loaded
//            try
//            {
//                ApplyAllSavedPresets();
//            }
//            catch (Exception ex)
//            {
//                Log.Error($"[PawnControl] Error applying global presets: {ex}");
//            }
//        }

//        public static void ApplyAllSavedPresets()
//        {
//            if (modInstance == null)
//            {
//                Log.Error("[PawnControl] Could not find mod instance!");
//                return;
//            }

//            if (modInstance.Settings?.globalRuntimeExtensions == null ||
//                modInstance.Settings.globalRuntimeExtensions.Count == 0)
//            {
//                return;
//            }

//            Log.Message($"[PawnControl] Applying {modInstance.Settings.globalRuntimeExtensions.Count} saved global presets");

//            int applied = 0;
//            foreach (var record in modInstance.Settings.globalRuntimeExtensions)
//            {
//                try
//                {
//                    if (string.IsNullOrEmpty(record.targetDefName)) continue;

//                    // Find the target ThingDef
//                    ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(record.targetDefName);
//                    if (def == null)
//                    {
//                        Log.Warning($"[PawnControl] Could not find ThingDef {record.targetDefName} to apply saved preset");
//                        continue;
//                    }

//                    // Check if extension already exists
//                    bool alreadyExists = false;
//                    if (def.modExtensions != null)
//                    {
//                        foreach (var ext in def.modExtensions)
//                        {
//                            if (ext is NonHumanlikePawnControlExtension)
//                            {
//                                alreadyExists = true;
//                                break;
//                            }
//                        }
//                    }

//                    if (alreadyExists)
//                    {
//                        Log.Warning($"[PawnControl] ThingDef {record.targetDefName} already has a mod extension, skipping");
//                        continue;
//                    }

//                    // Ensure modExtensions list exists
//                    if (def.modExtensions == null)
//                        def.modExtensions = new List<DefModExtension>();

//                    // Add the saved extension
//                    def.modExtensions.Add(record.extension);

//                    // Mark as loaded from settings, not XML
//                    record.extension.fromXML = false;

//                    // Ensure caches are rebuilt
//                    record.extension.CacheSimulatedSkillLevels();
//                    record.extension.CacheSkillPassions();

//                    // Update the cache
//                    Utility_CacheManager.ClearModExtensionCachePerInstance(def);
//                    Utility_CacheManager.GetModExtension(def); // Force re-cache

//                    applied++;
//                    Log.Message($"[PawnControl] Applied global preset to {def.defName}");
//                }
//                catch (Exception ex)
//                {
//                    Log.Error($"[PawnControl] Error applying preset to {record.targetDefName}: {ex}");
//                }
//            }

//            Log.Message($"[PawnControl] Applied {applied} global presets from settings");
//        }

//        // Call this to save a preset applied from the main menu
//        public static void SaveGlobalPreset(ThingDef def, NonHumanlikePawnControlExtension extension)
//        {
//            if (def == null || extension == null)
//                return;

//            if (modInstance == null)
//            {
//                modInstance = LoadedModManager.GetMod<Mod_SimpleNonHumanlikePawnControl>();
//                if (modInstance == null)
//                {
//                    Log.Error("[PawnControl] Could not find mod instance when trying to save preset!");
//                    return;
//                }
//            }

//            // Ensure settings are initialized
//            if (modInstance.Settings == null)
//                return;

//            if (modInstance.Settings.globalRuntimeExtensions == null)
//                modInstance.Settings.globalRuntimeExtensions = new List<RuntimeModExtensionRecord>();

//            // Remove any existing record for this def
//            modInstance.Settings.globalRuntimeExtensions.RemoveAll(r => r.targetDefName == def.defName);

//            // Add the new record
//            modInstance.Settings.globalRuntimeExtensions.Add(
//                new RuntimeModExtensionRecord(def.defName, extension));

//            // Save settings to disk immediately
//            modInstance.SaveSettings();

//            Log.Message($"[PawnControl] Saved global preset for {def.defName}");
//        }

//        // Call this when removing a preset
//        public static void RemoveGlobalPreset(ThingDef def)
//        {
//            if (def == null)
//                return;

//            if (modInstance == null)
//            {
//                modInstance = LoadedModManager.GetMod<Mod_SimpleNonHumanlikePawnControl>();
//                if (modInstance == null || modInstance.Settings?.globalRuntimeExtensions == null)
//                    return;
//            }

//            int count = modInstance.Settings.globalRuntimeExtensions.RemoveAll(r => r.targetDefName == def.defName);

//            if (count > 0)
//            {
//                modInstance.SaveSettings();
//                Log.Message($"[PawnControl] Removed global preset for {def.defName}");
//            }
//        }
//    }
//}