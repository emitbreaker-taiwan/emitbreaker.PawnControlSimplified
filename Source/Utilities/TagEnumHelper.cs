using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace emitbreaker.PawnControl
{
    public static class TagEnumHelper
    {
        private static readonly Dictionary<string, PawnTag> tagToEnum = new Dictionary<string, PawnTag>()
        {
            { "ForceAnimal", PawnTag.ForceAnimal },
            { "ForceDraftable", PawnTag.ForceDraftable },
            { "ForceWork", PawnTag.ForceWork },
            { "BlockAllWork", PawnTag.BlockAllWork },
            { "AllowAllWork", PawnTag.AllowAllWork },
            { "ForceTrainerTab", PawnTag.ForceTrainerTab },
            { "AutoDraftInjection", PawnTag.AutoDraftInjection },

            { "ForceVFECombatTree", PawnTag.ForceVFECombatTree },
            { "VFECore_Combat", PawnTag.VFECore_Combat },

            { "Mech_DefendBeacon", PawnTag.Mech_DefendBeacon },
            { "Mech_EscortCommander", PawnTag.Mech_EscortCommander },
        };

        public static PawnTag ToEnum(string tag)
        {
            if (tag == null) return PawnTag.Unknown;

            PawnTag value;
            if (tagToEnum.TryGetValue(tag, out value))
                return value;

            return PawnTag.Unknown;
        }

        public static string ToString(PawnTag tag)
        {
            foreach (var kv in tagToEnum)
            {
                if (kv.Value == tag)
                    return kv.Key;
            }
            return null;
        }
    }
}
