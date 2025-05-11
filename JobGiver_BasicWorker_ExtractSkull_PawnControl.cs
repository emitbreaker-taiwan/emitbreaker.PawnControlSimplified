using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Enhanced JobGiver that assigns skull extraction jobs to eligible pawns.
    /// Requires the BasicWorker work tag to be enabled.
    /// </summary>
    public class JobGiver_BasicWorker_ExtractSkull_PawnControl : JobGiver_BasicWorker_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Use Hauling work tag
        /// </summary>
        protected override string WorkTag => "Hauling";

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "ExtractSkull";

        /// <summary>
        /// The designation this job giver targets
        /// </summary>
        protected override DesignationDef TargetDesignation => DesignationDefOf.ExtractSkull;

        /// <summary>
        /// The job to create
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.ExtractSkull;

        #endregion

        #region Validation overrides

        /// <summary>
        /// Override the target filtering to ensure only valid corpses with heads are processed
        /// </summary>
        protected override bool IsValidTarget(Thing thing, Pawn worker)
        {
            // First do the base class validation
            if (!base.IsValidTarget(thing, worker))
                return false;

            // Additional validation for skull extraction
            if (thing is Corpse corpse && corpse.InnerPawn?.health?.hediffSet != null)
            {
                return corpse.InnerPawn.health.hediffSet.HasHead;
            }

            return false;
        }

        /// <summary>
        /// Override TryGiveJob to add additional checks specific to skull extraction
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            // IMPORTANT: Only player pawns and slaves owned by player should extract skulls
            if (pawn.Faction != Faction.OfPlayer &&
                !(pawn.IsSlave && pawn.HostFaction == Faction.OfPlayer))
                return null;

            // Make sure we can extract skulls on this game
            if (ModsConfig.IdeologyActive && !CanPawnExtractSkull(pawn))
            {
                return null;
            }

            // Let the base class handle the rest of the job creation process
            return base.TryGiveJob(pawn);
        }

        /// <summary>
        /// Override GetTargets to filter corpses that have heads
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // Use base implementation first to get designated corpses
            foreach (Thing thing in base.GetTargets(map))
            {
                // Additional filtering for corpses with heads
                if (thing is Corpse corpse &&
                    corpse.Spawned &&
                    corpse.InnerPawn?.health?.hediffSet != null &&
                    corpse.InnerPawn.health.hediffSet.HasHead)
                {
                    yield return thing;
                }
            }
        }

        /// <summary>
        /// Process the cached targets and create a job for the pawn
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // Skip if no targets or if the pawn can't extract skulls
            if (targets.Count == 0 || (ModsConfig.IdeologyActive && !CanPawnExtractSkull(pawn)))
                return null;

            // Use bucketing system to find closest valid target
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                targets,
                thing => (thing.Position - pawn.Position).LengthHorizontalSquared,
                DistanceThresholds);

            // Find the first valid target
            Thing bestTarget = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets, pawn, IsValidTarget, _reachabilityCache);

            // Create a job for the target if found
            if (bestTarget != null)
            {
                return CreateJobForTarget(bestTarget);
            }

            return null;
        }

        #endregion

        #region Ideology compatibility

        /// <summary>
        /// Checks if the pawn can extract skulls based on ideology requirements
        /// </summary>
        private bool CanPawnExtractSkull(Pawn pawn)
        {
            // Non-ideology games can always extract skulls
            if (!ModsConfig.IdeologyActive)
                return true;

            // Default to player's ideology
            return CanPlayerExtractSkull();
        }

        /// <summary>
        /// Checks if player factions can extract skulls based on ideological requirements
        /// Direct port of WorkGiver_ExtractSkull.CanPlayerExtractSkull
        /// </summary>
        public bool CanPlayerExtractSkull()
        {
            if (Find.IdeoManager.classicMode || CanExtractSkull(Faction.OfPlayer.ideos.PrimaryIdeo))
                return true;

            foreach (Ideo ideo in Faction.OfPlayer.ideos.IdeosMinorListForReading)
            {
                if (CanExtractSkull(ideo))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Checks if a specific ideology allows skull extraction
        /// Direct port of WorkGiver_ExtractSkull.CanExtractSkull
        /// </summary>
        public static bool CanExtractSkull(Ideo ideo)
        {
            if (ideo.classicMode || ideo.HasPrecept(PreceptDefOf.Skullspike_Desired))
                return true;

            return ModsConfig.AnomalyActive && ResearchProjectDefOf.AdvancedPsychicRituals.IsFinished;
        }

        #endregion
    }
}