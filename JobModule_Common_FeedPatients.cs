using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Common abstract base class for modules that feed hungry patients.
    /// Can be used by both Doctor and Handling job modules for feeding patients.
    /// </summary>
    public abstract class JobModule_Common_FeedPatients : JobModuleCore
    {
        // Cache for hungry pawns to improve performance
        protected static readonly Dictionary<int, List<Pawn>> _hungryPawnCache = new Dictionary<int, List<Pawn>>();
        protected static readonly Dictionary<int, Dictionary<Pawn, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        protected static int _lastCacheUpdateTick = -999;
        protected const int CACHE_UPDATE_INTERVAL = 180; // Update every 3 seconds

        // Distance thresholds for bucketing
        protected static readonly float[] DISTANCE_THRESHOLDS = new float[] { 100f, 400f, 900f }; // 10, 20, 30 tiles

        // Configuration to be overridden by subclasses
        protected virtual bool FeedHumanlikesOnly => true;
        protected virtual bool FeedAnimalsOnly => false;
        protected virtual bool FeedPrisonersOnly => false;

        /// <summary>
        /// Initialize or reset caches
        /// </summary>
        public override void ResetStaticData()
        {
            // Clear caches
            _hungryPawnCache.Clear();
            _reachabilityCache.Clear();
            _lastCacheUpdateTick = -999;
        }

        /// <summary>
        /// Check if this pawn needs to be fed
        /// Implementation shared by both Doctor and Handling modules
        /// </summary>
        public bool ShouldProcessTarget(Pawn patient, Map map)
        {
            if (patient == null || !patient.Spawned || map == null) return false;

            // Skip pawns that don't match our criteria
            if (ShouldSkipPawn(patient))
                return false;

            // Check if hungry and should be fed
            if (!FeedPatientUtility.IsHungry(patient) || !FeedPatientUtility.ShouldBeFed(patient))
                return false;

            // Check prisoner status if needed
            if (FeedPrisonersOnly && !patient.IsPrisoner)
                return false;

            if (!FeedPrisonersOnly && patient.IsPrisoner)
                return false;

            return true;
        }

        /// <summary>
        /// Check if the feeder can feed this pawn
        /// Implementation shared by both Doctor and Handling modules
        /// </summary>
        public bool ValidateFeedingJob(Pawn patient, Pawn feeder)
        {
            if (patient == null || feeder == null || !patient.Spawned || !feeder.Spawned)
                return false;

            // Skip if patient is forbidden or can't reserve
            if (patient.IsForbidden(feeder) || !feeder.CanReserve(patient, 1, -1, null, false))
                return false;

            // Skip if patient is no longer hungry or shouldn't be fed
            if (!FeedPatientUtility.IsHungry(patient) || !FeedPatientUtility.ShouldBeFed(patient))
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
        }

        /// <summary>
        /// Create a job to feed a patient
        /// Implementation shared by both Doctor and Handling modules
        /// </summary>
        public Job CreateFeedingJob(Pawn feeder, Pawn patient)
        {
            if (patient == null || feeder == null || !patient.Spawned || !feeder.Spawned)
                return null;

            // Find the best food source
            Thing foodSource;
            ThingDef foodDef;
            bool starving = patient.needs?.food?.CurCategory == HungerCategory.Starving;

            if (FoodUtility.TryFindBestFoodSourceFor(feeder, patient, starving,
                out foodSource, out foodDef, false, canUsePackAnimalInventory: true, allowVenerated: true))
            {
                float nutrition = FoodUtility.GetNutrition(patient, foodSource, foodDef);
                Job job = JobMaker.MakeJob(JobDefOf.FeedPatient);
                job.targetA = foodSource;
                job.targetB = patient;
                job.count = FoodUtility.WillIngestStackCountOf(patient, foodDef, nutrition);

                if (Prefs.DevMode)
                {
                    string foodInfo = "";
                    if (FoodUtility.MoodFromIngesting(patient, foodSource, FoodUtility.GetFinalIngestibleDef(foodSource)) < 0)
                        foodInfo = " (disliked food)";

                    Utility_DebugManager.LogNormal($"{feeder.LabelShort} created job to feed {patient.LabelShort}{foodInfo}");
                }

                return job;
            }

            return null;
        }

        /// <summary>
        /// Updates the cache of hungry pawns that need to be fed
        /// </summary>
        protected void UpdateHungryPawnCache(Map map)
        {
            if (map == null) return;

            int currentTick = Find.TickManager.TicksGame;
            int mapId = map.uniqueID;

            if (currentTick > _lastCacheUpdateTick + CACHE_UPDATE_INTERVAL ||
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
                    if (ShouldProcessTarget(potentialPatient, map))
                    {
                        _hungryPawnCache[mapId].Add(potentialPatient);
                    }
                }

                _lastCacheUpdateTick = currentTick;
                
                // Record whether we found any targets
                SetHasTargets(map, _hungryPawnCache[mapId].Count > 0);
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
            if (FeedAnimalsOnly && !pawn.RaceProps.Animal) return true;

            // Skip ourselves - can't feed yourself
            if (pawn == Current.Game.CurrentMap?.mapPawns?.FreeColonistsSpawned.FirstOrDefault(p => p.CurJob?.def.defName == "FeedPatient"))
                return true;

            return false;
        }

        /// <summary>
        /// Find a valid patient to feed from the cache
        /// </summary>
        protected Pawn FindValidPatientToFeed(Pawn feeder, Map map)
        {
            if (feeder?.Map == null) return null;
            
            // Update the cache first
            UpdateHungryPawnCache(map);

            int mapId = map.uniqueID;
            if (!_hungryPawnCache.ContainsKey(mapId) || _hungryPawnCache[mapId].Count == 0)
                return null;

            // Use distance bucketing for efficient selection
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                feeder,
                _hungryPawnCache[mapId],
                (patient) => (patient.Position - feeder.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Find the best patient to feed
            return Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                feeder,
                (patient, pawn) => ValidateFeedingJob(patient, pawn),
                _reachabilityCache
            );
        }
    }
}