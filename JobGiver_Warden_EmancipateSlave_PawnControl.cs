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

        protected override float[] DistanceThresholds => new float[] { 225f, 400f, 625f }; // 15, 20, 25 tiles
        private const int CACHE_UPDATE_INTERVAL = 180; // Update every 3 seconds

        #endregion

        #region Static Resources
        
        // Static string caching for better performance
        protected static string NotSupportedWithoutIdeologyTrans;
        
        #endregion

        #region Initialization
        
        // Reset static strings when language changes
        public static void ResetStaticData()
        {
            NotSupportedWithoutIdeologyTrans = "Requires Ideology DLC";
        }

        #endregion

        #region Core Flow

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
            // Check if pawn is in a mental state
            if (pawn.InMentalState)
                return true;
            return false;
        }

        protected override float GetBasePriority(string workTag)
        {
            return 6.5f;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Ideology check is already done in ShouldSkip method, but keep it here for safety
            if (!ModLister.CheckIdeology("Slave imprisonment"))
            {
                return null;
            }

            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Warden_EmancipateSlave_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) => {
                    if (p?.Map == null) return null;

                    // Update slave cache
                    int lastUpdateTick = 0;
                    if (_lastWardenCacheUpdate.ContainsKey(p.Map.uniqueID))
                    {
                        lastUpdateTick = _lastWardenCacheUpdate[p.Map.uniqueID];
                    }

                    UpdatePrisonerCache(
                        p.Map,
                        ref lastUpdateTick,
                        CACHE_UPDATE_INTERVAL,
                        _prisonerCache,
                        _prisonerReachabilityCache,
                        FilterEmancipationTargets
                    );
                    _lastWardenCacheUpdate[p.Map.uniqueID] = lastUpdateTick;

                    // Process cached targets
                    List<Thing> targets;
                    if (_prisonerCache.TryGetValue(p.Map.uniqueID, out var slaveList) && slaveList != null)
                    {
                        targets = new List<Thing>(slaveList.Cast<Thing>());
                    }
                    else
                    {
                        targets = new List<Thing>();
                    }

                    return ProcessCachedTargets(p, targets, forced);
                },
                debugJobDesc: "emancipate slave");
        }

        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // Only proceed if Ideology is active
            if (!ModLister.CheckIdeology("Slave imprisonment"))
            {
                yield break;
            }

            // Return slaves marked for emancipation
            foreach (var slave in map.mapPawns.AllPawnsSpawned)
            {
                if (slave.IsSlave && FilterEmancipationTargets(slave))
                {
                    yield return slave;
                }
            }
        }

        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // Only proceed if Ideology is active
            if (!ModLister.CheckIdeology("Slave imprisonment"))
            {
                return null;
            }
            
            foreach (var target in targets)
            {
                if (target is Pawn slave && ValidateCanEmancipate(slave, pawn))
                {
                    return CreateEmancipationJob(pawn, slave);
                }
            }
            return null;
        }
        
        #endregion

        #region Target Filtering
        
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
        private bool ValidateCanEmancipate(Pawn slave, Pawn warden)
        {
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
        
        #endregion

        #region Job Creation
        
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
        public static new void ResetCache()
        {
            ResetWardenCache();
            ResetStaticData();
        }
        
        #endregion

        #region Object Information
        
        public override string ToString()
        {
            return "JobGiver_Warden_EmancipateSlave_PawnControl";
        }
        
        #endregion
    }
}