using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Base JobGiver for feeding patients. Handles common logic for all patient feeding jobs.
    /// Can be specialized through inheritance to target specific patient types.
    /// </summary>
    public abstract class JobGiver_FeedPatient_PawnControl : ThinkNode_JobGiver
    {
        // Cache for hungry pawns to improve performance
        private static readonly Dictionary<int, List<Pawn>> _hungryPawnCache = new Dictionary<int, List<Pawn>>();
        private static readonly Dictionary<int, Dictionary<Pawn, bool>> _reachabilityCache = new Dictionary<int, Dictionary<Pawn, bool>>();
        private static int _lastCacheUpdateTick = -999;
        private const int CACHE_UPDATE_INTERVAL = 180; // Update every 3 seconds

        // Distance thresholds for bucketing
        private static readonly float[] DISTANCE_THRESHOLDS = new float[] { 100f, 400f, 900f }; // 10, 20, 30 tiles

        // Configuration to be overridden by subclasses
        protected virtual bool FeedHumanlikesOnly => true;
        protected virtual bool FeedAnimalsOnly => false;
        protected virtual bool FeedPrisonersOnly => false;
        protected virtual string WorkTagForJob => "Doctor";
        protected virtual float JobPriority => 8.0f; // High priority - people shouldn't starve
        protected virtual string JobDescription => "feed patient";

        public override float GetPriority(Pawn pawn)
        {
            return JobPriority;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            // IMPORTANT: Only player pawns and slaves owned by player should feed patients
            if (pawn.Faction != Faction.OfPlayer &&
                !(pawn.IsSlave && pawn.HostFaction == Faction.OfPlayer))
                return null;

            return Utility_JobGiverManager.StandardTryGiveJob<Pawn>(
                pawn,
                WorkTagForJob,
                (p, forced) => {
                    // Update hungry pawn cache
                    UpdateHungryPawnCache(p.Map);
                    
                    // Find and create a job for feeding a patient
                    return TryCreateFeedJob(p, forced);
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
            // FIXED LINE: Remove the check for jobGiverClass since it doesn't exist
            if (pawn == Current.Game.CurrentMap?.mapPawns?.FreeColonistsSpawned.FirstOrDefault(p => p.CurJob?.def.defName == "FeedPatient"))
                return true;

            return false;
        }

        /// <summary>
        /// Create a job to feed a hungry patient
        /// </summary>
        protected virtual Job TryCreateFeedJob(Pawn pawn, bool forced = false)
        {
            if (pawn?.Map == null) return null;

            int mapId = pawn.Map.uniqueID;
            if (!_hungryPawnCache.ContainsKey(mapId) || _hungryPawnCache[mapId].Count == 0)
                return null;

            // Use JobGiverManager for distance bucketing
            var buckets = Utility_JobGiverManager.CreateDistanceBuckets(
                pawn,
                _hungryPawnCache[mapId],
                (patient) => (patient.Position - pawn.Position).LengthHorizontalSquared,
                DISTANCE_THRESHOLDS
            );

            // Find the best patient to feed
            Pawn targetPatient = Utility_JobGiverManager.FindFirstValidTargetInBuckets(
                buckets,
                pawn,
                (patient, p) => {
                    // Skip if no longer a valid hungry patient
                    if (!FeedPatientUtility.IsHungry(patient) || !FeedPatientUtility.ShouldBeFed(patient))
                        return false;

                    // Skip if forbidden or can't reserve
                    if (patient.IsForbidden(p) || !p.CanReserve(patient, 1, -1, null, forced))
                        return false;

                    // Check food restriction policy
                    if (patient.foodRestriction != null)
                    {
                        FoodPolicy respectedRestriction = patient.foodRestriction.GetCurrentRespectedRestriction(p);
                        if (respectedRestriction != null && respectedRestriction.filter.AllowedDefCount == 0)
                            return false;
                    }

                    // Check if there's food available for this patient
                    Thing foodSource;
                    ThingDef foodDef;
                    bool starving = patient.needs?.food?.CurCategory == HungerCategory.Starving;
                    return FoodUtility.TryFindBestFoodSourceFor(p, patient, starving, 
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
                
                if (FoodUtility.TryFindBestFoodSourceFor(pawn, targetPatient, starving,
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

                        Utility_DebugManager.LogNormal($"{pawn.LabelShort} created job to feed {targetPatient.LabelShort}{foodInfo}");
                    }

                    return job;
                }
            }

            return null;
        }

        /// <summary>
        /// Reset caches when loading game or changing maps
        /// </summary>
        public static void ResetCache()
        {
            Utility_CacheManager.ResetJobGiverCache(_hungryPawnCache, _reachabilityCache);
            _lastCacheUpdateTick = -999;
        }

        public override string ToString()
        {
            return "JobGiver_FeedPatient_PawnControl";
        }
    }
}