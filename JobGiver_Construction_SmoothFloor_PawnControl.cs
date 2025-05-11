using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that allows pawns to smooth floors in designated areas.
    /// Uses the Construction work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Construction_SmoothFloor_PawnControl : JobGiver_Common_ConstructAffectFloor_PawnControl
    {
        #region Configuration

        protected override DesignationDef DesDef => DesignationDefOf.SmoothFloor;
        protected override JobDef JobDef => JobDefOf.SmoothFloor;

        #endregion

        #region Overrides

        /// <summary>
        /// Explicitly override TryGiveJob to maintain consistent pattern,
        /// even though functionality is identical to parent class
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            // Quick early exit if there are no designations on the map
            if (pawn?.Map == null || !pawn.Map.designationManager.AnySpawnedDesignationOfDef(DesDef))
                return null;

            // Use the StandardTryGiveJob pattern
            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Construction_SmoothFloor_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) => {
                    if (p?.Map == null) return null;

                    // Update cache
                    UpdateDesignatedCellsCache(p.Map);

                    int mapId = p.Map.uniqueID;
                    if (!_designatedCellsCache.ContainsKey(mapId) ||
                        !_designatedCellsCache[mapId].ContainsKey(DesDef) ||
                        _designatedCellsCache[mapId][DesDef].Count == 0)
                        return null;

                    // Create distance-based buckets manually since we're dealing with IntVec3
                    List<IntVec3>[] buckets = new List<IntVec3>[DISTANCE_THRESHOLDS.Length + 1];
                    for (int i = 0; i < buckets.Length; i++)
                        buckets[i] = new List<IntVec3>();

                    // Sort cells into buckets by distance
                    foreach (IntVec3 cell in _designatedCellsCache[mapId][DesDef])
                    {
                        float distSq = (cell - p.Position).LengthHorizontalSquared;
                        int bucketIndex = buckets.Length - 1;

                        for (int i = 0; i < DISTANCE_THRESHOLDS.Length; i++)
                        {
                            if (distSq <= DISTANCE_THRESHOLDS[i])
                            {
                                bucketIndex = i;
                                break;
                            }
                        }

                        buckets[bucketIndex].Add(cell);
                    }

                    // Process each bucket in order
                    for (int i = 0; i < buckets.Length; i++)
                    {
                        if (buckets[i].Count == 0)
                            continue;

                        // Randomize within bucket for even distribution
                        buckets[i].Shuffle();

                        foreach (IntVec3 cell in buckets[i])
                        {
                            // Skip if cell is no longer designated
                            if (p.Map.designationManager.DesignationAt(cell, DesDef) == null)
                                continue;

                            // Skip if forbidden or unreachable
                            if (cell.IsForbidden(p) ||
                                !p.CanReserve(cell, 1, -1, null, forced) ||
                                !p.CanReach(cell, PathEndMode.Touch, Danger.Some))
                                continue;

                            // Create the job
                            Job job = JobMaker.MakeJob(JobDef, cell);

                            Utility_DebugManager.LogNormal($"{p.LabelShort} created job to smooth floor at {cell}");
                            return job;
                        }
                    }

                    return null;
                },
                debugJobDesc: DebugName);
        }

        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // Implement a basic version of ProcessCachedTargets to satisfy the abstract requirement.
            // This method can be customized further based on the specific behavior you want.
            foreach (var target in targets)
            {
                if (target is Building building && pawn.CanReserveAndReach(building, PathEndMode.Touch, Danger.Some, 1, -1, null, forced))
                {
                    return JobMaker.MakeJob(JobDef, target.Position);
                }
            }
            return null;
        }

        #endregion
    }
}