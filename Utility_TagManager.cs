using RimWorld;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Verse;

namespace emitbreaker.PawnControl
{
    public static class Utility_TagManager
    {
        #region Tag Flags Cache

        /// <summary>
        /// Gets the cached PawnTagFlags for a ThingDef, or builds them if not cached
        /// </summary>
        public static PawnTagFlags GetTagFlags(ThingDef def)
        {
            if (!Utility_Common.RaceDefChecker(def))
            {
                return PawnTagFlags.None;
            }

            if (!Utility_CacheManager.TagFlags.TryGetValue(def, out PawnTagFlags flags))
            {
                flags = BuildTagFlags(def);
                Utility_CacheManager.TagFlags[def] = flags;
            }
            return flags;
        }

        /// <summary>
        /// Builds PawnTagFlags for a ThingDef from its string-based tags
        /// </summary>
        private static PawnTagFlags BuildTagFlags(ThingDef def)
        {
            if (!Utility_Common.RaceDefChecker(def))
            {
                return PawnTagFlags.None;
            }

            PawnTagFlags flags = PawnTagFlags.None;
            var modExtension = Utility_CacheManager.GetModExtension(def);

            if (modExtension == null || modExtension.tags == null)
            {
                return flags;
            }

            // Convert string tags to flags
            foreach (string tag in modExtension.tags)
            {
                if (string.IsNullOrEmpty(tag)) continue;

                // Force type tags
                if (tag == ManagedTags.ForceAnimal) flags |= PawnTagFlags.ForceAnimal;
                else if (tag == ManagedTags.ForceMechanoid) flags |= PawnTagFlags.ForceMechanoid;
                else if (tag == ManagedTags.ForceHumanlike) flags |= PawnTagFlags.ForceHumanlike;

                // Behavior flags
                else if (tag == ManagedTags.ForceDraftable) flags |= PawnTagFlags.ForceDraftable;
                else if (tag == ManagedTags.ForceEquipWeapon) flags |= PawnTagFlags.ForceEquipWeapon;
                else if (tag == ManagedTags.ForceWearApparel) flags |= PawnTagFlags.ForceWearApparel;
                else if (tag == PawnEnumTags.ForceWork.ToStringSafe()) flags |= PawnTagFlags.ForceWork;
                else if (tag == PawnEnumTags.ForceTrainerTab.ToStringSafe()) flags |= PawnTagFlags.ForceTrainerTab;
                else if (tag == PawnEnumTags.AutoDraftInjection.ToStringSafe()) flags |= PawnTagFlags.AutoDraftInjection;

                // Work related flags - general
                else if (tag == ManagedTags.AllowAllWork) flags |= PawnTagFlags.AllowAllWork;
                else if (tag == ManagedTags.BlockAllWork) flags |= PawnTagFlags.BlockAllWork;

                // VFE compatibility
                else if (tag == ManagedTags.ForceVFECombatTree) flags |= PawnTagFlags.ForceVFECombatTree;
                else if (tag == ManagedTags.VFECore_Combat) flags |= PawnTagFlags.VFECore_Combat;
                else if (tag == ManagedTags.Mech_DefendBeacon) flags |= PawnTagFlags.Mech_DefendBeacon;
                else if (tag == ManagedTags.Mech_EscortCommander) flags |= PawnTagFlags.Mech_EscortCommander;
                else if (tag == ManagedTags.VFESkipWorkJobGiver) flags |= PawnTagFlags.VFESkipWorkJobGiver;
                else if (tag == ManagedTags.DisableVFEAIJobs) flags |= PawnTagFlags.DisableVFEAIJobs;

                // Allow work tags (prefix handling)
                else if (tag.StartsWith(ManagedTags.AllowWorkPrefix))
                {
                    string workType = tag.Substring(ManagedTags.AllowWorkPrefix.Length);
                    flags |= GetAllowWorkFlag(workType);
                }
                // Block work tags (prefix handling)
                else if (tag.StartsWith(ManagedTags.BlockWorkPrefix))
                {
                    string workType = tag.Substring(ManagedTags.BlockWorkPrefix.Length);
                    flags |= GetBlockWorkFlag(workType);
                }
            }

            // Apply extension properties directly
            if (modExtension != null)
            {
                if (modExtension.forceDraftable) flags |= PawnTagFlags.ForceDraftable;
                if (modExtension.forceEquipWeapon) flags |= PawnTagFlags.ForceEquipWeapon;
                if (modExtension.forceWearApparel) flags |= PawnTagFlags.ForceWearApparel;
            }

            return flags;
        }

