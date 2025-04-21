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
            NonHumanlikePawnControlTags.AutoDraftInjection,
            NonHumanlikePawnControlTags.ForceDraftable
        };

        public static readonly HashSet<string> WorkTags = new HashSet<string>
        {
            NonHumanlikePawnControlTags.ForceWork,
            NonHumanlikePawnControlTags.BlockAllWork,
            NonHumanlikePawnControlTags.AllowAllWork
        };

        public static readonly HashSet<string> UIOverrideTags = new HashSet<string>
        {
            NonHumanlikePawnControlTags.ForceTrainerTab
        };

        // Optional: Mapping tag to category label
        public static readonly Dictionary<string, string> TagToCategory = new Dictionary<string, string>
        {
            { NonHumanlikePawnControlTags.AutoDraftInjection, "Draft" },
            { NonHumanlikePawnControlTags.ForceDraftable, "Draft" },
            { NonHumanlikePawnControlTags.ForceWork, "Work" },
            { NonHumanlikePawnControlTags.BlockAllWork, "Work" },
            { NonHumanlikePawnControlTags.ForceTrainerTab, "UI" }
        };
    }
}
