using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace emitbreaker.PawnControl
{
    public class PawnTagDef : Def
    {
        [NoTranslate]
        public string category;

        public Color? color = null;

        public override void ResolveReferences()
        {
            base.ResolveReferences();
            // fallback color assignment if needed
            if (!color.HasValue)
                color = new Color(1f, 1f, 1f); // default white
        }
    }
}
