using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace emitbreaker.PawnControl
{
    public class CompProperties_CEWeaponRestriction : CompProperties
    {
        public bool allowRanged = true;
        public bool allowMelee = true;
        public bool allowGrenades = false;

        // Usage in XML:
        // <comps>
        //   <li Class="emitbreaker.PawnControl.CompProperties_CEWeaponRestriction">
        //     <allowRanged>true</allowRanged>
        //     <allowMelee>false</allowMelee>
        //     <allowGrenades>false</allowGrenades>
        //   </li>
        // </comps>

        public CompProperties_CEWeaponRestriction()
        {
            this.compClass = typeof(CompCEWeaponRestriction);
        }
    }
}