        /// <summary>
        /// Maps a work type string to its corresponding AllowWork flag
        /// </summary>
        private static PawnTagFlags GetAllowWorkFlag(string workType)
        {
            if (string.IsNullOrEmpty(workType)) return PawnTagFlags.None;

            // Handle work types using their string names
            if (workType == "Firefighter") return PawnTagFlags.AllowWork_Firefighter;
            else if (workType == "Patient") return PawnTagFlags.AllowWork_Patient;
            else if (workType == "Doctor") return PawnTagFlags.AllowWork_Doctor;
            else if (workType == "PatientBedRest") return PawnTagFlags.AllowWork_PatientBedRest;
            else if (workType == "BasicWorker") return PawnTagFlags.AllowWork_BasicWorker;
            else if (workType == "Warden") return PawnTagFlags.AllowWork_Warden;
            else if (workType == "Handling") return PawnTagFlags.AllowWork_Handling;
            else if (workType == "Cooking") return PawnTagFlags.AllowWork_Cooking;
            else if (workType == "Hunting") return PawnTagFlags.AllowWork_Hunting;
            else if (workType == "Construction") return PawnTagFlags.AllowWork_Construction;
            else if (workType == "Growing") return PawnTagFlags.AllowWork_Growing;
            else if (workType == "Mining") return PawnTagFlags.AllowWork_Mining;
            else if (workType == "PlantCutting") return PawnTagFlags.AllowWork_PlantCutting;
            else if (workType == "Smithing") return PawnTagFlags.AllowWork_Smithing;
            else if (workType == "Tailoring") return PawnTagFlags.AllowWork_Tailoring;
            else if (workType == "Art") return PawnTagFlags.AllowWork_Art;
            else if (workType == "Crafting") return PawnTagFlags.AllowWork_Crafting;
            else if (workType == "Hauling") return PawnTagFlags.AllowWork_Hauling;
            else if (workType == "Cleaning") return PawnTagFlags.AllowWork_Cleaning;
            else if (workType == "Research") return PawnTagFlags.AllowWork_Research;
            else if (workType == "Childcare") return PawnTagFlags.AllowWork_Childcare;
            else if (workType == "DarkStudy") return PawnTagFlags.AllowWork_DarkStudy;

            return PawnTagFlags.None; // No matching flag found
        }

        /// <summary>
        /// Maps a work type string to its corresponding BlockWork flag
        /// </summary>
        private static PawnTagFlags GetBlockWorkFlag(string workType)
        {
            if (string.IsNullOrEmpty(workType)) return PawnTagFlags.None;

            // Handle work types using their string names
            if (workType == "Firefighter") return PawnTagFlags.BlockWork_Firefighter;
            else if (workType == "Patient") return PawnTagFlags.BlockWork_Patient;
            else if (workType == "Doctor") return PawnTagFlags.BlockWork_Doctor;
            else if (workType == "PatientBedRest") return PawnTagFlags.BlockWork_PatientBedRest;
            else if (workType == "BasicWorker") return PawnTagFlags.BlockWork_BasicWorker;
            else if (workType == "Warden") return PawnTagFlags.BlockWork_Warden;
            else if (workType == "Handling") return PawnTagFlags.BlockWork_Handling;
            else if (workType == "Cooking") return PawnTagFlags.BlockWork_Cooking;
            else if (workType == "Hunting") return PawnTagFlags.BlockWork_Hunting;
            else if (workType == "Construction") return PawnTagFlags.BlockWork_Construction;
            else if (workType == "Growing") return PawnTagFlags.BlockWork_Growing;
            else if (workType == "Mining") return PawnTagFlags.BlockWork_Mining;
            else if (workType == "PlantCutting") return PawnTagFlags.BlockWork_PlantCutting;
            else if (workType == "Smithing") return PawnTagFlags.BlockWork_Smithing;
            else if (workType == "Tailoring") return PawnTagFlags.BlockWork_Tailoring;
            else if (workType == "Art") return PawnTagFlags.BlockWork_Art;
            else if (workType == "Crafting") return PawnTagFlags.BlockWork_Crafting;
            else if (workType == "Hauling") return PawnTagFlags.BlockWork_Hauling;
            else if (workType == "Cleaning") return PawnTagFlags.BlockWork_Cleaning;
            else if (workType == "Research") return PawnTagFlags.BlockWork_Research;
            else if (workType == "Childcare") return PawnTagFlags.BlockWork_Childcare;
            else if (workType == "DarkStudy") return PawnTagFlags.BlockWork_DarkStudy;

            return PawnTagFlags.None; // No matching flag found
        }

