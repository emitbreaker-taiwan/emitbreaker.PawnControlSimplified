using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace emitbreaker.PawnControl
{
    public class ModSettings_SimpleNonHumanlikePawnControl : ModSettings
    {
        public bool harmonyPatchAll = true;
        public Dictionary<string, List<string>> virtualTagsSerialized = new Dictionary<string, List<string>>();
        public bool debugMode = false; // ✅ New setting

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref virtualTagsSerialized, "virtualTagsSerialized", LookMode.Value, LookMode.Value);
            if (virtualTagsSerialized == null)
            {
                virtualTagsSerialized = new Dictionary<string, List<string>>();
            }
            Scribe_Values.Look(ref debugMode, "debugMode", false);
        }
    }
}
