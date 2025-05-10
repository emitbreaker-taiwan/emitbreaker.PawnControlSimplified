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
    /// JobGiver that assigns construction tasks specifically for delivering resources to construction frames.
    /// Uses the Construction work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Construction_ConstructDeliverResourcesToFrames_PawnControl : JobGiver_Common_ConstructDeliverResourcesToFrames_PawnControl
    {
        protected override string WorkTypeDef => "Construction";
        
        protected override string JobDescription => "delivering resources to frames (construction) assignment";
        
        public override float GetPriority(Pawn pawn)
        {
            // Construction workers should prioritize this even higher than haulers
            return 5.8f;
        }
    }
}