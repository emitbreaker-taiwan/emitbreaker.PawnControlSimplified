using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobModule for opening containers
    /// </summary>
    public class JobModule_BasicWorker_Open : JobModule_BasicWorker
    {
        public override string UniqueID => "OpenContainers";
        public override float Priority => 6.1f; // Same as original JobGiver
        public override string Category => "BasicWorker";

        /// <summary>
        /// The designation def this job module handles
        /// </summary>
        protected override DesignationDef TargetDesignation => DesignationDefOf.Open;

        /// <summary>
        /// Determine if the target should be processed
        /// </summary>
        public override bool ShouldProcessBasicTarget(Thing target, Map map)
        {
            // Check if target has open designation
            return HasTargetDesignation(target, map);
        }

        /// <summary>
        /// Validates if pawn can open the target
        /// </summary>
        public override bool ValidateBasicWorkerJob(Thing target, Pawn worker)
        {
            // Basic worker validation with path mode closest touch
            return CanWorkOn(target, worker, PathEndMode.ClosestTouch);
        }

        /// <summary>
        /// Creates a job to open the container
        /// </summary>
        protected override Job CreateBasicWorkerJob(Pawn worker, Thing target)
        {
            return JobMaker.MakeJob(JobDefOf.Open, target);
        }
    }
}