        /// <summary>
        /// Gets the work flag for a specific work type
        /// </summary>
        private static PawnTagFlags GetWorkTypeFlag(string workTypeName, bool isBlock = false)
        {
            return isBlock ? GetBlockWorkFlag(workTypeName) : GetAllowWorkFlag(workTypeName);
        }

        #endregion

        #region Legacy API (String-based)

        public static bool HasTagSet(ThingDef def, NonHumanlikePawnControlExtension modExtension = null)
        {
            if (!Utility_Common.RaceDefChecker(def))
            {
                return false;
            }

            modExtension = Utility_CacheManager.GetModExtension(def);

            if (modExtension == null)
            {
                return false;
            }

            // We can check flags existence as well
            PawnTagFlags flags = GetTagFlags(def);
            if (flags != PawnTagFlags.None)
            {
                return true;
            }

            return modExtension.tags != null && modExtension.tags.Count > 0;
        }

        public static bool HasTag(Pawn pawn, string tag)
        {
            if (!Utility_Common.PawnChecker(pawn) || string.IsNullOrEmpty(tag))
            {
                return false; // Invalid input
            }

            // First check pawn-specific tags (if you implement that)
            // Then fall back to race-level tags
            return HasTag(pawn.def, tag);
        }

        public static bool HasTag(ThingDef def, string tag)
        {
            if (!Utility_Common.RaceDefChecker(def) || string.IsNullOrEmpty(tag))
            {
                return false; // Invalid input
            }

            // NEW IMPLEMENTATION: Use flag-based check if possible
            PawnTagFlags tagFlag = ConvertTagToFlag(tag);
            if (tagFlag != PawnTagFlags.None)
            {
                return HasFlag(def, tagFlag);
            }

            // LEGACY IMPLEMENTATION: Fall back to string-based check if no flag equivalent found
            if (!HasTagSet(def))
            {
                return false;
            }

            // Use GetTag to retrieve the cached or built tag set
            var tagSet = GetTags(def);

            // Check if the tag exists in the set
            bool hasTag = tagSet.Contains(tag);
            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Checking tag '{tag}' for def={def.defName}: {hasTag}");
            }
            return hasTag;
        }

        public static bool WorkTypeSettingEnabled(Pawn pawn, WorkTypeDef workTypeDef)
        {
            if (!Utility_Common.RaceDefChecker(pawn.def) || workTypeDef == null) return false;

            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null) return false; // No mod extension found

            // Race‐tags check unchanged…
            if (!IsWorkEnabled(pawn, workTypeDef)) return false;

            // If the Pawn_WorkSettings hasn't *ever* been enabled, do it now
            if (pawn.workSettings == null)
            {
                Utility_DebugManager.LogWarning($"[{pawn.LabelShort}] has no work setting.");
                pawn.workSettings = new Pawn_WorkSettings(pawn);
            }
            if (!pawn.workSettings.EverWork)
            {
                pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
            }

            // Now this will reflect the checkbox/slider in the UI
            bool active = pawn.workSettings.WorkIsActive(workTypeDef);
            int priority = pawn.workSettings.GetPriority(workTypeDef);

            if (priority > 0)
            {
                active = true; // Disabled in UI, but we want to override it
            }

            if (!active && Prefs.DevMode)
                Utility_DebugManager.LogWarning($"[{pawn.LabelShort}] {workTypeDef.defName} is disabled in UI");

