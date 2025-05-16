using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Abstract base class for all building removal JobGivers in PawnControl.
    /// This allows non-humanlike pawns to remove buildings with appropriate designation.
    /// </summary>
    public abstract class JobGiver_Common_RemoveBuilding_PawnControl : JobGiver_Construction_PawnControl
    {
        #region Configuration

        // Must be implemented by subclasses to specify which designation to target
        protected abstract override DesignationDef TargetDesignation { get; }

        // Must be implemented by subclasses to specify which job to use for removal
        protected abstract override JobDef WorkJobDef { get; }

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        public override bool RequiresDesignator => true;

        /// <summary>
        /// Whether this job giver requires a designator to operate (zone designation, etc.)
        /// Most cleaning jobs require designators so default is true
        /// </summary>
        public override bool RequiresMapZoneorArea => true;

        /// <summary>
        /// Whether this job giver requires player faction (always true for designations)
        /// </summary>
        public override bool RequiresPlayerFaction => true;

        /// <summary>
        /// Override cache interval - slightly longer than default since designations don't change as frequently
        /// </summary>
        protected override int CacheUpdateInterval => base.CacheUpdateInterval;

        /// <summary>
        /// Override debug name for better logging
        /// </summary>
        protected override string DebugName => $"RemoveBuilding({TargetDesignation?.defName ?? "null"})";

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_Common_RemoveBuilding_PawnControl() : base()
        {
            // Base constructor already initializes the cache system
        }

        #endregion

        #region Faction Validation

        /// <summary>
        /// Determines if the pawn's faction is allowed to perform construction work
        /// Override the base method to implement custom faction validation for removal jobs
        /// </summary>
        protected override bool IsValidFactionForConstruction(Pawn pawn)
        {
            // Since designations can only be issued by player, only player pawns, 
            // player's mechanoids or player's slaves should perform them
            return Utility_JobGiverManager.IsValidFactionInteraction(null, pawn, RequiresPlayerFaction);
        }

        /// <summary>
        /// Legacy method for backward compatibility - now redirecting to the new method name
        /// </summary>
        [System.Obsolete("Use IsValidFactionForConstruction instead")]
        protected virtual bool IsValidFactionForRemoval(Pawn pawn)
        {
            return IsValidFactionForConstruction(pawn);
        }

        /// <summary>
        /// Legacy method for backward compatibility - now redirecting to the new method name
        /// </summary>
        [System.Obsolete("Use IsValidFactionForConstruction instead")]
        protected virtual bool IsPawnValidFaction(Pawn pawn)
        {
            return IsValidFactionForConstruction(pawn);
        }

        #endregion

        #region Core Flow

        /// <summary>
        /// Checks if the map meets requirements for this removal job
        /// </summary>
        protected override bool AreMapRequirementsMet(Pawn pawn)
        {
            // Check if map has the required designations
            return pawn?.Map != null &&
                   TargetDesignation != null &&
                   pawn.Map.designationManager.AnySpawnedDesignationOfDef(TargetDesignation);
        }

        #endregion

        #region Target Selection

        /// <summary>
        /// Job-specific cache update method that implements specialized removal target collection
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            return GetRemovalTargets(map);
        }

        /// <summary>
        /// Get targets from the map based on designations
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            // Default implementation now calls the specialized method
            return GetRemovalTargets(map);
        }

        /// <summary>
        /// Gets all designated buildings that need removal
        /// </summary>
        protected virtual IEnumerable<Thing> GetRemovalTargets(Map map)
        {
            if (map == null || TargetDesignation == null)
                return Enumerable.Empty<Thing>();

            // Find all designated things for removal
            var targets = new List<Thing>();
            var designations = map.designationManager.SpawnedDesignationsOfDef(TargetDesignation);
            foreach (Designation designation in designations)
            {
                Thing thing = designation.target.Thing;
                if (thing != null && thing.Spawned)
                {
                    targets.Add(thing);

                    // Limit collection size for performance
                    if (targets.Count >= 100)
                        break;
                }
            }

            return targets;
        }

        #endregion

        #region Job Creation

        /// <summary>
        /// Creates a construction job for the pawn
        /// </summary>
        protected override Job CreateConstructionJob(Pawn pawn, bool forced)
        {
            if (pawn?.Map == null)
                return null;

            int mapId = pawn.Map.uniqueID;

            // Get targets from the cache
            List<Thing> targets = GetCachedTargets(mapId);

            // If cache is empty or not yet populated
            if (targets == null || targets.Count == 0)
            {
                // Try to update cache if needed
                if (ShouldUpdateCache(mapId))
                {
                    UpdateCache(mapId, pawn.Map);
                    targets = GetCachedTargets(mapId);
                }

                // If still empty, get targets directly
                if (targets == null || targets.Count == 0)
                {
                    targets = GetRemovalTargets(pawn.Map).ToList();
                }
            }

            if (targets.Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                targets,
                (thing) => (thing.Position - pawn.Position).LengthHorizontalSquared,
                DistanceThresholds
            );

            // Find the best target to remove using the ValidateTarget method
            Thing bestTarget = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (thing, validator) => ValidateConstructionTarget(thing, validator, forced),
                null
            );

            // Create job if target found
            if (bestTarget != null)
            {
                Job job = JobMaker.MakeJob(WorkJobDef, bestTarget);
                Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to {WorkJobDef.defName} {bestTarget.LabelCap}");
                return job;
            }

            return null;
        }

        #endregion

        #region Thing-Based Helpers

        /// <summary>
        /// Basic validation for construction target things
        /// Override the base implementation to add specialized validation for removal targets
        /// </summary>
        protected override bool ValidateConstructionTarget(Thing thing, Pawn pawn, bool forced = false)
        {
            // IMPORTANT: Check faction interaction validity first
            if (!Utility_JobGiverManager.IsValidFactionInteraction(thing, pawn, RequiresPlayerFaction))
                return false;

            // Skip if no longer valid
            if (thing == null || thing.Destroyed || !thing.Spawned)
                return false;

            // Skip if no longer designated
            if (thing.Map.designationManager.DesignationOn(thing, TargetDesignation) == null)
                return false;

            // Check for timed explosives - avoid removing things about to explode
            CompExplosive explosive = thing.TryGetComp<CompExplosive>();
            if (explosive != null && explosive.wickStarted)
                return false;

            // Skip if forbidden or unreachable
            if (thing.IsForbidden(pawn) ||
                !pawn.CanReserve(thing, 1, -1, null, forced) ||
                !pawn.CanReach(thing, PathEndMode.Touch, Danger.Some))
                return false;

            return true;
        }

        #endregion

        #region Utility

        public override string ToString()
        {
            return $"JobGiver_RemoveBuilding_PawnControl({TargetDesignation?.defName ?? "null"})";
        }

        #endregion

        #region Reset

        /// <summary>
        /// Reset the cache - implements IResettableCache
        /// </summary>
        public override void Reset()
        {
            // Use centralized cache reset
            base.Reset();
        }

        #endregion
    }
}