using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Verse.AI;
using static System.Net.Mime.MediaTypeNames;

namespace emitbreaker.PawnControl
{
    public class Utility_CompatibilityManager
    {
        public static object TryGetModExtensionNoDependency(ThingDef def, string defModExtensionTypeOf)
        {
            if (def == null || def.modExtensions == null)
            {
                return null;
            }

            foreach (var modExtension in def.modExtensions)
            {
                var modExtensionType = modExtension?.GetType();
                if (modExtensionType == null)
                {
                    continue;
                }

                if (modExtensionType.FullName == defModExtensionTypeOf || modExtensionType.Name == defModExtensionTypeOf)
                {
                    return modExtension;
                }
            }

            return null;
        }

        public static class HumanoidAlienRaces
        {
            private static readonly Dictionary<ThingDef, bool> _harRaceCache = new Dictionary<ThingDef, bool>();
            private static readonly Type alienRaceType = Type.GetType("AlienRace.ThingDef_AlienRace, AlienRace");

            public static bool HARActive
            {
                get
                {
                    foreach (var mod in ModsConfig.ActiveModsInLoadOrder)
                    {
                        if (mod.PackageId != null && mod.PackageId.ToLowerInvariant().Contains("alienrace"))
                        {
                            return true;
                        }
                    }
                    return false;
                }
            }

            public static bool IsHARRace(ThingDef def)
            {
                if (def == null || !HARActive)
                {
                    return false;
                }

                bool cached;
                if (_harRaceCache.TryGetValue(def, out cached))
                {
                    return cached;
                }

                bool result = alienRaceType != null && alienRaceType.IsInstanceOfType(def);
                _harRaceCache[def] = result;
                return result;
            }

            public static string GetAlienBodyType(Pawn pawn)
            {
                if (!IsHARRace(pawn.def)) return null;

                var alienProps = pawn.def.GetType().GetProperty("alienRace");
                if (alienProps == null) return null;

                var alienRace = alienProps.GetValue(pawn.def, null);
                if (alienRace == null) return null;

                var generalSettings = alienRace.GetType().GetProperty("generalSettings")?.GetValue(alienRace, null);
                if (generalSettings == null) return null;

                var bodyGen = generalSettings.GetType().GetProperty("alienPartGenerator")?.GetValue(generalSettings, null);
                if (bodyGen == null) return null;

                var bodyTypeList = bodyGen.GetType().GetProperty("bodyTypes")?.GetValue(bodyGen, null) as List<BodyTypeDef>;
                if (bodyTypeList != null && bodyTypeList.Count > 0)
                {
                    return bodyTypeList[0].defName;
                }

                return null;
            }

            public static bool IsAllowedBodyType(Pawn pawn, List<BodyTypeDef> allowed)
            {
                if (!HARActive || pawn == null || allowed == null || allowed.Count == 0) return true;

                BodyTypeDef curBody = pawn.story?.bodyType;
                return curBody != null && allowed.Contains(curBody);
            }

            public static bool MatchesVisualProfile(Pawn pawn, string expectedBodyType)
            {
                return pawn?.story?.bodyType?.defName == expectedBodyType;
            }

            public static bool HasRequiredGenes(Pawn pawn, List<GeneDef> required)
            {
                if (pawn?.genes == null || required == null || required.Count == 0)
                    return true;

                foreach (GeneDef def in required)
                {
                    if (!pawn.genes.HasActiveGene(def))
                        return false;
                }
                return true;
            }

            public static bool IsApparelCompatibleWithPawn(Pawn pawn, ThingDef apparelDef)
            {
                var modExtension = Utility_UnifiedCache.GetModExtension(pawn.def);

                if (modExtension != null)
                {
                    if (modExtension.restrictApparelByBodyType && !IsAllowedBodyType(pawn, modExtension.allowedBodyTypes))
                    {
                        return false;
                    }
                }

                return true;
            }

            public static void ClearCache()
            {
                _harRaceCache.Clear();
            }
        }
    
        public static class CombatExtended
        {
            public static readonly Type CompAmmoUserType = AccessTools.TypeByName("CombatExtended.CompAmmoUser");

            public static readonly Type CompFireModesType = AccessTools.TypeByName("CombatExtended.CompFireModes");

            private static readonly Dictionary<ThingDef, bool> _isRangedCache = new Dictionary<ThingDef, bool>();
            private static readonly Dictionary<ThingDef, string> _weaponClassCache = new Dictionary<ThingDef, string>();

            public static bool CEActive
            {
                get
                {
                    foreach (var mod in ModsConfig.ActiveModsInLoadOrder)
                    {
                        if (mod.PackageId != null && mod.PackageId.ToLowerInvariant().Contains("combatextended"))
                        {
                            return true;
                        }
                    }
                    return false;
                }
            }

            public static bool HasCompAmmoUser(ThingDef def)
            {
                return CompAmmoUserType != null &&
                       def.comps.Any(c => c.compClass?.FullName == CompAmmoUserType.FullName);
            }

