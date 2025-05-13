using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tasks to shear animals for wool.
    /// Uses the Handler work tag for eligibility checking.
    /// </summary>
    public class JobGiver_Handling_Shear_PawnControl : JobGiver_Handling_GatherAnimalBodyResources_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        protected override bool RequiresMapZoneorArea => false;

        /// <summary>
        /// The JobDef to use for shearing animals
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.Shear;

        /// <summary>
        /// For shearing animals, we can use the default handling distance thresholds
        /// </summary>
        protected override float[] DistanceThresholds => base.DistanceThresholds;

        /// <summary>
        /// Cache key suffix specifically for shearable animals
        /// </summary>
        private const string SHEARABLE_CACHE_SUFFIX = "_ShearableAnimals";

        #endregion

        #region Target Selection

        /// <summary>
        /// Gets the shearable component from the animal
        /// </summary>
        protected override CompHasGatherableBodyResource GetComp(Pawn animal)
        {
            return animal.TryGetComp<CompShearable>();
        }

        /// <summary>
        /// Override to use shearing-specific cache keys
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            if (map == null)
                yield break;

            Faction playerFaction = Faction.OfPlayer;
            if (playerFaction == null)
                yield break;

            // Find all animals with shearable resources
            List<Pawn> shearableAnimals = new List<Pawn>();

            // Use map.mapPawns.SpawnedPawnsInFaction for better performance
            foreach (Pawn animal in map.mapPawns.SpawnedPawnsInFaction(playerFaction))
            {
                if (animal == null || !animal.IsNonMutantAnimal)
                    continue;

                CompShearable comp = animal.TryGetComp<CompShearable>();
                if (comp != null && comp.ActiveAndFull && !animal.Downed &&
                    animal.CanCasuallyInteractNow() &&
                    (animal.roping == null || !animal.roping.IsRopedByPawn))
                {
                    shearableAnimals.Add(animal);
                }
            }

            // Convert to Things for the base class caching system
            foreach (Pawn animal in shearableAnimals)
            {
                yield return animal;
            }
        }

        #endregion

        #region Job Processing

        /// <summary>
        /// Check if an animal is valid for shearing
        /// </summary>
        protected override bool IsValidGatherableAnimal(Pawn animal, Pawn handler, bool forced)
        {
            // Fast-fail conditions first
            if (animal == null || handler == null || animal == handler)
                return false;

            if (animal.Destroyed || !animal.Spawned || animal.Dead || animal.Downed)
                return false;

            // More expensive checks second
            if (!base.IsValidGatherableAnimal(animal, handler, forced))
                return false;

            // Shear-specific check
            CompShearable comp = animal.TryGetComp<CompShearable>();
            return comp != null && comp.ActiveAndFull;
        }

        /// <summary>
        /// Create a job specifically for shearing an animal
        /// </summary>
        protected override Job CreateJobForAnimal(Pawn pawn, Pawn animal, bool forced)
        {
            if (!IsValidGatherableAnimal(animal, pawn, forced))
                return null;

            Job job = JobMaker.MakeJob(WorkJobDef, animal);

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to shear {animal.LabelShort}");
            }

            return job;
        }

        #endregion

        #region Reset

        /// <summary>
        /// Reset the shearing-specific cache
        /// </summary>
        public override void Reset()
        {
            // The parent class Reset() already handles clearing all caches
            base.Reset();

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Reset caches for {this.GetType().Name}");
            }
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return "JobGiver_Handling_Shear_PawnControl";
        }

        #endregion
    }
}