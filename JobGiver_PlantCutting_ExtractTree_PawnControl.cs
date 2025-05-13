using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobGiver that assigns tree extraction tasks to pawns with the PlantCutting work tag.
    /// This allows non-humanlike pawns to extract trees with the ExtractTree designation.
    /// </summary>
    public class JobGiver_PlantCutting_ExtractTree_PawnControl : JobGiver_Common_RemoveBuilding_PawnControl
    {
        #region Configuration

        /// <summary>
        /// Override WorkTag to specify "PlantCutting" work type
        /// </summary>
        public override string WorkTag => "PlantCutting";

        /// <summary>
        /// Whether this job requires specific tag for non-humanlike pawns
        /// </summary>
        public override PawnEnumTags RequiredTag => PawnEnumTags.AllowWork_PlantCutting;

        /// <summary>
        /// Use ExtractTree designation
        /// </summary>
        protected override DesignationDef TargetDesignation => DesignationDefOf.ExtractTree;

        /// <summary>
        /// Use ExtractTree job
        /// </summary>
        protected override JobDef WorkJobDef => JobDefOf.ExtractTree;

        /// <summary>
        /// Human-readable name for debug logging
        /// </summary>
        protected override string DebugName => "ExtractTree";

        /// <summary>
        /// Cache update interval - plants change less often
        /// </summary>
        protected override int CacheUpdateInterval => 300; // 5 seconds

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_PlantCutting_ExtractTree_PawnControl() : base()
        {
            // Base constructor already initializes the cache system
        }

        #endregion

        #region Core Flow

        /// <summary>
        /// Define priority level for tree extraction work
        /// </summary>
        protected override float GetBasePriority(string workTag)
        {
            return 5.8f;
        }

        #endregion

        #region Target Selection

        /// <summary>
        /// Job-specific cache update method for tree extraction targets
        /// </summary>
        protected override IEnumerable<Thing> UpdateJobSpecificCache(Map map)
        {
            // Use the base implementation but filter for plants
            foreach (Thing thing in base.UpdateJobSpecificCache(map))
            {
                if (thing is Plant)
                {
                    yield return thing;
                }
            }
        }

        /// <summary>
        /// Gets all plants with the ExtractTree designation
        /// </summary>
        protected override IEnumerable<Thing> GetRemovalTargets(Map map)
        {
            // Use the base implementation but filter for plants
            foreach (Thing thing in base.GetRemovalTargets(map))
            {
                if (thing is Plant)
                {
                    yield return thing;
                }
            }
        }

        #endregion

        #region Faction Validation

        /// <summary>
        /// Determines if the pawn's faction is allowed to perform this work
        /// Can be overridden by derived classes to customize faction rules
        /// </summary>
        protected override bool IsValidFactionForConstruction(Pawn pawn)
        {
            // PlantCutting tasks have similar faction requirements to construction
            return Utility_JobGiverManager.IsValidFactionInteraction(null, pawn, RequiresPlayerFaction);
        }

        #endregion

        #region Thing-Based Helpers

        /// <summary>
        /// Basic validation for plant extraction targets
        /// </summary>
        protected override bool ValidateConstructionTarget(Thing thing, Pawn pawn, bool forced = false)
        {
            // Use base validation from RemoveBuilding parent
            if (!base.ValidateConstructionTarget(thing, pawn, forced))
                return false;

            // Must be a plant
            if (!(thing is Plant plant))
                return false;

            // Check plant skill requirements
            if (plant.def.plant.sowMinSkill > 0)
            {
                int plantSkill = pawn.skills?.GetSkill(SkillDefOf.Plants)?.Level ?? 0;
                if (plantSkill < plant.def.plant.sowMinSkill)
                {
                    JobFailReason.Is("UnderAllowedSkill".Translate(plant.def.plant.sowMinSkill));
                    return false;
                }
            }

            return true;
        }

        #endregion

        #region Job Creation

        /// <summary>
        /// Creates a construction job for extracting trees
        /// </summary>
        protected override Job CreateConstructionJob(Pawn pawn, bool forced)
        {
            // Use the base implementation that creates jobs for designated targets
            return base.CreateConstructionJob(pawn, forced);
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
            return "JobGiver_PlantCutting_ExtractTree_PawnControl";
        }

        #endregion
    }
}