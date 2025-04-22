using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace emitbreaker.PawnControl
{
    public static class NonHumanlikePawnControlTagGroups
    {
        public static readonly HashSet<string> DraftTags = new HashSet<string>
        {
            ManagedTags.AutoDraftInjection,
            ManagedTags.ForceDraftable
        };

        public static readonly HashSet<string> WorkTags = new HashSet<string>
        {
            ManagedTags.ForceWork,
            ManagedTags.BlockAllWork,
            ManagedTags.AllowAllWork
        };

        public static readonly HashSet<string> UIOverrideTags = new HashSet<string>
        {
            ManagedTags.ForceTrainerTab
        };

        // Optional: Mapping tag to category label
        public static readonly Dictionary<string, string> TagToCategory = new Dictionary<string, string>
        {
            { ManagedTags.AutoDraftInjection, "Draft" },
            { ManagedTags.ForceDraftable, "Draft" },
            { ManagedTags.ForceWork, "Work" },
            { ManagedTags.BlockAllWork, "Work" },
            { ManagedTags.ForceTrainerTab, "UI" }
        };
    }
}
