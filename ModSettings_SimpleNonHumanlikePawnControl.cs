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
        public bool debugMode = false; // ✅ New setting

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref debugMode, "debugMode", false);
        }
        //// Store applied presets by race defName
        //public Dictionary<string, string> appliedPresets = new Dictionary<string, string>();

        //// RuntimeModExtensionRecords for presets applied from main menu
        //public List<RuntimeModExtensionRecord> globalRuntimeExtensions = new List<RuntimeModExtensionRecord>();

        //public bool debugMode = false; // ✅ New setting

        //public override void ExposeData()
        //{
        //    base.ExposeData();
        //    Scribe_Collections.Look(ref appliedPresets, "appliedPresets", LookMode.Value, LookMode.Value);
        //    Scribe_Collections.Look(ref globalRuntimeExtensions, "globalRuntimeExtensions", LookMode.Deep);

        //    Scribe_Values.Look(ref debugMode, "debugMode", false);
        //}
    }
}