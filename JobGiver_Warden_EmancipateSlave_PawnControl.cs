using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Enhanced JobGiver for wardens to emancipate slaves.
    /// Requires the Warden work tag to be enabled.
    /// </summary>
    public class JobGiver_Warden_EmancipateSlave_PawnControl : JobGiver_Warden_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Human-readable name for debug logging 
        /// </summary>
        protected override string DebugName => "EmancipateSlave";

        /// <summary>
        /// Cache update interval in ticks (180 ticks = 3 seconds)
        /// </summary>
        protected override int CacheUpdateInterval => 180;

        /// <summary>
        /// Distance thresholds for bucketing (15, 20, 25 tiles squared)
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 225f, 400f, 625f };

        #endregion

        #region Static Resources

        // Static string caching for better performance
        protected static string NotSupportedWithoutIdeologyTrans;

        /// <summary>
        /// Reset static strings when language changes
        /// </summary>
        public static void ResetStaticData()
        {
            NotSupportedWithoutIdeologyTrans = "Requires Ideology DLC";
        }

        #endregion

        #region Core flow

        /// <summary>
        /// Gets base priority for the job giver
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            // Slave emancipation is moderately high priority
            return 6.5f;
        }

        /// <summary>
        /// Creates a job for the warden to emancipate a slave
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            // Ideology check is required for slave-related features
            if (!ModLister.CheckIdeology("Slave imprisonment"))
            {
                return null;
            }

            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Warden_EmancipateSlave_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) => {
                    // Process cached targets to create job
                    if (p?.Map == null) return null;

                    // Get slaves from centralized cache system
                    var slaves = GetOrCreatePrisonerCache(p.Map);

                    // Convert to Thing list for processing
                    List<Thing> targets = new List<Thing>();
                    foreach (Pawn slave in slaves)
                    {
                        if (slave != null && !slave.Dead && slave.Spawned && slave.IsSlave)
                            targets.Add(slave);
                    }

                    return ProcessCachedTargets(p, targets, forced);
                },
                debugJobDesc: "emancipate slave");
        }

        /// <summary>
        /// Checks whether this job giver should be skipped for a pawn
        /// </summary>
        public override bool ShouldSkip(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.InMentalState)
                return true;

            var modExtension = Utility_CacheManager.GetModExtension(pawn.def);
            if (modExtension == null)
                return true;

            // Skip if pawn is not a warden
            if (!Utility_TagManager.WorkEnabled(pawn.def, WorkTag))
                return true;

            // Check if Ideology is active
            if (!ModLister.CheckIdeology("Slave imprisonment"))
                return true;

            return false;
        }

        /// <summary>
        /// Determines if the job giver should execute based on cache status
        /// </summary>
        protected override bool ShouldExecuteNow(int mapId)
        {
            // Skip if Ideology is inactive
            if (!ModLister.CheckIdeology("Slave imprisonment"))
                return false;

            // Check if there's any slaves on the map
            Map map = Find.Maps.Find(m => m.uniqueID == mapId);
            if (map == null || !map.mapPawns.SlavesOfColonySpawned.Any())
                return false;

            // Check if cache needs updating
            string cacheKey = this.GetType().Name + PRISONERS_CACHE_SUFFIX;
            int lastUpdateTick = Utility_MapCacheManager.GetLastCacheUpdateTick(mapId, cacheKey);

            return Find.TickManager.TicksGame > lastUpdateTick + CacheUpdateInterval;
        }

        #endregion

        #region Prisoner Selection

        /// <summary>
        /// Get slaves that match the criteria for emancipation
        /// </summary>
        protected override IEnumerable<Pawn> GetPrisonersMatchingCriteria(Map map)
        {
            if (map == null)
                yield break;

            // Only proceed if Ideology is active
            if (!ModLister.CheckIdeology("Slave imprisonment"))
                yield break;

            // Return slaves marked for emancipation
            foreach (var slave in map.mapPawns.AllPawnsSpawned)
            {
                if (slave.IsSlave && FilterEmancipationTargets(slave))
                {
                    yield return slave;
                }
            }
        }

        /// <summary>
        /// Filter function to identify slaves marked for emancipation
        /// </summary>
        private bool FilterEmancipationTargets(Pawn slave)
        {
            // Skip invalid slaves
            if (slave?.guest == null || slave.Dead || !slave.Spawned || !slave.IsSlave)
                return false;

            // Only include slaves explicitly marked for emancipation
            return slave.guest.slaveInteractionMode == SlaveInteractionModeDefOf.Emancipate &&
                   !slave.Downed &&
                   slave.Awake();
        }

        /// <summary>
        /// Validates if a warden can emancipate a specific slave
        /// </summary>
        protected override bool IsValidPrisonerTarget(Pawn slave, Pawn warden)
        {
            // First check base class validation
            if (!base.IsValidPrisonerTarget(slave, warden))
                return false;

            // Skip if slave status changed
            if (slave?.guest == null ||
                !slave.IsSlave ||
                slave.guest.slaveInteractionMode != SlaveInteractionModeDefOf.Emancipate ||
                slave.Downed ||
                !slave.Awake())
                return false;

            // Check if warden can reach slave
            if (!ShouldTakeCareOfSlave(warden, slave) ||
                !warden.CanReserveAndReach(slave, PathEndMode.OnCell, Danger.Deadly))
                return false;

            return true;
        }

        /// <summary>
        /// Create a job for the given slave
        /// </summary>
        protected override Job CreateJobForPrisoner(Pawn warden, Pawn slave, bool forced)
        {
            return CreateEmancipationJob(warden, slave);
        }

        #endregion

        #region Job creation

        /// <summary>
        /// Creates the emancipation job for the warden
        /// </summary>
        private Job CreateEmancipationJob(Pawn warden, Pawn slave)
        {
            Job job = JobMaker.MakeJob(JobDefOf.SlaveEmancipation, slave);
            job.count = 1;

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"{warden.LabelShort} created job for emancipating slave {slave.LabelShort}");
            }

            return job;
        }

        #endregion

        #region Validation

        /// <summary>
        /// Check if the pawn should take care of a slave (ported from WorkGiver_Warden)
        /// </summary>
        private bool ShouldTakeCareOfSlave(Pawn warden, Thing slave)
        {
            return slave is Pawn pawnSlave &&
                   pawnSlave.IsSlave &&
                   pawnSlave.Spawned &&
                   !pawnSlave.Dead &&
                   !pawnSlave.InAggroMentalState &&
                   pawnSlave.guest != null &&
                   warden.CanReach(pawnSlave, PathEndMode.OnCell, Danger.Some);
        }

        #endregion

        #region Cache Management

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetEmancipateSlaveCache()
        {
            ResetWardenCache();
            ResetStaticData();
        }

        #endregion

        #region Debug

        /// <summary>
        /// For debugging purposes
        /// </summary>
        public override string ToString()
        {
            return "JobGiver_Warden_EmancipateSlave_PawnControl";
        }

        #endregion
    }
}