using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Abstract JobGiver for gathering resources from animals (milk, wool, etc.).
    /// Uses the Handler work tag for eligibility checking.
    /// </summary>
    public abstract class JobGiver_Handling_GatherAnimalBodyResources_PawnControl : JobGiver_Handling_PawnControl
    {
        #region Cach management

        // Add this field to the class
        private static Dictionary<int, Dictionary<Pawn, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();

        public static void ResetReachabilityCache()
        {
            _reachabilityCache.Clear();
            Utility_DebugManager.LogNormal("Reset JobGiver_Scan_PawnControl cache");
        }

        #endregion

        #region Overrides

        protected override string WorkTag => "Handling";
        protected override int CacheUpdateInterval => 250;

        /// <summary>
        /// The JobDef to use when creating jobs
        /// </summary>
        protected abstract JobDef JobDef { get; }

        /// <summary>
        /// Gets the appropriate CompHasGatherableBodyResource component from the animal
        /// </summary>
        protected abstract CompHasGatherableBodyResource GetComp(Pawn animal);

        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            if (map == null) yield break;

            Faction playerFaction = Faction.OfPlayer;
            if (playerFaction == null) yield break;

            // Get animals from the faction that have gatherable resources
            foreach (Pawn animal in map.mapPawns.SpawnedPawnsInFaction(playerFaction))
            {
                if (animal != null && animal.IsNonMutantAnimal)
                {
                    CompHasGatherableBodyResource comp = GetComp(animal);
                    if (comp != null && comp.ActiveAndFull && !animal.Downed &&
                        animal.CanCasuallyInteractNow() &&
                        (animal.roping == null || !animal.roping.IsRopedByPawn))
                    {
                        yield return animal;
                    }
                }
            }
        }

        // Inside ProcessCachedTargets method in JobGiver_Handling_GatherAnimalBodyResources_PawnControl.cs
        protected override Job ProcessCachedTargets(Pawn pawn, List<Thing> targets, bool forced)
        {
            if (pawn?.Map == null || targets.Count == 0)
                return null;

            // Distance thresholds for bucketing (25, 50, 100 tiles squared)
            float[] distanceThresholds = new float[] { 625f, 2500f, 10000f };

            // Create distance buckets for optimized searching
            var animalTargets = targets.Cast<Pawn>().ToList();
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                animalTargets,
                (animal) => (animal.Position - pawn.Position).LengthHorizontalSquared,
                distanceThresholds
            );

            // Class-specific reachability cache instead of local variable
            // This ensures the cache persists between calls
            if (_reachabilityCache == null)
            {
                _reachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
            }

            int mapId = pawn.Map.uniqueID;
            if (!_reachabilityCache.ContainsKey(mapId))
            {
                _reachabilityCache[mapId] = new Dictionary<Pawn, bool>();
            }

            // Find a valid animal to gather resources from
            Pawn targetAnimal = Utility_JobGiverManager.FindFirstValidTargetInBuckets<Pawn>(
                buckets,
                pawn,
                (animal, p) => {
                    // Skip if trying to gather from self
                    if (animal == p)
                        return false;

                    // Skip if no longer valid
                    if (animal.Destroyed || !animal.Spawned || animal.Map != p.Map)
                        return false;

                    // Skip if not of same faction
                    if (animal.Faction != p.Faction)
                        return false;

                    // Skip if not an animal or doesn't have gatherable resources
                    CompHasGatherableBodyResource comp = GetComp(animal);
                    if (!animal.IsNonMutantAnimal || comp == null || !comp.ActiveAndFull)
                        return false;

                    // Skip if downed, roped, or can't interact
                    if (animal.Downed || !animal.CanCasuallyInteractNow() ||
                        (animal.roping != null && animal.roping.IsRopedByPawn))
                        return false;

                    // Skip if forbidden or unreachable
                    if (animal.IsForbidden(p) ||
                        !p.CanReserve((LocalTargetInfo)animal, ignoreOtherReservations: forced) ||
                        !p.CanReach((LocalTargetInfo)animal, PathEndMode.Touch, Danger.Some))
                        return false;

                    return true;
                },
                _reachabilityCache
            );

            if (targetAnimal == null)
                return null;

            // Create job if target found
            Job job = JobMaker.MakeJob(JobDef, targetAnimal);
            Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to gather resources from {targetAnimal.LabelShort}");
            return job;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // Basic validation - only player faction pawns can gather resources
            if (pawn?.Map == null || pawn.Faction != Faction.OfPlayer)
                return null;

            return Utility_JobGiverManager.StandardTryGiveJob<Pawn>(
                pawn,
                WorkTag,
                (p, forced) => base.TryGiveJob(p),
                (p, setFailReason) => {
                    // Additional check for animals work tag
                    if (p.WorkTagIsDisabled(WorkTags.Animals))
                    {
                        if (setFailReason)
                            JobFailReason.Is("CannotPrioritizeWorkTypeDisabled".Translate(WorkTypeDefOf.Handling.gerundLabel).CapitalizeFirst());
                        return false;
                    }
                    return true;
                },
                debugJobDesc: "animal resource gathering");
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return this.GetType().Name;
        }

        #endregion
    }
}