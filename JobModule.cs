using emitbreaker.PawnControl;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

/// <summary>
/// Generic base class for job modules that work with specific target types
/// </summary>
/// <typeparam name="T">The type of target this module processes</typeparam>
public abstract class JobModule<T> : JobModuleCore where T : Thing
{
    // Cooldown system for job modules
    private static readonly Dictionary<string, Dictionary<int, int>> _pawnModuleCooldowns = new Dictionary<string, Dictionary<int, int>>();

    // Default cooldown duration (can be overridden)
    public virtual int CooldownDurationTicks => 1800; // 30 seconds default

    // Check if a pawn is on cooldown for this module
    public bool IsPawnOnCooldown(Pawn pawn)
    {
        if (pawn == null) return false;

        int currentTick = Find.TickManager.TicksGame;
        string moduleId = UniqueID;
        int pawnId = pawn.thingIDNumber;

        // Check if module has cooldowns
        if (!_pawnModuleCooldowns.TryGetValue(moduleId, out var cooldowns))
            return false;

        // Check if pawn is on cooldown
        return cooldowns.TryGetValue(pawnId, out int cooldownEndTick) &&
               currentTick < cooldownEndTick;
    }

    // Get remaining cooldown ticks
    public int GetRemainingCooldownTicks(Pawn pawn)
    {
        if (pawn == null) return 0;

        int currentTick = Find.TickManager.TicksGame;
        string moduleId = UniqueID;
        int pawnId = pawn.thingIDNumber;

        // Check if module has cooldowns
        if (!_pawnModuleCooldowns.TryGetValue(moduleId, out var cooldowns))
            return 0;

        // Check if pawn is on cooldown
        if (cooldowns.TryGetValue(pawnId, out int cooldownEndTick) && currentTick < cooldownEndTick)
            return cooldownEndTick - currentTick;

        return 0;
    }

    // Start cooldown for a pawn
    public void StartCooldown(Pawn pawn)
    {
        if (pawn == null) return;

        int currentTick = Find.TickManager.TicksGame;
        string moduleId = UniqueID;
        int pawnId = pawn.thingIDNumber;

        // Initialize cooldowns dictionary if needed
        if (!_pawnModuleCooldowns.TryGetValue(moduleId, out var cooldowns))
        {
            cooldowns = new Dictionary<int, int>();
            _pawnModuleCooldowns[moduleId] = cooldowns;
        }

        // Set cooldown
        cooldowns[pawnId] = currentTick + CooldownDurationTicks;
    }

    // Reset cooldown for a pawn
    public void ResetCooldown(Pawn pawn)
    {
        if (pawn == null) return;

        string moduleId = UniqueID;
        int pawnId = pawn.thingIDNumber;

        // Check if module has cooldowns
        if (_pawnModuleCooldowns.TryGetValue(moduleId, out var cooldowns))
        {
            // Remove cooldown
            cooldowns.Remove(pawnId);
        }
    }

    // Clear all cooldowns for this module
    public void ClearAllCooldowns()
    {
        string moduleId = UniqueID;

        if (_pawnModuleCooldowns.TryGetValue(moduleId, out var cooldowns))
        {
            cooldowns.Clear();
        }
    }

    // Reset all cooldowns (for all modules)
    public static void ResetAllCooldowns()
    {
        _pawnModuleCooldowns.Clear();
    }

    // Simple fixed priority (higher number = higher priority)
    public override float Priority => 5f; // Default priority

    /// <summary>
    /// Filter function to identify valid targets for this job
    /// </summary>
    public abstract bool ShouldProcessTarget(T target, Map map);

    /// <summary>
    /// Update cache for targets (called during cache update phase)
    /// </summary>
    public virtual void UpdateCache(Map map, List<T> targetCache) { }

    /// <summary>
    /// Cached sectors already processed this update cycle
    /// </summary>
    private static readonly Dictionary<int, HashSet<int>> _processedSectors =
        new Dictionary<int, HashSet<int>>();

    /// <summary>
    /// Optional score function to prioritize targets based on specific criteria
    /// </summary>
    protected virtual float GetTargetScore(T target, Pawn pawn)
    {
        return 0f; // Default implementation returns neutral score
    }

