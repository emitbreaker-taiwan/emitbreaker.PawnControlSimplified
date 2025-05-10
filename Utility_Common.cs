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
        public static bool PawnChecker(Pawn pawn)
        {
            if (pawn == null || pawn.RaceProps.Humanlike || pawn.Dead || !pawn.Spawned || pawn.IsDessicated())
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

        public static WorkTypeDef WorkTypeDefNamed(string defName)
        {
            return DefDatabase<WorkTypeDef>.GetNamed(defName);
        }

        public static ThinkTreeDef ThinkTreeDefNamed(string defName)
        {
            return DefDatabase<ThinkTreeDef>.GetNamed(defName);
        }

        public static BodyPartDef BodyPartDefNamed(string defName)
        {
            return DefDatabase<BodyPartDef>.GetNamed(defName);
        }

        // UPDATED: Cache the names on first access to avoid repeated reflection.
        private static readonly List<string> _pawnEnumTagNamesListCache = Enum.GetNames(typeof(PawnEnumTags)).ToList();
        private static readonly HashSet<string> _pawnEnumTagNamesHashSetCache = new HashSet<string>(_pawnEnumTagNamesListCache);

        /// <summary>
        /// Returns all PawnEnumTags as a List of their names.
        /// </summary>
        /// <returns>List of enum member names</returns>
        public static List<string> GetPawnEnumTagNames()
        {
            return _pawnEnumTagNamesListCache;
        }

        public static HashSet<string> GetPawnEnumTagNamesHashSet()
        {
            return _pawnEnumTagNamesHashSetCache;
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
    }
}
