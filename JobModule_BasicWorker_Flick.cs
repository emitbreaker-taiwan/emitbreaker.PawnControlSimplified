using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobModule for flicking switches
    /// </summary>
    public class JobModule_BasicWorker_Flick : JobModule_BasicWorker
    {
        public override string UniqueID => "FlickSwitches";
        public override float Priority => 6.2f; // Same as original JobGiver
        public override string Category => "BasicWorker";

        /// <summary>
        /// The designation def this job module handles
        /// </summary>
        protected override DesignationDef TargetDesignation => DesignationDefOf.Flick;

        /// <summary>
        /// Determine if the target should be processed
        /// </summary>
        public override bool ShouldProcessBasicTarget(Thing target, Map map)
        {
            // Check if target has flick designation
            return HasTargetDesignation(target, map);
        }

        /// <summary>
        /// Validates if pawn can flick the target
        /// </summary>
        public override bool ValidateBasicWorkerJob(Thing target, Pawn worker)
        {
            // Check if pawn is authorized to flick switches (player pawns and slaves only)
            if (worker.Faction != Faction.OfPlayer && 
                !(worker.IsSlave && worker.HostFaction == Faction.OfPlayer))
                return false;
                
            // Basic worker validation with path mode touch
            return CanWorkOn(target, worker, PathEndMode.Touch);
        }

        /// <summary>
        /// Creates a job to flick the switch
        /// </summary>
        protected override Job CreateBasicWorkerJob(Pawn worker, Thing target)
        {
            return JobMaker.MakeJob(JobDefOf.Flick, target);
        }
    }
}