    /// <summary>
    /// Progressive cache update that only processes part of the map each time
    /// </summary>
    protected void UpdateCacheProgressively(
        Map map,
        List<T> targetCache,
        ref int lastUpdateTick,
        HashSet<ThingRequestGroup> requestGroups,
        Func<T, bool> validator,
        Dictionary<int, List<T>> moduleCache = null,
        int updateInterval = 120,
        int sectorSize = 32)
    {
        if (map == null) return;

        int currentTick = Find.TickManager.TicksGame;
        int mapId = map.uniqueID;

        bool fullRefresh = currentTick > lastUpdateTick + updateInterval;

        // Initialize sector tracking for this map
        if (!_processedSectors.TryGetValue(mapId, out var sectors))
        {
            sectors = new HashSet<int>();
            _processedSectors[mapId] = sectors;
        }

        // Reset sector tracking if we're doing a fresh cycle
        if (fullRefresh && currentTick % (updateInterval * 10) == 0)
        {
            sectors.Clear();
        }

        if (fullRefresh)
        {
            // Calculate map sectors
            int numSectorsX = map.Size.x / sectorSize + 1;
            int numSectorsZ = map.Size.z / sectorSize + 1;
            int totalSectors = numSectorsX * numSectorsZ;

            // Choose sector using a weighted strategy based on player activity
            int sectorIndex;
            if (Rand.Value < 0.4f && Find.Selector.SelectedObjects.Count > 0 && Find.Selector.SelectedObjects[0] is Thing selectedThing)
            {
                // 40% chance to scan near selected objects
                IntVec3 focus = selectedThing.Position;
                int sectorXa = Mathf.Clamp(focus.x / sectorSize, 0, numSectorsX - 1);
                int sectorZa = Mathf.Clamp(focus.z / sectorSize, 0, numSectorsZ - 1);
                sectorIndex = sectorZa * numSectorsX + sectorXa;

                // If this sector was already processed, use standard rotation
                if (sectors.Contains(sectorIndex))
                {
                    // Get next unprocessed sector
                    int startIndex = (currentTick / updateInterval) % totalSectors;
                    sectorIndex = GetNextUnprocessedSector(sectors, startIndex, totalSectors);
                }
            }
            else
            {
                // Get next unprocessed sector
                int startIndex = (currentTick / updateInterval) % totalSectors;
                sectorIndex = GetNextUnprocessedSector(sectors, startIndex, totalSectors);
            }

            // Mark this sector as processed
            sectors.Add(sectorIndex);

            // If all sectors are processed, reset tracking
            if (sectors.Count >= totalSectors)
            {
                sectors.Clear();
            }

            int sectorX = sectorIndex % numSectorsX;
            int sectorZ = sectorIndex / numSectorsX;

            IntVec3 sectorMin = new IntVec3(sectorX * sectorSize, 0, sectorZ * sectorSize);
            IntVec3 sectorMax = new IntVec3(
                Math.Min((sectorX + 1) * sectorSize, map.Size.x),
                0,
                Math.Min((sectorZ + 1) * sectorSize, map.Size.z)
            );

            // Initialize or clean module cache if provided
            List<T> localCache = null;
            if (moduleCache != null)
            {
                if (!moduleCache.ContainsKey(mapId))
                {
                    moduleCache[mapId] = new List<T>(MaxCacheEntries / 2); // Pre-allocate with reasonable capacity
                }
                else
                {
                    // Only remove things from this sector
                    moduleCache[mapId].RemoveAll(t =>
                        t != null && t.Spawned &&
                        t.Position.x >= sectorMin.x && t.Position.x < sectorMax.x &&
                        t.Position.z >= sectorMin.z && t.Position.z < sectorMax.z);

                    // Ensure the cache doesn't grow too large
                    if (moduleCache[mapId].Count > MaxCacheEntries)
                    {
                        // Keep 75% of the most recent entries
                        int keepCount = (int)(MaxCacheEntries * 0.75f);
                        moduleCache[mapId] = moduleCache[mapId]
                            .Skip(moduleCache[mapId].Count - keepCount)
                            .ToList();

                        if (Prefs.DevMode)
                        {
                            Utility_DebugManager.LogNormal($"Trimmed module cache for {UniqueID} to {keepCount} entries");
                        }
                    }
                }

                localCache = moduleCache[mapId];
            }

            // Process targets from the current sector
            if (requestGroups != null)
            {
                foreach (var requestGroup in requestGroups)
                {
                    ProcessSectorForRequestGroup(
                        map,
                        requestGroup,
                        sectorMin,
                        sectorMax,
                        validator,
                        localCache,
                        targetCache);
                }
            }

            lastUpdateTick = currentTick;
        }
        else if (moduleCache != null && moduleCache.TryGetValue(mapId, out List<T> existingCache))
        {
            // Add cached items to target cache, filtering out destroyed/despawned ones
            // Use a temporary copy to avoid collection modification issues
            List<T> staleItems = null;

            foreach (T item in existingCache)
            {
                if (item != null && !item.Destroyed && item.Spawned)
                {
                    // Occasionally revalidate some cached items using a staggered approach
                    // Use thingIDNumber + tick to distribute validation across ticks
                    if ((item.thingIDNumber + currentTick) % 600 == 0)
                    {
                        if (!validator(item))
                        {
                            if (staleItems == null) staleItems = new List<T>();
                            staleItems.Add(item);
                            continue;
                        }
                    }

                    targetCache.Add(item);
                }
                else
                {
                    if (staleItems == null) staleItems = new List<T>();
                    staleItems.Add(item);
                }
            }

            // Remove stale items in a separate pass
            if (staleItems != null)
            {
                foreach (T item in staleItems)
                {
                    existingCache.Remove(item);
                }
            }
        }
    }

