using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace emitbreaker.PawnControl
{
    public static class NonHumanlikePawnRolePresets
    {
        public static readonly string[] Worker = new[]
        {
            NonHumanlikePawnControlTags.ForceWork,
            "AllowWork_Construct",
            "AllowWork_Mining",
            "AllowWork_Hauling",
            "AllowWork_Cleaning"
        };

        public static readonly string[] Medic = new[]
        {
            NonHumanlikePawnControlTags.ForceWork,
            "AllowWork_Doctor",
            "AllowWork_Hauling",
            "BlockWork_Social"
        };

        public static readonly string[] Janitor = new[]
        {
            NonHumanlikePawnControlTags.ForceWork,
            "AllowWork_Cleaning",
            "AllowWork_Hauling",
            "BlockWork_Social",
            "BlockWork_Artistic"
        };

        public static readonly string[] CombatServitor = new[]
        {
            NonHumanlikePawnControlTags.ForceWork,
            NonHumanlikePawnControlTags.ForceDraftable,
            "AllowWork_Firefighter",
            "AllowWork_Hunting",
            "AllowWork_Hauling",
            "BlockWork_Artistic",
            "BlockWork_Social"
        };
    }
}
