using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobModule for extracting skulls from corpses
    /// </summary>
    public class JobModule_BasicWorker_ExtractSkull : JobModule_BasicWorker
    {
        public override string UniqueID => "ExtractSkull";
        public override float Priority => 6.0f; // Same as original JobGiver
        public override string Category => "BasicWorker";

        private Dictionary<int, bool> _hasHeadCache = new Dictionary<int, bool>();
        private int _lastIdeologyCheckTick = -1;
        private bool _cachedCanExtractResult = false;
        private const int IDEOLOGY_CHECK_INTERVAL = 250;
        private const int MAX_CACHE_SIZE = 200;

        /// <summary>
        /// The designation def this job module handles
        /// </summary>
        protected override DesignationDef TargetDesignation => DesignationDefOf.ExtractSkull;

        /// <summary>
        /// Determine if the target should be processed
        /// </summary>
        public override bool ShouldProcessBasicTarget(Thing target, Map map)
        {
            // Use C# 7.3 compatible pattern matching
            Corpse corpse = target as Corpse;
            if (corpse == null) return false;

            // Check if the corpse is designated and has a head
            return HasTargetDesignation(corpse, map) &&
                   HasExtractableHead(corpse);
        }

        /// <summary>
        /// Validates if pawn can extract skull from target
        /// </summary>
        public override bool ValidateBasicWorkerJob(Thing target, Pawn worker)
        {
            // Basic worker validation
            if (!CanWorkOn(target, worker)) return false;

            // Make sure target is still valid for skull extraction
            // Use C# 7.3 compatible pattern matching
            Corpse corpse = target as Corpse;
            if (corpse == null) return false;

            // Use cached head check
            if (!HasExtractableHead(corpse)) return false;

            // Check ideology requirements
            if (ModsConfig.IdeologyActive && !CanPawnExtractSkull(worker)) return false;

            return true;
        }

        /// <summary>
        /// Creates a job to extract skull from corpse
        /// </summary>
        protected override Job CreateBasicWorkerJob(Pawn worker, Thing target)
        {
            Job job = JobMaker.MakeJob(JobDefOf.ExtractSkull, target);

            // Find nearby corpses for batching
            TryFindNearbyCorpses(worker, target, job);

            return job;
        }

        /// <summary>
        /// Checks if the corpse has an extractable head, with caching
        /// </summary>
        private bool HasExtractableHead(Corpse corpse)
        {
            if (corpse?.InnerPawn == null) return false;

            int thingId = corpse.InnerPawn.thingIDNumber;

            // Check cache first
            if (_hasHeadCache.TryGetValue(thingId, out bool hasHead))
                return hasHead;

            // Check for head and update cache
            hasHead = corpse.InnerPawn.health.hediffSet.HasHead;

            // Handle compatibility with mods
            if (hasHead && ModLister.HasActiveModWithName("Combat Extended"))
            {
                // CE compatibility check would go here
                // hasHead = hasHead && CEHasExtractableSkull(corpse);
            }

            // Add to cache
            _hasHeadCache[thingId] = hasHead;

            // Clean cache if too large
            if (_hasHeadCache.Count > MAX_CACHE_SIZE)
                CleanupCache();

            return hasHead;
        }

        /// <summary>
        /// Checks if the pawn can extract skulls based on ideology requirements
        /// </summary>
        private bool CanPawnExtractSkull(Pawn pawn)
        {
            // Non-ideology games can always extract skulls
            if (!ModsConfig.IdeologyActive)
                return false;

            // Default to player's ideology
            return CheckCachedPlayerExtractSkull();
        }

        /// <summary>
        /// Cached check for extracting skulls
        /// </summary>
        private bool CheckCachedPlayerExtractSkull()
        {
            int currentTick = Find.TickManager.TicksGame;
            if (currentTick > _lastIdeologyCheckTick + IDEOLOGY_CHECK_INTERVAL)
            {
                _lastIdeologyCheckTick = currentTick;
                _cachedCanExtractResult = CanPlayerExtractSkull();
            }
            return _cachedCanExtractResult;
        }

        /// <summary>
        /// Checks if player factions can extract skulls based on ideological requirements
        /// Direct port of WorkGiver_ExtractSkull.CanPlayerExtractSkull
        /// </summary>
        private bool CanPlayerExtractSkull()
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
        private static bool CanExtractSkull(Ideo ideo)
        {
            if (ideo.classicMode || ideo.HasPrecept(PreceptDefOf.Skullspike_Desired))
                return true;

            return ModsConfig.AnomalyActive && ResearchProjectDefOf.AdvancedPsychicRituals.IsFinished;
        }

        /// <summary>
        /// Try to find nearby corpses to extract skulls from
        /// </summary>
        private void TryFindNearbyCorpses(Pawn worker, Thing primaryTarget, Job job)
        {
            const float RADIUS = 8f;
            const int MAX_TARGETS = 5;

            if (job == null) return;

            int queuedCount = 0;

            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(
                primaryTarget.Position, primaryTarget.Map, RADIUS, true))
            {
                if (queuedCount >= MAX_TARGETS) break;

                if (thing != primaryTarget && thing is Corpse corpse &&
                    HasTargetDesignation(thing, thing.Map) &&
                    ValidateBasicWorkerJob(thing, worker))
                {
                    job.AddQueuedTarget(TargetIndex.A, thing);
                    queuedCount++;
                }
            }

            // Sort by distance for efficiency
            if (job.targetQueueA != null && job.targetQueueA.Count > 0)
            {
                job.targetQueueA.SortBy(t =>
                    (t.Thing.Position - worker.Position).LengthHorizontalSquared);
            }
        }

        /// <summary>
        /// Cleanup the head cache to prevent memory leaks
        /// </summary>
        private void CleanupCache()
        {
            if (_hasHeadCache.Count > MAX_CACHE_SIZE)
            {
                _hasHeadCache.Clear();
                Utility_DebugManager.LogNormal("Cleared skull extraction head cache due to size limit");
            }
        }

        /// <summary>
        /// Reset caches when needed
        /// </summary>
        public override void ResetStaticData()
        {
            base.ResetStaticData();
            _hasHeadCache.Clear();
            _lastIdeologyCheckTick = -1;
        }
    }
}