            return active;
        }

        public static bool WorkTypeSettingEnabled(Pawn pawn, string workTypeName)
        {
            var workTypeDef = Utility_WorkTypeManager.Named(workTypeName);

            if (!Utility_Common.RaceDefChecker(pawn.def) || string.IsNullOrEmpty(workTypeName))
            {
                return false; // Invalid work type name
            }

            return WorkTypeSettingEnabled(pawn, workTypeDef);
        }

        public static bool ForceDraftable(Pawn pawn, string tag = ManagedTags.ForceDraftable)
        {
            if (!Utility_Common.RaceDefChecker(pawn.def) || string.IsNullOrEmpty(tag))
            {
                return false; // Invalid input
            }

            var key = new ValueTuple<Pawn, string>(pawn, tag);

            // Try to get from cache first
            if (Utility_CacheManager.ForceDraftable.TryGetValue(key, out bool result))
            {
                return result;
            }

            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null)
            {
                return false; // No mod extension found
            }

            // NEW IMPLEMENTATION: Use flags
            if (tag == ManagedTags.ForceDraftable)
            {
                PawnTagFlags flags = GetTagFlags(pawn.def);
                result = (flags & PawnTagFlags.ForceDraftable) != 0;
            }
            else
            {
                // LEGACY IMPLEMENTATION: For non-standard tag
                result = HasTag(pawn, tag) || modExtension.forceDraftable;
            }

            // Store in cache
            Utility_CacheManager.ForceDraftable[key] = result;
            return result;
        }

        public static bool ForceEquipWeapon(Pawn pawn, string tag = ManagedTags.ForceEquipWeapon)
        {
            if (!Utility_Common.RaceDefChecker(pawn.def) || string.IsNullOrEmpty(tag))
            {
                return false; // Invalid input
            }

            var key = new ValueTuple<Pawn, string>(pawn, tag);

            // Try to get from cache first
            if (Utility_CacheManager.ForceEquipWeapon.TryGetValue(key, out bool result))
            {
                return result;
            }

            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null)
            {
                return false; // No mod extension found
            }

            if (!ForceDraftable(pawn))
            {
                Utility_DebugManager.LogError($"{pawn.def.defName} is not forceDraftable, cannot force equip weapon.");
                return false;
            }

            // NEW IMPLEMENTATION: Use flags 
            if (tag == ManagedTags.ForceEquipWeapon)
            {
                PawnTagFlags flags = GetTagFlags(pawn.def);
                result = (flags & PawnTagFlags.ForceEquipWeapon) != 0;
            }
            else
            {
                // LEGACY IMPLEMENTATION: For non-standard tag
                result = HasTag(pawn, tag) || modExtension.forceEquipWeapon;
            }

            // Store in cache
            Utility_CacheManager.ForceEquipWeapon[key] = result;
            return result;
        }

        public static bool ForceWearApparel(Pawn pawn, string tag = ManagedTags.ForceWearApparel)
        {
            if (!Utility_Common.RaceDefChecker(pawn.def) || string.IsNullOrEmpty(tag))
            {
                return false; // Invalid input
            }

            var key = new ValueTuple<Pawn, string>(pawn, tag);

            // Try to get from cache first
            if (Utility_CacheManager.ForceWearApparel.TryGetValue(key, out bool result))
            {
                return result;
            }

            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null)
            {
                return false; // No mod extension found
            }

            if (!ForceDraftable(pawn))
            {
                Utility_DebugManager.LogError($"{pawn.def.defName} is not forceDraftable, cannot force wear apparel.");
                return false;
            }

            // NEW IMPLEMENTATION: Use flags
            if (tag == ManagedTags.ForceWearApparel)
            {
                PawnTagFlags flags = GetTagFlags(pawn.def);
                result = (flags & PawnTagFlags.ForceWearApparel) != 0;
            }
            else
            {
                // LEGACY IMPLEMENTATION: For non-standard tag
                result = HasTag(pawn, tag) || modExtension.forceWearApparel;
            }

            // Store in cache
            Utility_CacheManager.ForceWearApparel[key] = result;
            return result;
        }

        public static HashSet<string> GetTags(ThingDef def)
        {
            if (!Utility_Common.RaceDefChecker(def))
            {
                return new HashSet<string>();
            }

            if (!Utility_CacheManager.Tags.TryGetValue(def, out var set))
            {
                set = BuildTags(def);
                Utility_CacheManager.Tags[def] = set;
            }
            return set;
        }

        private static HashSet<string> BuildTags(ThingDef def)
        {
            if (!Utility_Common.RaceDefChecker(def))
            {
                return new HashSet<string>(); // Return empty set, not null
            }

            HashSet<string> set = new HashSet<string>();

            var modExtension = Utility_CacheManager.GetModExtension(def);

            if (modExtension == null || modExtension.tags == null)
            {
                return set; // Return empty set
            }

            foreach (string tag in modExtension.tags)
            {
                if (tag != null)
                    set.Add(tag);
            }

            // Legacy force flags
            if (set.Contains(PawnEnumTags.ForceAnimal.ToStringSafe())) set.Add(PawnEnumTags.ForceAnimal.ToStringSafe());
            if (set.Contains(PawnEnumTags.ForceDraftable.ToStringSafe())) set.Add(PawnEnumTags.ForceDraftable.ToStringSafe());

            return set;
        }

        public static bool HasAnyTagWithPrefix(ThingDef def, string prefix)
        {
            if (!Utility_Common.RaceDefChecker(def) || string.IsNullOrEmpty(prefix))
            {
                return false;
            }

            // NEW IMPLEMENTATION: Handle common prefixes with flags
            if (prefix == ManagedTags.AllowWorkPrefix)
            {
                PawnTagFlags flags = GetTagFlags(def);
                PawnTagFlags allowWorkFlags = flags & (
                    PawnTagFlags.AllowWork_Firefighter | PawnTagFlags.AllowWork_Patient |
                    PawnTagFlags.AllowWork_Doctor | PawnTagFlags.AllowWork_PatientBedRest |
                    PawnTagFlags.AllowWork_BasicWorker | PawnTagFlags.AllowWork_Warden |
                    PawnTagFlags.AllowWork_Handling | PawnTagFlags.AllowWork_Cooking |
                    PawnTagFlags.AllowWork_Hunting | PawnTagFlags.AllowWork_Construction |
                    PawnTagFlags.AllowWork_Growing | PawnTagFlags.AllowWork_Mining |
                    PawnTagFlags.AllowWork_PlantCutting | PawnTagFlags.AllowWork_Smithing |
                    PawnTagFlags.AllowWork_Tailoring | PawnTagFlags.AllowWork_Art |
                    PawnTagFlags.AllowWork_Crafting | PawnTagFlags.AllowWork_Hauling |
                    PawnTagFlags.AllowWork_Cleaning | PawnTagFlags.AllowWork_Research |
                    PawnTagFlags.AllowWork_Childcare | PawnTagFlags.AllowWork_DarkStudy
                );

                return allowWorkFlags != PawnTagFlags.None;
            }
            else if (prefix == ManagedTags.BlockWorkPrefix)
            {
                PawnTagFlags flags = GetTagFlags(def);
                PawnTagFlags blockWorkFlags = flags & (
                    PawnTagFlags.BlockWork_Firefighter | PawnTagFlags.BlockWork_Patient |
                    PawnTagFlags.BlockWork_Doctor | PawnTagFlags.BlockWork_PatientBedRest |
                    PawnTagFlags.BlockWork_BasicWorker | PawnTagFlags.BlockWork_Warden |
                    PawnTagFlags.BlockWork_Handling | PawnTagFlags.BlockWork_Cooking |
                    PawnTagFlags.BlockWork_Hunting | PawnTagFlags.BlockWork_Construction |
                    PawnTagFlags.BlockWork_Growing | PawnTagFlags.BlockWork_Mining |
                    PawnTagFlags.BlockWork_PlantCutting | PawnTagFlags.BlockWork_Smithing |
                    PawnTagFlags.BlockWork_Tailoring | PawnTagFlags.BlockWork_Art |
                    PawnTagFlags.BlockWork_Crafting | PawnTagFlags.BlockWork_Hauling |
                    PawnTagFlags.BlockWork_Cleaning | PawnTagFlags.BlockWork_Research |
                    PawnTagFlags.BlockWork_Childcare | PawnTagFlags.BlockWork_DarkStudy
                );

                return blockWorkFlags != PawnTagFlags.None;
            }

            // LEGACY IMPLEMENTATION: Fallback for any other prefixes
            if (!HasTagSet(def))
            {
                return false;
            }

            var tagSet = GetTags(def);

            foreach (string t in tagSet)
            {
                if (t != null && t.StartsWith(prefix))
                {
                    return true;
                }
            }

            return false;
        }

        #endregion

        #region New API (Flag-based)

        /// <summary>
        /// Checks if a ThingDef has the specified PawnTagFlag
        /// </summary>
        public static bool HasFlag(ThingDef def, PawnTagFlags flag)
        {
            if (!Utility_Common.RaceDefChecker(def))
            {
                return false;
            }

            var flags = GetTagFlags(def);
            return (flags & flag) == flag;
        }

        /// <summary>
        /// Checks if a Pawn has the specified PawnTagFlag
        /// </summary>
        public static bool HasFlag(Pawn pawn, PawnTagFlags flag)
        {
            if (!Utility_Common.PawnChecker(pawn))
            {
                return false;
            }

            return HasFlag(pawn.def, flag);
        }

        /// <summary>
        /// Checks if a ThingDef has any of the specified PawnTagFlags
        /// </summary>
        public static bool HasAnyFlag(ThingDef def, PawnTagFlags flags)
        {
            if (!Utility_Common.RaceDefChecker(def))
            {
                return false;
            }

            var currentFlags = GetTagFlags(def);
            return (currentFlags & flags) != 0;
        }

        /// <summary>
        /// Checks if a Pawn has any of the specified PawnTagFlags
        /// </summary>
        public static bool HasAnyFlag(Pawn pawn, PawnTagFlags flags)
        {
            if (!Utility_Common.PawnChecker(pawn))
            {
                return false;
            }

            return HasAnyFlag(pawn.def, flags);
        }

        /// <summary>
        /// Checks if a work type is enabled using the flag system
        /// </summary>
        public static bool IsWorkEnabled(Pawn pawn, string workTypeName)
        {
            if (!Utility_Common.PawnChecker(pawn) || string.IsNullOrEmpty(workTypeName))
            {
                return false;
            }

            var flags = GetTagFlags(pawn.def);

            // Check for AllowAllWork first
            if ((flags & PawnTagFlags.AllowAllWork) != 0)
            {
                return true;
            }

            // Check for specific allow flag
            var allowFlag = GetWorkTypeFlag(workTypeName, isBlock: false);
            return allowFlag != PawnTagFlags.None && (flags & allowFlag) != 0;
        }
        public static bool IsWorkEnabled(Pawn pawn, WorkTypeDef workTypeDef)
        {
            var workTypeName = workTypeDef.ToStringSafe();
            return IsWorkEnabled(pawn, workTypeName);
        }

        /// <summary>
        /// Checks if a work type is disabled using the flag system
        /// </summary>
        public static bool IsWorkDisabled(Pawn pawn, string workTypeName)
        {
            if (!Utility_Common.PawnChecker(pawn) || string.IsNullOrEmpty(workTypeName))
            {
                return false;
            }

            var flags = GetTagFlags(pawn.def);
            bool hasAllowAllWork = (flags & PawnTagFlags.AllowAllWork) != 0;
            bool hasBlockAllWork = (flags & PawnTagFlags.BlockAllWork) != 0;

            // Block all work overrides everything else
            if (!hasAllowAllWork && hasBlockAllWork)
            {
                return true;
            }

            // Check for specific block/allow flags
            var blockFlag = GetWorkTypeFlag(workTypeName, isBlock: true);
            var allowFlag = GetWorkTypeFlag(workTypeName, isBlock: false);

            // If BlockAllWork not set, check specific flags
            return !hasBlockAllWork &&
                   blockFlag != PawnTagFlags.None &&
                   (flags & blockFlag) != 0 &&
                   (allowFlag == PawnTagFlags.None || (flags & allowFlag) == 0);
        }
        public static bool IsWorkDisabled(Pawn pawn, WorkTypeDef workTypeDef)
        {
            var workTypeName = workTypeDef.ToStringSafe();
            return IsWorkDisabled(pawn, workTypeName);
        }

        #endregion

        #region Cache Management

        /// <summary>
        /// Clears all cache entries for a specific race without affecting other races.
        /// Use this instead of ResetCache() when working with a single race.
        /// </summary>
        public static void ClearCacheForRace(ThingDef def)
        {
            Pawn pawn = ThingMaker.MakeThing(def) as Pawn;
            if (pawn == null) return;

            if (!Utility_Common.RaceDefChecker(pawn.def)) return;

            // Clear flag cache
            Utility_CacheManager.TagFlags.Remove(pawn.def);

            // Clear string tag cache
            Utility_CacheManager.Tags.Remove(pawn.def);

            // Find and remove all tuple entries that reference this race
            var keysToRemove = new List<ValueTuple<Pawn, string>>();

            // Clear work enabled cache for this race
            foreach (var key in Utility_CacheManager.WorkEnabled.Keys)
            {
                if (key.Item1.def == def)
                    keysToRemove.Add(key);
            }
            foreach (var key in keysToRemove)
                Utility_CacheManager.WorkEnabled.Remove(key);

            // Reset and reuse list for work disabled cache
            keysToRemove.Clear();
            foreach (var key in Utility_CacheManager.WorkDisabled.Keys)
            {
                if (key.Item1.def == def)
                    keysToRemove.Add(key);
            }
            foreach (var key in keysToRemove)
                Utility_CacheManager.WorkDisabled.Remove(key);

            // Reset and reuse list for force draftable cache
            keysToRemove.Clear();
            foreach (var key in Utility_CacheManager.ForceDraftable.Keys)
            {
                if (key.Item1.def == def)
                    keysToRemove.Add(key);
            }
            foreach (var key in keysToRemove)
                Utility_CacheManager.ForceDraftable.Remove(key);

            // Reset and reuse list for force equip weapon cache
            keysToRemove.Clear();
            foreach (var key in Utility_CacheManager.ForceEquipWeapon.Keys)
            {
                if (key.Item1.def == def)
                    keysToRemove.Add(key);
            }
            foreach (var key in keysToRemove)
                Utility_CacheManager.ForceEquipWeapon.Remove(key);

            // Reset and reuse list for force wear apparel cache
            keysToRemove.Clear();
            foreach (var key in Utility_CacheManager.ForceWearApparel.Keys)
            {
                if (key.Item1.def == def)
                    keysToRemove.Add(key);
            }
            foreach (var key in keysToRemove)
                Utility_CacheManager.ForceWearApparel.Remove(key);

            // Also clear related caches in other managers if needed
            Utility_CacheManager.IsAnimal.Remove(pawn.def);
            Utility_CacheManager.IsHumanlike.Remove(pawn.def);
            Utility_CacheManager.IsMechanoid.Remove(pawn.def);

            Utility_DebugManager.LogNormal($"Cache entries for race {pawn.def.defName} successfully cleared");
        }

        public static void ClearCacheForPawn(Pawn pawn)
        {
            if (!Utility_Common.RaceDefChecker(pawn.def)) return;

            // Clear flag cache
            Utility_CacheManager.TagFlags.Remove(pawn.def);

            // Clear race's tag cache
            Utility_CacheManager.Tags.Remove(pawn.def);

            // Find and remove all tuple entries that reference this race
            var keysToRemove = new List<ValueTuple<Pawn, string>>();

            // Clear work enabled cache for this race
            foreach (var key in Utility_CacheManager.WorkEnabled.Keys)
            {
                if (key.Item1 == pawn)
                    keysToRemove.Add(key);
            }
            foreach (var key in keysToRemove)
                Utility_CacheManager.WorkEnabled.Remove(key);

            // Reset and reuse list for work disabled cache
            keysToRemove.Clear();
            foreach (var key in Utility_CacheManager.WorkDisabled.Keys)
            {
                if (key.Item1 == pawn)
                    keysToRemove.Add(key);
            }
            foreach (var key in keysToRemove)
                Utility_CacheManager.WorkDisabled.Remove(key);

            // Reset and reuse list for force draftable cache
            keysToRemove.Clear();
            foreach (var key in Utility_CacheManager.ForceDraftable.Keys)
            {
                if (key.Item1 == pawn)
                    keysToRemove.Add(key);
            }
            foreach (var key in keysToRemove)
                Utility_CacheManager.ForceDraftable.Remove(key);

            // Reset and reuse list for force equip weapon cache
            keysToRemove.Clear();
            foreach (var key in Utility_CacheManager.ForceEquipWeapon.Keys)
            {
                if (key.Item1 == pawn)
                    keysToRemove.Add(key);
            }
            foreach (var key in keysToRemove)
                Utility_CacheManager.ForceEquipWeapon.Remove(key);

            // Reset and reuse list for force wear apparel cache
            keysToRemove.Clear();
            foreach (var key in Utility_CacheManager.ForceWearApparel.Keys)
            {
                if (key.Item1 == pawn)
                    keysToRemove.Add(key);
            }
            foreach (var key in keysToRemove)
                Utility_CacheManager.ForceWearApparel.Remove(key);

            // Also clear related caches in other managers if needed
            Utility_CacheManager.IsAnimal.Remove(pawn.def);
            Utility_CacheManager.IsHumanlike.Remove(pawn.def);
            Utility_CacheManager.IsMechanoid.Remove(pawn.def);

            Utility_DebugManager.LogNormal($"Cache entries for race {pawn.def.defName} successfully cleared");
        }

        public static void ClearAllCaches()
        {
            Utility_CacheManager.TagFlags.Clear();
            Utility_CacheManager.Tags.Clear();
            Utility_CacheManager.WorkEnabled.Clear();
            Utility_CacheManager.WorkDisabled.Clear();
            Utility_CacheManager.ForceDraftable.Clear();
            Utility_CacheManager.ForceEquipWeapon.Clear();
            Utility_CacheManager.ForceWearApparel.Clear();
            Utility_CacheManager.IsAnimal.Clear();
            Utility_CacheManager.IsHumanlike.Clear();
            Utility_CacheManager.IsMechanoid.Clear();

            Utility_DebugManager.LogNormal("All tag caches cleared");
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Converts a tag string to its corresponding PawnTagFlag
        /// </summary>
        private static PawnTagFlags ConvertTagToFlag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return PawnTagFlags.None;

            // Force type tags
            if (tag == ManagedTags.ForceAnimal) return PawnTagFlags.ForceAnimal;
            if (tag == ManagedTags.ForceMechanoid) return PawnTagFlags.ForceMechanoid;
            if (tag == ManagedTags.ForceHumanlike) return PawnTagFlags.ForceHumanlike;

            // Behavior flags
            if (tag == ManagedTags.ForceDraftable) return PawnTagFlags.ForceDraftable;
            if (tag == ManagedTags.ForceEquipWeapon) return PawnTagFlags.ForceEquipWeapon;
            if (tag == ManagedTags.ForceWearApparel) return PawnTagFlags.ForceWearApparel;
            if (tag == PawnEnumTags.ForceWork.ToStringSafe()) return PawnTagFlags.ForceWork;
            if (tag == PawnEnumTags.ForceTrainerTab.ToStringSafe()) return PawnTagFlags.ForceTrainerTab;
            if (tag == PawnEnumTags.AutoDraftInjection.ToStringSafe()) return PawnTagFlags.AutoDraftInjection;

            // Work related flags - general
            if (tag == ManagedTags.AllowAllWork) return PawnTagFlags.AllowAllWork;
            if (tag == ManagedTags.BlockAllWork) return PawnTagFlags.BlockAllWork;

            // VFE and mechanic compatibility
            if (tag == ManagedTags.ForceVFECombatTree) return PawnTagFlags.ForceVFECombatTree;
            if (tag == ManagedTags.VFECore_Combat) return PawnTagFlags.VFECore_Combat;
            if (tag == ManagedTags.Mech_DefendBeacon) return PawnTagFlags.Mech_DefendBeacon;
            if (tag == ManagedTags.Mech_EscortCommander) return PawnTagFlags.Mech_EscortCommander;
            if (tag == ManagedTags.VFESkipWorkJobGiver) return PawnTagFlags.VFESkipWorkJobGiver;
            if (tag == ManagedTags.DisableVFEAIJobs) return PawnTagFlags.DisableVFEAIJobs;

            // Check for work prefixes
            if (tag.StartsWith(ManagedTags.AllowWorkPrefix))
            {
                string workType = tag.Substring(ManagedTags.AllowWorkPrefix.Length);
                return GetAllowWorkFlag(workType);
            }

            if (tag.StartsWith(ManagedTags.BlockWorkPrefix))
            {
                string workType = tag.Substring(ManagedTags.BlockWorkPrefix.Length);
                return GetBlockWorkFlag(workType);
            }

            return PawnTagFlags.None; // No match found
        }

        #endregion
    }
}