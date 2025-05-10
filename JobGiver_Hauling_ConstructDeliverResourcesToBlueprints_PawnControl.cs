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
    /// JobGiver that assigns hauling tasks specifically for delivering resources to construction blueprints.
    /// Uses the Hauling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Hauling_ConstructDeliverResourcesToBlueprints_PawnControl : JobGiver_Common_ConstructDeliverResourcesToBlueprints_PawnControl
    {
        protected override string WorkTypeDef => "Hauling";

        protected override string JobDescription => "delivering resources to blueprints (hauling) assignment";

        public override float GetPriority(Pawn pawn)
        {
            // Blueprint delivery is slightly more important than frame delivery
            return 5.7f;
        }
    }
}