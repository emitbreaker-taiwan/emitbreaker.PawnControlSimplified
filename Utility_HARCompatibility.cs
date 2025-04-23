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
    public class Utility_HARCompatibility
    {

        private static readonly Dictionary<ThingDef, bool> harRaceCache = new Dictionary<ThingDef, bool>();
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
                return false;

            bool cached;
            if (harRaceCache.TryGetValue(def, out cached))
                return cached;

            bool result = alienRaceType != null && alienRaceType.IsInstanceOfType(def);
            harRaceCache[def] = result;
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
            var physicalModExtension = pawn.def.GetModExtension<NonHumanlikePawnControlExtension>();

            if (physicalModExtension != null)
            {
                if (physicalModExtension.restrictApparelByBodyType && !IsAllowedBodyType(pawn, physicalModExtension.allowedBodyTypes))
                {
                    return false;
                }
            }

            return true;
        }

        public static void ClearCache()
        {
            harRaceCache.Clear();
        }
    }
}