            public static bool IsCERangedWeapon(ThingDef def)
            {
                if (def == null || !CEActive) return false;

                if (_isRangedCache.TryGetValue(def, out var cached))
                    return cached;

                bool result = def.IsRangedWeapon || HasCompAmmoUser(def);
                _isRangedCache[def] = result;
                return result;
            }

            public static string GetCEWeaponClass(ThingDef def)
            {
                if (_weaponClassCache.TryGetValue(def, out var cached)) return cached;

                if (!def.IsWeapon || def.Verbs.NullOrEmpty())
                {
                    _weaponClassCache[def] = "Unarmed";
                    return "Unarmed";
                }

                bool hasMelee = def.Verbs.Any(v => v.IsMeleeAttack);
                bool hasRanged = def.Verbs.Any(v => !v.IsMeleeAttack);

                string result = hasMelee && hasRanged ? "Hybrid"
                              : hasMelee ? "Melee"
                              : hasRanged ? "Ranged"
                              : "Unknown";

                _weaponClassCache[def] = result;
                return result;
            }

            public static bool IsCECombatBusy(Pawn pawn)
            {
                if (!CEActive || pawn?.jobs?.curJob == null) return false;

                var jobDefName = pawn.jobs.curJob.def.defName;
                return jobDefName.Contains("Shoot") || jobDefName.Contains("Combat") || jobDefName.Contains("CE");
            }

            public static bool IsCEWeapon(Pawn pawn, out ThingWithComps weapon, out object compAmmoUser)
            {
                compAmmoUser = null;
                weapon = pawn.equipment?.Primary as ThingWithComps;

                if (!CEActive)
                {
                    return false;
                }

                if (weapon == null || CompAmmoUserType == null)
                {
                    return false;
                }

                compAmmoUser = weapon.AllComps.FirstOrDefault(c => CompAmmoUserType.IsInstanceOfType(c));

                return compAmmoUser != null;
            }

            public static int GetCEBurstCount(object compAmmoUser)
            {
                if (!CEActive)
                {
                    return 1;
                }

                if (compAmmoUser == null || CompAmmoUserType == null)
                    return 1;

                var burstField = CompAmmoUserType.GetProperty("BurstShotCount");
                return (int?)burstField?.GetValue(compAmmoUser) ?? 1;
            }

            public static float GetCEMaxRange(object compAmmoUser)
            {
                if (!CEActive)
                {
                    return 0f;
                }

                if (compAmmoUser == null || CompAmmoUserType == null)
                    return 0f;

                var rangeField = CompAmmoUserType.GetProperty("EffectiveRange");
                return (float?)rangeField?.GetValue(compAmmoUser) ?? 0f;
            }

            public static float GetCEAccuracy(object compAmmoUser)
            {
                if (!CEActive)
                {
                    return 0f;
                }

                if (compAmmoUser == null || CompAmmoUserType == null)
                {
                    return 0f;
                }

                var accField = CompAmmoUserType.GetProperty("Accuracy");
                return (float?)accField?.GetValue(compAmmoUser) ?? 0f;
            }

            public static float GetDefaultDutyRadius(Pawn p)
            {
                if (!CEActive)
                {
                    return 18f;
                }

                if (IsCEWeapon(p, out var weapon, out var comp))
                {
                    float range = GetCEMaxRange(comp);
                    int burst = GetCEBurstCount(comp);
                    float acc = GetCEAccuracy(comp);

                    // Basic fallback logic based on range
                    if (range >= 30f) return 34f;       // sniper
                    if (range >= 18f) return 28f;       // standard
                    return 22f;                         // shotgun/SMG
                }

                return 18f; // melee
            }

            public static bool IsWeaponTooHeavy(Pawn pawn, ThingDef weapon)
            {
                if (pawn == null || weapon == null)
                {
                    return true; // If either pawn or weapon is null, consider the weapon too heavy.
                }

                // Check if the weapon's mass exceeds the pawn's carrying capacity.
                float weaponMass = weapon.BaseMass;
                float pawnCarryingCapacity = pawn.GetStatValue(StatDefOf.CarryingCapacity);

                // Assume a threshold where a weapon is considered too heavy if it exceeds 50% of the pawn's carrying capacity.
                return weaponMass > (pawnCarryingCapacity * 0.5f);
            }

            public static void ClearCaches()
            {
                _isRangedCache.Clear();
                _weaponClassCache.Clear();
            }
        }

        public static class VanillaExtendedFramework
        {
            public static readonly bool VFEActive = ModsConfig.ActiveModsInLoadOrder.Any(m => m.PackageId?.ToLowerInvariant().Contains("vfe") == true);
        }

        public static class VehicleFramework 
        {
            private static readonly Type vehiclePawnType = Type.GetType("Vehicles.VehiclePawn, Vehicles");

            public static bool IsVehiclePawn(Pawn pawn)
            {
                return pawn != null && vehiclePawnType != null && vehiclePawnType.IsInstanceOfType(pawn);
            }
        }
    }
}
