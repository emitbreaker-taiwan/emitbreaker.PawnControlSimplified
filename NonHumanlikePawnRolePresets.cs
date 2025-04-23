using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimWorld;
using Verse;

namespace emitbreaker.PawnControl
{
    public static class NonHumanlikePawnRolePresets
    {
        public static readonly string[] Worker = new[]
        {
            ManagedTags.ForceWork,
            ManagedTags.AllowWorkPrefix + WorkTypeDefOf.Construction.labelShort,
            ManagedTags.AllowWorkPrefix + WorkTypeDefOf.Mining.labelShort,
            ManagedTags.AllowWorkPrefix + WorkTypeDefOf.Hauling.labelShort,
            ManagedTags.AllowWorkPrefix + WorkTypeDefOf.Cleaning.labelShort
        };

        public static readonly string[] Medic = new[]
        {
            ManagedTags.ForceWork,
            ManagedTags.AllowWorkPrefix + WorkTypeDefOf.Doctor.labelShort,
            ManagedTags.AllowWorkPrefix + WorkTypeDefOf.Hauling.labelShort,
            ManagedTags.BlockWorkPrefix + WorkTypeDefOf.Warden.labelShort
        };

        public static readonly string[] Janitor = new[]
        {
            ManagedTags.ForceWork,
            ManagedTags.AllowWorkPrefix + WorkTypeDefOf.Cleaning.labelShort,
            ManagedTags.AllowWorkPrefix + WorkTypeDefOf.Hauling.labelShort,
            ManagedTags.BlockWorkPrefix + WorkTypeDefOf.Warden.labelShort,
            "BlockWork_Art"
        };

        public static readonly string[] CombatServitor = new[]
        {
            ManagedTags.ForceWork,
            ManagedTags.ForceDraftable,
            ManagedTags.AllowWorkPrefix + WorkTypeDefOf.Firefighter.labelShort,
            ManagedTags.AllowWorkPrefix + WorkTypeDefOf.Hunting.labelShort,
            ManagedTags.AllowWorkPrefix + WorkTypeDefOf.Hauling.labelShort,
            "BlockWork_Art",
            ManagedTags.BlockWorkPrefix + WorkTypeDefOf.Warden.labelShort
        };
    }
}
