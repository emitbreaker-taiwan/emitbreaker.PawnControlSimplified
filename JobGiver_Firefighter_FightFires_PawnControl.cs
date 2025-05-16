using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Enhanced JobGiver that assigns firefighting jobs to eligible pawns.
    /// Optimized for large colonies with 1000+ pawns and frequent fire events.
    /// </summary>
    public class JobGiver_Firefighter_FightFires_PawnControl : JobGiver_Scan_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        public override bool RequiresMapZoneorArea => true;

        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.BeatFire;

        public override PawnEnumTags RequiredTag => PawnEnumTags.AllowWork_Firefighter;

        /// <summary>
        /// Use the Firefighter work tag
        /// </summary>
        public override string WorkTag => "Firefighter";

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "FightFires";

        /// <summary>
        /// Update cache more frequently for fires (every second)
        /// </summary>
        protected override int CacheUpdateInterval => base.CacheUpdateInterval;

        /// <summary>
        /// Local optimization parameters
        /// </summary>
        private const int MAX_CACHE_ENTRIES = 500;       // Cap cache size to avoid memory issues
        private const int NEARBY_PAWN_RADIUS = 15;       // Same as original WorkGiver
        private const float HANDLED_DISTANCE = 5f;       // Same as original WorkGiver

        /// <summary>
        /// Distance thresholds for bucketing - smaller for fires (10, 20, 25 tiles)
        /// </summary>
        protected virtual float[] DistanceThresholds => new float[] { 100f, 400f, 625f };

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_Firefighter_FightFires_PawnControl() : base()
        {
            // Base constructor already initializes the cache system
        }

        #endregion

        #region Core Flow

        /// <summary>
        /// Execute more frequently for fires - check every tick
        /// </summary>
        protected override bool ShouldExecuteNow(int mapId)
        {
            // Fires are emergencies - check every tick
            return true;
        }

        /// <summary>
        /// Override to set the appropriate priority for firefighting jobs
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            return 9f;
        }

        #endregion

        #region Target Selection

        /// <summary>
        /// Get all fires on the map
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // Return all fires that should be extinguished
            if (map?.listerThings != null)
            {
                foreach (Thing thing in map.listerThings.ThingsOfDef(ThingDefOf.Fire))
                {
                    if (thing is Fire fire)
                    {
                        // Only return home area fires unless on a pawn
                        if (fire.parent is Pawn || map.areaManager.Home[fire.Position])
                        {
                            yield return fire;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Job-specific cache update method that implements specialized target collection logic
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            var targets = GetTargets(map).ToList();

            // Limit cache size for memory efficiency
            if (targets.Count > MAX_CACHE_ENTRIES)
            {
                return targets.Take(MAX_CACHE_ENTRIES);
            }

            return targets;
        }

        #endregion

        #region Job Creation

        /// <summary>
        /// Processes cached targets to find a valid job.
        /// </summary>
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            if (pawn?.Map == null || targets == null || targets.Count == 0)
                return null;

            int mapId = pawn.Map.uniqueID;

            // Step 1: Pre-filter valid fires (this is special handling specific to fires)
            List<Fire> validFires = new List<Fire>();
            foreach (Thing thing in targets)
            {
                if (thing is Fire fire && !fire.Destroyed && fire.Spawned)
                {
                    // Special handling for fires on pawns
                    if (fire.parent is Pawn parentPawn)
                    {
                        // Skip if the burning pawn is the firefighter
                        if (parentPawn == pawn)
                            continue;

                        // Skip if the burning pawn is an enemy
                        if ((parentPawn.Faction == null || parentPawn.Faction != pawn.Faction) &&
                            (parentPawn.HostFaction == null || (parentPawn.HostFaction != pawn.Faction && parentPawn.HostFaction != pawn.HostFaction)))
                            continue;

                        // Skip distant burning pawns outside home area
                        if (!pawn.Map.areaManager.Home[fire.Position] &&
                            IntVec3Utility.ManhattanDistanceFlat(pawn.Position, parentPawn.Position) > NEARBY_PAWN_RADIUS)
                            continue;

                        // Skip unreachable pawn fires
                        if (!pawn.CanReach(parentPawn, PathEndMode.Touch, Danger.Deadly))
                            continue;
                    }
                    else
                    {
                        // Skip non-pawn fires outside home area
                        if (!pawn.Map.areaManager.Home[fire.Position])
                            continue;
                    }

                    // Skip fires being handled by others
                    if (FireIsBeingHandled(fire, pawn))
                        continue;

                    validFires.Add(fire);
                }
            }

            // No valid fires found
            if (validFires.Count == 0)
                return null;

            // Step 2: Use JobGiverManager for distance bucketing and target selection
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                validFires,
                (fire) => (fire.Position - pawn.Position).LengthHorizontalSquared,
                DistanceThresholds
            );

            // Step 3: Find the best fire using the manager and centralized cache system
            Fire targetFire = (Fire)Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (fire, p) => !fire.IsForbidden(p) && p.CanReserveAndReach(fire, PathEndMode.Touch, p.NormalMaxDanger()),
                null // Pass null to use the centralized caching system
            );

            // Step 4: Create job if we found a target
            if (targetFire != null)
            {
                Job job = JobMaker.MakeJob(WorkJobDef, targetFire);

                if (Prefs.DevMode)
                {
                    Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job for fighting fire at {targetFire.Position}");
                }

                return job;
            }

            return null;
        }

        /// <summary>
        /// Check if a fire is already being handled by another pawn nearby
        /// Logic adapted from the original WorkGiver_FightFires
        /// </summary>
        private bool FireIsBeingHandled(Fire fire, Pawn potentialHandler)
        {
            if (!fire.Spawned)
                return false;

            Pawn pawn = fire.Map.reservationManager.FirstRespectedReserver(fire, potentialHandler);
            return pawn != null && pawn.Position.InHorDistOf(fire.Position, HANDLED_DISTANCE);
        }

        #endregion

        #region Reset

        /// <summary>
        /// Reset the cache - implements IResettableCache
        /// </summary>
        public override void Reset()
        {
            // Use centralized cache reset from parent
            base.Reset();
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return $"JobGiver_Firefighter_FightFires_PawnControl({DebugName})";
        }

        #endregion
    }
}