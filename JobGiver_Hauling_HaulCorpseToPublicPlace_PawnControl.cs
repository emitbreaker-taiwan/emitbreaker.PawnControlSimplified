using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks for colonists in CorpseObsession mental state
    /// to haul corpses to public places like tables or outside the colony.
    /// Uses the Hauling work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Hauling_HaulCorpseToPublicPlace_PawnControl : JobGiver_Hauling_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        protected override bool RequiresMapZoneorArea => false;

        /// <summary>
        /// The job to create when a valid target is found
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.HaulCorpseToPublicPlace;

        /// <summary>
        /// Human-readable name for debug logging
        /// </summary>
        protected override string DebugName => "HaulCorpseToPublicPlace";

        /// <summary>
        /// Update cache every 2 seconds - mental breaks should be responsive
        /// </summary>
        protected override int CacheUpdateInterval => 120;

        /// <summary>
        /// Smaller distance thresholds for corpses during mental breaks - cover colony area quickly
        /// </summary>
        protected override float[] DistanceThresholds => new float[] { 100f, 225f, 625f }; // 10, 15, 25 tiles

        /// <summary>
        /// Local parameters for corpse finding
        /// </summary>
        private const int MAX_SEARCH_DISTANCE = 999;
        private static readonly List<IntVec3> _tmpCells = new List<IntVec3>();

        #endregion

        #region Core flow

        /// <summary>
        /// This JobGiver only applies to pawns in CorpseObsession mental state
        /// </summary>
        public override float GetPriority(Pawn pawn)
        {
            // Only give this job during corpse obsession mental break
            if (pawn?.MentalState is MentalState_CorpseObsession)
                return 9.9f; // Higher priority than regular hauling

            return 0f; // No priority for non-obsessed pawns
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Only apply to pawns in CorpseObsession mental state
            if (!(pawn?.MentalState is MentalState_CorpseObsession))
                return null;

            return Utility_JobGiverManager.StandardTryGiveJob<JobGiver_Hauling_HaulCorpseToPublicPlace_PawnControl>(
                pawn,
                WorkTag,
                (p, forced) =>
                {
                    if (p?.Map == null)
                        return null;

                    int mapId = p.Map.uniqueID;
                    int now = Find.TickManager.TicksGame;

                    // Always execute for mental breaks
                    if (!ShouldExecuteNow(mapId))
                        return null;

                    // Use the shared cache updating logic from base class
                    if (!_lastHaulingCacheUpdate.TryGetValue(mapId, out int last)
                        || now - last >= CacheUpdateInterval)
                    {
                        _lastHaulingCacheUpdate[mapId] = now;
                        _haulableCache[mapId] = new List<Thing>(GetTargets(p.Map));
                    }

                    // Get all potential targets
                    if (!_haulableCache.TryGetValue(mapId, out var possibleTargets) || possibleTargets.Count == 0)
                        return null;

                    // Find a corpse or grave containing a corpse
                    Thing bestTarget = FindBestCorpseTarget(p, possibleTargets);
                    if (bestTarget == null)
                        return null;

                    // Find a destination for the corpse (table or public location)
                    IntVec3 displayCell = FindCorpseDisplayLocation(p);
                    if (!displayCell.IsValid)
                        return null;

                    // Create job based on source type
                    if (bestTarget is Building_Grave grave && grave.Corpse != null)
                    {
                        // Create job to dig up corpse and display it
                        return JobMaker.MakeJob(WorkJobDef, grave.Corpse, grave, displayCell);
                    }
                    else if (bestTarget is Corpse corpse)
                    {
                        // Create job to display corpse
                        return JobMaker.MakeJob(WorkJobDef, corpse, null, displayCell);
                    }

                    return null;
                },
                debugJobDesc: DebugName,
                skipEmergencyCheck: true); // Mental breaks are emergencies
        }

        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            // Validate input
            if (pawn == null || targets == null || targets.Count == 0)
                return null;

            // Find the best target from the cached list
            Thing bestTarget = FindBestCorpseTarget(pawn, targets);
            if (bestTarget == null)
                return null;

            // Find a destination for the corpse (table or public location)
            IntVec3 displayCell = FindCorpseDisplayLocation(pawn);
            if (!displayCell.IsValid)
                return null;

            // Create job based on source type
            if (bestTarget is Building_Grave grave && grave.Corpse != null)
            {
                // Create job to dig up corpse and display it
                return JobMaker.MakeJob(WorkJobDef, grave.Corpse, grave, displayCell);
            }
            else if (bestTarget is Corpse corpse)
            {
                // Create job to display corpse
                return JobMaker.MakeJob(WorkJobDef, corpse, null, displayCell);
            }

            return null;
        }

        /// <summary>
        /// Always execute for mental breaks
        /// </summary>
        protected override bool ShouldExecuteNow(int mapId)
        {
            return true; // Always check for mental breaks
        }

        #endregion

        #region Target selection

        /// <summary>
        /// Gets all corpses and graves with corpses on the map
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null)
                yield break;

            // First check graves for corpses
            foreach (Building_Grave grave in map.listerBuildings.AllBuildingsColonistOfDef(ThingDefOf.Grave))
            {
                if (grave?.Corpse != null)
                {
                    yield return grave;
                }
            }

            // Then check for loose corpses
            foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse))
            {
                if (thing is Corpse corpse && corpse.Spawned)
                {
                    yield return corpse;
                }
            }
        }

        /// <summary>
        /// Find the best corpse target for the mental break
        /// </summary>
        private Thing FindBestCorpseTarget(Pawn pawn, List<Thing> possibleTargets)
        {
            // First try corpses already in graves
            foreach (Thing thing in possibleTargets)
            {
                if (thing is Building_Grave grave &&
                    grave.Corpse != null &&
                    pawn.CanReserve(grave) &&
                    pawn.CanReach(grave, PathEndMode.InteractionCell, Danger.Deadly))
                {
                    return grave;
                }
            }

            // Then try corpses directly
            foreach (Thing thing in possibleTargets)
            {
                if (thing is Corpse corpse &&
                    pawn.CanReserve(corpse) &&
                    pawn.CanReach(corpse, PathEndMode.ClosestTouch, Danger.Deadly))
                {
                    return corpse;
                }
            }

            return null;
        }

        /// <summary>
        /// Find a suitable location to display a corpse (table or outdoor location)
        /// </summary>
        private IntVec3 FindCorpseDisplayLocation(Pawn pawn)
        {
            // Try to find a table cell (80% chance)
            if (Rand.Chance(0.8f) && TryFindTableCell(pawn, out IntVec3 tableCell))
            {
                return tableCell;
            }

            // If no table cell found, try an outdoor location
            if (RCellFinder.TryFindRandomSpotJustOutsideColony(pawn, out IntVec3 outsideSpot) &&
                CellFinder.TryRandomClosewalkCellNear(outsideSpot, pawn.Map, 5, out IntVec3 nearbyCell,
                    x => pawn.CanReserve(x) && x.GetFirstItem(pawn.Map) == null))
            {
                return nearbyCell;
            }

            // Fallback: just find any valid spot
            return CellFinder.RandomClosewalkCellNear(pawn.Position, pawn.Map, 10,
                x => pawn.CanReserve(x) && x.GetFirstItem(pawn.Map) == null);
        }

        /// <summary>
        /// Try to find a table cell for corpse display
        /// </summary>
        private bool TryFindTableCell(Pawn pawn, out IntVec3 cell)
        {
            _tmpCells.Clear();

            // Find all tables in the colony
            List<Building> allTables = pawn.Map.listerBuildings.allBuildingsColonist
                .Where(b => b.def.IsTable).ToList();

            // Find valid cells on tables
            foreach (Building table in allTables)
            {
                foreach (IntVec3 c in table.OccupiedRect())
                {
                    if (pawn.CanReserveAndReach(c, PathEndMode.OnCell, Danger.Deadly) &&
                        c.GetFirstItem(pawn.Map) == null)
                    {
                        _tmpCells.Add(c);
                    }
                }
            }

            // Pick a random table cell if any were found
            if (_tmpCells.Any())
            {
                cell = _tmpCells.RandomElement();
                return true;
            }

            cell = IntVec3.Invalid;
            return false;
        }

        #endregion
    }
}