using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Threading.Tasks;
using Verse;
using Verse.AI.Group;
using Verse.AI;
using System.Reflection;
using static emitbreaker.PawnControl.HarmonyPatches;
using UnityEngine;
using System.Security.Cryptography;

namespace emitbreaker.PawnControl
{

    public class Mod_SimpleNonHumanlikePawnControl : Mod
    {
        public override string SettingsCategory()
        {
            return "PawnControl_ModName".Translate();
        }

        public Mod_SimpleNonHumanlikePawnControl(ModContentPack content) : base(content)
        {
            // Preload work + draftable humanlike override cache
            Utility_CacheManager.PreloadWorkHumanlikeCache();
            Utility_CacheManager.BuildRaceReverseMap();
        }
    }
}