    /// <summary>
    /// Process a sector for a specific request group
    /// </summary>
    private void ProcessSectorForRequestGroup(
        Map map,
        ThingRequestGroup requestGroup,
        IntVec3 sectorMin,
        IntVec3 sectorMax,
        Func<T, bool> validator,
        List<T> localCache,
        List<T> targetCache)
    {
        // Use an optimized sector-based query if the map supports it
        IEnumerable<Thing> thingsInGroup;

        // TODO: Implement a sector-based query for ThingsInGroup if needed
        // For now, use the standard query and filter by position
        thingsInGroup = map.listerThings.ThingsInGroup(requestGroup);

        foreach (Thing thing in thingsInGroup)
        {
            if (!(thing is T typedThing)) continue;

            // Check if thing is in our current sector
            if (thing.Position.x < sectorMin.x || thing.Position.x >= sectorMax.x ||
                thing.Position.z < sectorMin.z || thing.Position.z >= sectorMax.z)
            {
                continue;
            }

            if (validator(typedThing))
            {
                if (localCache != null)
                {
                    localCache.Add(typedThing);
                }

                targetCache.Add(typedThing);
            }
        }
    }

    /// <summary>
    /// Find the next unprocessed sector
    /// </summary>
    private int GetNextUnprocessedSector(HashSet<int> processedSectors, int startIndex, int totalSectors)
    {
        // Start from preferred index, then wrap around
        for (int i = 0; i < totalSectors; i++)
        {
            int candidateIndex = (startIndex + i) % totalSectors;
            if (!processedSectors.Contains(candidateIndex))
            {
                return candidateIndex;
            }
        }

        // If all sectors have been processed, just return the starting index
        return startIndex;
    }

    /// <summary>
    /// Validates if the pawn can perform this job on the target
    /// </summary>
    public abstract bool ValidateJob(T target, Pawn actor);

    /// <summary>
    /// Creates the job for the pawn to perform on the target
    /// </summary>
    public abstract Job CreateJob(Pawn actor, T target);

    /// <summary>
    /// Optimize a list of targets for a specific pawn by scoring and sorting them
    /// </summary>
    protected void OptimizeTargetsForPawn(List<T> targets, Pawn pawn)
    {
        if (targets == null || targets.Count < 2 || pawn == null)
            return;

        // Calculate scores for each target
        var scoreCache = new Dictionary<T, float>(targets.Count);
        foreach (var target in targets)
        {
            scoreCache[target] = CalculateTargetScore(target, pawn);
        }

        // Sort targets by score (descending)
        targets.Sort((a, b) => scoreCache[b].CompareTo(scoreCache[a]));
    }

    /// <summary>
    /// Calculate a comprehensive score for a target, considering various factors
    /// </summary>
    private float CalculateTargetScore(T target, Pawn pawn)
    {
        if (target == null || pawn == null)
            return float.MinValue;

        float baseScore = GetTargetScore(target, pawn);

        // Factor in distance - closer is better
        float distance = (target.Position - pawn.Position).LengthHorizontal;
        float distanceFactor = Mathf.Max(1f, 100f - (distance * 2)); // Distance penalty

        // Path cost estimation - use terrain if available
        float pathCostFactor = 1.0f;
        if (distance < 40f) // Only check for nearby targets
        {
            int manhattanDist = Mathf.Abs(target.Position.x - pawn.Position.x) +
                              Mathf.Abs(target.Position.z - pawn.Position.z);

            // Sample terrain at a few points
            for (int i = 1; i <= 3; i++)
            {
                IntVec3 sample = new IntVec3(
                    pawn.Position.x + ((target.Position.x - pawn.Position.x) * i) / 4,
                    0,
                    pawn.Position.z + ((target.Position.z - pawn.Position.z) * i) / 4
                );

                if (sample.InBounds(pawn.Map))
                {
                    TerrainDef terrain = pawn.Map.terrainGrid.TerrainAt(sample);
                    if (terrain != null)
                    {
                        pathCostFactor += terrain.pathCost / 30.0f;
                    }
                }
            }
        }

        // Combine all factors - base score is most important, distance and path cost reduce it
        return baseScore + (distanceFactor / pathCostFactor);
    }

    /// <summary>
    /// Reset any static data when language changes or game reloads
    /// </summary>
    public override void ResetStaticData()
    {
        base.ResetStaticData();
        _processedSectors.Clear();
    }
}