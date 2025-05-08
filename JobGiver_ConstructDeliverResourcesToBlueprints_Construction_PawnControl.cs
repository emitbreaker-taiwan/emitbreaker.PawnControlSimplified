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
    /// JobGiver that assigns construction tasks specifically for delivering resources to construction blueprints.
    /// Uses the Construction work tag for eligibility checking.
    /// </summary>
    public class JobGiver_ConstructDeliverResourcesToBlueprints_Construction_PawnControl : JobGiver_ConstructDeliverResourcesToBlueprints_Base_PawnControl
    {
        protected override string WorkTypeDef => "Construction";
        
        protected override string JobDescription => "delivering resources to blueprints (construction) assignment";
        
        public override float GetPriority(Pawn pawn)
        {
            // Construction workers should prioritize this even higher than haulers
            return 5.9f;
        }
    }
}