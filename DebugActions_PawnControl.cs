using LudeonTK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace emitbreaker.PawnControl
{
    public static class DebugActions_PawnControl
    {
        [DebugAction("PawnControl", "Log Active Tags")]
        public static void LogTags()
        {
            foreach (var def in DefDatabase<ThingDef>.AllDefs)
            {
                if (def.HasModExtension<NonHumanlikePawnControlExtension>())
                {
                    var tags = def.GetModExtension<NonHumanlikePawnControlExtension>().tags;
                    if (tags != null)
                        Log.Message(def.defName + "PawnControl_DebugTools_Tags".Translate() + " " + string.Join(", ", tags));
                }
            }
        }

        [DebugAction("PawnControl", "Dump Tags of Colonists")]
        public static void DumpPawnControlTags()
        {
            if (Find.CurrentMap == null)
            {
                Log.Warning("No map found.");
                return;
            }

            foreach (var pawn in Find.CurrentMap.mapPawns.AllPawns.Where(p => p.IsColonist))
            {
                var tags = Utility_CacheManager.Tags.Get(pawn.def);
                string tagLine = tags.Any()
                    ? string.Join(", ", tags)
                    : "[No tags]";

                Log.Message($"{pawn.LabelShort.CapitalizeFirst()}: {tagLine}");
            }
        }

        [DebugAction("PawnControl", "Dump Lord Duties")]
        public static void DumpLordDuties()
        {
            if (Find.CurrentMap == null)
            {
                Log.Warning("No map found.");
                return;
            }

            foreach (var pawn in Find.CurrentMap.mapPawns.AllPawns.Where(p => p.mindState?.duty != null))
            {
                var duty = pawn.mindState.duty.def?.defName ?? "none";
                Log.Message(pawn.LabelShort.CapitalizeFirst() + "PawnControl_DebugTools_Duty:" + " " + duty);
            }
        }
    }
}
