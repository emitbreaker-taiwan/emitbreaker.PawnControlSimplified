using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace emitbreaker.PawnControl
{
    public static class Utility_JobGiverManager
    {
        #region Validation

        /// <summary>
        /// Checks if a pawn is eligible for specialized JobGiver processing
        /// </summary>
        public static bool IsEligibleForSpecializedJobGiver(Pawn pawn, string workTypeName)
        {
            if (pawn == null || !pawn.Spawned || pawn.Dead)
                return false;

            // Skip pawns without mod extension - let vanilla handle them
            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null)
                return false;

            // Using ThinkTreeManager for consistent tagging across codebase
            if (!Utility_ThinkTreeManager.HasAllowWorkTag(pawn.def))
                return false;

            // Use WorkTypeSettingEnabled to check both tag permissions and work settings
            // This handles both the work setting check and tag permission check
            if (pawn.RaceProps.Humanlike && !Utility_TagManager.WorkTypeSettingEnabled(pawn, workTypeName))
            {
                if (Prefs.DevMode)
                {
                    Utility_DebugManager.LogNormal(
                        $"{pawn.LabelShort} (non-humanlike bypass = False) is not eligible for {workTypeName} work, skipping job");
                }
                return false;
            }

            return true;
        }

        #endregion
    }
}
