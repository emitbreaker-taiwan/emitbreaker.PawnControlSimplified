using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// JobModule for handling tasks at campfires with bills.
    /// </summary>
    public class JobModule_Hauling_HaulCampfire : JobModule_Hauling
    {
        public override string UniqueID => "HaulCampfire";
        public override float Priority => 5.4f; // Same priority as the original JobGiver
        public override string Category => "CookingHauling"; // Added category for consistency
        public override int CacheUpdateInterval => 150; // Update every 2.5 seconds

        // Static caches for map-specific data persistence
        private static readonly Dictionary<int, List<Thing>> _campfireCache = new Dictionary<int, List<Thing>>();
        private static readonly Dictionary<int, Dictionary<Thing, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Thing, bool>>();
        private static int _lastCacheUpdateTick = -999;

        // Cache to optimize lookups
        private static readonly Dictionary<int, Dictionary<Thing, Building_WorkTable>> _workTableMap = new Dictionary<int, Dictionary<Thing, Building_WorkTable>>();
        private static List<ThingDef> _cachedCampfireDefs;

        // These ThingRequestGroups are what this module cares about
        public override HashSet<ThingRequestGroup> RelevantThingRequestGroups =>
            new HashSet<ThingRequestGroup> { ThingRequestGroup.BuildingArtificial };

        // Get WorkGiverDef for Campfire
        private static WorkGiverDef CampfireWorkGiver()
        {
            WorkGiverDef workGiver = Utility_Common.WorkGiverDefNamed("DoBillsHaulCampfire");
            if (workGiver == null)
            {
                Utility_DebugManager.LogError("WorkGiverDef DoBillsHaulCampfire not found.");
            }
            return workGiver;
        }

        /// <summary>
        /// Gets the list of campfire defs to look for
        /// </summary>
        private static List<ThingDef> GetCampfireDefs()
        {
            // Cache the defs list to avoid repeated lookups
            if (_cachedCampfireDefs != null && _cachedCampfireDefs.Count > 0)
                return _cachedCampfireDefs;

            _cachedCampfireDefs = new List<ThingDef>();

            // Try to get from workgiver first
            var workGiver = CampfireWorkGiver();
            if (workGiver?.fixedBillGiverDefs != null && workGiver.fixedBillGiverDefs.Count > 0)
            {
                _cachedCampfireDefs.AddRange(workGiver.fixedBillGiverDefs);
            }
            else
            {
                // Fallback to using Campfire if for some reason we can't get the defs from workgiver
                ThingDef fallbackDef = ThingDef.Named("Campfire");
                if (fallbackDef != null)
                {
                    _cachedCampfireDefs.Add(fallbackDef);
                }

                Utility_DebugManager.LogWarning("Could not find DoBillsHaulCampfire WorkGiverDef defs, using fallback method for campfires");
            }

            return _cachedCampfireDefs;
        }

        public override void UpdateCache(Map map, List<Thing> targetCache)
        {
            base.UpdateCache(map, targetCache);

            if (map == null) return;
            int mapId = map.uniqueID;
            bool hasTargets = false;

            int currentTick = Find.TickManager.TicksGame;

            // Initialize caches if needed
            if (!_campfireCache.ContainsKey(mapId))
                _campfireCache[mapId] = new List<Thing>();

            if (!_reachabilityCache.ContainsKey(mapId))
                _reachabilityCache[mapId] = new Dictionary<Thing, bool>();

            if (!_workTableMap.ContainsKey(mapId))
                _workTableMap[mapId] = new Dictionary<Thing, Building_WorkTable>();

            // Use the base class's progressive cache update
            UpdateCacheProgressively(
                map,
                targetCache,
                ref _lastCacheUpdateTick,
                RelevantThingRequestGroups,
                thing => {
                    // Skip non-WorkTables
                    Building_WorkTable workTable = thing as Building_WorkTable;
                    if (workTable == null || !workTable.Spawned)
                        return false;

                    // Check if it's a valid campfire type
                    List<ThingDef> campfireDefs = GetCampfireDefs();
                    if (!campfireDefs.Contains(workTable.def))
                        return false;

                    // Check if it has active bills
                    if (workTable.BillStack == null || !workTable.BillStack.AnyShouldDoNow)
                        return false;

                    // Update the lookup table
                    _workTableMap[mapId][thing] = workTable;
                    return true;
                },
                _campfireCache,
                CacheUpdateInterval
            );

            SetHasTargets(map, hasTargets || (targetCache != null && targetCache.Count > 0));
        }

        public override bool ShouldHaulItem(Thing item, Map map)
        {
            if (item == null || map == null || !item.Spawned)
                return false;

            int mapId = map.uniqueID;

            // Check from cache first for better performance
            if (_campfireCache.ContainsKey(mapId) && _campfireCache[mapId].Contains(item))
                return true;

            try
            {
                // For bill givers, we need to check if it's a campfire with bills
                Building_WorkTable workTable = item as Building_WorkTable;
                if (workTable == null || !workTable.Spawned)
                    return false;

                // Ensure it's a campfire or appropriate bill giver
                List<ThingDef> campfireDefs = GetCampfireDefs();
                if (!campfireDefs.Contains(workTable.def))
                    return false;

                return workTable.BillStack != null && workTable.BillStack.AnyShouldDoNow;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in ShouldHaulItem for campfire: {ex}");
                return false;
            }
        }

        public override bool ValidateHaulingJob(Thing target, Pawn hauler)
        {
            if (target == null || hauler == null || !target.Spawned || !hauler.Spawned)
                return false;

            try
            {
                Building_WorkTable workTable = target as Building_WorkTable;
                if (workTable == null)
                    return false;

                // Check faction interaction
                if (!Utility_JobGiverManager.IsValidFactionInteraction(target, hauler, requiresDesignator: false))
                    return false;

                if (target.IsForbidden(hauler))
                    return false;

                // Check if pawn can reach the campfire
                if (!hauler.CanReserve(target, 1, -1) ||
                    !hauler.CanReach(workTable, PathEndMode.InteractionCell, hauler.NormalMaxDanger()))
                    return false;

                // Check if there are bills that this pawn can do
                bool canDoBill = false;
                foreach (Bill bill in workTable.BillStack)
                {
                    if (bill.ShouldDoNow() && bill.PawnAllowedToStartAnew(hauler))
                    {
                        canDoBill = true;
                        break;
                    }
                }

                return canDoBill;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error validating campfire job: {ex}");
                return false;
            }
        }

        protected override Job CreateHaulingJob(Pawn hauler, Thing target)
        {
            try
            {
                if (target == null || hauler == null)
                    return null;

                Building_WorkTable workTable = target as Building_WorkTable;
                if (workTable == null)
                    return null;

                // Use standard bill work logic from WorkGiver_DoBill
                foreach (Bill bill in workTable.BillStack)
                {
                    if (!bill.ShouldDoNow() || !bill.PawnAllowedToStartAnew(hauler))
                        continue;

                    // Try to find ingredients based on the bill requirements
                    Job job = TryStartNewDoBillJob(hauler, workTable, bill);
                    if (job != null)
                    {
                        Utility_DebugManager.LogNormal($"{hauler.LabelShort} created job to work on {workTable.LabelCap} for bill {bill.LabelCap}");
                        return job;
                    }
                }
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error creating campfire hauling job: {ex}");
            }

            return null;
        }

        /// <summary>
        /// Creates a job to work on a specific bill, similar to WorkGiver_DoBill.StartBillJob
        /// </summary>
        private Job TryStartNewDoBillJob(Pawn pawn, IBillGiver giver, Bill bill)
        {
            try
            {
                // Find required ingredients for the bill
                Job job = WorkGiverUtility.HaulStuffOffBillGiverJob(pawn, giver, null);
                if (job != null)
                {
                    return job;
                }

                if (bill.recipe.ingredients.Count == 0)
                {
                    Job job2 = JobMaker.MakeJob(JobDefOf.DoBill, (Thing)giver);
                    job2.targetQueueB = new List<LocalTargetInfo>();
                    job2.countQueue = new List<int>();
                    job2.haulMode = HaulMode.ToCellNonStorage;
                    job2.bill = bill;
                    return job2;
                }

                // Find and gather ingredients
                List<Thing> ingredientsLookup = new List<Thing>();
                if (!TryFindBestBillIngredients(bill, pawn, (Thing)giver, ingredientsLookup))
                {
                    if (FloatMenuMakerMap.makingFor == pawn)
                    {
                        JobFailReason.Is("MissingMaterials".Translate());
                    }
                    return null;
                }

                Job job3 = JobMaker.MakeJob(JobDefOf.DoBill, (Thing)giver);
                job3.targetQueueB = new List<LocalTargetInfo>(ingredientsLookup.Count);
                job3.countQueue = new List<int>(ingredientsLookup.Count);

                // Simplified count handling without accessing ingredientValueGetterMode
                for (int i = 0; i < ingredientsLookup.Count; i++)
                {
                    job3.targetQueueB.Add(ingredientsLookup[i]);
                    job3.countQueue.Add(ingredientsLookup[i].stackCount);
                }

                job3.haulMode = HaulMode.ToCellNonStorage;
                job3.bill = bill;

                return job3;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error in TryStartNewDoBillJob: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Finds the best ingredients for a bill using standard RimWorld methods
        /// </summary>
        private bool TryFindBestBillIngredients(Bill bill, Pawn pawn, Thing billGiver, List<Thing> chosen)
        {
            if (bill.recipe.ingredients.Count == 0)
            {
                return true;
            }

            try
            {
                // Use RimWorld's standard bill ingredient finding logic
                // Using an alternative approach since TryFindBestIngredientsHelper might not be available
                foreach (IngredientCount ingredient in bill.recipe.ingredients)
                {
                    bool found = false;

                    // Try to find ingredients the pawn can access
                    List<Thing> availableThings = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver)
                        .Where(t => !t.IsForbidden(pawn) &&
                                   pawn.CanReserve(t) &&
                                   ingredient.filter.Allows(t) &&
                                   pawn.CanReach(t, PathEndMode.Touch, pawn.NormalMaxDanger()))
                        .OrderBy(t => (t.Position - billGiver.Position).LengthHorizontalSquared)
                        .ToList();

                    foreach (Thing thing in availableThings)
                    {
                        if (thing.def.IsStuff && thing.def.stuffProps.CanMake(bill.recipe.ProducedThingDef))
                        {
                            chosen.Add(thing);
                            found = true;
                            break;
                        }
                    }

                    if (!found && ingredient.IsFixedIngredient)
                    {
                        // For fixed ingredients, try to find any that match the filter
                        Thing bestThing = availableThings.FirstOrDefault();
                        if (bestThing != null)
                        {
                            chosen.Add(bestThing);
                            found = true;
                        }
                    }

                    if (!found)
                    {
                        return false; // Failed to find all ingredients
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Utility_DebugManager.LogWarning($"Error finding bill ingredients: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public override void ResetStaticData()
        {
            Utility_CacheManager.ResetJobGiverCache(_campfireCache, _reachabilityCache);

            // Clear the work table mapping
            foreach (var mapDict in _workTableMap.Values)
            {
                mapDict.Clear();
            }
            _workTableMap.Clear();

            // Reset cached defs
            _cachedCampfireDefs = null;

            _lastCacheUpdateTick = -999;
        }
    }
}