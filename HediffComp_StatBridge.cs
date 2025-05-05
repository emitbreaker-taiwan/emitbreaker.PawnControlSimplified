using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// One-time migration of stat modifiers into pawn's race ThingDef.statBases.
    /// Removes itself after applying.
    /// </summary>
    public class HediffComp_StatBridge : HediffComp
    {
        public HediffCompProperties_StatBridge Props => (HediffCompProperties_StatBridge)props;

        public override void CompPostPostAdd(DamageInfo? dinfo)
        {
            base.CompPostPostAdd(dinfo);

            Pawn pawn = parent?.pawn;
            if (pawn == null || pawn.def == null || pawn.def.statBases == null)
                return;

            var modExt = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExt == null)
                return;

            bool updated = false;

            foreach (SkillDef skillDef in Utility_StatManager.GetSkillsNeedingStatSupport(pawn))
            {
                if (!Utility_StatManager.skillToStatMap.TryGetValue(skillDef, out var statMap))
                    continue;

                foreach (var kvp in statMap)
                {
                    if (pawn.def.statBases.GetStatValueFromList(kvp.Key, default(float)) <= 0f)
                    {
                        pawn.def.statBases.Add(new StatModifier
                        {
                            stat = kvp.Key,
                            value = kvp.Value
                        });

                        updated = true;
                    }
                }
            }

            if (updated && Prefs.DevMode)
            {
                Log.Message($"[PawnControl] StatBase updated for {pawn.def.defName} via HediffComp_StatBridge");
            }

            pawn.health.RemoveHediff(parent); // ✅ Remove self immediately
        }
        //public override void CompPostMake()
        //{
        //    base.CompPostMake();

        //    ApplyAndRemove();
        //}

        //public override void CompExposeData()
        //{
        //    base.CompExposeData();

        //    if (Scribe.mode == LoadSaveMode.PostLoadInit)
        //    {
        //        ApplyAndRemove();
        //    }
        //}

        //private void ApplyAndRemove()
        //{
        //    Pawn pawn = parent?.pawn;
        //    if (pawn?.def == null || parent?.def?.stages?.Count == 0)
        //        return;

        //    HediffStage stage = parent.def.stages[0];
        //    if (stage.statOffsets == null || stage.statOffsets.Count == 0)
        //        return;

        //    if (pawn.def.statBases == null)
        //    {
        //        pawn.def.statBases = new List<StatModifier>();
        //    }

        //    int appliedCount = 0;

        //    foreach (StatModifier mod in stage.statOffsets)
        //    {
        //        if (mod == null || mod.stat == null)
        //            continue;

        //        var existing = pawn.def.statBases.FirstOrDefault(s => s.stat == mod.stat);
        //        if (existing != null)
        //        {
        //            if (existing.value > 0)
        //                continue; // already has real value
        //            existing.value = mod.value;
        //            appliedCount++;
        //        }
        //        else
        //        {
        //            pawn.def.statBases.Add(new StatModifier
        //            {
        //                stat = mod.stat,
        //                value = mod.value
        //            });
        //            appliedCount++;
        //        }
        //    }

        //    if (Prefs.DevMode && appliedCount > 0)
        //    {
        //        Log.Message($"[PawnControl] Applied {appliedCount} statBases to {pawn.def.defName} via StatBridge, removing hediff.");
        //    }

        //    // Remove self
        //    pawn.health.RemoveHediff(parent);
        //}
    }
}
