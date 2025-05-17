using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace emitbreaker.PawnControl
{
    public static class Utility_Common
    {
        public static bool PawnCompatibilityChecker(Pawn pawn)
        {
            if (pawn.RaceProps.Humanlike || pawn.Dead || !pawn.Spawned || pawn.IsDessicated())
            {
                return false;
            }

            if (Utility_CompatibilityManager.VehicleFramework.IsVehiclePawn(pawn))
            {
                return false;
            }

            if (Utility_CompatibilityManager.HumanoidAlienRaces.IsHARRace(pawn.def))
            {
                return false;
            }

            return true;
        }

        public static bool PawnIsNotPlayerFaction(Pawn pawn)
        {
            if (pawn.Faction != Faction.OfPlayer && !(pawn.IsSlave && pawn.HostFaction == Faction.OfPlayer))
                return true;
            return false;
        }

        public static bool PawnChecker(Pawn pawn)
        {
            if (pawn == null || pawn.def == null || pawn.def.race == null)
            {
                return false;
            }

            return true;
        }

        public static bool RaceDefChecker(ThingDef def)
        {
            if (def == null || def.race == null)
            {
                return false;
            }

            return true;
        }

        public static JobDef JobDefNamed(string defName)
        {
            return DefDatabase<JobDef>.GetNamed(defName);
        }

        public static NeedDef NeedDefNamed(string defName)
        {
            return DefDatabase<NeedDef>.GetNamed(defName);
        }

        public static SkillDef SkillDefNamed(string defName)
        {
            return DefDatabase<SkillDef>.GetNamed(defName);
        }

        public static PreceptDef PreceptDefNamed(string defName)
        {
            return DefDatabase<PreceptDef>.GetNamed(defName);
        }

        public static WorkGiverDef WorkGiverDefNamed(string defName)
        {
            return DefDatabase<WorkGiverDef>.GetNamed(defName);
        }

        public static BodyPartDef BodyPartDefNamed(string defName)
        {
            return DefDatabase<BodyPartDef>.GetNamed(defName);
        }

        /// <summary>
        /// Finds the pawn that owns the given work settings instance
        /// </summary>
        public static Pawn FindPawnWithWorkSettings(Pawn_WorkSettings workSettings)
        {
            if (workSettings == null)
                return null;

            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravelingTransportPods_Alive)
            {
                if (pawn.workSettings == workSettings)
                    return pawn;
            }

            return null;
        }

        /// <summary>
        /// Safely gets a value from a dictionary with a default fallback if the key is not found
        /// </summary>
        /// <typeparam name="TKey">The type of keys in the dictionary</typeparam>
        /// <typeparam name="TValue">The type of values in the dictionary</typeparam>
        /// <param name="dictionary">The dictionary to retrieve from</param>
        /// <param name="key">The key to look up</param>
        /// <param name="defaultValue">The default value to return if key is not found</param>
        /// <returns>The value from the dictionary if found, otherwise the default value</returns>
        public static TValue GetValueSafe<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue defaultValue)
        {
            if (dictionary == null)
                return defaultValue;

            if (dictionary.TryGetValue(key, out var value))
                return value;

            return defaultValue;
        }

        /// <summary>
        /// Creates or gets a dictionary entry based on a key, similar to ConcurrentDictionary's GetOrAdd
        /// </summary>
        /// <typeparam name="TKey">The type of keys in the dictionary</typeparam>
        /// <typeparam name="TValue">The type of values in the dictionary</typeparam>
        /// <param name="dictionary">The dictionary to retrieve from or add to</param>
        /// <param name="key">The key to look up or add</param>
        /// <param name="valueFactory">Function that creates the default value if key not found</param>
        /// <returns>The existing value if key found, otherwise the newly created value</returns>
        public static TValue GetOrCreate<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, Func<TValue> valueFactory)
        {
            if (dictionary.TryGetValue(key, out var value))
                return value;

            value = valueFactory();
            dictionary[key] = value;
            return value;
        }
    }
}