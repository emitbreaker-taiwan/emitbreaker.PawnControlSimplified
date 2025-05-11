using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Base JobGiver for feeding patients. Handles common logic for all patient feeding jobs.
    /// Can be specialized through inheritance to target specific patient types.
    /// </summary>
    public abstract class JobGiver_Common_FeedPatient_PawnControl : JobGiver_Scan_PawnControl
    {
        #region Configuration

        // Cache for hungry pawns to improve performance
        private static readonly Dictionary<int, List<Pawn>> _hungryPawnCache = new Dictionary<int, List<Pawn>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 180; // Update every 3 seconds

        // Distance thresholds for bucketing
        protected static readonly float[] DISTANCE_THRESHOLDS = new float[] { 100f, 400f, 900f }; // 10, 20, 30 tiles

        #endregion

        #region Override Configuration

        // Configuration to be overridden by subclasses
        protected virtual bool FeedHumanlikesOnly => true;
        protected virtual bool FeedAnimalsOnly => false;
        protected virtual bool FeedPrisonersOnly => false;
        protected override string WorkTag => "Doctor";
        protected virtual float JobPriority => 8.0f; // High priority - people shouldn't starve
        protected virtual string JobDescription => "feed patient";

        /// <summary>
        /// Override cache interval for feeding jobs
        /// </summary>
        protected override int CacheUpdateInterval => CACHE_UPDATE_INTERVAL;

        #endregion

        #region Overrides

        public override float GetPriority(Pawn pawn)
        {
            return JobPriority;
        }

        /// <summary>
        /// Override to return hungry pawns as targets
        /// </summary>
        protected override IEnumerable<Thing> GetTargets(Map map)
        {
            UpdateHungryPawnCache(map);
            int mapId = map.uniqueID;

            if (_hungryPawnCache.ContainsKey(mapId) && _hungryPawnCache[mapId].Count > 0)
                return _hungryPawnCache[mapId].Cast<Thing>();

            return new List<Thing>();
        }

        /// <summary>
        /// Main job creation method, uses the CreateFeedJob helper
        /// </summary>
        protected override Job TryGiveJob(Pawn pawn)
        {
            return CreateFeedJob<JobGiver_Common_FeedPatient_PawnControl>(pawn);
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Generic helper method to create a patient feeding job that can be used by all subclasses
        /// </summary>
        /// <typeparam name="T">The specific JobGiver subclass type</typeparam>
        /// <param name="pawn">The pawn that will perform the feeding job</param>
        /// <returns>A job to feed a patient, or null if no valid job could be created</returns>
        protected Job CreateFeedJob<T>(Pawn pawn) where T : JobGiver_Common_FeedPatient_PawnControl
        {
            // IMPORTANT: Only player pawns and slaves owned by player should feed patients
            if (pawn.Faction != Faction.OfPlayer &&
                !(pawn.IsSlave && pawn.HostFaction == Faction.OfPlayer))
                return null;

            return Utility_JobGiverManager.StandardTryGiveJob<T>(
                pawn,
                WorkTag,
                (p, forced) => {
                    // Make sure cache is fresh
                    UpdateHungryPawnCache(p.Map);

                    if (p?.Map == null) return null;

                    int mapId = p.Map.uniqueID;
                    if (!_hungryPawnCache.ContainsKey(mapId) || _hungryPawnCache[mapId].Count == 0)
                        return null;

                    // Use JobGiverManager for distance bucketing
                    var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                        p,
                        _hungryPawnCache[mapId],
                        (patient) => (patient.Position - p.Position).LengthHorizontalSquared,
                        DISTANCE_THRESHOLDS
                    );

                    // Find the best patient to feed
                    Pawn targetPatient = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                        buckets,
                        p,
                        (patient, feeder) => {
                            // Skip if no longer a valid hungry patient
                            if (!FeedPatientUtility.IsHungry(patient) || !FeedPatientUtility.ShouldBeFed(patient))
                                return false;

                            // Skip if forbidden or can't reserve
                            if (patient.IsForbidden(feeder) || !feeder.CanReserve(patient, 1, -1, null, forced))
                                return false;

                            // Check food restriction policy
                            if (patient.foodRestriction != null)
                            {
                                FoodPolicy respectedRestriction = patient.foodRestriction.GetCurrentRespectedRestriction(feeder);
                                if (respectedRestriction != null && respectedRestriction.filter.AllowedDefCount == 0)
                                    return false;
                            }

                            // Check if there's food available for this patient
                            Thing foodSource;
                            ThingDef foodDef;
                            bool starving = patient.needs?.food?.CurCategory == HungerCategory.Starving;
                            return FoodUtility.TryFindBestFoodSourceFor(feeder, patient, starving,
                                out foodSource, out foodDef, false, canUsePackAnimalInventory: true, allowVenerated: true);
                        },
                        _reachabilityCache
                    );

                    // Create job if target found
                    if (targetPatient != null)
                    {
                        // Find the best food source
                        Thing foodSource;
                        ThingDef foodDef;
                        bool starving = targetPatient.needs?.food?.CurCategory == HungerCategory.Starving;

                        if (FoodUtility.TryFindBestFoodSourceFor(p, targetPatient, starving,
                            out foodSource, out foodDef, false, canUsePackAnimalInventory: true, allowVenerated: true))
                        {
                            float nutrition = FoodUtility.GetNutrition(targetPatient, foodSource, foodDef);
                            Job job = JobMaker.MakeJob(JobDefOf.FeedPatient);
                            job.targetA = foodSource;
                            job.targetB = targetPatient;
                            job.count = FoodUtility.WillIngestStackCountOf(targetPatient, foodDef, nutrition);

                            if (Prefs.DevMode)
                            {
                                string foodInfo = "";
                                if (FoodUtility.MoodFromIngesting(targetPatient, foodSource, FoodUtility.GetFinalIngestibleDef(foodSource)) < 0)
                                    foodInfo = " (disliked food)";

                                Utility_DebugManager.LogNormal($"{p.LabelShort} created job to feed {targetPatient.LabelShort}{foodInfo}");
                            }

                            return job;
                        }
                    }

                    return null;
                },
                debugJobDesc: JobDescription);
        }

        /// <summary>
        /// Updates the cache of hungry pawns that need to be fed
        /// </summary>
        protected virtual void UpdateHungryPawnCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CacheUpdateInterval ||
                !_hungryPawnCache.ContainsKey(mapId))
            {
                // Clear outdated cache
                if (_hungryPawnCache.ContainsKey(mapId))
                    _hungryPawnCache[mapId].Clear();
                else
                    _hungryPawnCache[mapId] = new List<Pawn>();

                // Clear reachability cache too
                if (_reachabilityCache.ContainsKey(mapId))
                    _reachabilityCache[mapId].Clear();
                else
                    _reachabilityCache[mapId] = new Dictionary<Pawn, bool>();

                // Find all hungry pawns that need feeding
                foreach (Pawn potentialPatient in map.mapPawns.AllPawnsSpawned)
                {
                    // Skip pawns that don't match our criteria
                    if (ShouldSkipPawn(potentialPatient))
                        continue;

                    // Check if hungry and should be fed
                    if (!FeedPatientUtility.IsHungry(potentialPatient) ||
                        !FeedPatientUtility.ShouldBeFed(potentialPatient))
                        continue;

                    // Check prisoner status if needed
                    if (FeedPrisonersOnly && !potentialPatient.IsPrisoner)
                        continue;

                    if (!FeedPrisonersOnly && potentialPatient.IsPrisoner)
                        continue;

                    // Add to the cache
                    _hungryPawnCache[mapId].Add(potentialPatient);
                }

                _lastCacheUpdateTick = currentTick;
            }
        }

        /// <summary>
        /// Determines if a pawn should be skipped based on race and other criteria
        /// </summary>
        protected virtual bool ShouldSkipPawn(Pawn pawn)
        {
            // Check for null pawn
            if (pawn == null) return true;

            // Skip babies - they're handled differently
            if (pawn.DevelopmentalStage.Baby()) return true;

            // Apply species filters
            if (FeedHumanlikesOnly && !pawn.RaceProps.Humanlike) return true;
            if (FeedAnimalsOnly && !pawn.IsNonMutantAnimal) return true;

            // Skip ourselves - can't feed yourself
            if (pawn == Current.Game.CurrentMap?.mapPawns?.FreeColonistsSpawned.FirstOrDefault(p => p.CurJob?.def.defName == "FeedPatient"))
                return true;

            return false;
        }

        #endregion

        #region Utility

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetFeedPatientCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_hungryPawnCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_Common_FeedPatient_PawnControl";
        }

        #endregion
    }
}