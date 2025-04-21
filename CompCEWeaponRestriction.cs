using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace emitbreaker.PawnControl
{
    public class CompCEWeaponRestriction : ThingComp
    {
        public CompProperties_CEWeaponRestriction Props => (CompProperties_CEWeaponRestriction)this.props;
    }
}
