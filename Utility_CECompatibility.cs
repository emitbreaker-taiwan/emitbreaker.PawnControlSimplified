using HarmonyLib;
using RimWorld;
using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace emitbreaker.PawnControl
{
    public class Utility_CECompatibility
    {
        public static readonly Type CompAmmoUserType =
            AccessTools.TypeByName("CombatExtended.CompAmmoUser");

        public static readonly Type CompFireModesType =
            AccessTools.TypeByName("CombatExtended.CompFireModes");

        private static readonly Dictionary<ThingDef, bool> isRangedCache = new Dictionary<ThingDef, bool>();
        private static readonly Dictionary<ThingDef, string> weaponClassCache = new Dictionary<ThingDef, string>();

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

            if (isRangedCache.TryGetValue(def, out var cached))
                return cached;

            bool result = def.IsRangedWeapon || HasCompAmmoUser(def);
            isRangedCache[def] = result;
            return result;
        }

        public static string GetCEWeaponClass(ThingDef def)
        {
            if (weaponClassCache.TryGetValue(def, out var cached)) return cached;

            if (!def.IsWeapon || def.Verbs.NullOrEmpty())
            {
                weaponClassCache[def] = "Unarmed";
                return "Unarmed";
            }

            bool hasMelee = def.Verbs.Any(v => v.IsMeleeAttack);
            bool hasRanged = def.Verbs.Any(v => !v.IsMeleeAttack);

            string result = hasMelee && hasRanged ? "Hybrid"
                          : hasMelee ? "Melee"
                          : hasRanged ? "Ranged"
                          : "Unknown";

            weaponClassCache[def] = result;
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
            isRangedCache.Clear();
            weaponClassCache.Clear();
        }
    }
}
