using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns hauling tasks specifically for delivering resources to construction frames.
    /// Uses the Hauling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Hauling_ConstructDeliverResourcesToFrames_PawnControl : JobGiver_Common_ConstructDeliverResourcesToFrames_PawnControl
    {
        protected override string WorkTypeDef => "Hauling";

        protected override string JobDescription => "delivering resources to frames (hauling) assignment";

        public override float GetPriority(Pawn pawn)
        {
            // Frame delivery is important hauling
            return 5.6f;
        }
    }
}