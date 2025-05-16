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
    /// Base class for job givers that process bills at workstations.
    /// Can be reused for Crafting, Smithing, Tailoring, Art, Cooking, Doctor work.
    /// </summary>
    public abstract class JobGiver_Common_DoBill_PawnControl : JobGiver_Crafting_PawnControl
    {
        #region Configuration

        /// <summary>
        /// The fixed bill giver defs this job giver can work with, if any
        /// </summary>
        protected override List<ThingDef> FixedBillGiverDefs => null;

        /// <summary>
        /// Should we check for pawns as bill givers?
        /// </summary>
        protected override bool BillGiversAllHumanlikes => false;

        /// <summary>
        /// Should we check for mechanoids as bill givers?
        /// </summary>
        protected override bool BillGiversAllMechanoids => false;

        /// <summary>
        /// Should we check for animals as bill givers?
        /// </summary>
        protected override bool BillGiversAllAnimals => false;

        /// <summary>
        /// Should we check for humanlike corpses as bill givers?
        /// </summary>
        protected override bool BillGiversAllHumanlikesCorpses => false;

        /// <summary>
        /// Should we check for mechanoid corpses as bill givers?
        /// </summary>
        protected override bool BillGiversAllMechanoidsCorpses => false;

        /// <summary>
        /// Should we check for animal corpses as bill givers?
        /// </summary>
        protected override bool BillGiversAllAnimalsCorpses => false;

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor that initializes the cache system
        /// </summary>
        public JobGiver_Common_DoBill_PawnControl() : base()
        {
            // Base constructor already initializes the cache
        }

        #endregion

        #region Faction Validation

        /// <summary>
        /// Determines if a pawn's faction is allowed to perform bill work
        /// Can be overridden by derived classes to customize faction rules
        /// </summary>
        protected override bool IsValidFactionForCrafting(Pawn pawn)
        {
            // Only player pawns and player's slaves can process bills by default
            return pawn != null && (pawn.Faction == Faction.OfPlayer ||
                   (pawn.IsSlave && pawn.HostFaction == Faction.OfPlayer));
        }

        #endregion

        #region Core Flow

        /// <summary>
        /// Checks if the map meets requirements for bill processing
        /// </summary>
        protected virtual bool AreMapRequirementsMet(Pawn pawn)
        {
            // For bill processing, we just need a valid map
            return pawn?.Map != null;
        }

        #endregion

        #region Target selection

        /// <summary>
        /// Gets all bill givers that can be used by this job giver
        /// </summary>
        protected virtual IEnumerable<Thing> GetBillGivers(Map map)
        {
            return GetTargets(map);
        }

        #endregion

        #region Cache management

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetDoBillsCache()
        {
            // Uses the parent class's Reset method, which calls the centralized cache system
        }

        #endregion
    }
}