using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace emitbreaker.PawnControl
{
    /// <summary>
    /// Manages global state flags for colony needs, job availability, and pawn capabilities
    /// </summary>
    public static class Utility_GlobalStateManager
    {
        #region Map-Level State Flags

        // Job category state flags for each map (map ID → job category → enabled)
        private static readonly Dictionary<int, Dictionary<JobCategory, bool>> _mapJobCategoryStates =
            new Dictionary<int, Dictionary<JobCategory, bool>>();

        // Colony need levels for each map (map ID → need type → need level)
        private static readonly Dictionary<int, Dictionary<ColonyNeedType, NeedLevel>> _mapNeedLevels =
            new Dictionary<int, Dictionary<ColonyNeedType, NeedLevel>>();

        // Work tag importance overrides based on colony needs (map ID → work tag → importance multiplier)
        private static readonly Dictionary<int, Dictionary<string, float>> _workTagImportanceOverrides =
            new Dictionary<int, Dictionary<string, float>>();

        // Cache update ticks
        private static readonly Dictionary<int, int> _lastMapUpdateTicks = new Dictionary<int, int>();
        private const int MAP_UPDATE_INTERVAL = 250; // Update map states every ~4 seconds

        /// <summary>
        /// Global job categories for quick filtering
        /// </summary>
        public enum JobCategory
        {
            Basic,              // Essential jobs like eating, sleeping
            Firefighting,       // Firefighting jobs
            Medical,            // Medical jobs (doctoring, patient)
            WorkBasic,          // Basic work (hauling, cleaning)
            WorkProduction,     // Production work (crafting, cooking)
            WorkConstruction,   // Building, mining
            WorkGrowing,        // Growing, plant cutting
            WorkAnimal,         // Animal handling, hunting, taming
            Social,             // Social interactions, warden duties
            Recreation,         // Recreation activities
            Deconstruction,     // Deconstruction and mining activities
            Combat             // Combat-related activities
        }

        /// <summary>
        /// Colony need types for tracking and prioritizing
        /// </summary>
        public enum ColonyNeedType
        {
            Safety,             // Colony safety (threats present)
            Food,               // Food availability
            Medicine,           // Medical needs
            Rest,               // Rest needs
            Construction,       // Construction needs
            Production,         // Production needs
            Defense,            // Defense readiness
            Temperature,        // Temperature management
            Power,              // Power availability
            Wealth              // Colony wealth/prosperity
        }

        /// <summary>
        /// Need level for colony priorities
        /// </summary>
        public enum NeedLevel
        {
            Critical = 0,       // Critical/emergency need
            High = 1,           // High priority need
            Medium = 2,         // Medium priority need
            Low = 3,            // Low priority need
            Satisfied = 4       // Need satisfied
        }

        #endregion

        #region Pawn Capability Flags

        // Pawn capability flags (pawn ID → capability flags)
        private static readonly Dictionary<int, PawnCapabilityFlags> _pawnCapabilityFlags =
            new Dictionary<int, PawnCapabilityFlags>();

        // Cached work tag compatibility (pawn ID → work tag → compatible)
        private static readonly Dictionary<int, Dictionary<string, bool>> _pawnWorkTagCompatibility =
            new Dictionary<int, Dictionary<string, bool>>();

        // Cache update ticks for pawns
        private static readonly Dictionary<int, int> _lastPawnUpdateTicks = new Dictionary<int, int>();
        private const int PAWN_UPDATE_INTERVAL = 500; // Update pawn capabilities every ~8 seconds

        /// <summary>
        /// Flags representing pawn capabilities
        /// </summary>
        [Flags]
        public enum PawnCapabilityFlags
        {
            None = 0,
            CanWalk = 1 << 0,           // Pawn can walk
            CanManipulate = 1 << 1,     // Pawn can manipulate (has manipulators)
            CanHaul = 1 << 2,           // Pawn can haul items
            CanSocialize = 1 << 3,      // Pawn can socialize
            CanDoViolence = 1 << 4,     // Pawn is capable of violence
            CanDoFineWork = 1 << 5,     // Pawn can do fine manipulation work
            CanEat = 1 << 6,            // Pawn can eat normally
            CanReach = 1 << 7,          // Pawn can reach (not isolated)
            CanLearn = 1 << 8,          // Pawn can learn
            CanBreed = 1 << 9,          // Pawn can breed
            HasIntelligence = 1 << 10,  // Pawn has intelligence
            IsDraftable = 1 << 11,      // Pawn can be drafted
            IsControlled = 1 << 12,     // Pawn is player-controlled
            IsCurrentlyDrafted = 1 << 13 // Pawn is currently drafted
        }

        #endregion

        #region WorkTag Metadata

        // Required capability flags for each work tag
        private static readonly Dictionary<string, PawnCapabilityFlags> _workTagRequiredCapabilities =
            new Dictionary<string, PawnCapabilityFlags>();

        // Applicable job categories for each work tag
        private static readonly Dictionary<string, JobCategory> _workTagCategories =
            new Dictionary<string, JobCategory>();

        // Need responsiveness for each work tag (which colony needs this work tag addresses)
        private static readonly Dictionary<string, Dictionary<ColonyNeedType, float>> _workTagNeedResponsiveness =
            new Dictionary<string, Dictionary<ColonyNeedType, float>>();

        #endregion

        #region Map State Management

        /// <summary>
        /// Updates the global state flags for a specific map
        /// </summary>
        public static void UpdateMapStateFlags(Map map)
        {
            if (map == null)
                return;

            int mapId = map.uniqueID;
            int currentTick = Find.TickManager.TicksGame;

            // Check if update is needed
            if (_lastMapUpdateTicks.TryGetValue(mapId, out int lastUpdate) &&
                currentTick - lastUpdate < MAP_UPDATE_INTERVAL)
                return;

            _lastMapUpdateTicks[mapId] = currentTick;

            // Initialize dictionaries if needed
            if (!_mapJobCategoryStates.TryGetValue(mapId, out var categoryStates))
            {
                categoryStates = new Dictionary<JobCategory, bool>();
                _mapJobCategoryStates[mapId] = categoryStates;
            }

            if (!_mapNeedLevels.TryGetValue(mapId, out var needLevels))
            {
                needLevels = new Dictionary<ColonyNeedType, NeedLevel>();
                _mapNeedLevels[mapId] = needLevels;
            }

            // Update job category states
            UpdateFirefightingState(map, categoryStates);
            UpdateMedicalState(map, categoryStates);
            UpdateWorkStates(map, categoryStates);
            UpdateCombatState(map, categoryStates);

            // Always enable basic needs
            categoryStates[JobCategory.Basic] = true;

            // Update colony need levels
            UpdateFoodNeedLevel(map, needLevels);
            UpdateSafetyNeedLevel(map, needLevels);
            UpdateMedicalNeedLevel(map, needLevels);
            UpdateConstructionNeedLevel(map, needLevels);
            UpdateDefenseNeedLevel(map, needLevels);
            UpdateTemperatureNeedLevel(map, needLevels);

            // Update work tag importance overrides based on need levels
            UpdateWorkTagImportance(mapId, needLevels);

            if (Prefs.DevMode)
            {
                Utility_DebugManager.LogNormal($"Updated state flags for map {mapId}");
            }
        }

        /// <summary>
        /// Updates firefighting job category state
        /// </summary>
        private static void UpdateFirefightingState(Map map, Dictionary<JobCategory, bool> states)
        {
            // Check if there are any fires on the map
            bool firesExist = map.listerThings.ThingsOfDef(ThingDefOf.Fire).Any();
            states[JobCategory.Firefighting] = firesExist;
        }

        /// <summary>
        /// Updates medical job category state
        /// </summary>
        private static void UpdateMedicalState(Map map, Dictionary<JobCategory, bool> states)
        {
            // Check if anyone needs medical attention
            bool medicalNeeded = map.mapPawns.AllPawnsSpawned.Any(p =>
                p.Faction == Faction.OfPlayer &&
                (p.health.HasHediffsNeedingTend() || HealthAIUtility.ShouldSeekMedicalRest(p)));

            states[JobCategory.Medical] = medicalNeeded;
        }

        /// <summary>
        /// Updates work-related job category states
        /// </summary>
        private static void UpdateWorkStates(Map map, Dictionary<JobCategory, bool> states)
        {
            // Work categories are generally always enabled, but could be disabled based on factors like:
            // - Time of day (pawns sleeping)
            // - Weather conditions
            // - Special events

            bool isNightTime = GenLocalDate.HourOfDay(map) < 6 || GenLocalDate.HourOfDay(map) > 20;
            bool badWeather = map.weatherManager.curWeather.rainRate > 0.7f ||
                             map.weatherManager.curWeather == WeatherDef.Named("Rain") ||
                             map.weatherManager.curWeather == WeatherDef.Named("DryThunderstorm");

            // Basic work is almost always needed
            states[JobCategory.WorkBasic] = true;

            // Other work types might be affected by conditions
            states[JobCategory.WorkProduction] = !isNightTime || map.mapPawns.FreeColonistsSpawned.Count <= 5;
            states[JobCategory.WorkConstruction] = !badWeather || map.mapPawns.FreeColonistsSpawned.Count <= 5;
            states[JobCategory.WorkGrowing] = !badWeather && !isNightTime;
            states[JobCategory.WorkAnimal] = true;
            states[JobCategory.Social] = true;
        }

        /// <summary>
        /// Updates combat job category state
        /// </summary>
        private static void UpdateCombatState(Map map, Dictionary<JobCategory, bool> states)
        {
            // Check if there is a current threat
            bool dangerPresent = map.dangerWatcher.DangerRating >= StoryDanger.Low ||
                                GenHostility.AnyHostileActiveThreatToPlayer(map);

            states[JobCategory.Combat] = dangerPresent;
        }

        /// <summary>
        /// Updates food need level
        /// </summary>
        private static void UpdateFoodNeedLevel(Map map, Dictionary<ColonyNeedType, NeedLevel> needLevels)
        {
            float totalFood = map.resourceCounter.TotalHumanEdibleNutrition;
            int colonistCount = map.mapPawns.FreeColonistsSpawned.Count;

            if (colonistCount == 0)
            {
                needLevels[ColonyNeedType.Food] = NeedLevel.Satisfied;
                return;
            }

            float daysOfFood = totalFood / (colonistCount * 1.6f); // 1.6 nutrition per day

            if (daysOfFood < 0.5f)
                needLevels[ColonyNeedType.Food] = NeedLevel.Critical;
            else if (daysOfFood < 2f)
                needLevels[ColonyNeedType.Food] = NeedLevel.High;
            else if (daysOfFood < 5f)
                needLevels[ColonyNeedType.Food] = NeedLevel.Medium;
            else if (daysOfFood < 10f)
                needLevels[ColonyNeedType.Food] = NeedLevel.Low;
            else
                needLevels[ColonyNeedType.Food] = NeedLevel.Satisfied;
        }

        /// <summary>
        /// Updates safety need level
        /// </summary>
        private static void UpdateSafetyNeedLevel(Map map, Dictionary<ColonyNeedType, NeedLevel> needLevels)
        {
            StoryDanger danger = map.dangerWatcher.DangerRating;

            switch (danger)
            {
                case StoryDanger.High:
                    needLevels[ColonyNeedType.Safety] = NeedLevel.Critical;
                    break;
                case StoryDanger.None:
                    needLevels[ColonyNeedType.Safety] = NeedLevel.Satisfied;
                    break;
                default: // Use Low instead of Medium
                    needLevels[ColonyNeedType.Safety] = NeedLevel.High;
                    break;
            }
        }

        /// <summary>
        /// Updates medical need level
        /// </summary>
        private static void UpdateMedicalNeedLevel(Map map, Dictionary<ColonyNeedType, NeedLevel> needLevels)
        {
            // Count pawns that need medical attention
            int totalColonists = map.mapPawns.FreeColonistsSpawned.Count;
            int needingTreatment = map.mapPawns.FreeColonistsSpawned.Count(p =>
                p.health.HasHediffsNeedingTend());
            int bedridden = map.mapPawns.FreeColonistsSpawned.Count(p =>
                HealthAIUtility.ShouldSeekMedicalRest(p));

            if (totalColonists == 0)
            {
                needLevels[ColonyNeedType.Medicine] = NeedLevel.Satisfied;
                return;
            }

            float sickRatio = (float)(needingTreatment + bedridden * 2) / totalColonists;

            if (sickRatio > 0.5f || bedridden > 2)
                needLevels[ColonyNeedType.Medicine] = NeedLevel.Critical;
            else if (sickRatio > 0.3f || bedridden > 0)
                needLevels[ColonyNeedType.Medicine] = NeedLevel.High;
            else if (sickRatio > 0.1f)
                needLevels[ColonyNeedType.Medicine] = NeedLevel.Medium;
            else if (needingTreatment > 0)
                needLevels[ColonyNeedType.Medicine] = NeedLevel.Low;
            else
                needLevels[ColonyNeedType.Medicine] = NeedLevel.Satisfied;
        }

        /// <summary>
        /// Updates construction need level
        /// </summary>
        private static void UpdateConstructionNeedLevel(Map map, Dictionary<ColonyNeedType, NeedLevel> needLevels)
        {
            // Check blueprints and frames
            int highPriorityBlueprints = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint)
                .Cast<Blueprint>()
                .Count(bp => IsHighPriorityBuilding(bp.def));

            int highPriorityFrames = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame)
                .Cast<Frame>()
                .Count(f => IsHighPriorityBuilding(f.def));

            int totalConstructionItems = highPriorityBlueprints + highPriorityFrames * 2 +
                map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).Count +
                map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame).Count;

            if (totalConstructionItems > 15 || highPriorityFrames > 2)
                needLevels[ColonyNeedType.Construction] = NeedLevel.Critical;
            else if (totalConstructionItems > 10 || highPriorityBlueprints > 2)
                needLevels[ColonyNeedType.Construction] = NeedLevel.High;
            else if (totalConstructionItems > 5)
                needLevels[ColonyNeedType.Construction] = NeedLevel.Medium;
            else if (totalConstructionItems > 0)
                needLevels[ColonyNeedType.Construction] = NeedLevel.Low;
            else
                needLevels[ColonyNeedType.Construction] = NeedLevel.Satisfied;
        }

        private static bool IsHighPriorityBuilding(ThingDef def)
        {
            if (def?.entityDefToBuild == null) return false;

            // Check if it's a turret, research table or bed
            return def.entityDefToBuild.defName.Contains("Turret") ||
                   def.entityDefToBuild.defName.Contains("Table") ||
                   def.entityDefToBuild.defName.Contains("Bed");
        }

        /// <summary>
        /// Updates defense need level
        /// </summary>
        private static void UpdateDefenseNeedLevel(Map map, Dictionary<ColonyNeedType, NeedLevel> needLevels)
        {
            // Check recent damage and calculate defense readiness
            bool recentDamage = map.dangerWatcher.DangerRating > StoryDanger.None;
            int defensiveBuildings = map.listerBuildings.allBuildingsColonist.Count(b =>
                b.def.defName.Contains("Turret"));
            int armedPawns = map.mapPawns.FreeColonistsSpawned.Count(p =>
                p.equipment.Primary != null && p.equipment.Primary.def.IsWeapon && p.equipment.Primary.def.IsMeleeWeapon);

            int totalDefenseRating = defensiveBuildings * 3 + armedPawns;
            int neededDefense = map.mapPawns.FreeColonistsSpawned.Count + 1;

            if (recentDamage && totalDefenseRating < neededDefense)
                needLevels[ColonyNeedType.Defense] = NeedLevel.Critical;
            else if (totalDefenseRating < neededDefense / 2)
                needLevels[ColonyNeedType.Defense] = NeedLevel.High;
            else if (totalDefenseRating < neededDefense)
                needLevels[ColonyNeedType.Defense] = NeedLevel.Medium;
            else if (totalDefenseRating < neededDefense * 2)
                needLevels[ColonyNeedType.Defense] = NeedLevel.Low;
            else
                needLevels[ColonyNeedType.Defense] = NeedLevel.Satisfied;
        }

        /// <summary>
        /// Updates temperature need level
        /// </summary>
        private static void UpdateTemperatureNeedLevel(Map map, Dictionary<ColonyNeedType, NeedLevel> needLevels)
        {
            float mapTemp = map.mapTemperature.OutdoorTemp;
            bool extremeTemperature = mapTemp < -10f || mapTemp > 30f;

            // Check if we have temperature control in living spaces
            var livingRooms = map.listerBuildings.allBuildingsColonist
                .Where(b => b.def.defName.Contains("Bed") && !b.IsBrokenDown())
                .Select(b => b.GetRoom())
                .Where(r => r != null)
                .Distinct()
                .ToList();

            if (livingRooms.Count == 0)
            {
                needLevels[ColonyNeedType.Temperature] = extremeTemperature ? NeedLevel.High : NeedLevel.Low;
                return;
            }

            int problematicRooms = livingRooms.Count(r =>
                r.Temperature < 10f || r.Temperature > 30f);

            float badRoomsRatio = (float)problematicRooms / livingRooms.Count;

            if (badRoomsRatio > 0.7f && extremeTemperature)
                needLevels[ColonyNeedType.Temperature] = NeedLevel.Critical;
            else if (badRoomsRatio > 0.5f)
                needLevels[ColonyNeedType.Temperature] = NeedLevel.High;
            else if (badRoomsRatio > 0.3f)
                needLevels[ColonyNeedType.Temperature] = NeedLevel.Medium;
            else if (badRoomsRatio > 0)
                needLevels[ColonyNeedType.Temperature] = NeedLevel.Low;
            else
                needLevels[ColonyNeedType.Temperature] = NeedLevel.Satisfied;
        }

        /// <summary>
        /// Updates WorkTag importance overrides based on colony needs
        /// </summary>
        private static void UpdateWorkTagImportance(int mapId, Dictionary<ColonyNeedType, NeedLevel> needLevels)
        {
            if (!_workTagImportanceOverrides.TryGetValue(mapId, out var importanceOverrides))
            {
                importanceOverrides = new Dictionary<string, float>();
                _workTagImportanceOverrides[mapId] = importanceOverrides;
            }
            else
            {
                importanceOverrides.Clear();
            }

            // Calculate importance overrides for all registered work tags
            foreach (var workTag in _workTagNeedResponsiveness.Keys)
            {
                float importanceMultiplier = 1.0f;
                bool anyNeedCritical = false;

                // Check each need that this work tag responds to
                if (_workTagNeedResponsiveness.TryGetValue(workTag, out var responsiveness))
                {
                    foreach (var needResponsiveness in responsiveness)
                    {
                        ColonyNeedType needType = needResponsiveness.Key;
                        float responseStrength = needResponsiveness.Value;

                        // Skip needs that aren't applicable to this map
                        if (!needLevels.TryGetValue(needType, out NeedLevel level))
                            continue;

                        // Calculate impact based on need level and response strength
                        float levelImpact;
                        switch (level)
                        {
                            case NeedLevel.Critical:
                                levelImpact = 2.0f;
                                anyNeedCritical = true;
                                break;
                            case NeedLevel.High:
                                levelImpact = 1.5f;
                                break;
                            case NeedLevel.Medium:
                                levelImpact = 1.0f;
                                break;
                            case NeedLevel.Low:
                                levelImpact = 0.8f;
                                break;
                            default: // Satisfied
                                levelImpact = 0.5f;
                                break;
                        }

                        // Apply impact to multiplier based on response strength
                        importanceMultiplier += (levelImpact - 1.0f) * responseStrength;
                    }
                }

                // Ensure minimum and maximum values
                importanceMultiplier = Math.Max(0.1f, Math.Min(importanceMultiplier, anyNeedCritical ? 3.0f : 2.0f));

                // Only store non-default multipliers
                if (Math.Abs(importanceMultiplier - 1.0f) > 0.05f)
                {
                    importanceOverrides[workTag] = importanceMultiplier;

                    if (Prefs.DevMode && importanceMultiplier > 1.2f)
                    {
                        Utility_DebugManager.LogNormal(
                            $"Boosting importance of work tag '{workTag}' by {(importanceMultiplier - 1.0f) * 100:F0}% due to colony needs");
                    }
                }
            }
        }

        /// <summary>
        /// Checks if a specific job category is enabled on a map
        /// </summary>
        public static bool IsJobCategoryEnabled(Map map, JobCategory category)
        {
            if (map == null)
                return true; // Default to enabled

            int mapId = map.uniqueID;

            // Ensure state flags are updated
            UpdateMapStateFlags(map);

            // Check if the category is enabled
            if (_mapJobCategoryStates.TryGetValue(mapId, out var categoryStates) &&
                categoryStates.TryGetValue(category, out bool isEnabled))
                return isEnabled;

            return true; // Default to enabled
        }

        /// <summary>
        /// Gets the current need level for a specific colony need on a map
        /// </summary>
        public static NeedLevel GetColonyNeedLevel(Map map, ColonyNeedType needType)
        {
            if (map == null)
                return NeedLevel.Satisfied; // Default to satisfied

            int mapId = map.uniqueID;

            // Ensure state flags are updated
            UpdateMapStateFlags(map);

            // Get the need level
            if (_mapNeedLevels.TryGetValue(mapId, out var needLevels) &&
                needLevels.TryGetValue(needType, out NeedLevel level))
                return level;

            return NeedLevel.Satisfied; // Default to satisfied
        }

        /// <summary>
        /// Gets the importance multiplier for a specific work tag on a map
        /// </summary>
        public static float GetWorkTagImportanceMultiplier(Map map, string workTag)
        {
            if (map == null || string.IsNullOrEmpty(workTag))
                return 1.0f; // Default multiplier

            int mapId = map.uniqueID;

            // Get the override if it exists
            if (_workTagImportanceOverrides.TryGetValue(mapId, out var overrides) &&
                overrides.TryGetValue(workTag, out float multiplier))
                return multiplier;

            return 1.0f; // Default multiplier
        }

        #endregion

        #region Pawn Capability Management

        /// <summary>
        /// Updates capability flags for a specific pawn
        /// </summary>
        public static void UpdatePawnCapabilities(Pawn pawn)
        {
            if (pawn == null)
                return;

            int pawnId = pawn.thingIDNumber;
            int currentTick = Find.TickManager.TicksGame;

            // Check if update is needed
            if (_lastPawnUpdateTicks.TryGetValue(pawnId, out int lastUpdate) &&
                currentTick - lastUpdate < PAWN_UPDATE_INTERVAL)
                return;

            _lastPawnUpdateTicks[pawnId] = currentTick;

            // Calculate pawn capabilities
            var capabilities = PawnCapabilityFlags.None;

            // Movement capability
            if (pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving))
                capabilities |= PawnCapabilityFlags.CanWalk;

            // Manipulation capability
            if (pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                capabilities |= PawnCapabilityFlags.CanManipulate;

            // Fine work capability
            if (pawn.health.capacities.GetLevel(PawnCapacityDefOf.Manipulation) > 0.8f)
                capabilities |= PawnCapabilityFlags.CanDoFineWork;

            // Hauling capability
            if ((capabilities & PawnCapabilityFlags.CanWalk) != 0 &&
                (capabilities & PawnCapabilityFlags.CanManipulate) != 0)
                capabilities |= PawnCapabilityFlags.CanHaul;

            // Social capability
            if (pawn.health.capacities.CapableOf(PawnCapacityDefOf.Talking))
                capabilities |= PawnCapabilityFlags.CanSocialize;

            // Violence capability
            if (!pawn.WorkTagIsDisabled(WorkTags.Violent))
                capabilities |= PawnCapabilityFlags.CanDoViolence;

            // Eating capability - use consciousness instead since Eating isn't defined
            if (pawn.health.capacities.CapableOf(PawnCapacityDefOf.Consciousness))
                capabilities |= PawnCapabilityFlags.CanEat;

            // Reachability (not isolated)
            if (pawn.Spawned && !pawn.Position.Fogged(pawn.Map) &&
                pawn.Map.reachability.CanReachMapEdge(pawn.Position, TraverseParms.For(pawn)))
                capabilities |= PawnCapabilityFlags.CanReach;

            // Learning capability
            if (pawn.health.capacities.CapableOf(PawnCapacityDefOf.Consciousness))
                capabilities |= PawnCapabilityFlags.CanLearn;

            // Breeding capability
            if (pawn.gender != Gender.None && pawn.ageTracker.CurLifeStage.reproductive)
                capabilities |= PawnCapabilityFlags.CanBreed;

            // Intelligence capability
            if (pawn.RaceProps.intelligence >= Intelligence.ToolUser)
                capabilities |= PawnCapabilityFlags.HasIntelligence;

            // Draftable
            if (pawn.drafter != null)
                capabilities |= PawnCapabilityFlags.IsDraftable;

            // Player controlled
            if (pawn.Faction == Faction.OfPlayer)
                capabilities |= PawnCapabilityFlags.IsControlled;

            // Currently drafted
            if (pawn.Drafted)
                capabilities |= PawnCapabilityFlags.IsCurrentlyDrafted;

            // Store the capabilities
            _pawnCapabilityFlags[pawnId] = capabilities;

            // Clear compatibility cache if it exists
            if (_pawnWorkTagCompatibility.ContainsKey(pawnId))
                _pawnWorkTagCompatibility[pawnId].Clear();
        }

        /// <summary>
        /// Gets capability flags for a specific pawn
        /// </summary>
        public static PawnCapabilityFlags GetPawnCapabilities(Pawn pawn)
        {
            if (pawn == null)
                return PawnCapabilityFlags.None;

            int pawnId = pawn.thingIDNumber;

            // Ensure capabilities are updated
            UpdatePawnCapabilities(pawn);

            // Get the capabilities
            if (_pawnCapabilityFlags.TryGetValue(pawnId, out PawnCapabilityFlags capabilities))
                return capabilities;

            return PawnCapabilityFlags.None;
        }

        /// <summary>
        /// Checks if a pawn has specific capabilities
        /// </summary>
        public static bool PawnHasCapabilities(Pawn pawn, PawnCapabilityFlags requiredCapabilities)
        {
            if (requiredCapabilities == PawnCapabilityFlags.None)
                return true; // No capabilities required

            PawnCapabilityFlags capabilities = GetPawnCapabilities(pawn);
            return (capabilities & requiredCapabilities) == requiredCapabilities;
        }

        /// <summary>
        /// Checks if a specific work tag is compatible with a pawn's capabilities
        /// </summary>
        public static bool IsWorkTagCompatibleWithPawn(string workTag, Pawn pawn)
        {
            if (string.IsNullOrEmpty(workTag) || pawn == null)
                return true; // Default to compatible

            int pawnId = pawn.thingIDNumber;

            // Check cache first
            if (_pawnWorkTagCompatibility.TryGetValue(pawnId, out var compatibilityDict) &&
                compatibilityDict.TryGetValue(workTag, out bool isCompatible))
                return isCompatible;

            // Get required capabilities for this work tag
            if (!_workTagRequiredCapabilities.TryGetValue(workTag, out PawnCapabilityFlags requiredCapabilities))
                return true; // No specific requirements

            // Check if the pawn has the required capabilities
            bool result = PawnHasCapabilities(pawn, requiredCapabilities);

            // Cache the result
            if (!_pawnWorkTagCompatibility.TryGetValue(pawnId, out compatibilityDict))
            {
                compatibilityDict = new Dictionary<string, bool>();
                _pawnWorkTagCompatibility[pawnId] = compatibilityDict;
            }

            compatibilityDict[workTag] = result;

            return result;
        }

        #endregion

        #region WorkTag Metadata Management

        /// <summary>
        /// Registers metadata for a work tag
        /// </summary>
        public static void RegisterWorkTagMetadata(
            string workTag,
            JobCategory category,
            PawnCapabilityFlags requiredCapabilities = PawnCapabilityFlags.None,
            Dictionary<ColonyNeedType, float> needResponsiveness = null)
        {
            if (string.IsNullOrEmpty(workTag))
                return;

            // Register job category
            _workTagCategories[workTag] = category;

            // Register required capabilities
            if (requiredCapabilities != PawnCapabilityFlags.None)
                _workTagRequiredCapabilities[workTag] = requiredCapabilities;

            // Register need responsiveness
            if (needResponsiveness != null && needResponsiveness.Count > 0)
                _workTagNeedResponsiveness[workTag] = new Dictionary<ColonyNeedType, float>(needResponsiveness);
        }

        /// <summary>
        /// Gets the job category for a specific work tag
        /// </summary>
        public static JobCategory GetWorkTagCategory(string workTag)
        {
            if (string.IsNullOrEmpty(workTag))
                return JobCategory.Basic; // Default to basic

            // Get the category if registered
            if (_workTagCategories.TryGetValue(workTag, out JobCategory category))
                return category;

            return JobCategory.Basic; // Default to basic
        }

        /// <summary>
        /// Gets the required capabilities for a specific work tag
        /// </summary>
        public static PawnCapabilityFlags GetWorkTagRequiredCapabilities(string workTag)
        {
            if (string.IsNullOrEmpty(workTag))
                return PawnCapabilityFlags.None; // Default to none

            // Get the required capabilities if registered
            if (_workTagRequiredCapabilities.TryGetValue(workTag, out PawnCapabilityFlags capabilities))
                return capabilities;

            return PawnCapabilityFlags.None; // Default to none
        }

        #endregion

        #region Integration with JobGiver System

        /// <summary>
        /// Should a job be skipped based on global state flags?
        /// </summary>
        public static bool ShouldSkipJobGiverDueToGlobalState(string workTag, Pawn pawn)
        {
            if (string.IsNullOrEmpty(workTag) || pawn == null || !pawn.Spawned || pawn.Map == null)
                return false;

            // Check if the job category is enabled on the map
            JobCategory category = GetWorkTagCategory(workTag);
            if (!IsJobCategoryEnabled(pawn.Map, category))
            {
                if (Prefs.DevMode)
                    Utility_DebugManager.LogNormal($"Skipping work tag '{workTag}' for {pawn.LabelShort} - category {category} disabled");
                return true;
            }

            // Check if the pawn has the required capabilities
            if (!IsWorkTagCompatibleWithPawn(workTag, pawn))
            {
                if (Prefs.DevMode)
                    Utility_DebugManager.LogNormal($"Skipping work tag '{workTag}' for {pawn.LabelShort} - incompatible capabilities");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets the importance multiplier for a work tag for a specific pawn
        /// </summary>
        public static float GetWorkTagImportanceMultiplier(string workTag, Pawn pawn)
        {
            if (string.IsNullOrEmpty(workTag) || pawn == null || !pawn.Spawned || pawn.Map == null)
                return 1.0f;

            return GetWorkTagImportanceMultiplier(pawn.Map, workTag);
        }

        /// <summary>
        /// Legacy method for backward compatibility - Converts ThinkNode_JobGiver to work tag approach
        /// </summary>
        public static bool ShouldSkipJobGiverDueToGlobalState(ThinkNode_JobGiver jobGiver, Pawn pawn)
        {
            if (jobGiver == null || pawn == null || !pawn.Spawned)
                return false;

            Type jobGiverType = jobGiver.GetType();

            // Get work tag for this job giver type
            string workTag = Utility_JobGiverManager.GetWorkTagForJobGiverType(jobGiverType);

            // Use work tag-based approach if available
            if (!string.IsNullOrEmpty(workTag))
            {
                return ShouldSkipJobGiverDueToGlobalState(workTag, pawn);
            }

            // Fallback for job givers without work tags
            JobCategory category = GetJobGiverCategory(jobGiverType);
            if (!IsJobCategoryEnabled(pawn.Map, category))
            {
                if (Prefs.DevMode)
                    Utility_DebugManager.LogNormal($"Skipping {jobGiverType.Name} for {pawn.LabelShort} - category {category} disabled");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Legacy method - Gets the job category for a job giver type
        /// </summary>
        private static JobCategory GetJobGiverCategory(Type jobGiverType)
        {
            if (jobGiverType == null)
                return JobCategory.Basic;

            // Try to get work tag for this job giver
            string workTag = Utility_JobGiverManager.GetWorkTagForJobGiverType(jobGiverType);
            if (!string.IsNullOrEmpty(workTag))
            {
                return GetWorkTagCategory(workTag);
            }

            // Fallback to a sensible default based on job giver name
            string typeName = jobGiverType.Name;

            if (typeName.Contains("Medical") || typeName.Contains("Doctor") || typeName.Contains("Tend"))
                return JobCategory.Medical;
            else if (typeName.Contains("Fire"))
                return JobCategory.Firefighting;
            else if (typeName.Contains("Craft") || typeName.Contains("Cook"))
                return JobCategory.WorkProduction;
            else if (typeName.Contains("Build") || typeName.Contains("Construct"))
                return JobCategory.WorkConstruction;
            else if (typeName.Contains("Grow") || typeName.Contains("Plant"))
                return JobCategory.WorkGrowing;
            else if (typeName.Contains("Hunt") || typeName.Contains("Animal"))
                return JobCategory.WorkAnimal;
            else if (typeName.Contains("Clean") || typeName.Contains("Haul"))
                return JobCategory.WorkBasic;
            else if (typeName.Contains("Social") || typeName.Contains("Warden"))
                return JobCategory.Social;
            else if (typeName.Contains("Play") || typeName.Contains("Joy"))
                return JobCategory.Recreation;
            else if (typeName.Contains("Combat") || typeName.Contains("Attack"))
                return JobCategory.Combat;

            return JobCategory.Basic;
        }

        #endregion

        #region Memory Management

        /// <summary>
        /// Cleans up data for a specific map
        /// </summary>
        public static void CleanupMapData(int mapId)
        {
            _mapJobCategoryStates.Remove(mapId);
            _mapNeedLevels.Remove(mapId);
            _workTagImportanceOverrides.Remove(mapId);
            _lastMapUpdateTicks.Remove(mapId);
        }

        /// <summary>
        /// Cleans up data for a specific pawn
        /// </summary>
        public static void CleanupPawnData(int pawnId)
        {
            _pawnCapabilityFlags.Remove(pawnId);
            _pawnWorkTagCompatibility.Remove(pawnId);
            _lastPawnUpdateTicks.Remove(pawnId);
        }

        /// <summary>
        /// Resets all global state data
        /// </summary>
        public static void ResetAllData()
        {
            _mapJobCategoryStates.Clear();
            _mapNeedLevels.Clear();
            _workTagImportanceOverrides.Clear();
            _lastMapUpdateTicks.Clear();

            _pawnCapabilityFlags.Clear();
            _pawnWorkTagCompatibility.Clear();
            _lastPawnUpdateTicks.Clear();

            _workTagCategories.Clear();
            _workTagRequiredCapabilities.Clear();
            _workTagNeedResponsiveness.Clear();
        }

        #endregion
    }
}