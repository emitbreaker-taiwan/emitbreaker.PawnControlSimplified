using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that allows non-humanlike pawns to uninstall buildings with the Uninstall designation.
    /// </summary>
    public class JobGiver_Construction_Uninstall_PawnControl : JobGiver_Common_RemoveBuilding_PawnControl
    {
        protected override DesignationDef Designation => DesignationDefOf.Uninstall;
        
        protected override JobDef RemoveBuildingJob => JobDefOf.Uninstall;
        
        public override float GetPriority(Pawn pawn)
        {
            // Slightly lower priority than deconstruct
            return 5.8f;
        }
        
        protected override Job TryGiveJob(Pawn pawn)
        {
            // Standardized approach to job giving using your utility class
            return Utility_JobGiverManagerOld.StandardTryGiveJob<JobGiver_Construction_Uninstall_PawnControl>(
                pawn,
                "Construction", // This uses the Construction work type
                (p, forced) => {
                    // Update cache first
                    UpdateTargetCache(p.Map);
                    
                    // Get cached targets
                    int mapId = p.Map.uniqueID;
                    if (!_targetCache.ContainsKey(mapId) || _targetCache[mapId].Count == 0)
                        return null;
                    
                    // Use the same bucketing and target selection as base class
                    var buckets = Utility_JobGiverManagerOld.CreateDistanceBuckets(
                        p,
                        _targetCache[mapId],
                        (thing) => (thing.Position - p.Position).LengthHorizontalSquared,
                        DISTANCE_THRESHOLDS
                    );
                    
                    // Find best target with additional uninstall-specific validation
                    Thing bestTarget = Utility_JobGiverManagerOld.FindFirstValidTargetInBuckets(
                        buckets,
                        p,
                        (thing, pn) => {
                            // Basic validation from base class
                            if (!Utility_JobGiverManagerOld.IsValidFactionInteraction(thing, pn, requiresDesignator: true))
                                return false;
                                
                            if (thing == null || thing.Destroyed || !thing.Spawned)
                                return false;
                                
                            if (thing.Map.designationManager.DesignationOn(thing, Designation) == null)
                                return false;
                                
                            CompExplosive explosive = thing.TryGetComp<CompExplosive>();
                            if (explosive != null && explosive.wickStarted)
                                return false;
                                
                            if (thing.IsForbidden(pn) ||
                                !pn.CanReserve(thing, 1, -1, null, forced) ||
                                !pn.CanReach(thing, PathEndMode.Touch, Danger.Some))
                                return false;
                                
                            // UNINSTALL-SPECIFIC CHECKS
                            // Check ownership - if claimable, must be owned by pawn's faction
                            if (thing.def.Claimable)
                            {
                                if (thing.Faction != pn.Faction)
                                    return false;
                            }
                            // If not claimable, pawn must belong to player faction
                            else if (pn.Faction != Faction.OfPlayer)
                                return false;
                                
                            return true;
                        },
                        _reachabilityCache
                    );
                    
                    // Create job if target found
                    if (bestTarget != null)
                    {
                        Job job = JobMaker.MakeJob(RemoveBuildingJob, bestTarget);
                        Utility_DebugManager.LogNormal($"{p.LabelShort} created job to uninstall {bestTarget.LabelCap}");
                        return job;
                    }
                    
                    return null;
                },
                debugJobDesc: "uninstall assignment",
                skipEmergencyCheck: true);
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static new void ResetCache()
        {
            JobGiver_Common_RemoveBuilding_PawnControl.ResetCache();
        }

        public override string ToString()
        {
            return "JobGiver_Uninstall_PawnControl";
        }
    }
}